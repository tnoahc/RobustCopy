use super::runner::CopyRunner;
use crate::core::format;
use crate::core::models::{CopyJobRequest, CopyJobStage, CopyOutcome, RunError};
use crate::core::validator;

/// What the job workflow needs from the GUI while it runs on a worker thread.
pub trait JobHost {
    fn set_status(&self, text: &str);
    fn set_totals(&self, total_bytes: u64, total_files: usize);
    /// Asks the user to confirm Mirror, Purge, or Move. Blocks until they answer.
    fn confirm_destructive(&self, destination: &str, planned_deletes: usize, moves_source_items: bool) -> bool;
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct JobOutcome {
    pub stage: CopyJobStage,
    pub status: String,
    pub log_path: String,
    /// An error to show the user in a dialog.
    pub error: Option<String>,
}

/// Runs the pre-scan, confirms destructive modes, then runs the copy.
/// The request must already have passed validation.
pub fn run_job(runner: &dyn CopyRunner, request: &CopyJobRequest, host: &dyn JobHost) -> JobOutcome {
    let finish = |stage, status: &str, error| JobOutcome {
        stage,
        status: status.to_owned(),
        log_path: runner.current_log_path(),
        error,
    };

    let scan = match runner.scan(request) {
        Ok(scan) => scan,
        Err(RunError::Canceled(_)) => return finish(CopyJobStage::Canceled, "Operation canceled", None),
        Err(RunError::Failed(message)) => return finish(CopyJobStage::Failed, "The operation failed", Some(message)),
    };

    host.set_totals(scan.total_bytes, scan.file_count);
    if validator::is_destructive(request) {
        let has = |ids: &[&str]| request.options.iter().any(|option| ids.contains(&option.id.as_str()));
        let moves = has(&["MoveFiles", "MoveTree"]);
        // Robocopy lists *EXTRA items in /L mode regardless of switches; only /MIR and /PURGE delete them.
        let planned_deletes = if has(&["Mirror", "Purge"]) { scan.planned_delete_count } else { 0 };
        if !host.confirm_destructive(&request.destination_path, planned_deletes, moves) {
            return finish(CopyJobStage::Canceled, "Copy canceled before any changes were made", None);
        }
    }

    host.set_status(&if scan.file_count == 0 {
        "Checking destination…".to_owned()
    } else {
        format!("Copying {} file(s)…", format::grouped(scan.file_count))
    });

    match runner.start(request, &scan) {
        Ok(result) => {
            let stage = match result.outcome {
                CopyOutcome::Failure => CopyJobStage::Failed,
                CopyOutcome::Canceled => CopyJobStage::Canceled,
                CopyOutcome::Success | CopyOutcome::SuccessWithWarnings => CopyJobStage::Completed,
            };
            finish(stage, &result.summary, None)
        }
        Err(RunError::Canceled(_)) => finish(CopyJobStage::Canceled, "Operation canceled", None),
        Err(RunError::Failed(message)) => finish(CopyJobStage::Failed, "The operation failed", Some(message)),
    }
}

/// The confirmation text shown before a destructive run.
pub fn destructive_confirmation_message(destination: &str, planned_deletes: usize, moves_source_items: bool) -> String {
    let mut effects = Vec::new();
    if planned_deletes > 0 {
        effects.push(format!(
            "• Up to {} extra destination item(s) are scheduled for deletion.",
            format::grouped(planned_deletes)
        ));
    }

    if moves_source_items {
        effects.push("• Successfully copied source items will be deleted from the source.".to_owned());
    }

    if effects.is_empty() {
        effects.push(
            "• This mode is allowed to delete or move data even if the pre-scan found no current deletions.".to_owned(),
        );
    }

    format!(
        "Review this destructive operation carefully.\n\nDestination:\n{destination}\n\n{}\n\nContinue?",
        effects.join("\n")
    )
}
