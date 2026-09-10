using System.Text;
using ToolBox.Models;
using ToolBox.Services;

// 冒烟测试：直接调用程序里的转换引擎，不开界面。
// 核心思想是「能转的真的转一遍，不能转的提示要说人话」。
//
// 装了外部组件就做真实转换，没装就退而验证「缺组件时的中文提示」，
// 所以这个测试在任何机器上跑都不会假绿。
//
// 用法：powershell -File tests\run-smoke-test.ps1   或
//       dotnet run --project tests\ToolBox.SmokeTest -c Release

Console.OutputEncoding = Encoding.UTF8;

var passed = 0;
var failed = 0;
var skipped = 0;

void Pass(string label, string detail)
{
    passed++;
    Console.WriteLine($"  [通过] {label}  ->  {detail}");
}

void Fail(string label, string detail)
{
    failed++;
    Console.WriteLine($"  [失败] {label}  ->  {detail}");
}

void Skip(string label, string why)
{
    skipped++;
    Console.WriteLine($"  [跳过] {label}  ->  {why}");
}

static string? FindAsset(string relativePath)
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, relativePath);
        if (File.Exists(candidate)) return candidate;
    }

    return null;
}

static bool Has(ToolKind kind) => ToolLocator.Find(kind) is not null;

var root = Path.Combine(Path.GetTempPath(), "toolbox-smoke-" + Guid.NewGuid().ToString("N")[..6]);
var outputDirectory = Path.Combine(root, "转换结果");
Directory.CreateDirectory(outputDirectory);

Console.WriteLine($"测试工作目录：{root}");
Console.WriteLine($"FFmpeg={Has(ToolKind.Ffmpeg)}  LibreOffice={Has(ToolKind.Soffice)}  " +
                  $"Poppler={Has(ToolKind.PdfToPpm)}  Pandoc={Has(ToolKind.Pandoc)}");
Console.WriteLine();

async Task<ConversionOutcome> RunAsync(string source, string target, CategoryKind category, bool audioOnly = false)
{
    var job = new ConversionJob(source, outputDirectory, target, category, audioOnly);
    return await ConversionService.ConvertAsync(job, new InlineProgress<ProgressInfo>(), CancellationToken.None);
}

static bool Produced(ConversionOutcome outcome) =>
    outcome.Success && outcome.OutputPaths.Count > 0 && outcome.OutputPaths.All(File.Exists);

static string SizeOf(ConversionOutcome outcome) =>
    outcome.OutputPaths.Count == 1
        ? $"{new FileInfo(outcome.OutputPaths[0]).Length} 字节"
        : $"{outcome.OutputPaths.Count} 个文件";

// =====================================================================
Console.WriteLine("1. 图片转换（Magick.NET 内置，不需要外部组件）");

var seedPng = Path.Combine(root, "测试图片.png");
using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Orange, 160, 120))
{
    image.Format = ImageMagick.MagickFormat.Png;
    image.Write(seedPng);
}

foreach (var target in new[] { "jpg", "png", "webp", "bmp", "tiff" })
{
    var outcome = await RunAsync(seedPng, target, CategoryKind.Image);
    if (Produced(outcome)) Pass($"PNG -> {target.ToUpperInvariant()}", SizeOf(outcome));
    else Fail($"PNG -> {target.ToUpperInvariant()}", outcome.Message);
}

// =====================================================================
Console.WriteLine();
Console.WriteLine("2. HEIC 解码（iPhone 照片最常见的场景）");

var heicSample = FindAsset(Path.Combine("tests", "assets", "sample.heic"));
if (heicSample is null)
{
    Skip("HEIC 解码", "没找到 tests/assets/sample.heic");
}
else
{
    foreach (var target in new[] { "jpg", "png", "webp" })
    {
        var outcome = await RunAsync(heicSample, target, CategoryKind.Image);
        if (Produced(outcome)) Pass($"HEIC -> {target.ToUpperInvariant()}", SizeOf(outcome));
        else Fail($"HEIC -> {target.ToUpperInvariant()}", outcome.Message);
    }
}

// =====================================================================
Console.WriteLine();
Console.WriteLine("3. 做不到的事，提示必须说清楚");

