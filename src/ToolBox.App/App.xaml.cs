using System.Windows;
using ToolBox.Services;

namespace ToolBox;

/// <summary>
/// 程序入口。只做一件事：兜住所有没被处理的异常，给用户一句人话，而不是甩一个英文崩溃框。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Write("==== 程序启动 ====");
        AppLog.Write($"程序目录：{AppContext.BaseDirectory}");
        AppLog.Write($"输出目录：{AppPaths.OutputRoot}");
        AppLog.Write(ToolBox.Services.Converters.ImageConverter.DescribeCapabilities());
        AppLog.Write(ToolLocator.DescribeFoundTools());
        AppLog.Write($"文档转换引擎：{ConversionService.DocumentEngineName}；" +
                     $"PDF 转图片引擎：{ConversionService.PdfImageEngineName}");

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write(args.Exception);
            MessageBox.Show(
                FriendlyError.Describe(args.Exception),
                "出了点问题",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };
    }
}
