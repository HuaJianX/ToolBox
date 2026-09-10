using System.Globalization;
using ToolBox.Models;

namespace ToolBox.Services.Converters;

/// <summary>
/// PDF 相关转换，用 Poppler 的命令行工具（pdftoppm / pdftotext）。
/// Poppler 是免安装的绿色包，解压就能用，很适合塞进安装包里。
/// </summary>
internal static class PdfConverter
{
    public static async Task<ConversionOutcome> ToImagesAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        // 两条路都能出图：装了 poppler 就用 poppler（兼容性最好），
        // 没装就用 Windows 自带的 PDF 渲染，不需要用户为了这个功能再去装东西。
        var pdfToPpm = ToolLocator.Find(ToolKind.PdfToPpm);
        if (pdfToPpm is null)
            return await PdfWindowsRenderer.RenderAsync(job, progress, cancellationToken).ConfigureAwait(false);

        var isJpeg = job.TargetExtension.Equals("jpg", StringComparison.OrdinalIgnoreCase) ||
                     job.TargetExtension.Equals("jpeg", StringComparison.OrdinalIgnoreCase);
        var extension = isJpeg ? "jpg" : "png";
        var pdftoppmFormatArgument = isJpeg ? "-jpeg" : "-png";

        var baseName = Path.GetFileNameWithoutExtension(job.SourcePath);

        // 一个 PDF 会出来一堆图片，单独放一个同名文件夹，免得把「转换结果」堆乱。
        var targetFolder = FileUtil.UniqueDirectory(Path.Combine(job.OutputDirectory, baseName));
        Directory.CreateDirectory(targetFolder);

        var prefix = Path.Combine(targetFolder, baseName);

        progress.Report(ProgressInfo.Busy(
            $"正在把 PDF 转成图片…页数多的话会慢一点，图片放在「{Path.GetFileName(targetFolder)}」文件夹里。"));

        var result = await ProcessRunner.RunAsync(
            pdfToPpm,
            [pdftoppmFormatArgument, "-r", "150", "-cropbox", job.SourcePath, prefix],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var images = Directory.Exists(targetFolder)
            ? Directory.GetFiles(targetFolder, "*." + extension).OrderBy(PageNumber).ToList()
            : [];

        if (images.Count == 0)
        {
            AppLog.Write($"pdftoppm 没有产出图片。退出码 {result.ExitCode}；标准错误：{result.StandardError}");
            FileUtil.TryDeleteDirectory(targetFolder);

            if (FriendlyError.LooksLikeDiskFull(result.StandardError))
                return ConversionOutcome.Fail("磁盘空间不够了，清理一点空间再试。");

            return ConversionOutcome.Fail("这个 PDF 打不开，可能坏了，或者需要密码，换一个试试。");
        }

        progress.Report(new ProgressInfo(1, "图片转好了"));
        return ConversionOutcome.Ok([.. images]);
    }

    public static async Task<ConversionOutcome> ToTextAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        var pdfToText = ToolLocator.Find(ToolKind.PdfToText);

        // 没有 Poppler 就让 LibreOffice 兜底（把 PDF 当 Writer 文档导入再导出成文本）。
        if (pdfToText is null)
            return await LibreOfficeConverter
                .ConvertAsync(job, progress, cancellationToken, inputFilter: "writer_pdf_import")
                .ConfigureAwait(false);

        var outputPath = FileUtil.UniquePath(Path.Combine(
            job.OutputDirectory,
            Path.GetFileNameWithoutExtension(job.SourcePath) + ".txt"));

        progress.Report(ProgressInfo.Busy("正在把 PDF 里的文字抽出来…"));

        var result = await ProcessRunner.RunAsync(
            pdfToText,
            ["-enc", "UTF-8", "-nopgbrk", job.SourcePath, outputPath],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!File.Exists(outputPath))
        {
            AppLog.Write($"pdftotext 失败（退出码 {result.ExitCode}）：{result.StandardError}");
            return ConversionOutcome.Fail("这个 PDF 打不开，可能坏了，或者需要密码，换一个试试。");
        }

        // 扫描件的 PDF 里只有图片没有文字层，抽出来是空的。
        // 与其给用户一个空文件，不如告诉他该用哪个功能。
        var info = new FileInfo(outputPath);
        if (info.Length < 16)
        {
            try { File.Delete(outputPath); } catch { /* 删不掉也无所谓 */ }

            return ConversionOutcome.Fail(
                "这个 PDF 是扫描件，里面没有可以直接取的文字。可以改用「PDF 转图片」，或者用带文字识别的工具。");
        }

        progress.Report(new ProgressInfo(1, "文字抽好了"));
        return ConversionOutcome.Ok(outputPath);
    }

    public static async Task<ConversionOutcome> ToWordAsync(
        ConversionJob job,
        IProgress<ProgressInfo> progress,
        CancellationToken cancellationToken)
    {
        // PDF 转 Word 这一项离不了 LibreOffice。
        // 实测过让 Word 自己打开 PDF（它确实能），但会弹一个「要不要转换」的确认框，
        // 自动化的时候直接卡死，而且转出来的版式也乱。所以这条路不硬撑。
        if (ToolLocator.Find(ToolKind.Soffice) is null)
            return ConversionOutcome.Fail(
                "PDF 转 Word 需要装一个 LibreOffice（免费）。" +
                "其它 PDF 功能（转图片、转纯文本）现在就能用。");

        progress.Report(ProgressInfo.Busy("正在把 PDF 转成 Word…排版复杂的 PDF 可能需要几分钟。"));

        // writer_pdf_import：让 LibreOffice 用 Writer 而不是 Draw 来打开 PDF，
        // 这样才导得出 .docx。
        return await LibreOfficeConverter
            .ConvertAsync(job, progress, cancellationToken, inputFilter: "writer_pdf_import")
            .ConfigureAwait(false);
    }

    /// <summary>从 <c>报告-12.png</c> 里取出页码，让图片按 1、2、3…… 而不是 1、10、2 排。</summary>
    private static int PageNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var dash = name.LastIndexOf('-');
        if (dash < 0 || dash == name.Length - 1) return int.MaxValue;

        return int.TryParse(
            name[(dash + 1)..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var number)
            ? number
            : int.MaxValue;
    }
}
