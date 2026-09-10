using ToolBox.Models;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ToolBox.Services.Converters;

/// <summary>
/// PDF 转图片的"内置"路线：用 Windows 10/11 自带的 <see cref="PdfDocument"/> 渲染。
///
/// 为什么需要它：poppler 的 pdftoppm 是好东西，但很多机器上根本没有，
/// 而"为了把 PDF 转成图片去下一个 40 MB 的包"实在没必要 —— 系统自己就会渲染 PDF，
/// 这个 API 从 Windows 10 1809 起就有了。
///
/// 所以优先级是：装了 poppler 就用 poppler（久经考验、格式兼容最好），
/// 没装就用这个内置的，两条路都能出图。
/// </summary>
internal static class PdfWindowsRenderer
{
    /// <summary>渲染精度。PDF 的"点"按 96 DPI 定义，这里按 150 DPI 出图，清晰度和体积比较平衡。</summary>
    private const double TargetDpi = 150;
    private const double BaseDpi = 96;

    public static async Task<ConversionOutcome> RenderAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var isJpeg = job.TargetExtension.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
                     job.TargetExtension.Equals("jpeg", StringComparison.OrdinalIgnoreCase);
        var extension = isJpeg ? "jpg" : "png";

        var sourcePath = Path.GetFullPath(job.SourcePath);
        if (!File.Exists(sourcePath))
            return ConversionOutcome.Fail("这个文件找不到了，可能被删掉或者改名了。");

        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var targetFolder = FileUtil.UniqueDirectory(Path.Combine(job.OutputDirectory, baseName));
        Directory.CreateDirectory(targetFolder);

        progress.Report(ProgressInfo.Busy("正在把 PDF 转成图片…（用的是 Windows 自带的渲染）"));

        PdfDocument document;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(sourcePath).AsTask(cancellationToken).ConfigureAwait(false);
            document = await PdfDocument.LoadFromFileAsync(file).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            FileUtil.TryDeleteDirectory(targetFolder);
            return ConversionOutcome.Fail("这个 PDF 打不开，可能坏了，或者需要密码，换一个试试。");
        }

        // 注意：PdfDocument 在 CsWinRT 投影里没实现 IDisposable（它不实现 IClosable），
        // 所以不能用 using —— 它靠 GC 和 COM 引用计数回收。PdfPage 才实现 IDisposable。
        {
            var pageCount = document.PageCount;
            if (pageCount == 0)
            {
                FileUtil.TryDeleteDirectory(targetFolder);
                return ConversionOutcome.Fail("这个 PDF 一页都没有，换一个试试。");
            }

            var produced = new List<string>();
            var scale = TargetDpi / BaseDpi;

            for (var index = 0; index < pageCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.Report(new ProgressInfo(
                    (double)index / pageCount,
                    $"正在转第 {index + 1} / {pageCount} 页…"));

                var outputPath = Path.Combine(
                    targetFolder,
                    $"{baseName}-{index + 1}.{extension}");

                try
                {
                    await RenderPageAsync(document, (uint)index, outputPath, isJpeg, scale, cancellationToken)
                        .ConfigureAwait(false);
                    produced.Add(outputPath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // 单页出问题不整体作废，能出几页算几页
                    AppLog.Write($"第 {index + 1} 页渲染失败：{exception.Message}");
                }
            }

            if (produced.Count == 0)
            {
                FileUtil.TryDeleteDirectory(targetFolder);
                return ConversionOutcome.Fail("这个 PDF 打不开，可能坏了，或者需要密码，换一个试试。");
            }

            progress.Report(new ProgressInfo(1, "图片转好了"));
            return ConversionOutcome.Ok([.. produced]);
        }
    }

    private static async Task RenderPageAsync(
        PdfDocument document,
        uint pageIndex,
        string outputPath,
        bool isJpeg,
        double scale,
        CancellationToken cancellationToken)
    {
        using var page = document.GetPage(pageIndex);

        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale),
            BitmapEncoderId = isJpeg
                ? Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId
                : Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
        };

        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, options).AsTask(cancellationToken).ConfigureAwait(false);

        // WinRT 的内存流得自己倒进 byte[]，再落盘
        var size = checked((int)stream.Size);
        var buffer = new byte[size];

        stream.Seek(0);
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)size).AsTask(cancellationToken).ConfigureAwait(false);
            reader.ReadBytes(buffer);
        }

        await File.WriteAllBytesAsync(outputPath, buffer, cancellationToken).ConfigureAwait(false);
    }
}
