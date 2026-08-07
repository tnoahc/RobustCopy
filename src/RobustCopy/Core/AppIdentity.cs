using System.IO;

namespace RobustCopy.Core;

public static class AppIdentity
{
    public const string DisplayName = "RobustCopy";
    public const string LocalDataFolderName = "RobustCopy";
    public const string LegacyLocalDataFolderName = "RoboCopyGUI";

    public static string LogDirectory => GetLogDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LocalDataFolderName);

    public static string EnsureLogDirectory(string? localDataRoot = null)
    {
        var root = localDataRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDirectory = GetLogDirectory(root, LocalDataFolderName);
        Directory.CreateDirectory(logDirectory);

        CopyLegacyLogs(GetLogDirectory(root, LegacyLocalDataFolderName), logDirectory);
        return logDirectory;
    }

    private static string GetLogDirectory(string root, string folderName) =>
        Path.Combine(root, folderName, "Logs");

    private static void CopyLegacyLogs(string legacyDirectory, string logDirectory)
    {
        if (!Directory.Exists(legacyDirectory))
        {
            return;
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
        }
        catch (IOException)
        {
            // Log migration is best-effort and must not prevent the application from starting.
        }
        catch (UnauthorizedAccessException)
        {
            // The new log directory remains available even if legacy logs cannot be read.
        }
    }
}
