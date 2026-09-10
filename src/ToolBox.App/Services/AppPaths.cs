namespace ToolBox.Services;

/// <summary>程序用到的固定路径。</summary>
public static class AppPaths
{
    /// <summary>默认输出文件夹的名字（放在用户主目录下，老人也找得到）。</summary>
    public const string OutputFolderName = "转换结果";

    public const string AppFolderName = "电脑工具百宝箱";

    /// <summary>转好的文件默认都放这里，绝不覆盖原文件。</summary>
    public static string OutputRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), OutputFolderName);

    /// <summary>免安装的第三方转换组件目录（跟 exe 放一起，所以拷到哪都能用）。</summary>
    public static string ToolsRoot => Path.Combine(AppContext.BaseDirectory, "tools");

    /// <summary>出错时写的日志，排错用。用户平时不用管它。</summary>
    public static string LogFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName,
            "运行日志.txt");
}
