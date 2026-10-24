use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};

use super::app_identity;
use crate::NEWLINE;

/// Writes a crash report and returns its path, or `None` when the report could not be written.
/// This is called from the panic hook, so it must never panic.
pub fn write(details: &str, source: &str, directory: Option<&Path>) -> Option<PathBuf> {
    let log_directory = directory.map(Path::to_path_buf).unwrap_or_else(app_identity::log_directory);
    fs::create_dir_all(&log_directory).ok()?;

    let timestamp = chrono::Utc::now();
    let path = log_directory.join(format!("crash-{}.log", timestamp.format("%Y%m%d-%H%M%S")));
    let report = format!(
        "{name} crash report{NEWLINE}Time (UTC): {time}{NEWLINE}Version: {version}{NEWLINE}Source: {source}{NEWLINE}{details}{NEWLINE}{NEWLINE}",
        name = app_identity::DISPLAY_NAME,
        time = timestamp.to_rfc3339_opts(chrono::SecondsFormat::Micros, true),
        version = app_identity::VERSION,
    );

    // Append so that several failures within the same second share one report.
    let mut file = OpenOptions::new().create(true).append(true).open(&path).ok()?;
    file.write_all(report.as_bytes()).ok()?;
    Some(path)
}
