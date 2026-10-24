#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum OptionValueKind {
    None,
    Integer,
    Text,
    List,
    OptionalText,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum OptionArgumentStyle {
    Switch,
    ColonValue,
    SeparateList,
    OptionalColonValue,
}

#[derive(Debug, PartialEq, Eq)]
pub struct RobocopyOptionDefinition {
    pub id: &'static str,
    pub label: &'static str,
    pub flag: &'static str,
    pub description: &'static str,
    pub category: &'static str,
    pub value_kind: OptionValueKind,
    pub argument_style: OptionArgumentStyle,
    pub default_selected: bool,
    pub default_value: &'static str,
    pub is_destructive: bool,
    pub conflicts_with: &'static [&'static str],
}

pub const CATEGORIES: [&str; 6] =
    ["Folders", "Reliability", "Metadata", "Filters", "Performance", "Destructive operations"];

const fn switch(
    id: &'static str,
    label: &'static str,
    flag: &'static str,
    description: &'static str,
    category: &'static str,
) -> RobocopyOptionDefinition {
    RobocopyOptionDefinition {
        id,
        label,
        flag,
        description,
        category,
        value_kind: OptionValueKind::None,
        argument_style: OptionArgumentStyle::Switch,
        default_selected: false,
        default_value: "",
        is_destructive: false,
        conflicts_with: &[],
    }
}

const fn valued(
    id: &'static str,
    label: &'static str,
    flag: &'static str,
    description: &'static str,
    category: &'static str,
    value_kind: OptionValueKind,
    argument_style: OptionArgumentStyle,
) -> RobocopyOptionDefinition {
    RobocopyOptionDefinition { value_kind, argument_style, ..switch(id, label, flag, description, category) }
}

const fn selected(definition: RobocopyOptionDefinition) -> RobocopyOptionDefinition {
    RobocopyOptionDefinition { default_selected: true, ..definition }
}

const fn default_value(definition: RobocopyOptionDefinition, value: &'static str) -> RobocopyOptionDefinition {
    RobocopyOptionDefinition { default_value: value, ..definition }
}

const fn destructive(definition: RobocopyOptionDefinition) -> RobocopyOptionDefinition {
    RobocopyOptionDefinition { is_destructive: true, ..definition }
}

const fn conflicts(definition: RobocopyOptionDefinition, ids: &'static [&'static str]) -> RobocopyOptionDefinition {
    RobocopyOptionDefinition { conflicts_with: ids, ..definition }
}

use OptionArgumentStyle::{ColonValue, OptionalColonValue, SeparateList};
use OptionValueKind::{Integer, List, OptionalText, Text};

