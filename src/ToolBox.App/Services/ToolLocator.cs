namespace ToolBox.Services;

/// <summary>程序需要的外部转换组件。</summary>
public enum ToolKind
{
    Ffmpeg,
    Ffprobe,
    Soffice,
    Pandoc,
    PdfToPpm,
    PdfToText,
}

/// <summary>
/// 到处找转换组件。搜索顺序（先找到先用）：
/// 1. 程序目录的 tools 子目录（免安装分发就靠这个）
/// 2. 从程序目录往上若干层里的 tools 目录（开发时用仓库根目录那份）
/// 3. 组件常见的安装位置（比如 LibreOffice 的默认装法）
/// 4. 系统 PATH
/// </summary>
public static class ToolLocator
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ToolKind, string?> Cache = [];

    /// <summary>给用户看的组件名字，出错提示里要用。</summary>
    public static string FriendlyName(ToolKind kind) => kind switch
    {
        ToolKind.Ffmpeg or ToolKind.Ffprobe => "FFmpeg（音视频转换）",
        ToolKind.Soffice => "LibreOffice（文档转 PDF）",
        ToolKind.Pandoc => "Pandoc（Markdown 排版）",
        ToolKind.PdfToPpm or ToolKind.PdfToText => "Poppler（PDF 工具）",
        _ => "转换组件",
    };

    /// <summary>找得到就返回完整路径，找不到返回 null。</summary>
    public static string? Find(ToolKind kind)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(kind, out var cached)) return cached;
            var located = Locate(kind);
            Cache[kind] = located;
            if (located is not null) AppLog.Write($"找到组件 {kind}：{located}");
            return located;
        }
    }

    /// <summary>必须找得到，否则抛 ToolMissingException（上层会翻译成人话）。</summary>
    public static string Require(ToolKind kind) =>
        Find(kind) ?? throw new ToolMissingException(kind);

    /// <summary>这一类功能现在能不能用（界面上拿来给按钮加提示）。</summary>
    public static bool IsAvailable(params ToolKind[] kinds) =>
        kinds.All(kind => Find(kind) is not null);

    private static string? Locate(ToolKind kind)
    {
        var fileNames = FileNamesFor(kind);
        if (fileNames.Length == 0) return null;

        foreach (var root in SearchRoots(kind))
        {
            foreach (var fileName in fileNames)
            {
                var candidate = Path.Combine(root, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }

        foreach (var fileName in fileNames)
        {
            var onPath = FindOnPath(fileName);
            if (onPath is not null) return onPath;
        }

        return null;
    }

    private static string[] FileNamesFor(ToolKind kind) => kind switch
    {
        ToolKind.Ffmpeg => ["ffmpeg.exe"],
        ToolKind.Ffprobe => ["ffprobe.exe"],
        ToolKind.Soffice => ["soffice.exe", "soffice.com"],
        ToolKind.Pandoc => ["pandoc.exe"],
        ToolKind.PdfToPpm => ["pdftoppm.exe"],
        ToolKind.PdfToText => ["pdftotext.exe"],
        _ => [],
    };

    private static IEnumerable<string> SearchRoots(ToolKind kind)
    {
        var baseDirectory = AppContext.BaseDirectory;

        // 1) 程序目录下的 tools（分发时的布局）
        yield return Path.Combine(baseDirectory, "tools");
        yield return Path.Combine(baseDirectory, "tools", "ffmpeg", "bin");
        yield return Path.Combine(baseDirectory, "tools", "poppler", "bin");
        yield return Path.Combine(baseDirectory, "tools", "poppler", "Library", "bin");
        yield return Path.Combine(baseDirectory, "tools", "LibreOffice", "program");
        yield return Path.Combine(baseDirectory, "tools", "pandoc");
        yield return baseDirectory;

        // 2) 往上找仓库根目录的 tools（开发时用）
        var directory = new DirectoryInfo(baseDirectory);
        for (var depth = 0; depth < 7 && directory is not null; depth++, directory = directory.Parent)
        {
            yield return Path.Combine(directory.FullName, "tools");
            yield return Path.Combine(directory.FullName, "tools", "ffmpeg", "bin");
            yield return Path.Combine(directory.FullName, "tools", "poppler", "Library", "bin");
            yield return Path.Combine(directory.FullName, "tools", "LibreOffice", "program");
        }

        // 3) 常见安装位置
        if (kind == ToolKind.Soffice)
        {
            yield return @"C:\Program Files\LibreOffice\program";
            yield return @"C:\Program Files (x86)\LibreOffice\program";
            yield return @"C:\Program Files\LibreOffice 7\program";
        }

        if (kind is ToolKind.Pandoc)
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pandoc");
            yield return @"C:\Program Files\Pandoc";
        }
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(entry, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // PATH 里有非法路径很正常，跳过。
            }
        }

        return null;
    }
}
