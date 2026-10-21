using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using RobustCopy.Core;
using RobustCopy.Services;

namespace RobustCopy.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaximumVisibleLogCharacters = 250_000;
    private const long UiUpdateIntervalMilliseconds = 100;
    private readonly IRobocopyRunner _runner;
    private readonly IFolderPicker _folderPicker;
    private readonly IUserDialogService _dialogs;
    private readonly IDesktopLauncher _launcher;
    private readonly StringBuilder _visibleLog = new();
    private readonly object _pendingSync = new();
    private readonly StringBuilder _pendingLog = new();
    private CopyJobProgress? _pendingProgress;
    private bool _uiUpdateScheduled;
    private long _lastUiUpdate;
    private string _sourcePath = string.Empty;
    private string _destinationPath = string.Empty;
    private string _filePatterns = "*.*";
    private string _advancedOptions = string.Empty;
    private string _commandPreview = string.Empty;
    private string _statusText = "Ready to configure a copy";
    private string _validationText = string.Empty;
    private string _currentFile = "—";
    private string _logText = "[idle] Waiting to start…";
    private string _lastLogPath = string.Empty;
    private CopyJobStage _stage = CopyJobStage.Idle;
    private double _overallPercent;
    private double _currentFilePercent;
    private long _bytesCopied;
    private long _totalBytes;
    private double _bytesPerSecond;
    private TimeSpan? _eta;
    private int _filesCompleted;
    private int _totalFiles;
    private bool _isEstimated;
    private bool _isResolvingConflicts;

    public MainViewModel(
        IRobocopyRunner runner,
        IFolderPicker folderPicker,
        IUserDialogService dialogs,
        IDesktopLauncher launcher)
    {
        _runner = runner;
        _folderPicker = folderPicker;
        _dialogs = dialogs;
        _launcher = launcher;
        Options = new ObservableCollection<OptionItemViewModel>(
            RobocopyOptionCatalog.All.Select(definition => new OptionItemViewModel(definition, OnOptionChanged)));

        BrowseSourceCommand = new RelayCommand(BrowseSource, () => !IsBusy);
        BrowseDestinationCommand = new RelayCommand(BrowseDestination, () => !IsBusy);
        ClearPathsCommand = new RelayCommand(ClearPaths, () => !IsBusy);
        SwapCommand = new RelayCommand(SwapPaths, () => !IsBusy);
        StartCommand = new AsyncRelayCommand(StartAsync, () => Stage is CopyJobStage.Idle or CopyJobStage.Completed or CopyJobStage.Failed or CopyJobStage.Canceled, HandleCommandError);
        PauseCommand = new AsyncRelayCommand(() => RunAndApplyAsync(_runner.PauseAsync), () => Stage == CopyJobStage.Running, HandleCommandError);
        ResumeCommand = new AsyncRelayCommand(() => RunAndApplyAsync(_runner.ResumeAsync), () => Stage == CopyJobStage.Paused, HandleCommandError);
        StopCommand = new AsyncRelayCommand(_runner.StopAsync, () => IsBusy, HandleCommandError);
        CopyCommandCommand = new RelayCommand(() => _launcher.CopyText(CommandPreview), () => !string.IsNullOrWhiteSpace(CommandPreview));
        OpenLogCommand = new RelayCommand(() => _launcher.OpenPath(LastLogPath), () => File.Exists(LastLogPath));
        CopyLogCommand = new RelayCommand(CopyLog, () => File.Exists(LastLogPath));
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);

        _runner.ProgressChanged += OnProgressChanged;
        _runner.OutputReceived += OnOutputReceived;
        RefreshConflictMessages();
        RefreshPreview();
    }

    public ObservableCollection<OptionItemViewModel> Options { get; }
    public IEnumerable<OptionItemViewModel> FolderOptions => Options.Where(option => option.Category == "Folders");
    public IEnumerable<OptionItemViewModel> ReliabilityOptions => Options.Where(option => option.Category == "Reliability");
    public IEnumerable<OptionItemViewModel> MetadataOptions => Options.Where(option => option.Category == "Metadata");
    public IEnumerable<OptionItemViewModel> FilterOptions => Options.Where(option => option.Category == "Filters");
    public IEnumerable<OptionItemViewModel> PerformanceOptions => Options.Where(option => option.Category == "Performance");
    public IEnumerable<OptionItemViewModel> DestructiveOptions => Options.Where(option => option.Category == "Destructive operations");

    public RelayCommand BrowseSourceCommand { get; }
    public RelayCommand BrowseDestinationCommand { get; }
    public RelayCommand ClearPathsCommand { get; }
    public RelayCommand SwapCommand { get; }
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand PauseCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public RelayCommand CopyCommandCommand { get; }
    public RelayCommand OpenLogCommand { get; }
    public RelayCommand CopyLogCommand { get; }
    public RelayCommand OpenLogsFolderCommand { get; }

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetProperty(ref _sourcePath, value))
            {
                OnPropertyChanged(nameof(SourcePathDisplay));
                RefreshPreview();
            }
        }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                OnPropertyChanged(nameof(DestinationPathDisplay));
                RefreshPreview();
            }
        }
    }

    public string SourcePathDisplay => string.IsNullOrWhiteSpace(SourcePath) ? "Source directory here" : SourcePath;
    public string DestinationPathDisplay => string.IsNullOrWhiteSpace(DestinationPath) ? "Destination directory here" : DestinationPath;

    public string FilePatterns
    {
        get => _filePatterns;
        set { if (SetProperty(ref _filePatterns, value)) RefreshPreview(); }
    }

    public string AdvancedOptions
    {
        get => _advancedOptions;
        set { if (SetProperty(ref _advancedOptions, value)) RefreshPreview(); }
    }

    public string CommandPreview { get => _commandPreview; private set => SetProperty(ref _commandPreview, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string ValidationText { get => _validationText; private set => SetProperty(ref _validationText, value); }
    public string CurrentFile { get => _currentFile; private set => SetProperty(ref _currentFile, value); }
    public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }
    public string LastLogPath { get => _lastLogPath; private set { if (SetProperty(ref _lastLogPath, value)) RefreshCommands(); } }
    public CopyJobStage Stage { get => _stage; private set { if (SetProperty(ref _stage, value)) { OnPropertyChanged(nameof(StageText)); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(IsScanning)); RefreshCommands(); } } }
    public string StageText => Stage.ToString().ToUpperInvariant();
    public bool IsBusy => Stage is CopyJobStage.Scanning or CopyJobStage.Running or CopyJobStage.Paused or CopyJobStage.Stopping;
    public bool IsScanning => Stage == CopyJobStage.Scanning;
    public double OverallPercent { get => _overallPercent; private set { if (SetProperty(ref _overallPercent, value)) OnPropertyChanged(nameof(OverallPercentText)); } }
    public double CurrentFilePercent { get => _currentFilePercent; private set { if (SetProperty(ref _currentFilePercent, value)) OnPropertyChanged(nameof(CurrentFilePercentText)); } }
    public long BytesCopied { get => _bytesCopied; private set { if (SetProperty(ref _bytesCopied, value)) OnPropertyChanged(nameof(TransferredText)); } }
    public long TotalBytes { get => _totalBytes; private set { if (SetProperty(ref _totalBytes, value)) OnPropertyChanged(nameof(TransferredText)); } }
    public double BytesPerSecond { get => _bytesPerSecond; private set { if (SetProperty(ref _bytesPerSecond, value)) OnPropertyChanged(nameof(SpeedText)); } }
    public TimeSpan? Eta { get => _eta; private set { if (SetProperty(ref _eta, value)) OnPropertyChanged(nameof(EtaText)); } }
    public int FilesCompleted { get => _filesCompleted; private set { if (SetProperty(ref _filesCompleted, value)) OnPropertyChanged(nameof(FilesText)); } }
    public int TotalFiles { get => _totalFiles; private set { if (SetProperty(ref _totalFiles, value)) OnPropertyChanged(nameof(FilesText)); } }
    public bool IsEstimated { get => _isEstimated; private set { if (SetProperty(ref _isEstimated, value)) OnPropertyChanged(nameof(OverallLabel)); } }

    public string OverallLabel => IsEstimated ? "Overall progress (estimated)" : "Overall progress";
    public string OverallPercentText => $"{OverallPercent:0}%";
    public string CurrentFilePercentText => $"{CurrentFilePercent:0}%";
    public string TransferredText => $"{FormatBytes(BytesCopied)} / {FormatBytes(TotalBytes)}";
    public string SpeedText => BytesPerSecond <= 0 ? "0 MB/s" : $"{FormatBytes((long)BytesPerSecond)}/s";
    public string EtaText => Eta is null ? "—" : Eta.Value.TotalHours >= 1 ? $"{Eta:hh\\:mm\\:ss}" : $"{Eta:mm\\:ss}";
    public string FilesText => $"{FilesCompleted:N0} / {TotalFiles:N0}";

    public async Task StopForShutdownAsync()
    {
        if (_runner.IsBusy)
        {
            await _runner.StopAsync();
        }
    }

    private async Task StartAsync()
    {
        var request = BuildRequest();
        var errors = CopyJobValidator.Validate(request);
        ValidationText = string.Join("  ", errors);
        if (errors.Count > 0)
        {
            _dialogs.ShowError("Check copy settings", string.Join(Environment.NewLine, errors));
            return;
        }

        ResetProgress();
        _visibleLog.Clear();
        LogText = string.Empty;
        StatusText = "Scanning source and destination…";
        Stage = CopyJobStage.Scanning;

        try
        {
            var scan = await _runner.ScanAsync(request);
            ApplyPendingUpdates();
            TotalBytes = scan.TotalBytes;
            TotalFiles = scan.FileCount;
            if (CopyJobValidator.IsDestructive(request))
            {
                var moves = request.Options.Any(option => option.Id is "MoveFiles" or "MoveTree");
                // Robocopy lists *EXTRA items in /L mode regardless of switches; only /MIR and /PURGE delete them.
                var purges = request.Options.Any(option => option.Id is "Mirror" or "Purge");
                var plannedDeletes = purges ? scan.PlannedDeleteCount : 0;
                if (!_dialogs.ConfirmDestructive(request.DestinationPath, plannedDeletes, moves))
                {
                    Stage = CopyJobStage.Canceled;
                    StatusText = "Copy canceled before any changes were made";
                    return;
                }
            }

            StatusText = scan.FileCount == 0 ? "Checking destination…" : $"Copying {scan.FileCount:N0} file(s)…";
            var result = await _runner.StartAsync(request, scan);
            ApplyPendingUpdates();
            LastLogPath = result.LogPath;
            Stage = result.Outcome switch
            {
                CopyOutcome.Failure => CopyJobStage.Failed,
                CopyOutcome.Canceled => CopyJobStage.Canceled,
                _ => CopyJobStage.Completed
            };
            StatusText = result.Summary;
        }
        catch (OperationCanceledException)
        {
            ApplyPendingUpdates();
            Stage = CopyJobStage.Canceled;
            StatusText = "Operation canceled";
        }
        catch (Exception exception)
        {
            ApplyPendingUpdates();
            Stage = CopyJobStage.Failed;
            StatusText = "The operation failed";
            _dialogs.ShowError(AppIdentity.DisplayName, exception.Message);
        }
        finally
        {
            LastLogPath = _runner.CurrentLogPath;
            RefreshCommands();
        }
    }

    private async Task RunAndApplyAsync(Func<Task> action)
    {
        await action();
        ApplyPendingUpdates();
    }

    private CopyJobRequest BuildRequest() => new(
        SourcePath.Trim(),
        DestinationPath.Trim(),
        FilePatterns,
        Options.Where(option => option.IsSelected).Select(option => option.ToSelection()).ToArray(),
        AdvancedOptions);

    private void BrowseSource()
    {
        var selected = _folderPicker.PickFolder("Choose source folder", SourcePath);
        if (selected is not null) SourcePath = selected;
    }

    private void BrowseDestination()
    {
        var selected = _folderPicker.PickFolder("Choose destination folder", DestinationPath);
        if (selected is not null) DestinationPath = selected;
    }

    private void ClearPaths() => (SourcePath, DestinationPath) = (string.Empty, string.Empty);

    private void SwapPaths() => (SourcePath, DestinationPath) = (DestinationPath, SourcePath);

    private void OnOptionChanged(OptionItemViewModel changed)
    {
        if (_isResolvingConflicts) return;

        if (changed.IsSelected)
        {
            _isResolvingConflicts = true;
            try
            {
                foreach (var option in Options.Where(option => option.IsSelected && Conflicts(option, changed)).ToArray())
                {
                    option.IsSelected = false;
                }
            }
            finally
            {
                _isResolvingConflicts = false;
            }
        }

        RefreshConflictMessages();
        RefreshPreview();
    }

    private void RefreshConflictMessages()
    {
        var selected = Options.Where(option => option.IsSelected).ToArray();
        foreach (var option in Options)
        {
            var turnsOff = option.IsSelected
                ? []
                : selected.Where(candidate => Conflicts(candidate, option)).Select(candidate => $"“{candidate.Label}”").ToArray();
            option.ConflictMessage = turnsOff.Length == 0 ? string.Empty : $"Selecting this turns off {string.Join(", ", turnsOff)}.";
        }
    }

    private static bool Conflicts(OptionItemViewModel left, OptionItemViewModel right) =>
        left != right &&
        (left.Definition.ConflictsWith.Contains(right.Id, StringComparer.OrdinalIgnoreCase) ||
         right.Definition.ConflictsWith.Contains(left.Id, StringComparer.OrdinalIgnoreCase));

    private void RefreshPreview()
    {
        try
        {
            var request = BuildRequest();
            CommandPreview = RobocopyCommandBuilder.BuildDisplayCommand(request);
            ValidationText = string.Join("  ", CopyJobValidator.Validate(request, requireExistingSource: false));
        }
        catch (Exception exception)
        {
            CommandPreview = "Command preview unavailable";
            ValidationText = exception.Message;
        }

        CopyCommandCommand?.NotifyCanExecuteChanged();
    }

    private void OnProgressChanged(object? sender, CopyJobProgress progress)
    {
        lock (_pendingSync)
        {
            _pendingProgress = progress;
        }

        ScheduleUiUpdate();
    }

    private void OnOutputReceived(object? sender, string line)
    {
        lock (_pendingSync)
        {
            _pendingLog.AppendLine(line);
            if (_pendingLog.Length > MaximumVisibleLogCharacters)
            {
                _pendingLog.Remove(0, _pendingLog.Length - MaximumVisibleLogCharacters);
            }
        }

        ScheduleUiUpdate();
    }

    private void ScheduleUiUpdate()
    {
        long delay;
        lock (_pendingSync)
        {
            if (_uiUpdateScheduled)
            {
                return;
            }

            _uiUpdateScheduled = true;
            delay = Math.Max(0, UiUpdateIntervalMilliseconds - (Environment.TickCount64 - _lastUiUpdate));
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            ApplyPendingUpdates();
            return;
        }

        _ = Task.Delay(TimeSpan.FromMilliseconds(delay)).ContinueWith(
            _ => dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyPendingUpdates),
            TaskScheduler.Default);
    }

    // Runs on the UI thread. Also called right after runner calls so stage changes and terminal updates are never late or lost.
    private void ApplyPendingUpdates()
    {
        CopyJobProgress? progress;
        string? log = null;
        lock (_pendingSync)
        {
            progress = _pendingProgress;
            _pendingProgress = null;
            if (_pendingLog.Length > 0)
            {
                log = _pendingLog.ToString();
                _pendingLog.Clear();
            }

            _uiUpdateScheduled = false;
            _lastUiUpdate = Environment.TickCount64;
        }

        if (progress is not null)
        {
            ApplyProgress(progress);
        }

        if (log is not null)
        {
            _visibleLog.Append(log);
            if (_visibleLog.Length > MaximumVisibleLogCharacters)
            {
                _visibleLog.Remove(0, _visibleLog.Length - MaximumVisibleLogCharacters);
            }

            LogText = _visibleLog.ToString();
        }
    }

    private void ApplyProgress(CopyJobProgress progress)
    {
        Stage = progress.Stage;
        OverallPercent = progress.OverallPercent;
        CurrentFilePercent = progress.CurrentFilePercent;
        if (!string.IsNullOrWhiteSpace(progress.CurrentFile)) CurrentFile = progress.CurrentFile;
        BytesCopied = progress.BytesCopied;
        TotalBytes = progress.TotalBytes;
        BytesPerSecond = progress.BytesPerSecond;
        Eta = progress.EstimatedRemaining;
        FilesCompleted = progress.FilesCompleted;
        TotalFiles = progress.TotalFiles;
        IsEstimated = progress.IsEstimated;
    }

    private void ResetProgress()
    {
        OverallPercent = 0;
        CurrentFilePercent = 0;
        CurrentFile = "Waiting for Robocopy…";
        BytesCopied = 0;
        TotalBytes = 0;
        BytesPerSecond = 0;
        Eta = null;
        FilesCompleted = 0;
        TotalFiles = 0;
        IsEstimated = false;
    }

    private void CopyLog()
    {
        try
        {
            var tail = LogTailReader.Read(LastLogPath);
            _launcher.CopyText(tail.Text);
            _dialogs.ShowInformation("Log copied", tail.IsTruncated
                ? $"The transcript is {LogTailReader.FormatMegabytes(tail.FileBytes)}, so only the last {LogTailReader.FormatMegabytes(tail.MaximumBytes)} is on the clipboard. Open the log for the full transcript."
                : "The complete Robocopy transcript is on the clipboard.");
        }
        catch (Exception exception)
        {
            _dialogs.ShowError("Could not copy the log", exception.Message);
        }
    }

    private void OpenLogsFolder()
    {
        var directory = AppIdentity.EnsureLogDirectory();
        _launcher.OpenPath(directory);
    }

    private void HandleCommandError(Exception exception) => _dialogs.ShowError(AppIdentity.DisplayName, exception.Message);

    private void RefreshCommands()
    {
        BrowseSourceCommand.NotifyCanExecuteChanged();
        BrowseDestinationCommand.NotifyCanExecuteChanged();
        ClearPathsCommand.NotifyCanExecuteChanged();
        SwapCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        CopyCommandCommand.NotifyCanExecuteChanged();
        OpenLogCommand.NotifyCanExecuteChanged();
        CopyLogCommand.NotifyCanExecuteChanged();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        var display = (double)value;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        return $"{display:0.##} {units[unit]}";
    }

    public async ValueTask DisposeAsync()
    {
        _runner.ProgressChanged -= OnProgressChanged;
        _runner.OutputReceived -= OnOutputReceived;
        await _runner.DisposeAsync();
    }
}
