using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ToolBox.Models;

namespace ToolBox.Services.Converters;

/// <summary>
/// 文档转 PDF，走本机已经装好的 Microsoft Office（Word / Excel / PowerPoint 的 COM 自动化）。
///
/// 什么时候用它：机器上有 Office、但没有 LibreOffice 的时候。
/// 优先级仍然让 LibreOffice 排第一，因为它是 headless 的、天生为命令行转换设计，
/// 不会跟用户正在用的 Office 抢实例；Office COM 是"本机正好有，那就别浪费"的兜底。
///
/// 几个实测得出的要点：
///   1. Office 自动化要求 STA 线程。转换器的调用线程是线程池的 MTA，所以自己开一个 STA 线程。
///   2. 自己新建的实例默认就是隐藏的（Visible=False，无主窗口），不会弹出窗口打扰用户。
///   3. 如果用户本来就开着 Word，COM 会附着到他那个实例上 —— 这时候绝不能改 Visible、
///      也绝不能 Quit，否则会把人家正在编辑的东西弄没。所以下面到处都在判断 <c>ownedApp</c>。
///   4. 打开纯文本（.txt/.md）会弹"文件转换"编码对话框把程序卡死。
///      解法是给 Open 传第 15 个参数 NoEncodingDialog=true，并指定 Encoding=UTF-8。
/// </summary>
internal static class OfficeComConverter
{
    // Office 的导出格式常量
    private const int WordExportFormatPdf = 17;      // wdExportFormatPDF
    private const int ExcelExportFormatPdf = 0;      // xlTypePDF
    private const int PowerPointSaveAsPdf = 32;      // ppSaveAsPDF
    private const int MsoEncodingUtf8 = 65001;       // UTF-8

    /// <summary>单个文档最多让它转这么久，超了就当失败，免得界面一直转圈。</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private enum OfficeKind { Word, Excel, PowerPoint }

