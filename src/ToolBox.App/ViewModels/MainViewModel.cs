using System.Collections.ObjectModel;
using System.Diagnostics;
using ToolBox.Models;
using ToolBox.Services;

namespace ToolBox.ViewModels;

/// <summary>
/// 整个界面就两个状态：首页（挑一类转换）和转换页（三步走）。
/// 所有状态变化都通过属性通知，XAML 直接绑，代码里不碰控件。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private CancellationTokenSource? _cancellation;
    private CategoryInfo? _currentCategory;
    private TargetItem? _selectedTarget;
    private bool _userPickedTarget;
    private bool _isHome = true;
    private bool _isConverting;
    private bool _isFinished;
    private bool _isProgressIndeterminate;
    private double _progress;
    private string _statusText = string.Empty;
    private string _detailText = string.Empty;
    private string _finishedText = string.Empty;
    private string _filesSummary = "还没有选文件";
    private string _noteText = string.Empty;
    private string _lastOutputFolder = AppPaths.OutputRoot;

    public ObservableCollection<CategoryInfo> Categories { get; } = [.. FormatCatalog.All];

    public ObservableCollection<FileItem> Files { get; } = [];

    public ObservableCollection<TargetItem> Targets { get; } = [];

    // ---------- 界面状态 ----------

    public bool IsHome
    {
        get => _isHome;
        private set
        {
            if (Set(ref _isHome, value)) OnPropertyChanged(nameof(IsConvert));
        }
    }

    public bool IsConvert => !_isHome;

    public CategoryInfo? CurrentCategory
    {
        get => _currentCategory;
        private set => Set(ref _currentCategory, value);
    }

    public bool IsConverting
    {
        get => _isConverting;
        private set
        {
            if (Set(ref _isConverting, value))
            {
                OnPropertyChanged(nameof(IsReadyToStart));
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public bool IsFinished
    {
        get => _isFinished;
        private set
        {
            if (Set(ref _isFinished, value))
            {
                OnPropertyChanged(nameof(IsReadyToStart));
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    /// <summary>“第 3 步”那一块什么时候显示。</summary>
    public bool IsReadyToStart => !IsConverting && !IsFinished;

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set
        {
            if (Set(ref _isProgressIndeterminate, value)) OnPropertyChanged(nameof(ProgressText));
        }
    }

    public double Progress
    {
        get => _progress;
        private set
        {
            if (Set(ref _progress, value)) OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>“45%”这样的文字。说不准进度时就不显示，免得骗人。</summary>
    public string ProgressText => IsProgressIndeterminate ? string.Empty : $"{_progress * 100:0}%";

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => Set(ref _detailText, value);
    }

    public string FinishedText
    {
        get => _finishedText;
        private set => Set(ref _finishedText, value);
    }

    public string FilesSummary
    {
        get => _filesSummary;
        private set => Set(ref _filesSummary, value);
    }

    /// <summary>“有几个文件被跳过了”之类的提醒。</summary>
    public string NoteText
    {
        get => _noteText;
        private set => Set(ref _noteText, value);
    }

    public bool HasNote => !string.IsNullOrEmpty(NoteText);

    public string OutputFolder => AppPaths.OutputRoot;

    public string OutputHint =>
        $"转好的文件会放到：「{OutputFolder}」\n原来的文件一个字都不会动。";

    public bool CanStart => IsReadyToStart && Files.Count > 0 && _selectedTarget is not null;

    // ---------- 操作 ----------

    public void OpenCategory(CategoryInfo category)
    {
        CurrentCategory = category;
        Files.Clear();
        Targets.Clear();

        foreach (var option in category.Targets) Targets.Add(new TargetItem(option));

        _userPickedTarget = false;
        SelectTarget(Targets.FirstOrDefault(item => item.Option.IsRecommended) ?? Targets.FirstOrDefault());

        IsHome = false;
        IsFinished = false;
        IsConverting = false;
        StatusText = string.Empty;
        DetailText = string.Empty;
        NoteText = string.Empty;
        Progress = 0;
        IsProgressIndeterminate = false;
        RefreshFilesSummary();
    }

    public void GoHome()
    {
        if (IsConverting) Cancel();
        IsHome = true;
        IsFinished = false;
    }

    /// <summary>选了文件（对话框或拖拽进来的）。不认识的格式会被跳过，并告诉用户为什么。</summary>
    public void AddFiles(IEnumerable<string> paths)
    {
        if (CurrentCategory is null) return;

        var added = 0;
        var wrongType = 0;
        var missing = 0;
        var duplicate = 0;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (!File.Exists(path))
            {
                missing++;
                continue;
            }

            if (!CurrentCategory.Accepts(System.IO.Path.GetExtension(path)))
            {
                wrongType++;
                continue;
            }

            if (Files.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                duplicate++;
                continue;
            }

            Files.Add(new FileItem(path));
            added++;
        }

        // 第一次放文件进来时，按这个文件的格式重新推荐一次默认目标格式。
        if (added > 0 && !_userPickedTarget && Files.Count > 0)
        {
            var first = Files[0].Path;
            var preferred = CurrentCategory.DefaultTargetFor(System.IO.Path.GetExtension(first));
            var match = Targets.FirstOrDefault(item =>
                string.Equals(item.Option.Extension, preferred?.Extension, StringComparison.OrdinalIgnoreCase));
            if (match is not null) SelectTarget(match);
        }

        RefreshFilesSummary();

        var notes = new List<string>();
        if (wrongType > 0) notes.Add($"有 {wrongType} 个文件不是「{CurrentCategory.Title}」能处理的，跳过了");
        if (missing > 0) notes.Add($"有 {missing} 个文件找不到，跳过了");
        if (duplicate > 0) notes.Add($"有 {duplicate} 个文件已经在列表里了");
        NoteText = notes.Count > 0 ? string.Join("；", notes) + "。" : string.Empty;
        OnPropertyChanged(nameof(HasNote));
    }

    /// <summary>首页直接拖文件进来：自动认出这是哪一类转换，直接进转换页。</summary>
    public void AcceptDroppedFiles(IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0) return;

        var category = FormatCatalog.Detect(list[0]);
        if (category is null)
        {
            NoteText = "这些文件暂时不属于能转换的类型。";
            OnPropertyChanged(nameof(HasNote));
            return;
        }

        // 在首页拖的话，先切到对应的转换页；已经在转换页就按当前类型过滤。
        if (IsHome || CurrentCategory?.Kind != category.Kind) OpenCategory(category);

        AddFiles(list);
    }

    public void RemoveFile(FileItem item)
    {
        Files.Remove(item);
        RefreshFilesSummary();
    }

    public void SelectTarget(TargetItem? item)
    {
        if (item is null) return;

        foreach (var target in Targets) target.IsSelected = ReferenceEquals(target, item);

        _selectedTarget = item;
        _userPickedTarget = true;
        OnPropertyChanged(nameof(CanStart));
    }

    /// <summary>主流程：一个一个文件转，界面不卡，随时能取消。</summary>
    public async Task StartAsync()
    {
        if (!CanStart || CurrentCategory is null || _selectedTarget is null) return;

        var category = CurrentCategory;
        var target = _selectedTarget;

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        IsConverting = true;
        IsFinished = false;
        Progress = 0;
        IsProgressIndeterminate = true;
        DetailText = string.Empty;
        NoteText = string.Empty;
        OnPropertyChanged(nameof(HasNote));

        var outputPaths = new List<string>();
        var successCount = 0;
        var failureCount = 0;
        var firstProblem = string.Empty;

        try
        {
            Directory.CreateDirectory(AppPaths.OutputRoot);

            for (var index = 0; index < Files.Count; index++)
            {
                token.ThrowIfCancellationRequested();

                var file = Files[index];
                StatusText = Files.Count == 1
                    ? "正在转换…"
                    : $"正在转换第 {index + 1} / {Files.Count} 个：{file.Name}";

                var job = new ConversionJob(
                    SourcePath: file.Path,
                    OutputDirectory: AppPaths.OutputRoot,
                    TargetExtension: target.Option.Extension,
                    Category: category.Kind,
                    AudioOnly: target.Option.AudioOnly);

                var reporter = new Progress<ProgressInfo>(info =>
                {
                    IsProgressIndeterminate = info.IsIndeterminate;
                    if (!info.IsIndeterminate) Progress = info.Fraction;
                    if (!string.IsNullOrEmpty(info.Message)) DetailText = info.Message;
                });

                var outcome = await ConversionService
                    .ConvertAsync(job, reporter, token)
                    .ConfigureAwait(true);

                if (outcome.Success)
                {
                    successCount++;
                    outputPaths.AddRange(outcome.OutputPaths);
                }
                else
                {
                    failureCount++;
                    if (firstProblem.Length == 0) firstProblem = $"{file.Name}：{outcome.Message}";
                    AppLog.Write($"转换失败：{file.Path} → {target.Option.Extension}，{outcome.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已经取消了";
            DetailText = "原文件没有改动。";
            Progress = 0;
            IsProgressIndeterminate = false;
            IsConverting = false;
            return;
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }

        IsConverting = false;
        IsProgressIndeterminate = false;

        // 打开文件夹要定位到“真正放产出的那个目录”：PDF 转图片会多一层子文件夹。
        _lastOutputFolder = outputPaths.Count > 0
            ? System.IO.Path.GetDirectoryName(outputPaths[0]) ?? AppPaths.OutputRoot
            : AppPaths.OutputRoot;

        IsFinished = true;
        Progress = 1;

        if (failureCount == 0)
        {
            StatusText = "转换好了";
            FinishedText = Files.Count == 1
                ? "转换好了，文件已经在「转换结果」文件夹里。"
                : $"全部 {successCount} 个都转换好了，文件在「转换结果」文件夹里。";
        }
        else if (successCount == 0)
        {
            StatusText = "没转成功";
            FinishedText = firstProblem;
        }
        else
        {
            StatusText = "转换好了";
            FinishedText = $"成功 {successCount} 个，失败 {failureCount} 个。\n第一处问题：{firstProblem}";
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    /// <summary>再转一批：清空文件，留在同一类转换里。</summary>
    public void ConvertMore()
    {
        if (CurrentCategory is null) return;

        var category = CurrentCategory;
        OpenCategory(category);
    }

    public void OpenOutputFolder()
    {
        try
        {
            var folder = Directory.Exists(_lastOutputFolder) ? _lastOutputFolder : AppPaths.OutputRoot;
            Directory.CreateDirectory(folder);

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
            NoteText = "文件夹没能打开，你可以手动打开：" + _lastOutputFolder;
            OnPropertyChanged(nameof(HasNote));
        }
    }

    private void RefreshFilesSummary()
    {
        if (Files.Count == 0)
        {
            FilesSummary = "还没有选文件";
        }
        else
        {
            var total = Files.Sum(item => item.Size);
            FilesSummary = $"已经选了 {Files.Count} 个文件，一共 {FileUtil.DescribeSize(total)}";
        }

        OnPropertyChanged(nameof(CanStart));
    }
}