pub static ALL: [RobocopyOptionDefinition; 33] = [
    conflicts(
        switch("Subdirectories", "Subdirectories", "/S", "Copy subdirectories, excluding empty ones.", "Folders"),
        &["EmptySubdirectories", "Mirror"],
    ),
    conflicts(
        selected(switch(
            "EmptySubdirectories",
            "Include empty folders",
            "/E",
            "Copy all subdirectories, including empty ones.",
            "Folders",
        )),
        &["Subdirectories", "Mirror"],
    ),
    default_value(
        valued(
            "Levels",
            "Depth limit",
            "/LEV",
            "Only copy the specified number of directory levels.",
            "Folders",
            Integer,
            ColonValue,
        ),
        "1",
    ),
    switch(
        "ExcludeJunctions",
        "Exclude junctions",
        "/XJ",
        "Do not follow symbolic links or junction points.",
        "Folders",
    ),
    switch(
        "SymbolicLinks",
        "Copy symbolic links",
        "/SL",
        "Copy symbolic links as links rather than following their targets.",
        "Folders",
    ),
    switch(
        "Junctions",
        "Copy junctions",
        "/SJ",
        "Copy junctions as junctions rather than following their targets.",
        "Folders",
    ),
    conflicts(
        selected(switch(
            "Restartable",
            "Restartable mode",
            "/Z",
            "Allow interrupted file transfers to continue from their last checkpoint.",
            "Reliability",
        )),
        &["Backup", "RestartableBackup"],
    ),
    conflicts(
        switch(
            "Backup",
            "Backup mode",
            "/B",
            "Override file and folder ACLs when the current account has backup rights.",
            "Reliability",
        ),
        &["Restartable", "RestartableBackup"],
    ),
    conflicts(
        switch(
            "RestartableBackup",
            "Restartable + backup",
            "/ZB",
            "Use restartable mode, falling back to backup mode after access is denied.",
            "Reliability",
        ),
        &["Restartable", "Backup"],
    ),
    default_value(
        selected(valued(
            "Retries",
            "Retry count",
            "/R",
            "Number of retries after a failed copy. The GUI uses a safe default instead of Robocopy's one million.",
            "Reliability",
            Integer,
            ColonValue,
        )),
        "3",
    ),
    default_value(
        selected(valued(
            "Wait",
            "Retry wait (seconds)",
            "/W",
            "Seconds to wait between retry attempts.",
            "Reliability",
            Integer,
            ColonValue,
        )),
        "5",
    ),
    conflicts(
        default_value(
            valued(
                "FileMetadata",
                "File metadata",
                "/COPY",
                "Metadata flags: D data, A attributes, T timestamps, S ACLs, O owner, U auditing, X skip alternate streams.",
                "Metadata",
                Text,
                ColonValue,
            ),
            "DAT",
        ),
        &["CopyAll", "Security"],
    ),
    default_value(
        valued(
            "DirectoryMetadata",
            "Directory metadata",
            "/DCOPY",
            "Directory flags: D data, A attributes, T timestamps, E extended attributes, X skip alternate streams.",
            "Metadata",
            Text,
            ColonValue,
        ),
        "DA",
    ),
    conflicts(
        switch(
            "Security",
            "Copy security",
            "/SEC",
            "Copy file data, attributes, timestamps, and NTFS ACLs.",
            "Metadata",
        ),
        &["CopyAll", "FileMetadata"],
    ),
    conflicts(
        switch(
            "CopyAll",
            "Copy all metadata",
            "/COPYALL",
            "Copy data, attributes, timestamps, ACLs, owner, and auditing information.",
            "Metadata",
        ),
        &["Security", "FileMetadata"],
    ),
    valued(
        "ExcludeFiles",
        "Exclude files",
        "/XF",
        "Space-separated names, paths, or wildcard patterns to exclude.",
        "Filters",
        List,
        SeparateList,
    ),
    valued(
        "ExcludeDirectories",
        "Exclude folders",
        "/XD",
        "Space-separated directory names or paths to exclude.",
        "Filters",
        List,
        SeparateList,
    ),
    valued(
        "MaxSize",
        "Maximum size (bytes)",
        "/MAX",
        "Exclude files larger than this many bytes.",
        "Filters",
        Integer,
        ColonValue,
    ),
    valued(
        "MinSize",
        "Minimum size (bytes)",
        "/MIN",
        "Exclude files smaller than this many bytes.",
        "Filters",
        Integer,
        ColonValue,
    ),
    valued(
        "MaxAge",
        "Maximum age",
        "/MAXAGE",
        "Exclude files older than this many days or YYYYMMDD date.",
        "Filters",
        Integer,
        ColonValue,
    ),
    valued(
        "MinAge",
        "Minimum age",
        "/MINAGE",
        "Exclude files newer than this many days or YYYYMMDD date.",
        "Filters",
        Integer,
        ColonValue,
    ),
    switch(
        "ExcludeOlder",
        "Exclude older",
        "/XO",
        "Exclude source files older than matching destination files.",
        "Filters",
    ),
    switch(
        "ExcludeNewer",
        "Exclude newer",
        "/XN",
        "Exclude source files newer than matching destination files.",
        "Filters",
    ),
    switch(
        "ExcludeChanged",
        "Exclude changed",
        "/XC",
        "Exclude existing files with the same timestamp but different size.",
        "Filters",
    ),
    switch("Unbuffered", "Unbuffered I/O", "/J", "Use unbuffered I/O; useful for large files.", "Performance"),
    conflicts(
        default_value(
            valued(
                "MultiThreaded",
                "Multi-threaded",
                "/MT",
                "Copy with 1–128 worker threads. Overall progress becomes estimated.",
                "Performance",
                Integer,
                ColonValue,
            ),
            "8",
        ),
        &["InterPacketGap", "LowFreeSpace", "EfsRaw"],
    ),
    conflicts(
        valued(
            "InterPacketGap",
            "Inter-packet gap",
            "/IPG",
            "Milliseconds to wait between packets to reduce bandwidth usage.",
            "Performance",
            Integer,
            ColonValue,
        ),
        &["MultiThreaded"],
    ),
    conflicts(
        valued(
            "LowFreeSpace",
            "Low free-space mode",
            "/LFSM",
            "Pause Robocopy when destination free space reaches a floor; optionally enter a value such as 2G.",
            "Performance",
            OptionalText,
            OptionalColonValue,
        ),
        &["MultiThreaded", "EfsRaw"],
    ),
    conflicts(
        switch("EfsRaw", "EFS raw mode", "/EFSRAW", "Copy encrypted files in EFS raw mode.", "Performance"),
        &["MultiThreaded", "LowFreeSpace"],
    ),
    conflicts(
        destructive(switch(
            "Purge",
            "Purge destination extras",
            "/PURGE",
            "Delete destination items that no longer exist in the source.",
            "Destructive operations",
        )),
        &["Mirror"],
    ),
    conflicts(
        destructive(switch(
            "Mirror",
            "Mirror directory tree",
            "/MIR",
            "Mirror the source tree, including empty folders, and delete destination extras.",
            "Destructive operations",
        )),
        &["Purge", "Subdirectories", "EmptySubdirectories", "MoveFiles", "MoveTree"],
    ),
    conflicts(
        destructive(switch(
            "MoveFiles",
            "Move files",
            "/MOV",
            "Delete each source file after it is copied successfully.",
            "Destructive operations",
        )),
        &["MoveTree", "Mirror"],
    ),
    conflicts(
        destructive(switch(
            "MoveTree",
            "Move files and folders",
            "/MOVE",
            "Delete source files and directories after they are copied successfully.",
            "Destructive operations",
        )),
        &["MoveFiles", "Mirror"],
    ),
];

pub fn get(id: &str) -> Result<&'static RobocopyOptionDefinition, String> {
    ALL.iter()
        .find(|option| option.id.eq_ignore_ascii_case(id))
        .ok_or_else(|| format!("Unknown Robocopy option '{id}'."))
}

impl RobocopyOptionDefinition {
    pub fn conflicts_with(&self, other: &RobocopyOptionDefinition) -> bool {
        self.id != other.id
            && (self.conflicts_with.iter().any(|id| id.eq_ignore_ascii_case(other.id))
                || other.conflicts_with.iter().any(|id| id.eq_ignore_ascii_case(self.id)))
    }
}
