use std::fs;
use std::path::{Path, PathBuf};
use std::time::{Duration, SystemTime};

pub const DISPLAY_NAME: &str = "RobustCopy";
pub const LOCAL_DATA_FOLDER_NAME: &str = "RobustCopy";
pub const LEGACY_LOCAL_DATA_FOLDER_NAME: &str = "RoboCopyGUI";
pub const LOG_RETENTION_DAYS: u64 = 90;
pub const LEGACY_MIGRATION_MARKER_NAME: &str = ".legacy-migrated";
pub const VERSION: &str = env!("CARGO_PKG_VERSION");

const PRUNABLE_LOG_PREFIXES: [&str; 2] = ["robocopy-", "crash-"];

/// `%LocalAppData%`, falling back to the temporary directory when it is not set.
pub fn local_data_root() -> PathBuf {
    std::env::var_os("LOCALAPPDATA").map(PathBuf::from).unwrap_or_else(std::env::temp_dir)
}

pub fn log_directory() -> PathBuf {
    log_directory_in(&local_data_root(), LOCAL_DATA_FOLDER_NAME)
}

/// Creates the log directory and, once, copies logs from the legacy RoboCopyGUI folder into it.
pub fn ensure_log_directory(local_data_root: Option<&Path>) -> std::io::Result<PathBuf> {
    let root = local_data_root.map(Path::to_path_buf).unwrap_or_else(self::local_data_root);
    let log_directory = log_directory_in(&root, LOCAL_DATA_FOLDER_NAME);
    fs::create_dir_all(&log_directory)?;

    let marker = root.join(LOCAL_DATA_FOLDER_NAME).join(LEGACY_MIGRATION_MARKER_NAME);
    if !marker.exists() && copy_legacy_logs(&log_directory_in(&root, LEGACY_LOCAL_DATA_FOLDER_NAME), &log_directory) {
        // Without the marker, migration simply runs again next time.
        let _ = fs::write(&marker, chrono::Utc::now().to_rfc3339());
    }

    Ok(log_directory)
}

/// Deletes expired `robocopy-*.log` and `crash-*.log` files. Best-effort: it never fails, and
/// logs that are still open are skipped and retried at the next startup.
pub fn prune_old_logs(log_directory: &Path, now: SystemTime, retention_days: u64) -> usize {
    let cutoff = now.checked_sub(Duration::from_secs(retention_days * 24 * 60 * 60)).unwrap_or(SystemTime::UNIX_EPOCH);
    let Ok(entries) = fs::read_dir(log_directory) else {
        return 0;
    };

    let mut deleted = 0;
    for entry in entries.flatten() {
        let name = entry.file_name().to_string_lossy().into_owned();
        let Ok(metadata) = entry.metadata() else { continue };
        if !metadata.is_file() || !is_prunable_log(&name) {
            continue;
        }

        if metadata.modified().is_ok_and(|modified| modified < cutoff) && fs::remove_file(entry.path()).is_ok() {
            deleted += 1;
        }
    }

    deleted
}

fn log_directory_in(root: &Path, folder_name: &str) -> PathBuf {
    root.join(folder_name).join("Logs")
}

fn is_prunable_log(file_name: &str) -> bool {
    let lower = file_name.to_ascii_lowercase();
    lower.ends_with(".log") && PRUNABLE_LOG_PREFIXES.iter().any(|prefix| lower.starts_with(prefix))
}

fn copy_legacy_logs(legacy_directory: &Path, log_directory: &Path) -> bool {
    let Ok(entries) = fs::read_dir(legacy_directory) else {
        return false;
    };

    for entry in entries {
        let Ok(entry) = entry else { return false };
        let path = entry.path();
        if !path.extension().is_some_and(|extension| extension.eq_ignore_ascii_case("log")) {
            continue;
        }

        let destination = log_directory.join(entry.file_name());
        if !destination.exists() && fs::copy(&path, &destination).is_err() {
            // The new log directory remains available even if legacy logs cannot be read.
            return false;
        }
    }

    true
}
