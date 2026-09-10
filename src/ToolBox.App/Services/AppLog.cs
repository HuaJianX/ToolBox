using System.Text;

namespace ToolBox.Services;

/// <summary>
/// 极简日志。只写本地文件，不联网（要求里写了不上传任何东西）。
/// 日志本身写失败也绝不能影响转换，所以全都吞掉异常。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(Exception exception) => Write(exception.ToString());

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(AppPaths.LogFile);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                // 日志超过 1MB 就重新开始，免得无限长大。
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > 1024 * 1024) file.Delete();

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(AppPaths.LogFile, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写不进去就算了。
        }
    }
}
