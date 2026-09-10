using System.Text;
using ToolBox.Models;
using ToolBox.Services;
using ToolBox.ViewModels;

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

// 可选：命令行给一个已存在的目录，就把它当输出目录（方便人工检查产出长什么样）。
// 不给就用临时目录，跑完自己删干净。
var explicitOutput = args.Length > 0 && Directory.Exists(args[0]) ? args[0] : null;

var root = explicitOutput ?? Path.Combine(Path.GetTempPath(), "toolbox-smoke-" + Guid.NewGuid().ToString("N")[..6]);
var outputDirectory = explicitOutput is null ? Path.Combine(root, "转换结果") : explicitOutput;
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

    var audioOutputs = new Dictionary<string, string>();
    foreach (var target in new[] { "mp3", "flac", "m4a" })
    {
        var outcome = await RunAsync(wav, target, CategoryKind.Audio);
        if (Produced(outcome))
        {
            Pass($"WAV -> {target.ToUpperInvariant()}", SizeOf(outcome));
            audioOutputs[target] = outcome.OutputPaths[0];
        }
        else
        {
            Fail($"WAV -> {target.ToUpperInvariant()}", outcome.Message);
        }
    }

    // 反向：之前一直只从 WAV 往外转，从没验过「已经从 WAV 转出来的文件再转回去」
    foreach (var (from, to) in new[] { ("mp3", "wav"), ("mp3", "flac"), ("flac", "mp3"), ("m4a", "wav") })
    {
        if (!audioOutputs.TryGetValue(from, out var reverseSource))
        {
            Skip($"{from.ToUpperInvariant()} -> {to.ToUpperInvariant()}", "上游那个转换没成功");
            continue;
        }

        var outcome = await RunAsync(reverseSource, to, CategoryKind.Audio);
        if (Produced(outcome)) Pass($"{from.ToUpperInvariant()} -> {to.ToUpperInvariant()}", SizeOf(outcome));
        else Fail($"{from.ToUpperInvariant()} -> {to.ToUpperInvariant()}", outcome.Message);
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
    var videoOutputs = new Dictionary<string, string>();
    foreach (var target in new[] { "mkv", "avi", "mov" })
    {
        var job = new ConversionJob(mp4, outputDirectory, target, CategoryKind.Video);
        var reporter = new InlineProgress<ProgressInfo>();
        var outcome = await ConversionService.ConvertAsync(job, reporter, CancellationToken.None);

        if (Produced(outcome))
        {
            videoOutputs[target] = outcome.OutputPaths[0];
            var fractions = reporter.Values.Where(v => !v.IsIndeterminate).Select(v => v.Fraction).ToList();
            var progressNote = fractions.Count > 0
                ? $"，进度上报 {fractions.Count} 次（最高 {fractions.Max():P0}）"
                : "，但一次百分比进度都没收到";
            Pass($"MP4 -> {target.ToUpperInvariant()}", SizeOf(outcome) + progressNote);
            if (fractions.Count == 0)
                Fail("FFmpeg 进度解析", Has(ToolKind.Ffprobe)
                    ? "总时长为 2 秒却收不到任何百分比"
                    : "收不到百分比 —— 说明「没有 ffprobe 时改用 ffmpeg 读时长」的兜底没生效");
            else
                Pass("FFmpeg 进度解析", $"收到 {fractions.Count} 次，最高 {fractions.Max():P0}，末次 {fractions[fractions.Count - 1]:P0}" +
                     (Has(ToolKind.Ffprobe) ? "（走 ffprobe）" : "（本机没 ffprobe，走的是 ffmpeg 读时长兜底）"));
        }
        else
        {
            Fail($"MP4 -> {target.ToUpperInvariant()}", outcome.Message);
        }
    }

    // 反向：之前源文件一直是 MP4，从没试过拿 MKV / AVI 当输入
    foreach (var (from, to) in new[] { ("mkv", "mp4"), ("avi", "mkv"), ("mov", "avi"), ("mkv", "mov") })
    {
        if (!videoOutputs.TryGetValue(from, out var reverseSource))
        {
            Skip($"{from.ToUpperInvariant()} -> {to.ToUpperInvariant()}", "上游那个转换没成功");
            continue;
        }

        var outcome = await RunAsync(reverseSource, to, CategoryKind.Video);
        if (Produced(outcome)) Pass($"{from.ToUpperInvariant()} -> {to.ToUpperInvariant()}", SizeOf(outcome));
        else Fail($"{from.ToUpperInvariant()} -> {to.ToUpperInvariant()}", outcome.Message);
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

// PDF 转图片和 PDF 转文字用的是两套独立机制，分开验证，谁也不拖累谁。
Console.WriteLine($"  （转图片引擎：{ConversionService.PdfImageEngineName}）");

if (!ConversionService.CanRenderPdfToImages)
{
    var placeholder = Path.Combine(root, "占位.pdf");
    await File.WriteAllBytesAsync(placeholder, new byte[64]);
    var outcome = await RunAsync(placeholder, "png", CategoryKind.Pdf);
    if (!outcome.Success && outcome.Message.Contains("组件")) Pass("PDF 转图片（本机没引擎，验证提示）", outcome.Message);
    else Fail("PDF 转图片（没引擎的提示）", outcome.Success ? "居然成功了？" : outcome.Message);
}
else if (pdfSample is null)
{
    Skip("PDF -> 图片", "没找到 tests/assets/dummy.pdf");
}
else
{
    var pageImages = await RunAsync(pdfSample, "png", CategoryKind.Pdf);
    if (Produced(pageImages))
        Pass("PDF -> PNG", $"{pageImages.OutputPaths.Count} 张图，第一张 {new FileInfo(pageImages.OutputPaths[0]).Length} 字节");
    else
        Fail("PDF -> PNG", pageImages.Message);

    // 多页 PDF：确认「一页一张图」，而且页码顺序是对的（不是 1、10、2 那种）
    // 注意：这里必须用 Find 而不是 Require。
    // CI 机器上没装 poppler，用 Require 会直接抛异常把整个自检打崩
    // （本机装了 poppler 反而测不出来 —— 是 CI 抓到的这个问题）。
    var pdfToPpmPath = ToolLocator.Find(ToolKind.PdfToPpm);
    var pdfUnite = pdfToPpmPath is null
        ? null
        : Path.Combine(Path.GetDirectoryName(pdfToPpmPath)!, "pdfunite.exe");

    if (pdfUnite is null || !File.Exists(pdfUnite))
    {
        Skip("多页 PDF -> 图片", "本机没有 poppler 的 pdfunite，跳过");
    }
    else
    {
        var threePage = Path.Combine(root, "三页.pdf");
        await ProcessRunner.RunAsync(pdfUnite, [pdfSample, pdfSample, pdfSample, threePage]);

        if (!File.Exists(threePage))
        {
            Skip("多页 PDF -> 图片", "pdfunite 没能拼出多页 PDF");
        }
        else
        {
            var multiPage = await RunAsync(threePage, "png", CategoryKind.Pdf);
            if (Produced(multiPage) && multiPage.OutputPaths.Count == 3)
                Pass("多页 PDF -> 图片", $"3 页出 3 张图：{string.Join("、", multiPage.OutputPaths.Select(Path.GetFileName))}");
            else
                Fail("多页 PDF -> 图片", $"期望 3 张，实际 {multiPage.OutputPaths.Count} 张。{multiPage.Message}");
        }
    }
}

if (!ConversionService.CanExtractPdfText)
{
    var placeholder = Path.Combine(root, "占位2.pdf");
    await File.WriteAllBytesAsync(placeholder, new byte[64]);
    var outcome = await RunAsync(placeholder, "txt", CategoryKind.Pdf);
    if (!outcome.Success && outcome.Message.Contains("组件")) Pass("PDF 转文字（本机没组件，验证提示）", outcome.Message);
    else Fail("PDF 转文字（缺组件的提示）", outcome.Success ? "居然成功了？" : outcome.Message);
}
else if (pdfSample is null)
{
    Skip("PDF -> 文字", "没找到 tests/assets/dummy.pdf");
}
else
{
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

// PDF 转 Word 只有装了 LibreOffice 才做得到，这里验证「做不到时提示是人话」。
if (pdfSample is not null)
{
    var toWord = await RunAsync(pdfSample, "docx", CategoryKind.Pdf);
    if (Has(ToolKind.Soffice))
    {
        if (Produced(toWord)) Pass("PDF -> Word", SizeOf(toWord));
        else Fail("PDF -> Word", toWord.Message);
    }
    else if (!toWord.Success && toWord.Message.Contains("LibreOffice"))
    {
        Pass("PDF 转 Word（缺 LibreOffice 的提示）", toWord.Message);
    }
    else
    {
        Fail("PDF 转 Word（缺 LibreOffice 的提示）", toWord.Success ? "居然成功了？" : toWord.Message);
    }
}

// =====================================================================
Console.WriteLine();
Console.WriteLine("6. 文档转 PDF");
Console.WriteLine($"  （本机实际使用的引擎：{ConversionService.DocumentEngineName}）");

if (!ConversionService.CanConvertDocuments)
{
    var placeholder = Path.Combine(root, "占位.docx");
    await File.WriteAllBytesAsync(placeholder, new byte[64]);
    var outcome = await RunAsync(placeholder, "pdf", CategoryKind.Document);
    if (!outcome.Success && outcome.Message.Contains("文档")) Pass("文档转 PDF（没引擎，验证提示）", outcome.Message);
    else Fail("文档转 PDF（没引擎的提示）", outcome.Success ? "居然成功了？" : outcome.Message);
}
else
{
    var plainText = Path.Combine(root, "测试文档.txt");
    await File.WriteAllTextAsync(plainText, "电脑工具百宝箱\n这是一份用来测试的文档。\n", Encoding.UTF8);

    var outcome = await RunAsync(plainText, "pdf", CategoryKind.Document);
    if (Produced(outcome)) Pass("TXT -> PDF", SizeOf(outcome));
    else Fail("TXT -> PDF", outcome.Message);

    // 三种真正的 Office 格式。之前只验过 TXT，docx/xlsx/pptx 一次都没跑过。
    foreach (var (fixtureName, label) in new[]
             {
                 ("sample-word.docx", "Word"),
                 ("sample-excel.xlsx", "Excel"),
                 ("sample-slides.pptx", "PPT"),
             })
    {
        var fixture = FindAsset(Path.Combine("tests", "assets", fixtureName));
        if (fixture is null)
        {
            Skip($"{label} -> PDF", $"没找到 tests/assets/{fixtureName}");
            continue;
        }

        var doc = await RunAsync(fixture, "pdf", CategoryKind.Document);
        if (Produced(doc)) Pass($"{label} -> PDF", SizeOf(doc));
        else Fail($"{label} -> PDF", doc.Message);
    }

    // Markdown -> PDF 的完整链路：Pandoc 排版成 docx，再交给 LibreOffice 出 PDF
    var markdown = Path.Combine(root, "笔记.md");
    await File.WriteAllTextAsync(markdown, "# 标题\n\n这是**正文**内容。\n\n- 项目一\n- 项目二\n", new UTF8Encoding(false));

    var markdownOutcome = await RunAsync(markdown, "pdf", CategoryKind.Document);
    if (Produced(markdownOutcome)) Pass("Markdown -> PDF", SizeOf(markdownOutcome));
    else Fail("Markdown -> PDF", markdownOutcome.Message);
}

// =====================================================================
Console.WriteLine();
Console.WriteLine("7. 批量 + 界面那套流程（直接驱动 MainViewModel）");

var batchFolder = Path.Combine(root, "批量素材");
Directory.CreateDirectory(batchFolder);
for (var index = 1; index <= 3; index++)
{
    using var image = new ImageMagick.MagickImage(
        ImageMagick.MagickColors.CornflowerBlue, (uint)(40 + (index * 10)), 30u);
    image.Format = ImageMagick.MagickFormat.Png;
    image.Write(Path.Combine(batchFolder, $"批量图{index}.png"));
}

var viewModel = new MainViewModel();
viewModel.OpenCategory(viewModel.Categories.First(category => category.Kind == CategoryKind.Image));

if (viewModel.IsConvert && !viewModel.IsHome) Pass("点大按钮进入转换页", "IsConvert=true");
else Fail("点大按钮进入转换页", $"IsHome={viewModel.IsHome}");

viewModel.AddFiles(Directory.GetFiles(batchFolder));
if (viewModel.Files.Count == 3) Pass("选文件", $"列表里 3 个");
else Fail("选文件", $"期望 3 个，实际 {viewModel.Files.Count}");

var preselected = viewModel.Targets.FirstOrDefault(item => item.IsSelected);
if (preselected is not null && preselected.Option.Extension != "png")
    Pass("默认格式自动推荐", $"自动选中「{preselected.Option.Label}」（没推荐和源文件一样的格式）");
else
    Fail("默认格式自动推荐", preselected is null ? "一个都没选中" : $"选中了「{preselected.Option.Label}」，和源格式一样，不合理");

if (viewModel.CanStart) Pass("可以开始", "CanStart=true");
else Fail("可以开始", "CanStart=false，按钮会是灰的");

await viewModel.StartAsync();

if (viewModel.IsFinished && viewModel.StatusText == "转换好了")
    Pass("批量跑完的提示", $"StatusText=「{viewModel.StatusText}」，{viewModel.FinishedText.Replace("\n", " ")}");
else
    Fail("批量跑完的提示", $"IsFinished={viewModel.IsFinished} StatusText=「{viewModel.StatusText}」");

// 清理批量测试写进真实输出目录的文件
foreach (var leftover in Directory.GetFiles(AppPaths.OutputRoot, "批量图*"))
    FileUtil.TryDeleteFile(leftover);

// =====================================================================
Console.WriteLine();
Console.WriteLine("8. 取消（要真的把子进程杀掉，并且不留半个坏文件）");

if (!Has(ToolKind.Ffmpeg))
{
    Skip("取消", "没有 FFmpeg，没法造长视频");
}
else
{
    var ffmpegPath = ToolLocator.Require(ToolKind.Ffmpeg);
    var longVideo = Path.Combine(root, "长视频.mp4");

    // 这台机器 32 个逻辑核心，转码快得离谱：720p/15 秒只要 0.5 秒，1080p/30 秒也只要 1.8 秒。
    // 所以素材得做够大（1080p / 45 秒），而且取消要按得早（400 毫秒），才来得及打断。
    await ProcessRunner.RunAsync(ffmpegPath,
    [
        "-y", "-hide_banner", "-loglevel", "error",
        "-f", "lavfi", "-i", "testsrc=size=1920x1080:rate=30:duration=45",
        "-f", "lavfi", "-i", "sine=frequency=440:duration=45",
        "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-shortest", longVideo
    ]);

    if (!File.Exists(longVideo))
    {
        Skip("取消", "长视频没造出来");
    }
    else
    {
        var cancelViewModel = new MainViewModel();
        cancelViewModel.OpenCategory(cancelViewModel.Categories.First(c => c.Kind == CategoryKind.Video));
        cancelViewModel.AddFiles([longVideo]);
        cancelViewModel.SelectTarget(cancelViewModel.Targets.First(t => t.Option.Extension == "mkv"));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var running = cancelViewModel.StartAsync();
        await Task.Delay(400);
        cancelViewModel.Cancel();
        await running;
        stopwatch.Stop();

        if (cancelViewModel.StatusText.Contains("取消"))
            Pass("取消能停下来", $"{stopwatch.Elapsed.TotalSeconds:N1} 秒内响应，提示「{cancelViewModel.StatusText}」");
        else
            Fail("取消能停下来", $"提示是「{cancelViewModel.StatusText}」");

        // 取消后不该在「转换结果」里留下转了一半的视频
        var half = Path.Combine(AppPaths.OutputRoot, "长视频.mkv");
        if (File.Exists(half))
        {
            Fail("取消不留半个坏文件", $"{half} 还在（{new FileInfo(half).Length} 字节）");
            FileUtil.TryDeleteFile(half);
        }
        else
        {
            Pass("取消不留半个坏文件", "输出目录里没有残留");
        }

        // 取消后要能接着转下一批
        if (cancelViewModel.IsReadyToStart || cancelViewModel.IsFinished)
            Pass("取消后状态正常", "还能继续用，不用重启");
        else
            Fail("取消后状态正常", $"IsConverting={cancelViewModel.IsConverting}");
    }
}

// =====================================================================
Console.WriteLine();
Console.WriteLine(new string('-', 64));
Console.WriteLine($"结果：通过 {passed} 项，失败 {failed} 项，跳过 {skipped} 项。");

if (explicitOutput is null)
{
    try { Directory.Delete(root, recursive: true); } catch { /* 留着也不碍事 */ }
}
else
{
    Console.WriteLine($"（输出目录是你指定的，没有删：{explicitOutput}）");
}

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
