using System.Collections.ObjectModel;

namespace RobustCopy.Core;

public enum OptionValueKind
{
    None,
    Integer,
    Text,
    List,
    OptionalText
}

public enum OptionArgumentStyle
{
    Switch,
    ColonValue,
    SeparateList,
    OptionalColonValue
}

public sealed record RobocopyOptionDefinition(
    string Id,
    string Label,
    string Flag,
    string Description,
    string Category,
    OptionValueKind ValueKind = OptionValueKind.None,
    OptionArgumentStyle ArgumentStyle = OptionArgumentStyle.Switch,
    bool DefaultSelected = false,
    string DefaultValue = "",
    bool IsDestructive = false,
    params string[] ConflictsWith);

public static class RobocopyOptionCatalog
{
    public static readonly ReadOnlyCollection<RobocopyOptionDefinition> All = Array.AsReadOnly<RobocopyOptionDefinition>(
    [
        new("Subdirectories", "Subdirectories", "/S", "Copy subdirectories, excluding empty ones.", "Folders", ConflictsWith: ["EmptySubdirectories", "Mirror"]),
        new("EmptySubdirectories", "Include empty folders", "/E", "Copy all subdirectories, including empty ones.", "Folders", DefaultSelected: true, ConflictsWith: ["Subdirectories", "Mirror"]),
        new("Levels", "Depth limit", "/LEV", "Only copy the specified number of directory levels.", "Folders", OptionValueKind.Integer, OptionArgumentStyle.ColonValue, DefaultValue: "1"),
        new("ExcludeJunctions", "Exclude junctions", "/XJ", "Do not follow symbolic links or junction points.", "Folders"),
        new("SymbolicLinks", "Copy symbolic links", "/SL", "Copy symbolic links as links rather than following their targets.", "Folders"),
        new("Junctions", "Copy junctions", "/SJ", "Copy junctions as junctions rather than following their targets.", "Folders"),

        new("Restartable", "Restartable mode", "/Z", "Allow interrupted file transfers to continue from their last checkpoint.", "Reliability", DefaultSelected: true, ConflictsWith: ["Backup", "RestartableBackup"]),
        new("Backup", "Backup mode", "/B", "Override file and folder ACLs when the current account has backup rights.", "Reliability", ConflictsWith: ["Restartable", "RestartableBackup"]),
        new("RestartableBackup", "Restartable + backup", "/ZB", "Use restartable mode, falling back to backup mode after access is denied.", "Reliability", ConflictsWith: ["Restartable", "Backup"]),
        new("Retries", "Retry count", "/R", "Number of retries after a failed copy. The GUI uses a safe default instead of Robocopy's one million.", "Reliability", OptionValueKind.Integer, OptionArgumentStyle.ColonValue, true, "3"),
        new("Wait", "Retry wait (seconds)", "/W", "Seconds to wait between retry attempts.", "Reliability", OptionValueKind.Integer, OptionArgumentStyle.ColonValue, true, "5"),

        new("FileMetadata", "File metadata", "/COPY", "Metadata flags: D data, A attributes, T timestamps, S ACLs, O owner, U auditing, X skip alternate streams.", "Metadata", OptionValueKind.Text, OptionArgumentStyle.ColonValue, DefaultValue: "DAT", ConflictsWith: ["CopyAll", "Security"]),
        new("DirectoryMetadata", "Directory metadata", "/DCOPY", "Directory flags: D data, A attributes, T timestamps, E extended attributes, X skip alternate streams.", "Metadata", OptionValueKind.Text, OptionArgumentStyle.ColonValue, DefaultValue: "DA"),
        new("Security", "Copy security", "/SEC", "Copy file data, attributes, timestamps, and NTFS ACLs.", "Metadata", ConflictsWith: ["CopyAll", "FileMetadata"]),
        new("CopyAll", "Copy all metadata", "/COPYALL", "Copy data, attributes, timestamps, ACLs, owner, and auditing information.", "Metadata", ConflictsWith: ["Security", "FileMetadata"]),

        new("ExcludeFiles", "Exclude files", "/XF", "Space-separated names, paths, or wildcard patterns to exclude.", "Filters", OptionValueKind.List, OptionArgumentStyle.SeparateList),
        new("ExcludeDirectories", "Exclude folders", "/XD", "Space-separated directory names or paths to exclude.", "Filters", OptionValueKind.List, OptionArgumentStyle.SeparateList),
        new("MaxSize", "Maximum size (bytes)", "/MAX", "Exclude files larger than this many bytes.", "Filters", OptionValueKind.Integer, OptionArgumentStyle.ColonValue),
        new("MinSize", "Minimum size (bytes)", "/MIN", "Exclude files smaller than this many bytes.", "Filters", OptionValueKind.Integer, OptionArgumentStyle.ColonValue),
        new("MaxAge", "Maximum age", "/MAXAGE", "Exclude files older than this many days or YYYYMMDD date.", "Filters", OptionValueKind.Integer, OptionArgumentStyle.ColonValue),
        new("MinAge", "Minimum age", "/MINAGE", "Exclude files newer than this many days or YYYYMMDD date.", "Filters", OptionValueKind.Integer, OptionArgumentStyle.ColonValue),
        new("ExcludeOlder", "Exclude older", "/XO", "Exclude source files older than matching destination files.", "Filters"),
        new("ExcludeNewer", "Exclude newer", "/XN", "Exclude source files newer than matching destination files.", "Filters"),
        new("ExcludeChanged", "Exclude changed", "/XC", "Exclude existing files with the same timestamp but different size.", "Filters"),

        new("Unbuffered", "Unbuffered I/O", "/J", "Use unbuffered I/O; useful for large files.", "Performance"),
        new("MultiThreaded", "Multi-threaded", "/MT", "Copy with 1–128 worker threads. Overall progress becomes estimated.", "Performance", OptionValueKind.Integer, OptionArgumentStyle.ColonValue, DefaultValue: "8", ConflictsWith: ["InterPacketGap", "LowFreeSpace", "EfsRaw"]),
        new("InterPacketGap", "Inter-packet gap", "/IPG", "Milliseconds to wait between packets to reduce bandwidth usage.", "Performance", OptionValueKind.Integer, OptionArgumentStyle.ColonValue, ConflictsWith: ["MultiThreaded"]),
        new("LowFreeSpace", "Low free-space mode", "/LFSM", "Pause Robocopy when destination free space reaches a floor; optionally enter a value such as 2G.", "Performance", OptionValueKind.OptionalText, OptionArgumentStyle.OptionalColonValue, ConflictsWith: ["MultiThreaded", "EfsRaw"]),
        new("EfsRaw", "EFS raw mode", "/EFSRAW", "Copy encrypted files in EFS raw mode.", "Performance", ConflictsWith: ["MultiThreaded", "LowFreeSpace"]),

        new("Purge", "Purge destination extras", "/PURGE", "Delete destination items that no longer exist in the source.", "Destructive operations", IsDestructive: true, ConflictsWith: ["Mirror"]),
        new("Mirror", "Mirror directory tree", "/MIR", "Mirror the source tree, including empty folders, and delete destination extras.", "Destructive operations", IsDestructive: true, ConflictsWith: ["Purge", "Subdirectories", "EmptySubdirectories", "MoveFiles", "MoveTree"]),
        new("MoveFiles", "Move files", "/MOV", "Delete each source file after it is copied successfully.", "Destructive operations", IsDestructive: true, ConflictsWith: ["MoveTree", "Mirror"]),
        new("MoveTree", "Move files and folders", "/MOVE", "Delete source files and directories after they are copied successfully.", "Destructive operations", IsDestructive: true, ConflictsWith: ["MoveFiles", "Mirror"])
    ]);

    public static RobocopyOptionDefinition Get(string id) =>
        All.FirstOrDefault(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown Robocopy option '{id}'.", nameof(id));
}
