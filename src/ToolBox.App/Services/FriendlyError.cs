namespace ToolBox.Services;

/// <summary>找不到某个转换组件时抛这个，好让上层给出“怎么装”的提示。</summary>
public sealed class ToolMissingException(ToolKind kind) : Exception($"缺少转换组件：{ToolLocator.FriendlyName(kind)}")
{
    public ToolKind Kind { get; } = kind;
}

/// <summary>
/// 把各种技术性异常翻译成大白话。要求第 9 条：错误提示说人话。
/// </summary>
public static class FriendlyError
{
    // Windows 错误码（HResult 的低 16 位）
    private const int ErrorFileNotFound = 0x2;
    private const int ErrorPathNotFound = 0x3;
    private const int ErrorAccessDenied = 0x5;
    private const int ErrorSharingViolation = 0x20;
    private const int ErrorHandleDiskFull = 0x27;
    private const int ErrorDiskFull = 0x70;

    public static string Describe(Exception exception) => exception switch
    {
        ToolMissingException missing =>
            $"这台电脑还缺一个转换组件：{ToolLocator.FriendlyName(missing.Kind)}。" +
            $"把它放进程序目录的 tools 文件夹里就能用了。",

        FileNotFoundException or DirectoryNotFoundException =>
            "这个文件找不到了，可能被删掉或者改名了。",

        UnauthorizedAccessException =>
            "这个文件打不开，可能正被别的程序占用着。先把别的程序关掉再试。",

        IOException io when HasCode(io, ErrorDiskFull, ErrorHandleDiskFull) =>
            "磁盘空间不够了，清理一点空间再试。",

        IOException io when HasCode(io, ErrorSharingViolation, ErrorAccessDenied) =>
            "这个文件正被别的程序用着，先关掉它再试。",

        IOException io when HasCode(io, ErrorFileNotFound, ErrorPathNotFound) =>
            "这个文件找不到了，可能被删掉或者改名了。",

        IOException =>
            "读写文件的时候出错了，换个位置再试一次。",

        OutOfMemoryException =>
            "这个文件太大了，电脑内存不够。换个小一点的文件试试。",

        OperationCanceledException =>
            "已经取消了。",

        _ =>
            "这个文件打不开，换一个试试。",
    };

    /// <summary>把组件自己吐出来的英文错误也认一下“磁盘满了”。</summary>
    public static bool LooksLikeDiskFull(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains("No space left on device", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("disk full", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("not enough space", StringComparison.OrdinalIgnoreCase));

    /// <summary>退出码说明它读不懂这个文件。</summary>
    public static bool LooksLikeBadInput(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains("Invalid data found", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("could not open", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase));

    private static bool HasCode(IOException exception, params int[] codes)
    {
        var code = exception.HResult & 0xFFFF;
        return codes.Contains(code);
    }
}
