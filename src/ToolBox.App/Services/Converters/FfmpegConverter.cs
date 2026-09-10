using System.Globalization;
using ToolBox.Models;

namespace ToolBox.Services.Converters;

/// <summary>
/// 音视频转换，全靠 FFmpeg。
/// 进度不是靠猜的：先用 ffprobe 问出总时长，再读 ffmpeg 的 -progress 输出算百分比。
/// </summary>
internal static class FfmpegConverter
{
    public static async Task<ConversionOutcome> ConvertAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var ffmpeg = ToolLocator.Require(ToolKind.Ffmpeg);

        var extension = job.TargetExtension.ToLowerInvariant();
        var outputOptions = BuildOutputOptions(extension);
        if (outputOptions is null)
            return ConversionOutcome.Fail("这个目标格式暂时转不了，换一个试试。");

        var outputPath = FileUtil.UniquePath(Path.Combine(
            job.OutputDirectory,
            Path.GetFileNameWithoutExtension(job.SourcePath) + "." + extension));

        var busyMessage = job.AudioOnly
            ? "正在把声音取出来…"
            : job.Category == CategoryKind.Audio ? "正在转换音频…" : "正在转换视频…";

        progress.Report(ProgressInfo.Busy(busyMessage));

        // 总时长用来算百分比。问不到就退化成“来回滚动”的进度条，不假装有进度。
        var totalSeconds = await ProbeDurationAsync(job.SourcePath, cancellationToken).ConfigureAwait(false);

        var arguments = new List<string>
        {
            "-y",              // 覆盖我们自己指定的目标文件（目标名早就避开了重名）
            "-hide_banner",
            "-nostdin",        // 不要偷看键盘输入，GUI 程序必须加
            "-loglevel", "error",
            "-progress", "pipe:1",
            "-i", job.SourcePath,
        };
        arguments.AddRange(outputOptions);
        arguments.Add(outputPath);

        var lastFraction = 0d;

        var result = await ProcessRunner.RunAsync(
            ffmpeg,
            arguments,
            onOutputLine: line =>
            {
                if (totalSeconds <= 0) return;

                var seconds = ParseOutTime(line);
                if (seconds < 0) return;

                var fraction = Math.Clamp(seconds / totalSeconds, 0, 1);

                // 每变化 0.5% 才通知界面一次，避免几千次跨线程调用把界面拖卡。
                if (fraction - lastFraction < 0.005 && fraction < 1) return;
                lastFraction = fraction;
                progress.Report(new ProgressInfo(fraction, busyMessage));
            },
            onErrorLine: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || !File.Exists(outputPath))
        {
            AppLog.Write($"ffmpeg 失败（退出码 {result.ExitCode}）：{result.StandardError}");

            if (FriendlyError.LooksLikeDiskFull(result.StandardError))
                return ConversionOutcome.Fail("磁盘空间不够了，清理一点空间再试。");

            if (FriendlyError.LooksLikeBadInput(result.StandardError))
                return ConversionOutcome.Fail("这个文件打不开，可能已经损坏，换一个试试。");

            return ConversionOutcome.Fail("这个文件转不了，可能格式不对或者已经损坏，换一个试试。");
        }

        progress.Report(new ProgressInfo(1, busyMessage));
        return ConversionOutcome.Ok(outputPath);
    }

    /// <summary>
    /// 每种目标格式对应一组 FFmpeg 输出参数。
    /// 都是“普通用户直接能用”的稳妥默认值，不追求极限压缩率。
    /// </summary>
    private static List<string>? BuildOutputOptions(string extension) => extension switch
    {
        // ---- 纯音频：-vn 表示不要画面 ----
        "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
        "wav" => ["-vn", "-c:a", "pcm_s16le"],
        "flac" => ["-vn", "-c:a", "flac", "-compression_level", "5"],
        "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
        "aac" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
        "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
        "opus" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],

        // ---- 视频 ----
        // H.264 + AAC 是兼容性最好的组合，手机、电视、微信基本都认。
        "mp4" => ["-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"],
        "m4v" => ["-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"],
        "mkv" => ["-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-c:a", "aac", "-b:a", "192k"],
        "mov" => ["-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"],
        // AVI 装 H.264 不标准，播放器容易翻脸，用 mpeg4 更稳。
        "avi" => ["-c:v", "mpeg4", "-q:v", "4", "-c:a", "libmp3lame", "-b:a", "192k"],
        "webm" => ["-c:v", "libvpx-vp9", "-crf", "34", "-b:v", "0", "-c:a", "libopus"],
        "wmv" => ["-c:v", "wmv2", "-b:v", "4M", "-c:a", "wmav2"],

        _ => null,
    };

    /// <summary>问 FFmpeg 这个文件有多长（秒）。问不到就返回 0。</summary>
    private static async Task<double> ProbeDurationAsync(string path, CancellationToken cancellationToken)
    {
        var ffprobe = ToolLocator.Find(ToolKind.Ffprobe);
        if (ffprobe is null) return 0;

        try
        {
            var result = await ProcessRunner.RunAsync(
                ffprobe,
                ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", path],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return double.TryParse(
                result.StandardOutput.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds)
                ? seconds
                : 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // 读不出时长不影响转换，只是进度条变成不确定的。
            return 0;
        }
    }

    /// <summary>解析 ffmpeg -progress 输出里的 <c>out_time=00:00:05.123456</c>。</summary>
    private static double ParseOutTime(string line)
    {
        const string prefix = "out_time=";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return -1;

        var text = line[prefix.Length..].Trim();
        if (text.Length == 0 || text[0] is 'N' or 'n') return -1; // N/A

        var parts = text.Split(':');
        if (parts.Length != 3) return -1;

        if (!double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)) return -1;
        if (!double.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) return -1;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return -1;

        return (hours * 3600) + (minutes * 60) + seconds;
    }
}
