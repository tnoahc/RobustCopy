using System.IO;

namespace RoboCopyGui.Core;

public static class AppIdentity
{
    public const string DisplayName = "RobustCopy";
    public const string LocalDataFolderName = "RobustCopy";

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LocalDataFolderName,
        "Logs");
}
