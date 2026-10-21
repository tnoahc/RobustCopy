using System.IO;
using System.Reflection;

namespace RobustCopy.Core;

public static class CrashReporter
{
    /// <summary>
    /// Writes a crash report and returns its path, or null when the report could not be written.
    /// This is called from unhandled-exception handlers, so it must never throw.
    /// </summary>
    public static string? Write(object? exception, string source, string? directory = null)
    {
        try
        {
            var logDirectory = directory ?? AppIdentity.LogDirectory;
            Directory.CreateDirectory(logDirectory);

            var timestamp = DateTime.UtcNow;
            var path = Path.Combine(logDirectory, $"crash-{timestamp:yyyyMMdd-HHmmss}.log");
            var report =
                $"{AppIdentity.DisplayName} crash report{Environment.NewLine}" +
                $"Time (UTC): {timestamp:O}{Environment.NewLine}" +
                $"Version: {GetVersion()}{Environment.NewLine}" +
                $"Source: {source}{Environment.NewLine}" +
                $"{exception?.ToString() ?? "(no exception information)"}{Environment.NewLine}{Environment.NewLine}";

            // Append so that several failures within the same second share one report.
            File.AppendAllText(path, report);
            return path;
        }
        catch (Exception)
        {
            // Crash reporting is best-effort; failing to log must not raise a second fault.
            return null;
        }
    }

    private static string GetVersion()
    {
        var assembly = typeof(CrashReporter).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
