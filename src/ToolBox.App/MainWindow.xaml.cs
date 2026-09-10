using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ToolBox.Models;
using ToolBox.ViewModels;

namespace ToolBox;

/// <summary>
/// 界面层只做三件事：接按钮点击、弹选文件对话框、把结果转交给 ViewModel。
/// 转换逻辑一行都不在这，界面换掉也不影响功能。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CategoryInfo category }) _viewModel.OpenCategory(category);
    }

    private void GoHome_Click(object sender, RoutedEventArgs e) => _viewModel.GoHome();

    private void PickFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentCategory is not { } category) return;

        var dialog = new OpenFileDialog
        {
            Title = "选要转换的文件（按住 Ctrl 可以一次选好几个）",
            Multiselect = true,
            Filter = category.FileFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true) _viewModel.AddFiles(dialog.FileNames);
    }

    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FileItem item }) _viewModel.RemoveFile(item);
    }

    private void Target_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TargetItem item }) _viewModel.SelectTarget(item);
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await _viewModel.StartAsync();

    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.Cancel();

    private void ConvertMore_Click(object sender, RoutedEventArgs e) => _viewModel.ConvertMore();

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => _viewModel.OpenOutputFolder();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            _viewModel.AcceptDroppedFiles(paths);

        e.Handled = true;
    }
}
