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

        if (source is "md" or "markdown")
            return await PandocConverter.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false);

        return await LibreOfficeConverter.ConvertAsync(job, progress, cancellationToken).ConfigureAwait(false);
    }

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
