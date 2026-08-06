using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using RoboCopyGui.Core;

namespace RoboCopyGui.Services;

public sealed record RobocopyOutputRecord(string Path, long Bytes, double? Percent, bool IsExtra, bool IsDirectory)
{
    public bool HasFile => !string.IsNullOrWhiteSpace(Path);
}

public static partial class RobocopyOutputParser
{
    [GeneratedRegex(@"^\s*(?<status>.*?)\s+(?<bytes>-?\d+)\s+(?<path>(?:[A-Za-z]:\\|\\\\).+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex FileLineRegex();

    [GeneratedRegex(@"(?<percent>\d+(?:[\.,]\d+)?)\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();

    public static RobocopyOutputRecord Parse(string line)
    {
        var fileMatch = FileLineRegex().Match(line);
        var path = string.Empty;
        var bytes = 0L;
        var isExtra = false;
        var isDirectory = false;
        if (fileMatch.Success)
        {
            path = fileMatch.Groups["path"].Value.Trim();
            _ = long.TryParse(fileMatch.Groups["bytes"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out bytes);
            bytes = Math.Max(0, bytes);
            var status = fileMatch.Groups["status"].Value;
            isExtra = status.Contains("EXTRA", StringComparison.OrdinalIgnoreCase);
            isDirectory = status.Contains("Dir", StringComparison.OrdinalIgnoreCase) ||
                          path.EndsWith(Path.DirectorySeparatorChar) ||
                          path.EndsWith(Path.AltDirectorySeparatorChar);
        }

        double? percent = null;
        var percentMatch = PercentRegex().Match(line);
        if (percentMatch.Success)
        {
            var value = percentMatch.Groups["percent"].Value.Replace(',', '.');
            if (double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            {
                percent = Math.Clamp(parsed, 0, 100);
            }
        }

        return new(path, bytes, percent, isExtra, isDirectory);
    }

    public static ScanResult ParseScan(IEnumerable<string> lines)
    {
        long bytes = 0;
        var files = 0;
        var deletes = 0;
        var preview = new List<string>();
        foreach (var line in lines)
        {
            var record = Parse(line);
            if (!record.HasFile)
            {
                continue;
            }

            if (record.IsExtra)
            {
                deletes++;
            }
            else if (!record.IsDirectory)
            {
                bytes += record.Bytes;
                files++;
            }

            if (preview.Count < 200)
            {
                preview.Add(line.Trim());
            }
        }

        return new(bytes, files, deletes, preview);
    }
}

internal sealed class ProgressTracker
{
    private readonly ScanResult _scan;
    private readonly bool _estimated;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly HashSet<string> _completedPaths = new(StringComparer.OrdinalIgnoreCase);
    private DateTime? _pausedAt;
    private TimeSpan _pausedDuration;
    private string _currentPath = string.Empty;
    private long _currentSize;
    private double _currentPercent;
    private long _completedBytes;

    public ProgressTracker(ScanResult scan, bool estimated)
    {
        _scan = scan;
        _estimated = estimated;
    }

    public CopyJobProgress Update(string line, CopyJobStage stage)
    {
        var record = RobocopyOutputParser.Parse(line);
        if (record.HasFile && !record.IsExtra && !record.IsDirectory)
        {
            if (!string.IsNullOrWhiteSpace(_currentPath) && !record.Path.Equals(_currentPath, StringComparison.OrdinalIgnoreCase))
            {
                CompleteCurrentFile();
            }

            _currentPath = record.Path;
            _currentSize = record.Bytes;
            _currentPercent = 0;
        }

        if (record.Percent is { } percent && !string.IsNullOrWhiteSpace(_currentPath))
        {
            _currentPercent = Math.Max(_currentPercent, percent);
            if (_currentPercent >= 100)
            {
                CompleteCurrentFile();
            }
        }

        return Snapshot(stage, final: false);
    }

    public CopyJobProgress Pause()
    {
        _pausedAt ??= DateTime.UtcNow;
        return Snapshot(CopyJobStage.Paused, final: false);
    }

    public CopyJobProgress Resume()
    {
        if (_pausedAt is { } pausedAt)
        {
            _pausedDuration += DateTime.UtcNow - pausedAt;
            _pausedAt = null;
        }

        return Snapshot(CopyJobStage.Running, final: false);
    }

    public CopyJobProgress Complete(bool success)
    {
        if (success)
        {
            CompleteCurrentFile();
        }

        return Snapshot(success ? CopyJobStage.Completed : CopyJobStage.Failed, final: success);
    }

    private void CompleteCurrentFile()
    {
        if (string.IsNullOrWhiteSpace(_currentPath) || !_completedPaths.Add(_currentPath))
        {
            return;
        }

        _completedBytes += _currentSize;
        _currentPercent = 100;
    }

    private CopyJobProgress Snapshot(CopyJobStage stage, bool final)
    {
        var currentContribution = _currentPercent >= 100 ? 0 : (long)(_currentSize * (_currentPercent / 100d));
        var copied = Math.Max(0, _completedBytes + currentContribution);
        var denominator = Math.Max(_scan.TotalBytes, copied);
        var overall = denominator == 0 ? 0 : copied * 100d / denominator;
        overall = final ? 100 : Math.Clamp(overall, 0, 99);

        var now = _pausedAt ?? DateTime.UtcNow;
        var activeTime = now - _startedAt - _pausedDuration;
        var speed = activeTime.TotalSeconds <= 0 ? 0 : copied / activeTime.TotalSeconds;
        TimeSpan? eta = null;
        if (speed > 0 && _scan.TotalBytes > copied)
        {
            eta = TimeSpan.FromSeconds((_scan.TotalBytes - copied) / speed);
        }

        return new(
            stage,
            overall,
            _currentPercent,
            _currentPath,
            final ? Math.Max(_scan.TotalBytes, copied) : copied,
            Math.Max(_scan.TotalBytes, denominator),
            speed,
            eta,
            _completedPaths.Count,
            _scan.FileCount,
            _estimated);
    }
}