var heicTarget = await RunAsync(seedPng, "heic", CategoryKind.Image);
if (!heicTarget.Success && heicTarget.Message.Contains("HEIC"))
    Pass("PNG -> HEIC 的拒绝提示", heicTarget.Message);
else
    Fail("PNG -> HEIC 的拒绝提示", heicTarget.Success ? "居然成功了？" : heicTarget.Message);

var brokenFile = Path.Combine(root, "假装是图片.png");
await File.WriteAllTextAsync(brokenFile, "这根本不是图片");
var brokenOutcome = await RunAsync(brokenFile, "jpg", CategoryKind.Image);
if (!brokenOutcome.Success && brokenOutcome.Message.Contains("换一个"))
    Pass("坏文件的提示", brokenOutcome.Message);
else
    Fail("坏文件的提示", brokenOutcome.Success ? "居然成功了？" : brokenOutcome.Message);

// =====================================================================
Console.WriteLine();
Console.WriteLine("4. 音视频转换");

if (!Has(ToolKind.Ffmpeg))
{
    Console.WriteLine("  （本机没有 FFmpeg，改成验证「缺组件」的提示）");

    var placeholder = Path.Combine(root, "占位.mp3");
    await File.WriteAllBytesAsync(placeholder, new byte[64]);
    var outcome = await RunAsync(placeholder, "mp3", CategoryKind.Audio);
    if (!outcome.Success && outcome.Message.Contains("组件")) Pass("音频（缺组件）", outcome.Message);
    else Fail("音频（缺组件）", outcome.Success ? "居然成功了？" : outcome.Message);
}
else
{
    var ffmpeg = ToolLocator.Require(ToolKind.Ffmpeg);

    // 用 lavfi 合成测试素材，不需要外部样本文件
    var wav = Path.Combine(root, "测试声音.wav");
    await ProcessRunner.RunAsync(ffmpeg,
        ["-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
         "-c:a", "pcm_s16le", wav]);
    if (File.Exists(wav)) Pass("合成测试音频", $"{new FileInfo(wav).Length} 字节");
    else Fail("合成测试音频", "ffmpeg 没生成 WAV，后面的音视频测试没法做");

    foreach (var target in new[] { "mp3", "flac", "m4a" })
    {
        var outcome = await RunAsync(wav, target, CategoryKind.Audio);
        if (Produced(outcome)) Pass($"WAV -> {target.ToUpperInvariant()}", SizeOf(outcome));
        else Fail($"WAV -> {target.ToUpperInvariant()}", outcome.Message);
    }

    var mp4 = Path.Combine(root, "测试视频.mp4");
    await ProcessRunner.RunAsync(ffmpeg,
        ["-y", "-hide_banner", "-loglevel", "error",
         "-f", "lavfi", "-i", "testsrc=size=320x240:rate=15:duration=2",
         "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
         "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
         "-c:a", "aac", "-shortest", mp4]);
    if (File.Exists(mp4)) Pass("合成测试视频", $"{new FileInfo(mp4).Length} 字节");
    else Fail("合成测试视频", "ffmpeg 没生成 MP4，后面的视频测试没法做");

    // 视频转码 + 顺带验证进度解析（这一段的解析逻辑最容易出错）
    foreach (var target in new[] { "mkv", "avi", "mov" })
    {
        var job = new ConversionJob(mp4, outputDirectory, target, CategoryKind.Video);
        var reporter = new InlineProgress<ProgressInfo>();
        var outcome = await ConversionService.ConvertAsync(job, reporter, CancellationToken.None);

        if (Produced(outcome))
        {
            var fractions = reporter.Values.Where(v => !v.IsIndeterminate).Select(v => v.Fraction).ToList();
            var progressNote = fractions.Count > 0
                ? $"，进度上报 {fractions.Count} 次（最高 {fractions.Max():P0}）"
                : "，但一次百分比进度都没收到";
            Pass($"MP4 -> {target.ToUpperInvariant()}", SizeOf(outcome) + progressNote);
            if (fractions.Count == 0) Fail("FFmpeg 进度解析", "总时长为 2 秒却收不到任何百分比");
            else Pass("FFmpeg 进度解析", $"收到 {fractions.Count} 次，最高 {fractions.Max():P0}，末次 {fractions[fractions.Count - 1]:P0}");
        }
        else
        {
            Fail($"MP4 -> {target.ToUpperInvariant()}", outcome.Message);
        }
    }

    // 提取声音
    var audioOnly = await RunAsync(mp4, "mp3", CategoryKind.Video, audioOnly: true);
    if (Produced(audioOnly)) Pass("MP4 -> 只要声音（MP3）", SizeOf(audioOnly));
    else Fail("MP4 -> 只要声音（MP3）", audioOnly.Message);
}

