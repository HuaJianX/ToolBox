using ToolBox.Models;

namespace ToolBox.Services.Converters;

/// <summary>
/// Markdown → PDF。
/// Pandoc 负责把 Markdown 排版成 Word 文档，再让已经在用的 LibreOffice 把 Word 转成 PDF。
/// 这么绕是为了避开 LaTeX —— 那玩意儿一个多 G，跟“体积小、傻瓜化”完全冲突。
/// </summary>
internal static class PandocConverter
{
    public static async Task<ConversionOutcome> ConvertAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var pandoc = ToolLocator.Find(ToolKind.Pandoc);

        // 没装 Pandoc 也要能用：把 .md 当成纯文本交给 LibreOffice。
        // 标题符号（#）会原样留着不排版，但内容一个不少，属于合理的降级。
        if (pandoc is null)
        {
            progress.Report(ProgressInfo.Busy("正在转换 Markdown…"));
            return await ConvertAsPlainTextAsync(job, progress, cancellationToken).ConfigureAwait(false);
        }

        var workDirectory = FileUtil.CreateTempDirectory("markdown");
        try
        {
            var intermediateDocx = Path.Combine(
                workDirectory,
                Path.GetFileNameWithoutExtension(job.SourcePath) + ".docx");

            progress.Report(ProgressInfo.Busy("正在排版 Markdown…"));

            var result = await ProcessRunner.RunAsync(
                pandoc,
                ["-f", "markdown", "-t", "docx", "--standalone", "-o", intermediateDocx, job.SourcePath],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!File.Exists(intermediateDocx))
            {
                AppLog.Write($"pandoc 失败（退出码 {result.ExitCode}）：{result.StandardError}");
                // Pandoc 处理不了就退回纯文本路线，尽量把事办成。
                return await ConvertAsPlainTextAsync(job, progress, cancellationToken).ConfigureAwait(false);
            }

            // 交给下一个人收尾（LibreOffice 或本机 Office），产出文件仍然用原始文件名，
            // 用户看到的就是「笔记.pdf」。
            return await ConversionService
                .ConvertPlainDocumentAsync(job, progress, cancellationToken, intermediateDocx)
                .ConfigureAwait(false);
        }
        finally
        {
            FileUtil.TryDeleteDirectory(workDirectory);
        }
    }

    private static async Task<ConversionOutcome> ConvertAsPlainTextAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var workDirectory = FileUtil.CreateTempDirectory("markdown-text");
        try
        {
            // 复制成 .txt 再转，纯粹是因为 LibreOffice 不认 .md 这个后缀。
            var plainTextPath = Path.Combine(
                workDirectory,
                Path.GetFileNameWithoutExtension(job.SourcePath) + ".txt");

            File.Copy(job.SourcePath, plainTextPath, overwrite: true);

            return await ConversionService
                .ConvertPlainDocumentAsync(job, progress, cancellationToken, plainTextPath)
                .ConfigureAwait(false);
        }
        finally
        {
            FileUtil.TryDeleteDirectory(workDirectory);
        }
    }
}
