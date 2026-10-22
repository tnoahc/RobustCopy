use super::models::{CopyJobResult, CopyOutcome};

pub fn interpret(exit_code: i32, log_path: &str, canceled: bool) -> CopyJobResult {
    let result = |outcome, summary: &str| CopyJobResult {
        exit_code,
        outcome,
        summary: summary.to_owned(),
        log_path: log_path.to_owned(),
    };

    if canceled {
        return result(CopyOutcome::Canceled, "The copy was stopped by the user.");
    }

    if exit_code < 0 {
        return result(CopyOutcome::Failure, &format!("Robocopy terminated unexpectedly (exit code {exit_code})."));
    }

    if exit_code >= 8 {
        return result(CopyOutcome::Failure, "Robocopy reported one or more copy failures.");
    }

    let extras = exit_code & 2 != 0;
    let mismatches = exit_code & 4 != 0;
    let outcome = if extras || mismatches { CopyOutcome::SuccessWithWarnings } else { CopyOutcome::Success };
    let summary = match exit_code {
        0 => "Everything is already up to date.",
        1 => "All selected files were copied successfully.",
        2 => "No files were copied; extra items exist in the destination.",
        3 => "Files were copied; extra items also exist in the destination.",
        4 => "No files were copied; mismatched items were detected.",
        5 => "Files were copied; mismatched items were detected.",
        6 => "Extra and mismatched items were detected; no files were copied.",
        _ => "Files were copied; extra and mismatched items were detected.",
    };
    result(outcome, summary)
}