    public static async Task<ConversionOutcome> ConvertAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken,
        string? sourceOverride = null)
    {
        var sourcePath = sourceOverride ?? job.SourcePath;
        var kind = ResolveKind(sourcePath);
        if (kind is null)
            return ConversionOutcome.Fail("这种格式暂时转不了，换一个试试。");

        if (!IsAvailable(kind.Value))
            return ConversionOutcome.Fail(FriendlyError.Describe(new ToolMissingException(ToolKind.Soffice)));

        var outputPath = FileUtil.UniquePath(Path.Combine(
            job.OutputDirectory,
            Path.GetFileNameWithoutExtension(job.SourcePath) + "." + job.TargetExtension));

        progress.Report(ProgressInfo.Busy("正在用 Office 转换文档…第一次打开会慢一点"));

        // Office COM 必须跑在 STA 线程上
        var work = RunOnStaThread(() => ConvertCore(kind.Value, sourcePath, outputPath));

        var finished = await Task.WhenAny(work, Task.Delay(Timeout, cancellationToken)).ConfigureAwait(false);
        if (finished != work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ConversionOutcome.Fail(
                "这个文档转了很久还没好，可能是 Office 弹出了什么窗口。关掉 Office 再试一次。");
        }

        var (succeeded, message) = await work.ConfigureAwait(false);

        if (!succeeded) return ConversionOutcome.Fail(message);
        if (!File.Exists(outputPath)) return ConversionOutcome.Fail("转换没有成功，换一个文件试试。");

        return ConversionOutcome.Ok(outputPath);
    }

    /// <summary>对应的 Office 程序在这台机器上注册了没有。</summary>
    public static bool IsAvailable(OfficeKindProbe kind) => kind switch
    {
        OfficeKindProbe.Word => Type.GetTypeFromProgID("Word.Application", throwOnError: false) is not null,
        OfficeKindProbe.Excel => Type.GetTypeFromProgID("Excel.Application", throwOnError: false) is not null,
        OfficeKindProbe.PowerPoint => Type.GetTypeFromProgID("PowerPoint.Application", throwOnError: false) is not null,
        _ => false,
    };

    /// <summary>这台机器上任何一类 Office 文档能不能转（给界面提示用）。</summary>
    public static bool IsAnyOfficeAvailable =>
        IsAvailable(OfficeKindProbe.Word) ||
        IsAvailable(OfficeKindProbe.Excel) ||
        IsAvailable(OfficeKindProbe.PowerPoint);

    private static bool IsAvailable(OfficeKind kind) => kind switch
    {
        OfficeKind.Word => IsAvailable(OfficeKindProbe.Word),
        OfficeKind.Excel => IsAvailable(OfficeKindProbe.Excel),
        OfficeKind.PowerPoint => IsAvailable(OfficeKindProbe.PowerPoint),
        _ => false,
    };

    private static OfficeKind? ResolveKind(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant() switch
        {
            "doc" or "docx" or "docm" or "dotx" or "rtf" or "txt" or "odt" => OfficeKind.Word,
            "xls" or "xlsx" or "xlsm" or "csv" or "ods" => OfficeKind.Excel,
            "ppt" or "pptx" or "pps" or "ppsx" or "odp" => OfficeKind.PowerPoint,
            _ => null,
        };

    // ------------------------------------------------------------------ COM 干活

    private static (bool Succeeded, string Message) ConvertCore(OfficeKind kind, string sourcePath, string outputPath)
    {
        return kind switch
        {
            OfficeKind.Word => ConvertWord(sourcePath, outputPath),
            OfficeKind.Excel => ConvertExcel(sourcePath, outputPath),
            OfficeKind.PowerPoint => ConvertPowerPoint(sourcePath, outputPath),
            _ => (false, "这种格式暂时转不了。"),
        };
    }

    private static (bool, string) ConvertWord(string sourcePath, string outputPath)
    {
        // 用户本来就开着 Word 吗？开着就绝不能 Quit、也不能改 Visible
        var ownedApp = Process.GetProcessesByName("WINWORD").Length == 0;

        object? app = null, documents = null, document = null;
        try
        {
            app = CreateComObject("Word.Application");
            if (app is null) return (false, "这台电脑上没找到 Word。");

            if (ownedApp) SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", 0);
            try { SetProperty(app, "AutomationSecurity", 3); } catch { /* 老版本没有就算了 */ }

            documents = GetProperty(app, "Documents");
            if (documents is null) return (false, "Word 没能打开文档列表，换一个文件试试。");

            // Open 的参数表（一个都不能少，位置是固定的）：
            // 0 文件名 / 1 确认转换 / 2 只读 / 3 加入最近使用 /
            // 4-8 密码等 / 9 格式 / 10 编码 / 11 可见 / 12 打开并修复 / 13 文字方向 / 14 不弹编码对话框
            var isPlainText = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant()
                is "txt" or "csv" or "md" or "markdown";

            document = InvokeMethod(documents, "Open",
                sourcePath,                  // 0
                false,                       // 1 ConfirmConversions
                true,                        // 2 ReadOnly
                false,                       // 3 AddToRecentFiles
                Missing.Value,               // 4 PasswordDocument
                Missing.Value,               // 5 PasswordTemplate
                Missing.Value,               // 6 Revert
                Missing.Value,               // 7 WritePasswordDocument
                Missing.Value,               // 8 WritePasswordTemplate
                Missing.Value,               // 9 Format
                isPlainText ? MsoEncodingUtf8 : Missing.Value,   // 10 Encoding
                Missing.Value,               // 11 Visible
                Missing.Value,               // 12 OpenAndRepair
                Missing.Value,               // 13 DocumentDirection
                true);                       // 14 NoEncodingDialog ← 没这个就弹窗卡死

            if (document is null) return (false, "这个文件打不开，换一个试试。");
            InvokeMethod(document, "ExportAsFixedFormat", outputPath, WordExportFormatPdf);

            InvokeMethod(document, "Close", 0);
            document = null;
            return (true, string.Empty);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            return (false, DescribeComFailure(exception));
        }
        finally
        {
            if (document is not null) { try { InvokeMethod(document, "Close", 0); } catch { } Release(document); }
            if (documents is not null) Release(documents);
            if (app is not null)
            {
                // 只关我们自己启动的那个实例
                if (ownedApp) { try { InvokeMethod(app, "Quit", 0); } catch { } }
                Release(app);
            }
        }
    }

    private static (bool, string) ConvertExcel(string sourcePath, string outputPath)
    {
        var ownedApp = Process.GetProcessesByName("EXCEL").Length == 0;

        object? app = null, workbooks = null, workbook = null;
        try
        {
            app = CreateComObject("Excel.Application");
            if (app is null) return (false, "这台电脑上没找到 Excel。");

            if (ownedApp) SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", false);

            workbooks = GetProperty(app, "Workbooks");
            if (workbooks is null) return (false, "Excel 没能打开工作簿列表，换一个文件试试。");

            // Open(FileName, UpdateLinks, ReadOnly, ...)
            workbook = InvokeMethod(workbooks, "Open", sourcePath, Missing.Value, true);
            if (workbook is null) return (false, "这个文件打不开，换一个试试。");

            InvokeMethod(workbook, "ExportAsFixedFormat", ExcelExportFormatPdf, outputPath);

            InvokeMethod(workbook, "Close", false);
            workbook = null;
            return (true, string.Empty);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            return (false, DescribeComFailure(exception));
        }
        finally
        {
            if (workbook is not null) { try { InvokeMethod(workbook, "Close", false); } catch { } Release(workbook); }
            if (workbooks is not null) Release(workbooks);
            if (app is not null)
            {
                if (ownedApp) { try { InvokeMethod(app, "Quit"); } catch { } }
                Release(app);
            }
        }
    }

    private static (bool, string) ConvertPowerPoint(string sourcePath, string outputPath)
    {
        var ownedApp = Process.GetProcessesByName("POWERPNT").Length == 0;

        object? app = null, presentations = null, presentation = null;
        try
        {
            app = CreateComObject("PowerPoint.Application");
            if (app is null) return (false, "这台电脑上没找到 PowerPoint。");

            // PowerPoint 不允许 Visible=False（会直接报错），
            // 无窗口是靠 Open 的 WithWindow:=false 实现的。
            presentations = GetProperty(app, "Presentations");
            if (presentations is null) return (false, "PowerPoint 没能打开演示文稿列表，换一个文件试试。");

            // Open(FileName, ReadOnly, Untitled, WithWindow)
            presentation = InvokeMethod(presentations, "Open", sourcePath, true, false, false);
            if (presentation is null) return (false, "这个文件打不开，换一个试试。");

            InvokeMethod(presentation, "SaveAs", outputPath, PowerPointSaveAsPdf);

            InvokeMethod(presentation, "Close");
            presentation = null;
            return (true, string.Empty);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            return (false, DescribeComFailure(exception));
        }
        finally
        {
            if (presentation is not null) { try { InvokeMethod(presentation, "Close"); } catch { } Release(presentation); }
            if (presentations is not null) Release(presentations);
            if (app is not null)
            {
                if (ownedApp) { try { InvokeMethod(app, "Quit"); } catch { } }
                Release(app);
            }
        }
    }

    // ------------------------------------------------------------------ 小工具

    /// <summary>Office 的 COM 自动化要求 STA，线程池的线程是 MTA，所以自己开一个。</summary>
    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception exception) { completion.SetException(exception); }
        })
        {
            IsBackground = true,
            Name = "toolbox-office-com",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }

    private static object? CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: false);
        return type is null ? null : Activator.CreateInstance(type);
    }

    private static object? GetProperty(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static void SetProperty(object target, string name, object? value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static object? InvokeMethod(object target, string name, params object?[] arguments) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments);

    private static void Release(object? comObject)
    {
        if (comObject is null) return;
        try
        {
            if (Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
        }
        catch
        {
            // 释放失败无所谓，进程退出时系统会收
        }
    }

    private static string DescribeComFailure(Exception exception)
    {
        // COM 异常的 HResult 里带着 Office 自己的错误码
        if (exception is COMException com)
        {
            AppLog.Write($"Office COM 失败 HRESULT=0x{com.HResult:X8}：{com.Message}");

            return com.HResult switch
            {
                unchecked((int)0x800A1436) => "这个文档有密码保护，先去掉密码再试。",
                unchecked((int)0x80010105) => "Office 忙不过来，先把它关掉再试一次。",
                _ => "这个文件打不开，可能坏了，或者需要密码，换一个试试。",
            };
        }

        return "这个文件打不开，可能坏了，换一个试试。";
    }
}

/// <summary>给界面用的探测入口（不暴露内部枚举）。</summary>
public enum OfficeKindProbe
{
    Word,
    Excel,
    PowerPoint,
}
