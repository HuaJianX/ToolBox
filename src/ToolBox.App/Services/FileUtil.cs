namespace ToolBox.Services;

/// <summary>文件相关的小工具。</summary>
public static class FileUtil
{
    /// <summary>
    /// 生成一个不会覆盖已有文件的路径：<c>照片.jpg</c> → <c>照片 (2).jpg</c>。
    /// 要求里明确说了“不覆盖原文件”，这里是最后一道保险。
    /// </summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, $"{name} ({Guid.NewGuid():N}){extension}");
    }

    /// <summary>生成一个不会和已有文件夹重名的目录路径。</summary>
    public static string UniqueDirectory(string path)
    {
        if (!Directory.Exists(path)) return path;

        var parent = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileName(path);

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = Path.Combine(parent, $"{name} ({index})");
            if (!Directory.Exists(candidate)) return candidate;
        }

        return Path.Combine(parent, $"{name} ({Guid.NewGuid():N})");
    }

    /// <summary>建临时工作目录，用完就删。</summary>
    public static string CreateTempDirectory(string tag)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            AppPaths.AppFolderName,
            tag + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // 临时文件删不掉不是错误，下次开机系统会清。
        }
    }

    /// <summary>把字节数说成人话，用在界面提示里。</summary>
    public static string DescribeSize(long bytes)
    {
        string[] units = ["字节", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[0]}" : $"{size:0.#} {units[unit]}";
    }
}
