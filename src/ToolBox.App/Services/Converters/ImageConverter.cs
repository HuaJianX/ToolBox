using ImageMagick;
using ToolBox.Models;

namespace ToolBox.Services.Converters;

/// <summary>
/// 图片转换。用的是 ImageMagick 的 .NET 封装（Magick.NET）——
/// 原生库随 NuGet 一起分发，不需要用户另外装 ImageMagick，离线可用。
/// </summary>
internal static class ImageConverter
{
    /// <summary>
    /// 能“写出去”的格式。读取端不用列表：Magick.NET 按文件内容自动识别。
    ///
    /// 注意这里没有 HEIC/HEIF。实测（Magick.NET 14.17.1，Q16-x64）：
    ///     Heic: read=True  write=False   ← 有解码器，没有编码器
    /// HEIC 编码依赖 x265，因专利授权原因不随包分发，所以免费离线方案里
    /// 「转成 HEIC」做不到，但「HEIC 转成别的」完全没问题。
    /// </summary>
    private static readonly Dictionary<string, MagickFormat> WriteFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = MagickFormat.Jpg,
        ["jpeg"] = MagickFormat.Jpg,
        ["png"] = MagickFormat.Png,
        ["webp"] = MagickFormat.WebP,
        ["bmp"] = MagickFormat.Bmp,
        ["gif"] = MagickFormat.Gif,
        ["tif"] = MagickFormat.Tiff,
        ["tiff"] = MagickFormat.Tiff,
        ["avif"] = MagickFormat.Avif,
    };

    public static async Task<ConversionOutcome> ConvertAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var extension = job.TargetExtension.TrimStart('.').ToLowerInvariant();

        if (!WriteFormats.TryGetValue(extension, out var targetFormat))
        {
            // 与其让底层抛一句 “no encode delegate for this image format”，不如现在就说清楚。
            return extension is "heic" or "heif"
                ? ConversionOutcome.Fail("HEIC 图片只能读、不能生成（生成 HEIC 要额外的收费编码器）。建议转成 JPG 或 PNG。")
                : ConversionOutcome.Fail("这个目标格式暂时转不了，换一个试试。");
        }

        var outputPath = FileUtil.UniquePath(Path.Combine(
            job.OutputDirectory,
            Path.GetFileNameWithoutExtension(job.SourcePath) + "." + job.TargetExtension));

        progress.Report(ProgressInfo.Busy("正在转换图片…"));

        try
        {
            // 解码/编码是纯 CPU 活，扔到线程池里做，界面才不会卡。
            await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var image = new MagickImage(job.SourcePath);

                // 手机拍的照片带旋转标记，不摆正的话转出来会躺着。
                image.AutoOrient();

                if (targetFormat is MagickFormat.Jpg or MagickFormat.Bmp)
                {
                    // JPG / BMP 没有透明通道。不铺白底的话，PNG 的透明区域会变成黑块。
                    using var canvas = new MagickImage(MagickColors.White, image.Width, image.Height);
                    canvas.Composite(image, CompositeOperator.Over);
                    canvas.Format = targetFormat;
                    canvas.Quality = 92;
                    await canvas.WriteAsync(outputPath, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    image.Format = targetFormat;
                    await image.WriteAsync(outputPath, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消时可能已经写了一半，删掉，别在「转换结果」里留个坏图片
            FileUtil.TryDeleteFile(outputPath);
            throw;
        }
        catch (MagickException exception)
        {
            AppLog.Write(exception);

            // 实测 HEIC 解码是通的（两个真实样张都转成功了），所以走到这里通常是真的文件有问题。
            var sourceExtension = Path.GetExtension(job.SourcePath).TrimStart('.').ToLowerInvariant();
            if (sourceExtension is "heic" or "heif")
                return ConversionOutcome.Fail("这张 HEIC 照片读不出来，可能文件损坏了。可以在手机上重新存一份再试。");

            return ConversionOutcome.Fail("这个文件打不开，可能坏了或者不是真正的图片，换一个试试。");
        }

        if (!File.Exists(outputPath))
            return ConversionOutcome.Fail("转换没有成功，换一个文件试试。");

        return ConversionOutcome.Ok(outputPath);
    }

    /// <summary>
    /// 启动自检：把本机图片能力的实情写进日志。
    /// 以后有人问“为什么 HEIC 存不了”，看日志就明白了。
    /// </summary>
    public static string DescribeCapabilities()
    {
        try
        {
            var formats = MagickNET.SupportedFormats;
            var heic = formats.FirstOrDefault(info => info.Format == MagickFormat.Heic);

            return $"图片能力自检：可用格式 {formats.Count} 种；" +
                   $"HEIC 读取={heic?.SupportsReading ?? false}，HEIC 写入={heic?.SupportsWriting ?? false}。";
        }
        catch (Exception exception)
        {
            return "图片能力自检失败：" + exception.Message;
        }
    }
}
