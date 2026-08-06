using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using RoboCopyGui.Core;

namespace RoboCopyGui.Services;

public sealed class RobocopyRunner : IRobocopyRunner
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _controlGate = new(1, 1);
    private readonly SemaphoreSlim _logGate = new(1, 1);
    private readonly string _logDirectory;
    private Process? _currentProcess;
    private ProcessJob? _currentJob;
    private ProcessSuspender? _suspender;
    private ProgressTracker? _tracker;
    private string _logPath = string.Empty;
    private bool _stoppedByUser;
    private bool _disposed;

    public RobocopyRunner(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoboCopyGUI",
            "Logs");
    }

    public event EventHandler<CopyJobProgress>? ProgressChanged;
    public event EventHandler<string>? OutputReceived;

    public CopyJobStage Stage { get; private set; } = CopyJobStage.Idle;
    public bool IsBusy => Stage is CopyJobStage.Scanning or CopyJobStage.Running or CopyJobStage.Paused or CopyJobStage.Stopping;

    public async Task<ScanResult> ScanAsync(CopyJobRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureIdle();
        var errors = CopyJobValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, errors));
        }

        Directory.CreateDirectory(_logDirectory);
        _logPath = Path.Combine(_logDirectory, $"robocopy-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        _stoppedByUser = false;
        Stage = CopyJobStage.Scanning;
        ProgressChanged?.Invoke(this, new(Stage, 0, 0, "Scanning source…", 0, 0, 0, null, 0, 0));
        await WriteLogAsync($"[{DateTimeOffset.Now:O}] Pre-scan{Environment.NewLine}{RobocopyCommandBuilder.BuildDisplayCommand(request, RobocopyExecutionMode.Scan)}{Environment.NewLine}");

        var lines = new List<string>();
        try
        {
            var exitCode = await RunProcessAsync(
                RobocopyCommandBuilder.BuildArguments(request, RobocopyExecutionMode.Scan),
                line => lines.Add(line),
                cancellationToken);
            if (_stoppedByUser || cancellationToken.IsCancellationRequested)
            {
                Stage = CopyJobStage.Canceled;
                throw new OperationCanceledException("The pre-scan was stopped.", cancellationToken);
            }

            if (exitCode >= 8)
            {
                Stage = CopyJobStage.Failed;
                throw new InvalidOperationException($"Robocopy pre-scan failed with exit code {exitCode}. Review {_logPath}.");
            }

            var scan = RobocopyOutputParser.ParseScan(lines);
            Stage = CopyJobStage.Idle;
            ProgressChanged?.Invoke(this, new(Stage, 0, 0, "Ready", 0, scan.TotalBytes, 0, null, 0, scan.FileCount));
            return scan;
        }
        catch
        {
            if (Stage == CopyJobStage.Scanning)
            {
                Stage = CopyJobStage.Failed;
            }

            throw;
        }
    }

    public async Task<CopyJobResult> StartAsync(CopyJobRequest request, ScanResult scan, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureIdle();
        _stoppedByUser = false;
        _tracker = new ProgressTracker(scan, IsMultiThreaded(request));
        Stage = CopyJobStage.Running;
        ProgressChanged?.Invoke(this, new(Stage, 0, 0, "Starting Robocopy…", 0, scan.TotalBytes, 0, null, 0, scan.FileCount, IsMultiThreaded(request)));
        await WriteLogAsync($"{Environment.NewLine}[{DateTimeOffset.Now:O}] Copy started{Environment.NewLine}{RobocopyCommandBuilder.BuildDisplayCommand(request)}{Environment.NewLine}");

        try
        {
            var exitCode = await RunProcessAsync(
                RobocopyCommandBuilder.BuildArguments(request, RobocopyExecutionMode.Copy),
                line =>
                {
                    if (_tracker is { } tracker)
                    {
                        ProgressChanged?.Invoke(this, tracker.Update(line, Stage));
                    }
                },
                cancellationToken);

            if (_stoppedByUser || cancellationToken.IsCancellationRequested)
            {
                Stage = CopyJobStage.Canceled;
                var canceled = ExitCodeInterpreter.Interpret(exitCode, _logPath, canceled: true);
                await WriteLogAsync($"[{DateTimeOffset.Now:O}] {canceled.Summary}{Environment.NewLine}");
                return canceled;
            }

            var result = ExitCodeInterpreter.Interpret(exitCode, _logPath);
            Stage = result.Outcome == CopyOutcome.Failure ? CopyJobStage.Failed : CopyJobStage.Completed;
            if (_tracker is { } completedTracker)
            {
                ProgressChanged?.Invoke(this, completedTracker.Complete(result.Outcome != CopyOutcome.Failure));
            }

            await WriteLogAsync($"[{DateTimeOffset.Now:O}] Exit code {exitCode}: {result.Summary}{Environment.NewLine}");
            return result;
        }
        catch (OperationCanceledException) when (_stoppedByUser || cancellationToken.IsCancellationRequested)
        {
            Stage = CopyJobStage.Canceled;
            return ExitCodeInterpreter.Interpret(-1, _logPath, canceled: true);
        }
        catch
        {
            Stage = CopyJobStage.Failed;
            throw;
        }
    }

    public async Task PauseAsync()
    {
        await _controlGate.WaitAsync();
        try
        {
            if (Stage != CopyJobStage.Running || _currentProcess is not { HasExited: false } process)
            {
                throw new InvalidOperationException("There is no running copy to pause.");
            }

            _suspender ??= new ProcessSuspender();
            _suspender.Suspend(process);
            Stage = CopyJobStage.Paused;
            if (_tracker is { } tracker)
            {
                ProgressChanged?.Invoke(this, tracker.Pause());
            }

            await WriteLogAsync($"[{DateTimeOffset.Now:O}] Copy paused by user.{Environment.NewLine}");
        }
        finally
        {
            _controlGate.Release();
        }
    }

    public async Task ResumeAsync()
    {
        await _controlGate.WaitAsync();
        try
        {
            if (Stage != CopyJobStage.Paused)
            {
                throw new InvalidOperationException("There is no paused copy to resume.");
            }

            _suspender?.ResumeAll();
            Stage = CopyJobStage.Running;
            if (_tracker is { } tracker)
            {
                ProgressChanged?.Invoke(this, tracker.Resume());
            }

            await WriteLogAsync($"[{DateTimeOffset.Now:O}] Copy resumed by user.{Environment.NewLine}");
        }
        finally
        {
            _controlGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _controlGate.WaitAsync();
        try
        {
            if (!IsBusy)
            {
                return;
            }

            _stoppedByUser = true;
            Stage = CopyJobStage.Stopping;
            _suspender?.ResumeAll();
            Process? process;
            lock (_sync)
            {
                process = _currentProcess;
            }

            if (process is not null)
            {
                try
                {
                    var waitTask = process.WaitForExitAsync();
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    await waitTask;
                }
                catch (InvalidOperationException)
                {
                    // The runner completed and disposed the process during the stop request.
                }
            }

            Stage = CopyJobStage.Canceled;
            await WriteLogAsync($"[{DateTimeOffset.Now:O}] Operation stopped by user.{Environment.NewLine}");
        }
        finally
        {
            _controlGate.Release();
        }
    }

    private async Task<int> RunProcessAsync(IReadOnlyList<string> arguments, Action<string> onOutput, CancellationToken cancellationToken)
    {
        var robocopyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "robocopy.exe");
        if (!File.Exists(robocopyPath))
        {
            throw new FileNotFoundException("Robocopy is not available on this Windows installation.", robocopyPath);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var outputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var startInfo = new ProcessStartInfo(robocopyPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
        {
            throw new InvalidOperationException("Windows could not start Robocopy.");
        }

        ProcessJob? job = null;
        try
        {
            job = new ProcessJob();
            job.Assign(process);
        }
        catch (Exception exception)
        {
            job?.Dispose();
            job = null;
            await WriteLogAsync($"[Warning] Process containment was unavailable: {exception.Message}{Environment.NewLine}");
        }

        lock (_sync)
        {
            _currentProcess = process;
            _currentJob = job;
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The process may have exited between the check and the kill request.
            }
        });

        try
        {
            var outputTask = PumpAsync(process.StandardOutput, onOutput, cancellationToken);
            var errorTask = PumpAsync(process.StandardError, onOutput, cancellationToken);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(outputTask, errorTask);
            return process.ExitCode;
        }
        finally
        {
            lock (_sync)
            {
                _currentProcess = null;
                _currentJob = null;
            }

            _suspender?.ResumeAll();
            _suspender = null;
            job?.Dispose();
        }
    }

    private async Task PumpAsync(StreamReader reader, Action<string> onOutput, CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        var segment = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                break;
            }

            for (var index = 0; index < count; index++)
            {
                var character = buffer[index];
                if (character is '\r' or '\n')
                {
                    if (segment.Length > 0)
                    {
                        await EmitAsync(segment.ToString(), onOutput);
                        segment.Clear();
                    }
                }
                else if (character != '\0')
                {
                    segment.Append(character);
                }
            }
        }

        if (segment.Length > 0)
        {
            await EmitAsync(segment.ToString(), onOutput);
        }
    }

    private async Task EmitAsync(string line, Action<string> onOutput)
    {
        await WriteLogAsync(line + Environment.NewLine);
        OutputReceived?.Invoke(this, line);
        onOutput(line);
    }

    private async Task WriteLogAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(_logPath))
        {
            return;
        }

        await _logGate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_logPath, text, new UTF8Encoding(false));
        }
        finally
        {
            _logGate.Release();
        }
    }

    private static bool IsMultiThreaded(CopyJobRequest request) =>
        request.Options.Any(option => option.Id.Equals("MultiThreaded", StringComparison.OrdinalIgnoreCase)) ||
        ArgumentTokenizer.Parse(request.AdvancedOptions).Any(option => option.StartsWith("/MT", StringComparison.OrdinalIgnoreCase));

    private void EnsureIdle()
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("Another Robocopy operation is already active.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync();
        _disposed = true;
        _controlGate.Dispose();
        _logGate.Dispose();
        _currentJob?.Dispose();
    }
}
