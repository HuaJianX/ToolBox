using ToolBox.Models;

namespace ToolBox.Services.Converters;

/// <summary>
/// 文档转换，交给 LibreOffice 命令行（headless 模式）。
/// 它是唯一能离线、免费、不加广告地把 Word / Excel / PPT 转成 PDF 的东西。
/// </summary>
internal static class LibreOfficeConverter
{
    /// <summary>
    /// LibreOffice 的用户配置目录要复用，不能一次一个。
    /// 实测：每次新建配置，一次转换要 14–15 秒；复用现成配置只要 6 秒 ——
    /// 因为它每次都要重新初始化一遍配置，那部分白等。
    /// 代价是同一个配置目录不能被两个 soffice 同时用，所以用信号量排队。
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static string ProfileDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppPaths.AppFolderName,
        "office-profile");

    /// <summary>
    /// 启动时在后台把配置先建好。这样用户第一次点「开始转换」就是 6 秒，而不是干等 15 秒。
    /// 失败了也无所谓，真正转换的时候会自己再建一次。
    /// </summary>
    public static void WarmUpInBackground()
    {
        var soffice = ToolLocator.Find(ToolKind.Soffice);
        if (soffice is null) return;

        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(ProfileDirectory);

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = soffice,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var argument in new[]
                {
                    "--headless", "--norestore", "--invisible", "--nolockcheck", "--nodefault",
                    "--terminate_after_init",
                    "-env:UserInstallation=" + new Uri(ProfileDirectory).AbsoluteUri,
                })
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = System.Diagnostics.Process.Start(startInfo);
                process?.WaitForExit(60_000);
                AppLog.Write("LibreOffice 配置预热完成");
            }
            catch (Exception exception)
            {
                AppLog.Write("LibreOffice 预热失败（不影响使用）：" + exception.Message);
            }
        })
        {
            IsBackground = true,
            Name = "toolbox-lo-warmup",
        };

        thread.Start();
    }

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

        progress.Report(ProgressInfo.Busy("正在转换文档…"));

        var workDirectory = FileUtil.CreateTempDirectory("office");

        // 排队：同一个用户配置目录同一时刻只能被一个 soffice 用
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ProfileDirectory);
            var profileUri = new Uri(ProfileDirectory).AbsoluteUri;

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
            Gate.Release();
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
