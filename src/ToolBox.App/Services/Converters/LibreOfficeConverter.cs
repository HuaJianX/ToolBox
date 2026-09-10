using ToolBox.Models;

namespace ToolBox.Services.Converters;

/// <summary>
/// 文档转换，交给 LibreOffice 命令行（headless 模式）。
/// 它是唯一能离线、免费、不加广告地把 Word / Excel / PPT 转成 PDF 的东西。
/// </summary>
internal static class LibreOfficeConverter
{
    public static async Task<ConversionOutcome> ConvertAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken,
        string? sourceOverride = null,
        string? inputFilter = null)
    {
        var soffice = ToolLocator.Require(ToolKind.Soffice);
        var filter = ResolveExportFilter(job);
        if (filter is null)
            return ConversionOutcome.Fail("这种格式暂时转不了，换一个试试。");

        var sourcePath = sourceOverride ?? job.SourcePath;
        var outputPath = FileUtil.UniquePath(Path.Combine(
            job.OutputDirectory,
            Path.GetFileNameWithoutExtension(job.SourcePath) + "." + job.TargetExtension));

        progress.Report(ProgressInfo.Busy("正在转换文档…（第一次用会慢一点，请稍等）"));

        var workDirectory = FileUtil.CreateTempDirectory("office");
        try
        {
            // 每次转换都用独立的用户配置目录：
            // 一是不跟用户自己打开的 LibreOffice 抢锁，二是可以同时转多个文件。
            var profileUri = new Uri(Path.Combine(workDirectory, "profile")).AbsoluteUri;

            var arguments = new List<string>
            {
                "--headless",
                "--norestore",
                "--invisible",
                "--nolockcheck",
                "--nodefault",
                "-env:UserInstallation=" + profileUri,
            };

            if (!string.IsNullOrEmpty(inputFilter))
                arguments.Add("--infilter=" + inputFilter);

            arguments.Add("--convert-to");
            arguments.Add(filter);
            arguments.Add("--outdir");
            arguments.Add(workDirectory);
            arguments.Add(sourcePath);

            var result = await ProcessRunner.RunAsync(
                soffice,
                arguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // LibreOffice 有个坑：转换失败它也返回退出码 0。
            // 所以唯一可信的判断标准是“产物到底有没有生成”。
            var produced = Path.Combine(
                workDirectory,
                Path.GetFileNameWithoutExtension(sourcePath) + "." + job.TargetExtension);

            if (!File.Exists(produced))
            {
                AppLog.Write(
                    $"soffice 没有产出文件。退出码 {result.ExitCode}；" +
                    $"标准输出：{result.StandardOutput}；标准错误：{result.StandardError}");

                if (FriendlyError.LooksLikeDiskFull(result.StandardError))
                    return ConversionOutcome.Fail("磁盘空间不够了，清理一点空间再试。");

                return ConversionOutcome.Fail("这个文件打不开，可能坏了，或者里面有加密，换一个试试。");
            }

            File.Move(produced, outputPath, overwrite: false);
            return ConversionOutcome.Ok(outputPath);
        }
        finally
        {
            FileUtil.TryDeleteDirectory(workDirectory);
        }
    }

    /// <summary>
    /// 目标格式 → LibreOffice 导出过滤器。
    /// 显式写过滤器而不是只写扩展名，是因为不同文档类型（文字/表格/演示）要用不同的导出器，
    /// 让 LibreOffice 自己猜容易猜错。
    /// </summary>
    private static string? ResolveExportFilter(ConversionJob job)
    {
        var target = job.TargetExtension.ToLowerInvariant();
        var source = Path.GetExtension(job.SourcePath).TrimStart('.').ToLowerInvariant();

        if (target == "pdf")
        {
            return source switch
            {
                "xls" or "xlsx" or "ods" or "csv" => "pdf:calc_pdf_Export",
                "ppt" or "pptx" or "odp" => "pdf:impress_pdf_Export",
                "pdf" => "pdf:writer_pdf_Export",
                _ => "pdf:writer_pdf_Export",
            };
        }

        return target switch
        {
            "docx" => "docx:MS Word 2007 XML",
            "doc" => "doc:MS Word 97",
            "odt" => "odt:writer8",
            "txt" => "txt:Text",
            "rtf" => "rtf:Rich Text Format",
            "html" => "html:HTML (StarWriter)",
            "xlsx" => "xlsx:Calc MS Excel 2007 XML",
            "csv" => "csv:Text - txt - csv (StarCalc)",
            _ => null,
        };
    }
}
