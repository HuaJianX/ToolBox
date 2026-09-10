using ToolBox.Models;
using ToolBox.Services.Converters;

namespace ToolBox.Services;

/// <summary>
/// 转换总调度：看文件属于哪一类，交给对应的组件去干。
/// 所有异常都在这一层被翻译成大白话，界面只需要显示 outcome.Message。
/// </summary>
public static class ConversionService
{
    public static async Task<ConversionOutcome> ConvertAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(job.SourcePath))
                return ConversionOutcome.Fail("这个文件找不到了，可能被删掉或者改名了。");

            Directory.CreateDirectory(job.OutputDirectory);

            if (CheckDiskSpace(job) is { } spaceProblem)
                return ConversionOutcome.Fail(spaceProblem);

            return job.Category switch
            {
                CategoryKind.Image => await ImageConverter.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false),
                CategoryKind.Audio or CategoryKind.Video => await FfmpegConverter.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false),
                CategoryKind.Document => await ConvertDocumentAsync(job, progress, cancellationToken).ConfigureAwait(false),
                CategoryKind.Pdf => await ConvertPdfAsync(job, progress, cancellationToken).ConfigureAwait(false),
                _ => ConversionOutcome.Fail("这个功能还没做好。"),
            };
        }
        catch (OperationCanceledException)
        {
            throw; // 取消不是错误，交给上层安静处理。
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            return ConversionOutcome.Fail(FriendlyError.Describe(exception));
        }
    }

    private static async Task<ConversionOutcome> ConvertDocumentAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var source = Path.GetExtension(job.SourcePath).TrimStart('.').ToLowerInvariant();

        // Markdown 先交给 Pandoc 排版（没装 Pandoc 会自动退化成纯文本，见 PandocConverter）
        if (source is "md" or "markdown")
            return await PandocConverter.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false);

        return await ConvertPlainDocumentAsync(job, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 文档转 PDF 的优先级：
    ///   1. LibreOffice —— headless，天生为命令行转换设计，不会跟用户正在用的 Office 抢实例
    ///   2. Microsoft Office —— 本机装了就用 COM 自动化（不用为了这个功能再去装 350 MB 的东西）
    ///   3. 都没有 —— 给一句人话
    /// </summary>
    internal static Task<ConversionOutcome> ConvertPlainDocumentAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken,
        string? sourceOverride = null)
    {
        if (ToolLocator.Find(ToolKind.Soffice) is not null)
            return LibreOfficeConverter.ConvertAsync(job, progress, cancellationToken, sourceOverride);

        if (OfficeComConverter.IsAnyOfficeAvailable)
            return OfficeComConverter.ConvertAsync(job, progress, cancellationToken, sourceOverride);

        return Task.FromResult(ConversionOutcome.Fail(
            "这台电脑上没找到能转文档的软件。装个 Microsoft Office 或者 LibreOffice（免费）就能用了。"));
    }

    /// <summary>这台机器现在能不能把文档转成 PDF。</summary>
    public static bool CanConvertDocuments =>
        ToolLocator.Find(ToolKind.Soffice) is not null || OfficeComConverter.IsAnyOfficeAvailable;

    /// <summary>文档转换实际靠的是谁，写在日志和自检里，排错时一眼看明白。</summary>
    public static string DocumentEngineName =>
        ToolLocator.Find(ToolKind.Soffice) is not null ? "LibreOffice"
        : OfficeComConverter.IsAnyOfficeAvailable ? "Microsoft Office（COM 自动化）"
        : "无";

    /// <summary>
    /// PDF 转图片现在能不能用。两条路有其一就行：
    /// 装了 poppler 用 poppler；没装就用 Windows 10 1809+ 自带的 PDF 渲染。
    /// </summary>
    public static bool CanRenderPdfToImages =>
        ToolLocator.Find(ToolKind.PdfToPpm) is not null ||
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    /// <summary>PDF 转图片实际走的是哪条路。</summary>
    public static string PdfImageEngineName =>
        ToolLocator.Find(ToolKind.PdfToPpm) is not null
            ? "Poppler（pdftoppm）"
            : "Windows 内置渲染（Windows.Data.Pdf）";

    /// <summary>PDF 转纯文本能不能用。</summary>
    public static bool CanExtractPdfText =>
        ToolLocator.Find(ToolKind.PdfToText) is not null || ToolLocator.Find(ToolKind.Soffice) is not null;

    private static async Task<ConversionOutcome> ConvertPdfAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var target = job.TargetExtension.ToLowerInvariant();

        return target switch
        {
            "png" or "jpg" or "jpeg" => await PdfConverter.ToImagesAsync(job, progress, cancellationToken).ConfigureAwait(false),
            "txt" => await PdfConverter.ToTextAsync(job, progress, cancellationToken).ConfigureAwait(false),
            "docx" or "doc" or "odt" or "rtf" => await PdfConverter.ToWordAsync(job, progress, cancellationToken).ConfigureAwait(false),
            _ => ConversionOutcome.Fail("这个格式暂时转不了，换一个试试。"),
        };
    }

    /// <summary>
    /// 开工前先看看磁盘够不够。
    /// 转视频动辄几个 G，与其转到一半失败，不如现在就说清楚（要求第 9 条点名要这个提示）。
    /// </summary>
    private static string? CheckDiskSpace(ConversionJob job)
    {
        try
        {
            var source = new FileInfo(job.SourcePath);
            if (!source.Exists) return null;

            var root = Path.GetPathRoot(Path.GetFullPath(job.OutputDirectory));
            if (string.IsNullOrEmpty(root)) return null;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return null;

            // 粗略估计：产出文件一般不会比原文件大多少，按 2 倍留余量。
            var estimated = source.Length * 2;
            if (drive.AvailableFreeSpace < estimated)
                return "磁盘空间不够了，清理一点空间再试。";

            return null;
        }
        catch
        {
            // 估不出来就算了，别因为这个挡住用户。
            return null;
        }
    }
}
