namespace RoboCopyGui.Core;

public enum CopyJobStage
{
    Idle,
    Scanning,
    Running,
    Paused,
    Stopping,
    Completed,
    Failed,
    Canceled
}

public enum CopyOutcome
{
    Success,
    SuccessWithWarnings,
    Failure,
    Canceled
}

public sealed record CopyOptionValue(string Id, string? Value = null);

public sealed record CopyJobRequest(
    string SourcePath,
    string DestinationPath,
    string FilePatterns,
    IReadOnlyList<CopyOptionValue> Options,
    string AdvancedOptions);

public sealed record ScanResult(
    long TotalBytes,
    int FileCount,
    int PlannedDeleteCount,
    IReadOnlyList<string> PreviewLines);

public sealed record CopyJobProgress(
    CopyJobStage Stage,
    double OverallPercent,
    double CurrentFilePercent,
    string CurrentFile,
    long BytesCopied,
    long TotalBytes,
    double BytesPerSecond,
    TimeSpan? EstimatedRemaining,
    int FilesCompleted,
    int TotalFiles,
    bool IsEstimated = false);

public sealed record CopyJobResult(
    int ExitCode,
    CopyOutcome Outcome,
    string Summary,
    string LogPath);

public interface IRobocopyRunner : IAsyncDisposable
{
    event EventHandler<CopyJobProgress>? ProgressChanged;
    event EventHandler<string>? OutputReceived;

    CopyJobStage Stage { get; }
    bool IsBusy { get; }

    Task<ScanResult> ScanAsync(CopyJobRequest request, CancellationToken cancellationToken = default);
    Task<CopyJobResult> StartAsync(CopyJobRequest request, ScanResult scan, CancellationToken cancellationToken = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
}
