namespace ToolBox.Models;

/// <summary>五类转换，对应首页上的五个大按钮。</summary>
public enum CategoryKind
{
    Image,
    Document,
    Pdf,
    Audio,
    Video,
}

/// <summary>一种可选的“转成什么”，界面上就是一个小一点的按钮。</summary>
public sealed class FormatOption
{
    /// <summary>目标扩展名，不含点，小写。</summary>
    public required string Extension { get; init; }

    /// <summary>给用户看的大白话名字。</summary>
    public required string Label { get; init; }

    /// <summary>自动推荐给普通用户的默认格式（带“推荐”角标）。</summary>
    public bool IsRecommended { get; init; }

    /// <summary>视频只取声音时用，界面上显示成“只要声音”。</summary>
    public bool AudioOnly { get; init; }
}

/// <summary>首页上的一个大按钮：一类转换。</summary>
public sealed class CategoryInfo
{
    public required CategoryKind Kind { get; init; }

    /// <summary>按钮上的图标（emoji）。</summary>
    public required string Icon { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    /// <summary>这一类的主题色，让五个按钮一眼能分开。</summary>
    public required string AccentColor { get; init; }

    /// <summary>这一类能读进来的文件后缀，不含点，小写。</summary>
    public required IReadOnlyList<string> SourceExtensions { get; init; }

    /// <summary>可以转成哪些格式。</summary>
    public required IReadOnlyList<FormatOption> Targets { get; init; }

    /// <summary>“选文件”对话框用的过滤器。</summary>
    public string FileFilter
    {
        get
        {
            var patterns = string.Join(";", SourceExtensions.Select(extension => "*." + extension));
            return $"{Title}能处理的文件|{patterns}|所有文件|*.*";
        }
    }

    public bool Accepts(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return false;
        var normalized = extension.TrimStart('.').ToLowerInvariant();
        return SourceExtensions.Contains(normalized);
    }

    /// <summary>
    /// 挑一个最合适的默认格式。原则：不要推荐跟原文件一样的格式，免得用户白转一趟。
    /// </summary>
    public FormatOption? DefaultTargetFor(string? sourceExtension)
    {
        var source = sourceExtension?.TrimStart('.').ToLowerInvariant();

        var recommended = Targets.FirstOrDefault(
            target => target.IsRecommended &&
                      !string.Equals(target.Extension, source, StringComparison.OrdinalIgnoreCase));
        if (recommended is not null) return recommended;

        var different = Targets.FirstOrDefault(
            target => !string.Equals(target.Extension, source, StringComparison.OrdinalIgnoreCase));
        return different ?? Targets.FirstOrDefault();
    }
}

/// <summary>一次转换任务：把 SourcePath 转成 TargetExtension，放到 OutputDirectory。</summary>
public sealed record ConversionJob(
    string SourcePath,
    string OutputDirectory,
    string TargetExtension,
    CategoryKind Category,
    bool AudioOnly = false);

/// <summary>转换进度。Fraction 小于 0 表示“说不准还要多久”，界面显示成来回滚动的进度条。</summary>
public sealed record ProgressInfo(double Fraction, string Message)
{
    public bool IsIndeterminate => Fraction < 0;

    public static ProgressInfo Busy(string message) => new(-1, message);
}

/// <summary>一个文件的转换结果。</summary>
public sealed record ConversionOutcome(bool Success, string Message, IReadOnlyList<string> OutputPaths)
{
    public static ConversionOutcome Ok(params string[] outputPaths) =>
        new(true, "转换好了", outputPaths);

    public static ConversionOutcome Fail(string message) =>
        new(false, message, []);
}