// =====================================================================
Console.WriteLine();
Console.WriteLine("5. PDF 工具");

var pdfSample = FindAsset(Path.Combine("tests", "assets", "dummy.pdf"));

if (!Has(ToolKind.PdfToPpm))
{
    Console.WriteLine("  （本机没有 Poppler，改成验证「缺组件」的提示）");

    var placeholder = Path.Combine(root, "占位.pdf");
    await File.WriteAllBytesAsync(placeholder, new byte[64]);
    var outcome = await RunAsync(placeholder, "png", CategoryKind.Pdf);
    if (!outcome.Success && outcome.Message.Contains("组件")) Pass("PDF 转图片（缺组件）", outcome.Message);
    else Fail("PDF 转图片（缺组件）", outcome.Success ? "居然成功了？" : outcome.Message);
}
else if (pdfSample is null)
{
    Skip("PDF 工具", "没找到 tests/assets/dummy.pdf");
}
else
{
    var pageImages = await RunAsync(pdfSample, "png", CategoryKind.Pdf);
    if (Produced(pageImages))
        Pass("PDF -> PNG", $"{pageImages.OutputPaths.Count} 张图，第一张 {new FileInfo(pageImages.OutputPaths[0]).Length} 字节");
    else
        Fail("PDF -> PNG", pageImages.Message);

    var text = await RunAsync(pdfSample, "txt", CategoryKind.Pdf);
    if (Produced(text))
    {
        var content = await File.ReadAllTextAsync(text.OutputPaths[0]);
        var looksRight = content.Contains("Dummy", StringComparison.OrdinalIgnoreCase);
        Pass("PDF -> TXT", $"{content.Length} 个字符，内容{(looksRight ? "正确" : "可疑")}");
        if (!looksRight) Fail("PDF -> TXT 内容校验", $"期望包含 Dummy，实际开头是：{content.Substring(0, Math.Min(60, content.Length))}");
    }
    else
    {
        Fail("PDF -> TXT", text.Message);
    }
}

// =====================================================================
Console.WriteLine();
Console.WriteLine("6. 文档转 PDF");

if (!Has(ToolKind.Soffice))
{
    Console.WriteLine("  （本机没有 LibreOffice，改成验证「缺组件」的提示）");

    var placeholder = Path.Combine(root, "占位.docx");
    await File.WriteAllBytesAsync(placeholder, new byte[64]);
    var outcome = await RunAsync(placeholder, "pdf", CategoryKind.Document);
    if (!outcome.Success && outcome.Message.Contains("组件")) Pass("文档转 PDF（缺组件）", outcome.Message);
    else Fail("文档转 PDF（缺组件）", outcome.Success ? "居然成功了？" : outcome.Message);
}
else
{
    var plainText = Path.Combine(root, "测试文档.txt");
    await File.WriteAllTextAsync(plainText, "电脑工具百宝箱\n这是一份用来测试的文档。\n", Encoding.UTF8);

    var outcome = await RunAsync(plainText, "pdf", CategoryKind.Document);
    if (Produced(outcome)) Pass("TXT -> PDF", SizeOf(outcome));
    else Fail("TXT -> PDF", outcome.Message);
}

// =====================================================================
Console.WriteLine();
Console.WriteLine(new string('-', 64));
Console.WriteLine($"结果：通过 {passed} 项，失败 {failed} 项，跳过 {skipped} 项。");

try { Directory.Delete(root, recursive: true); } catch { /* 留着也不碍事 */ }

return failed == 0 ? 0 : 1;

/// <summary>
/// 同步上报的进度接收器。
/// 控制台程序里没有 SynchronizationContext，用 <see cref="Progress{T}"/> 的话
/// 回调会跑到线程池上，await 返回时数据还没写进列表，测试就会误判。
/// </summary>
internal sealed class InlineProgress<T> : IProgress<T>
{
    public List<T> Values { get; } = [];

    public void Report(T value) => Values.Add(value);
}
