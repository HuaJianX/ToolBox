using ToolBox.Models;

namespace ToolBox.ViewModels;

/// <summary>“要转成什么”按钮背后的一项。带选中状态，界面上高亮那个绿的。</summary>
public sealed class TargetItem : ObservableObject
{
    private bool _isSelected;

    public TargetItem(FormatOption option) => Option = option;

    public FormatOption Option { get; }

    public string Label => Option.Label;

    public bool IsRecommended => Option.IsRecommended;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>文件列表里的一行。界面显示文件名和大小，完整路径放提示里。</summary>
public sealed class FileItem
{
    public FileItem(string path)
    {
        Path = path;

        var info = new FileInfo(path);
        Size = info.Exists ? info.Length : 0;
        Name = System.IO.Path.GetFileName(path);
        Folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
    }

    public string Path { get; }

    public string Name { get; }

    public string Folder { get; }

    public long Size { get; }

    public string SizeText => Services.FileUtil.DescribeSize(Size);
}
