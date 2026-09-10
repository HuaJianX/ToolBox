using ToolBox.Models;

namespace ToolBox.Services;

/// <summary>
/// 全部格式定义都集中在这一个文件里。想加新格式，只改这里，界面会自动多出按钮。
/// </summary>
public static class FormatCatalog
{
    public static IReadOnlyList<CategoryInfo> All { get; } =
    [
        new CategoryInfo
        {
            Kind = CategoryKind.Image,
            Icon = "📷",
            Title = "图片转换",
            Subtitle = "JPG / PNG / WEBP / BMP / HEIC 互相转",
            AccentColor = "#1565C0",
            SourceExtensions = ["jpg", "jpeg", "png", "webp", "bmp", "heic", "heif", "gif", "tif", "tiff"],
            Targets =
            [
                new FormatOption { Extension = "jpg", Label = "JPG 图片", IsRecommended = true },
                new FormatOption { Extension = "png", Label = "PNG 图片" },
                new FormatOption { Extension = "webp", Label = "WEBP 图片" },
                new FormatOption { Extension = "bmp", Label = "BMP 图片" },
                // 这里故意没有 HEIC：实测 Magick.NET 自带的 libheif 只有解码器，没有编码器
                // （HEIC 编码依赖 x265，因专利原因不随包分发）。与其放一个点了就报错的按钮，
                // 不如不放。HEIC 作为“来源格式”是完全支持的，iPhone 照片转 JPG 没问题。
            ],
        },

        new CategoryInfo
        {
            Kind = CategoryKind.Document,
            Icon = "📄",
            Title = "文档转换",
            Subtitle = "Word / Excel / PPT / TXT / MD 转成 PDF",
            AccentColor = "#2E7D32",
            SourceExtensions = ["doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "md", "markdown", "rtf", "csv", "odt", "ods", "odp"],
            Targets =
            [
                new FormatOption { Extension = "pdf", Label = "PDF 文件", IsRecommended = true },
            ],
        },

        new CategoryInfo
        {
            Kind = CategoryKind.Pdf,
            Icon = "📕",
            Title = "PDF 工具",
            Subtitle = "PDF 转图片 / Word / 纯文本",
            AccentColor = "#C62828",
            SourceExtensions = ["pdf"],
            Targets =
            [
                new FormatOption { Extension = "png", Label = "图片（PNG）", IsRecommended = true },
                new FormatOption { Extension = "jpg", Label = "图片（JPG）" },
                new FormatOption { Extension = "docx", Label = "Word 文档" },
                new FormatOption { Extension = "txt", Label = "纯文本" },
            ],
        },

        new CategoryInfo
        {
            Kind = CategoryKind.Audio,
            Icon = "🎵",
            Title = "音频转换",
            Subtitle = "MP3 / WAV / FLAC / M4A 互相转",
            AccentColor = "#6A1B9A",
            SourceExtensions = ["mp3", "wav", "flac", "m4a", "aac", "ogg", "wma", "opus"],
            Targets =
            [
                new FormatOption { Extension = "mp3", Label = "MP3 音乐", IsRecommended = true },
                new FormatOption { Extension = "wav", Label = "WAV 无损" },
                new FormatOption { Extension = "flac", Label = "FLAC 无损" },
                new FormatOption { Extension = "m4a", Label = "M4A 音乐" },
            ],
        },

        new CategoryInfo
        {
            Kind = CategoryKind.Video,
            Icon = "🎬",
            Title = "视频转换",
            Subtitle = "MP4 / MKV / AVI / MOV 互相转，也能只要声音",
            AccentColor = "#EF6C00",
            SourceExtensions = ["mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "ts"],
            Targets =
            [
                new FormatOption { Extension = "mp4", Label = "MP4 视频", IsRecommended = true },
                new FormatOption { Extension = "mkv", Label = "MKV 视频" },
                new FormatOption { Extension = "mov", Label = "MOV 视频" },
                new FormatOption { Extension = "avi", Label = "AVI 视频" },
                new FormatOption { Extension = "mp3", Label = "只要声音（MP3）", AudioOnly = true },
            ],
        },
    ];

    /// <summary>按文件后缀猜它属于哪一类。首页直接拖文件进来时用得上。</summary>
    public static CategoryInfo? Detect(string? pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension)) return null;
        var extension = Path.GetExtension(pathOrExtension);
        if (string.IsNullOrEmpty(extension)) extension = pathOrExtension;
        var normalized = extension.TrimStart('.').ToLowerInvariant();
        return All.FirstOrDefault(category => category.Accepts(normalized));
    }
}
