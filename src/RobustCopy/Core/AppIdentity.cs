using System.IO;

namespace RobustCopy.Core;

public static class AppIdentity
{
    public const string DisplayName = "RobustCopy";
    public const string LocalDataFolderName = "RobustCopy";
    public const string LegacyLocalDataFolderName = "RoboCopyGUI";
    public const int LogRetentionDays = 90;
    public const string LegacyMigrationMarkerName = ".legacy-migrated";

    private static readonly string[] PrunableLogPrefixes = ["robocopy-", "crash-"];

    public static string LogDirectory => GetLogDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LocalDataFolderName);

    public static string EnsureLogDirectory(string? localDataRoot = null)
    {
        var root = localDataRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDirectory = GetLogDirectory(root, LocalDataFolderName);
        Directory.CreateDirectory(logDirectory);

        var marker = Path.Combine(root, LocalDataFolderName, LegacyMigrationMarkerName);
        if (!File.Exists(marker)
            && CopyLegacyLogs(GetLogDirectory(root, LegacyLocalDataFolderName), logDirectory))
        {
            TryWriteMarker(marker);
        }

        return logDirectory;
    }

    public static int PruneOldLogs(string logDirectory, DateTime utcNow, int retentionDays = LogRetentionDays)
    {
        var cutoff = utcNow.AddDays(-retentionDays);
        var deleted = 0;
        try
        {
            if (!Directory.Exists(logDirectory))
            {
                return 0;
            }

            foreach (var log in new DirectoryInfo(logDirectory).EnumerateFiles("*.log"))
            {
                if (!IsPrunableLog(log.Name) || log.LastWriteTimeUtc >= cutoff)
                {
                    continue;
                }

                try
                {
                    log.Delete();
                    deleted++;
                }
                catch (IOException)
                {
                    // The log may still be open; it is retried at the next startup.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
            // Retention is best-effort and must not prevent the application from starting.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return deleted;
    }

    private static string GetLogDirectory(string root, string folderName) =>
        Path.Combine(root, folderName, "Logs");

    private static bool IsPrunableLog(string fileName) =>
        fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
        && PrunableLogPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool CopyLegacyLogs(string legacyDirectory, string logDirectory)
    {
        if (!Directory.Exists(legacyDirectory))
        {
            return false;
        }

        try
        {
            foreach (var legacyLog in Directory.EnumerateFiles(legacyDirectory, "*.log"))
            {
                var destination = Path.Combine(logDirectory, Path.GetFileName(legacyLog));
                if (!File.Exists(destination))
                {
                    File.Copy(legacyLog, destination);
                }
            }

            return true;
        }
        catch (IOException)
        {
            // Log migration is best-effort and must not prevent the application from starting.
        }
        catch (UnauthorizedAccessException)
        {
            // The new log directory remains available even if legacy logs cannot be read.
        }

        return false;
    }

    private static void TryWriteMarker(string marker)
    {
        try
        {
            File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (IOException)
        {
            // Without the marker, migration simply runs again next time.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
