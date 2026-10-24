use std::fmt;
use std::time::Duration;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
pub enum CopyJobStage {
    #[default]
    Idle,
    Scanning,
    Running,
    Paused,
    Stopping,
    Completed,
    Failed,
    Canceled,
}

impl CopyJobStage {
    pub fn is_busy(self) -> bool {
        matches!(self, Self::Scanning | Self::Running | Self::Paused | Self::Stopping)
    }

    pub fn as_upper(self) -> &'static str {
        match self {
            Self::Idle => "IDLE",
            Self::Scanning => "SCANNING",
            Self::Running => "RUNNING",
            Self::Paused => "PAUSED",
            Self::Stopping => "STOPPING",
            Self::Completed => "COMPLETED",
            Self::Failed => "FAILED",
            Self::Canceled => "CANCELED",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum CopyOutcome {
    Success,
    SuccessWithWarnings,
    Failure,
    Canceled,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CopyOptionValue {
    pub id: String,
    pub value: Option<String>,
}

impl CopyOptionValue {
    pub fn new(id: &str) -> Self {
        Self { id: id.to_owned(), value: None }
    }

    pub fn with_value(id: &str, value: &str) -> Self {
        Self { id: id.to_owned(), value: Some(value.to_owned()) }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CopyJobRequest {
    pub source_path: String,
    pub destination_path: String,
    pub file_patterns: String,
    pub options: Vec<CopyOptionValue>,
    pub advanced_options: String,
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct ScanResult {
    pub total_bytes: u64,
    pub file_count: usize,
    pub planned_delete_count: usize,
    pub preview_lines: Vec<String>,
}

#[derive(Clone, Debug, PartialEq)]
pub struct CopyJobProgress {
    pub stage: CopyJobStage,
    pub overall_percent: f64,
    pub current_file_percent: f64,
    pub current_file: String,
    pub bytes_copied: u64,
    pub total_bytes: u64,
    pub bytes_per_second: f64,
    pub estimated_remaining: Option<Duration>,
    pub files_completed: usize,
    pub total_files: usize,
    pub is_estimated: bool,
}

impl CopyJobProgress {
    /// A progress report that carries only a stage, a message, and the planned totals.
    pub fn status(
        stage: CopyJobStage,
        message: &str,
        total_bytes: u64,
        total_files: usize,
        is_estimated: bool,
    ) -> Self {
        Self {
            stage,
            overall_percent: 0.0,
            current_file_percent: 0.0,
            current_file: message.to_owned(),
            bytes_copied: 0,
            total_bytes,
            bytes_per_second: 0.0,
            estimated_remaining: None,
            files_completed: 0,
            total_files,
            is_estimated,
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CopyJobResult {
    pub exit_code: i32,
    pub outcome: CopyOutcome,
    pub summary: String,
    pub log_path: String,
}

/// Why a scan or copy did not produce a result.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum RunError {
    /// The user stopped the operation.
    Canceled(String),
    /// The request was rejected or Robocopy could not run or failed.
    Failed(String),
}

impl fmt::Display for RunError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Canceled(message) | Self::Failed(message) => f.write_str(message),
        }
    }
}

impl std::error::Error for RunError {}
