//! Ports of the platform-independent tests from the C# RobustCopy.Tests runner.

use std::cell::RefCell;
use std::collections::HashMap;
use std::fs;
use std::path::Path;
use std::sync::Mutex;
use std::time::{Duration, SystemTime};

use robustcopy::core::catalog::{self, CATEGORIES};
use robustcopy::core::command_builder::{self, ExecutionMode};
use robustcopy::core::models::*;
use robustcopy::core::{app_identity, crash_reporter, exit_codes, tokenizer, validator};
use robustcopy::services::job::{self, JobHost};
use robustcopy::services::log_tail;
use robustcopy::services::output_parser;
use robustcopy::services::runner::{self, CopyRunner};
use robustcopy::ui::model::TransferForm;

fn request(source: &str, destination: &str, options: Vec<CopyOptionValue>) -> CopyJobRequest {
    CopyJobRequest {
        source_path: source.to_owned(),
        destination_path: destination.to_owned(),
        file_patterns: "*.*".to_owned(),
        options,
        advanced_options: String::new(),
    }
}

fn plain() -> CopyJobRequest {
    request(r"C:\Source", r"D:\Destination", vec![])
}

fn errors_for(request: &CopyJobRequest) -> Vec<String> {
    validator::validate(request, false)
}

#[test]
fn application_identity_uses_robustcopy() {
    assert_eq!("RobustCopy", app_identity::DISPLAY_NAME);
    assert_eq!("RobustCopy", app_identity::LOCAL_DATA_FOLDER_NAME);
    assert!(app_identity::log_directory().ends_with(Path::new("RobustCopy").join("Logs")));
}

#[test]
fn legacy_application_logs_migrate_to_robustcopy() {
    let root = tempfile::tempdir().unwrap();
    let legacy = root.path().join(app_identity::LEGACY_LOCAL_DATA_FOLDER_NAME).join("Logs");
    fs::create_dir_all(&legacy).unwrap();
    let legacy_log = legacy.join("robocopy-legacy.log");
    fs::write(&legacy_log, "legacy transcript").unwrap();

    let migrated = app_identity::ensure_log_directory(Some(root.path())).unwrap();
    assert_eq!(root.path().join(app_identity::LOCAL_DATA_FOLDER_NAME).join("Logs"), migrated);
    assert_eq!("legacy transcript", fs::read_to_string(migrated.join("robocopy-legacy.log")).unwrap());
    assert!(legacy_log.exists(), "Legacy logs should be copied without deleting the originals.");
}

#[test]
fn crash_reporter_writes_a_best_effort_crash_log() {
    let root = tempfile::tempdir().unwrap();
    let logs = root.path().join("logs");
    let path = crash_reporter::write("crash-report-marker", "Test", Some(&logs)).expect("report written");
    assert_eq!(Some(logs.as_path()), path.parent());
    let name = path.file_name().unwrap().to_string_lossy();
    assert!(name.starts_with("crash-") && name.ends_with(".log"), "{name}");
    let report = fs::read_to_string(&path).unwrap();
    for expected in ["crash-report-marker", "Time (UTC):", "Version:", "Source: Test"] {
        assert!(report.contains(expected), "missing {expected}");
    }

    assert_eq!(None, crash_reporter::write("x", "Test", Some(Path::new("invalid\0directory"))));
}

#[test]
fn legacy_log_migration_runs_once() {
    let root = tempfile::tempdir().unwrap();
    let legacy = root.path().join(app_identity::LEGACY_LOCAL_DATA_FOLDER_NAME).join("Logs");
    fs::create_dir_all(&legacy).unwrap();
    fs::write(legacy.join("robocopy-first.log"), "first").unwrap();

    let logs = app_identity::ensure_log_directory(Some(root.path())).unwrap();
    assert!(logs.join("robocopy-first.log").exists());
    assert!(
        root.path()
            .join(app_identity::LOCAL_DATA_FOLDER_NAME)
            .join(app_identity::LEGACY_MIGRATION_MARKER_NAME)
            .exists()
    );

    fs::write(legacy.join("robocopy-second.log"), "second").unwrap();
    app_identity::ensure_log_directory(Some(root.path())).unwrap();
    assert!(!logs.join("robocopy-second.log").exists(), "Migration should not run a second time.");

    let far_future = SystemTime::now() + Duration::from_secs(10 * 365 * 24 * 3600);
    app_identity::prune_old_logs(&logs, far_future, app_identity::LOG_RETENTION_DAYS);
    assert_eq!("first", fs::read_to_string(legacy.join("robocopy-first.log")).unwrap());
    assert_eq!("second", fs::read_to_string(legacy.join("robocopy-second.log")).unwrap());
}

#[test]
fn log_retention_prunes_only_expired_application_logs() {
    let root = tempfile::tempdir().unwrap();
    let logs = root.path().join("Logs");
    fs::create_dir_all(&logs).unwrap();
    let day = Duration::from_secs(24 * 3600);
    let now = SystemTime::UNIX_EPOCH + Duration::from_secs(1_792_584_000); // 2026-10-21 12:00 UTC
    let expired = now - day * (app_identity::LOG_RETENTION_DAYS as u32 + 1);
    let recent = now - day * (app_identity::LOG_RETENTION_DAYS as u32 - 1);

    let files = [
        ("robocopy-old.log", expired, false),
        ("crash-old.log", expired, false),
        ("robocopy-new.log", recent, true),
        ("crash-new.log", recent, true),
        ("notes.txt", expired, true),
        (".legacy-migrated", expired, true),
        ("other-old.log", expired, true),
        ("robocopy-old.log.bak", expired, true),
    ];
    for (name, modified, _) in files {
        let path = logs.join(name);
        fs::write(&path, name).unwrap();
        fs::File::options().write(true).open(&path).unwrap().set_modified(modified).unwrap();
    }

    let nested = logs.join("nested");
    fs::create_dir_all(&nested).unwrap();
    let nested_log = nested.join("robocopy-nested.log");
    fs::write(&nested_log, "nested").unwrap();
    fs::File::options().write(true).open(&nested_log).unwrap().set_modified(expired).unwrap();

    assert_eq!(2, app_identity::prune_old_logs(&logs, now, app_identity::LOG_RETENTION_DAYS));
    for (name, _, kept) in files {
        assert_eq!(kept, logs.join(name).exists(), "{name}");
    }

    assert!(nested_log.exists(), "Retention should not recurse into subdirectories.");
    assert_eq!(0, app_identity::prune_old_logs(&root.path().join("missing"), now, app_identity::LOG_RETENTION_DAYS));
}

#[test]
fn argument_tokenizer_preserves_quoted_values() {
    let tokens = tokenizer::parse("*.txt \"annual report*.pdf\" /FFT").unwrap();
    assert_eq!(vec!["*.txt", "annual report*.pdf", "/FFT"], tokens);
    assert!(tokenizer::parse("\"unterminated").is_err());
}

#[test]
fn command_builder_keeps_paths_as_individual_arguments() {
    let request = request(
        r"C:\Source Folder",
        r"D:\Destination Folder",
        vec![
            CopyOptionValue::new("EmptySubdirectories"),
            CopyOptionValue::new("Restartable"),
            CopyOptionValue::with_value("Retries", "3"),
            CopyOptionValue::with_value("Wait", "5"),
        ],
    );
    let arguments = command_builder::build_arguments(&request, ExecutionMode::Copy);
    assert_eq!(r"C:\Source Folder", arguments[0]);
    assert_eq!(r"D:\Destination Folder", arguments[1]);
    for expected in ["/E", "/R:3", "/BYTES"] {
        assert!(arguments.iter().any(|argument| argument == expected), "Expected {expected}.");
    }

    assert!(!arguments.iter().any(|argument| argument.contains('"')), "Arguments must not contain shell quotes.");
}

/// Splits a command line with the CommandLineToArgvW rules for arguments after the program name.
fn split_command_line(command: &str) -> Vec<String> {
    let mut result = Vec::new();
    let chars: Vec<char> = command.chars().collect();
    let mut index = 0;
    while index < chars.len() {
        while index < chars.len() && chars[index] == ' ' {
            index += 1;
        }

        if index >= chars.len() {
            break;
        }

        let mut current = String::new();
        let mut quoted = false;
        while index < chars.len() && (quoted || chars[index] != ' ') {
            let mut backslashes = 0;
            while index < chars.len() && chars[index] == '\\' {
                backslashes += 1;
                index += 1;
            }

            if index < chars.len() && chars[index] == '"' {
                current.extend(std::iter::repeat_n('\\', backslashes / 2));
                if backslashes % 2 == 1 {
                    current.push('"');
                } else {
                    quoted = !quoted;
                }

                index += 1;
            } else {
                current.extend(std::iter::repeat_n('\\', backslashes));
                if index < chars.len() && (quoted || chars[index] != ' ') {
                    current.push(chars[index]);
                    index += 1;
                }
            }
        }

        result.push(current);
    }

    result
}

#[test]
fn command_preview_quotes_paths_so_they_round_trip() {
    let cases = [
        (r"D:\My Backups\", r"D:\", r#"robocopy.exe "D:\My Backups\\" D:\ *.* "#),
        (r"C:\Source", r"D:\Destination", r"robocopy.exe C:\Source D:\Destination *.* "),
        (r#"C:\Odd"Name"#, r#"C:\Odd\"Name\"#, r#"robocopy.exe "C:\Odd\"Name" "C:\Odd\\\"Name\\" *.* "#),
    ];
    for (source, destination, expected) in cases {
        let command =
            command_builder::build_display_command(&request(source, destination, vec![]), ExecutionMode::Copy);
        assert!(command.starts_with(expected), "Expected '{expected}...', found '{command}'.");
        let parsed = split_command_line(&command);
        assert_eq!(source, parsed[1]);
        assert_eq!(destination, parsed[2]);
    }
}

#[test]
fn validator_blocks_overlapping_paths() {
    let errors = errors_for(&request(r"C:\Data", r"C:\Data\Backup", vec![]));
    assert!(errors.iter().any(|error| error.contains("contain")), "{errors:?}");
    let errors = errors_for(&request(r"C:\Data\", r"c:\data", vec![]));
    assert!(errors.iter().any(|error| error.contains("different folders")), "{errors:?}");
    assert!(errors_for(&request(r"C:\Data", r"C:\Database", vec![])).is_empty());
}

#[test]
fn validator_reports_option_conflicts() {
    let errors = errors_for(&request(
        r"C:\Source",
        r"D:\Destination",
        vec![CopyOptionValue::new("Subdirectories"), CopyOptionValue::new("EmptySubdirectories")],
    ));
    assert_eq!(1, errors.iter().filter(|error| error.contains("cannot be combined")).count(), "{errors:?}");
}

#[test]
fn option_conflicts_reference_known_ids_and_are_symmetric() {
    for definition in &catalog::ALL {
        for conflict in definition.conflicts_with {
            let other = catalog::get(conflict)
                .unwrap_or_else(|_| panic!("'{}' conflicts with unknown '{conflict}'", definition.id));
            assert_ne!(definition.id, *conflict);
            assert!(other.conflicts_with.contains(&definition.id), "'{conflict}' does not list '{}'", definition.id);
        }
    }
}

fn low_free_space(value: &str) -> Vec<String> {
    errors_for(&request(r"C:\Source", r"D:\Destination", vec![CopyOptionValue::with_value("LowFreeSpace", value)]))
}

#[test]
fn validator_accepts_valid_low_free_space_floors() {
    for value in ["", " ", "0", "100", "2G", "2g", "512M", "512m", "10K", "007M", "18446744073709551615"] {
        assert!(low_free_space(value).is_empty(), "Expected /LFSM value '{value}' to be valid.");
    }
}

#[test]
fn validator_rejects_invalid_low_free_space_floors() {
    for value in
        ["1.5G", "1.5", ".5G", "2.G", "1,5G", "2GB", "2T", "abc", "-1G", " 2G", "2 G", "18446744073709551616", "١٢G"]
    {
        assert!(
            low_free_space(value).iter().any(|error| error.contains("Low free-space floor")),
            "Expected /LFSM value '{value}' to be rejected."
        );
    }
}

#[test]
fn validator_checks_option_values() {
    let check = |id: &str, value: &str, expected: &str| {
        let errors = errors_for(&request(r"C:\S", r"D:\D", vec![CopyOptionValue::with_value(id, value)]));
        assert!(errors.iter().any(|error| error.contains(expected)), "{id}={value}: {errors:?}");
    };
    check("Retries", "", "Enter a value");
    check("Retries", "-1", "non-negative whole number");
    check("Retries", "1.5", "non-negative whole number");
    check("Levels", "0", "at least 1");
    check("MultiThreaded", "129", "between 1 and 128");
    check("FileMetadata", "DATQ", "File metadata can only contain");
    check("DirectoryMetadata", "DS", "Directory metadata can only contain");
    assert!(
        errors_for(&request(r"C:\S", r"D:\D", vec![CopyOptionValue::with_value("FileMetadata", "datsoux")])).is_empty()
    );
}

#[test]
fn option_catalog_preserves_the_original_category_organization() {
    let expected: HashMap<&str, usize> = [
        ("Folders", 6),
        ("Reliability", 5),
        ("Metadata", 4),
        ("Filters", 9),
        ("Performance", 5),
        ("Destructive operations", 4),
    ]
    .into();
    let form = TransferForm::default();
    assert_eq!(33, form.options.len());
    let mut ids: Vec<_> = form.options.iter().map(|option| option.id()).collect();
    ids.sort_unstable();
    ids.dedup();
    assert_eq!(33, ids.len());
    for category in CATEGORIES {
        assert_eq!(
            expected[category],
            form.options.iter().filter(|option| option.definition.category == category).count()
        );
    }
}

#[test]
fn selecting_an_option_turns_off_conflicting_options() {
    let mut form = TransferForm::default();
    let no_conflict_errors = |form: &TransferForm| {
        let mut request = form.build_request();
        request.source_path = r"C:\Source".into();
        request.destination_path = r"D:\Destination".into();
        let errors = errors_for(&request);
        assert!(!errors.iter().any(|error| error.contains("cannot be combined")), "{errors:?}");
    };

    assert!(form.option("EmptySubdirectories").selected && form.option("Restartable").selected);
    assert_eq!("Selecting this turns off “Include subfolders”.", form.option("Mirror").conflict_message);
    assert_eq!("", form.option("EmptySubdirectories").conflict_message);
    no_conflict_errors(&form);

    form.set_selected("Mirror", true);
    assert!(form.option("Mirror").selected && !form.option("EmptySubdirectories").selected);
    assert_eq!("", form.option("Mirror").conflict_message);
    assert_eq!("Selecting this turns off “Mirror”.", form.option("EmptySubdirectories").conflict_message);
    no_conflict_errors(&form);

    form.set_selected("Backup", true);
    assert!(form.option("Backup").selected && !form.option("Restartable").selected);
    no_conflict_errors(&form);

    form.set_selected("EmptySubdirectories", true);
    assert!(form.option("EmptySubdirectories").selected && !form.option("Mirror").selected);
    no_conflict_errors(&form);
}

#[test]
fn validator_protects_gui_owned_advanced_flags() {
    let mut request = plain();
    request.advanced_options = "/FFT /LOG:unsafe.txt /MIR".into();
    assert_eq!(2, errors_for(&request).iter().filter(|error| error.contains("managed by the GUI")).count());
}

#[test]
fn validator_protects_dash_prefixed_gui_owned_advanced_flags() {
    let mut request = plain();
    request.advanced_options = "-FFT -MIR -purge -LOG:unsafe.txt -MOV".into();
    let errors = errors_for(&request);
    assert_eq!(4, errors.iter().filter(|error| error.contains("managed by the GUI")).count());
    assert!(errors.iter().any(|error| error.contains("'/MIR'")), "Expected -MIR to be reported as /MIR.");
}

#[test]
fn validator_rejects_switches_in_file_patterns() {
    for patterns in ["*.* /MIR", "*.txt -PURGE", "\"/MOVE\""] {
        let mut request = plain();
        request.file_patterns = patterns.into();
        assert!(errors_for(&request).iter().any(|error| error.contains("Advanced field")), "{patterns}");
    }

    let mut valid = plain();
    valid.file_patterns = "*.txt \"annual report*.pdf\"".into();
    assert!(errors_for(&valid).is_empty());
}

#[test]
fn validator_rejects_switches_in_list_options() {
    for id in ["ExcludeFiles", "ExcludeDirectories"] {
        for value in ["foo.tmp /PURGE", "cache -MIR"] {
            let request = request(r"C:\Source", r"D:\Destination", vec![CopyOptionValue::with_value(id, value)]);
            assert!(errors_for(&request).iter().any(|error| error.contains("Advanced field")), "{id} {value}");
        }

        let valid =
            request(r"C:\Source", r"D:\Destination", vec![CopyOptionValue::with_value(id, "foo.tmp \"build output\"")]);
        assert!(errors_for(&valid).is_empty());
    }
}

#[test]
fn runner_detects_dash_prefixed_multi_threading() {
    let mut request = plain();
    assert!(!runner::is_multi_threaded(&request));
    for advanced in ["/MT:8", "-MT:8", "-mt"] {
        request.advanced_options = advanced.into();
        assert!(runner::is_multi_threaded(&request), "{advanced}");
    }
}

#[test]
fn output_parser_reads_bytes_paths_percentages_and_extras() {
    let file = output_parser::parse(r"    New File              10299    C:\Source Folder\pasted-text.txt");
    assert!(file.has_file());
    assert_eq!(10299, file.bytes);
    assert_eq!(r"C:\Source Folder\pasted-text.txt", file.path);
    assert_eq!(Some(47.5), output_parser::parse("        47.5%").percent);

    let scan = output_parser::parse_scan([
        r"    New File              100    C:\Source\one.bin",
        r"    *EXTRA File           50     D:\Destination\old.bin",
        r"    *EXTRA Dir            -1     D:\Destination\old-folder\",
    ]);
    assert_eq!(100, scan.total_bytes);
    assert_eq!(1, scan.file_count);
    assert_eq!(2, scan.planned_delete_count);
}

#[test]
fn output_parser_recognizes_percent_only_segments() {
    for line in ["  12.5%", "100%", "0%", "\t  7,5 % ", "  0.0%  "] {
        assert!(output_parser::is_percent_only(line), "'{line}'");
    }

    for line in
        ["", "   ", "%", "  12.5", r"    New File   10299    C:\Data\50%.txt", "  Speed : 12 %  done", "12.5% 13%"]
    {
        assert!(!output_parser::is_percent_only(line), "'{line}'");
    }
}

#[test]
fn exit_codes_map_to_robocopy_outcomes() {
    let outcome = |code| exit_codes::interpret(code, "log", false).outcome;
    assert_eq!(CopyOutcome::Success, outcome(0));
    assert_eq!(CopyOutcome::Success, outcome(1));
    for code in 2..=7 {
        assert_eq!(CopyOutcome::SuccessWithWarnings, outcome(code));
    }

    for code in [8, 9, 16] {
        assert_eq!(CopyOutcome::Failure, outcome(code));
    }

    for code in [-1, -2, i32::MIN] {
        let abnormal = exit_codes::interpret(code, "log", false);
        assert_eq!(CopyOutcome::Failure, abnormal.outcome);
        assert_eq!(format!("Robocopy terminated unexpectedly (exit code {code})."), abnormal.summary);
    }

    assert_eq!(CopyOutcome::Canceled, exit_codes::interpret(-1, "log", true).outcome);
}

#[test]
fn log_tail_returns_a_small_log_whole() {
    let root = tempfile::tempdir().unwrap();
    let path = root.path().join("small.log");
    let content = "first line\r\nsecond line — ünïcode\r\n";
    fs::write(&path, content).unwrap();
    let tail = log_tail::read(&path, log_tail::DEFAULT_MAXIMUM_BYTES).unwrap();
    assert!(!tail.is_truncated);
    assert_eq!(content, tail.text);
}

#[test]
fn log_tail_truncates_a_large_log_at_a_line_boundary() {
    let root = tempfile::tempdir().unwrap();
    let path = root.path().join("large.log");
    let mut content = String::new();
    let mut line = 0;
    while content.len() < 6 * 1024 * 1024 {
        line += 1;
        content.push_str(&format!("line {line:07} é漢字🙂\r\n"));
    }

    fs::write(&path, &content).unwrap();
    let tail = log_tail::read(&path, log_tail::DEFAULT_MAXIMUM_BYTES).unwrap();
    assert!(tail.is_truncated);
    assert_eq!(content.len() as u64, tail.file_bytes);
    let (header, body) = tail.text.split_once(robustcopy::NEWLINE).unwrap();
    assert!(header.starts_with("[Truncated: showing the last 5 MB of "), "{header}");
    assert!(header.ends_with("Open the log for the full transcript.]"), "{header}");
    assert!(body.starts_with("line "));
    assert!(!body.contains('\u{FFFD}'));
    assert!(content.ends_with(body));
    assert!(body.len() as u64 <= log_tail::DEFAULT_MAXIMUM_BYTES);
}

#[test]
fn log_tail_reads_a_log_that_is_open_for_writing() {
    use std::io::Write;
    let root = tempfile::tempdir().unwrap();
    let path = root.path().join("open.log");
    let mut writer = fs::File::create_new(&path).unwrap();
    writer.write_all(b"still being written\r\n").unwrap();
    writer.flush().unwrap();
    let tail = log_tail::read(&path, log_tail::DEFAULT_MAXIMUM_BYTES).unwrap();
    assert_eq!("still being written\r\n", tail.text);
    assert!(!tail.is_truncated);
}

/// A runner whose scan always succeeds with fixed totals and whose copy records the call.
struct FakeRunner {
    planned_deletes: usize,
    fail_scan: bool,
    log_path: String,
    start_calls: Mutex<usize>,
}

impl FakeRunner {
    fn new(planned_deletes: usize, fail_scan: bool) -> Self {
        Self { planned_deletes, fail_scan, log_path: "fake.log".into(), start_calls: Mutex::new(0) }
    }
}

impl CopyRunner for FakeRunner {
    fn scan(&self, _request: &CopyJobRequest) -> Result<ScanResult, RunError> {
        if self.fail_scan {
            return Err(RunError::Failed("Robocopy pre-scan failed with exit code 16.".into()));
        }

        Ok(ScanResult {
            total_bytes: 100,
            file_count: 1,
            planned_delete_count: self.planned_deletes,
            preview_lines: vec![],
        })
    }

    fn start(&self, _request: &CopyJobRequest, _scan: &ScanResult) -> Result<CopyJobResult, RunError> {
        *self.start_calls.lock().unwrap() += 1;
        Ok(exit_codes::interpret(1, &self.log_path, false))
    }

    fn pause(&self) -> Result<(), RunError> {
        Ok(())
    }

    fn resume(&self) -> Result<(), RunError> {
        Ok(())
    }

    fn stop(&self) -> Result<(), RunError> {
        Ok(())
    }

    fn stage(&self) -> CopyJobStage {
        CopyJobStage::Idle
    }

    fn current_log_path(&self) -> String {
        self.log_path.clone()
    }
}

struct FakeHost {
    confirm: bool,
    confirmations: RefCell<Vec<(String, usize, bool)>>,
}

impl JobHost for FakeHost {
    fn set_status(&self, _text: &str) {}
    fn set_totals(&self, _total_bytes: u64, _total_files: usize) {}

    fn confirm_destructive(&self, destination: &str, planned_deletes: usize, moves_source_items: bool) -> bool {
        self.confirmations.borrow_mut().push((destination.to_owned(), planned_deletes, moves_source_items));
        self.confirm
    }
}

fn host(confirm: bool) -> FakeHost {
    FakeHost { confirm, confirmations: RefCell::default() }
}

#[test]
fn destructive_confirmation_reports_deletions_only_for_mirror_or_purge() {
    let cases: [(&[&str], usize, bool); 5] = [
        (&["MoveFiles"], 0, true),
        (&["MoveTree"], 0, true),
        (&["Mirror"], 3, false),
        (&["Purge"], 3, false),
        (&["Purge", "MoveFiles"], 3, true),
    ];
    for (options, expected_deletes, expected_moves) in cases {
        let runner = FakeRunner::new(3, false);
        let host = host(false);
        let request = request(r"C:\S", r"D:\D", options.iter().map(|id| CopyOptionValue::new(id)).collect());
        let outcome = job::run_job(&runner, &request, &host);
        assert_eq!(CopyJobStage::Canceled, outcome.stage);
        assert_eq!(
            vec![(r"D:\D".to_owned(), expected_deletes, expected_moves)],
            *host.confirmations.borrow(),
            "{options:?}"
        );
        assert_eq!(0, *runner.start_calls.lock().unwrap());
    }
}

#[test]
fn failed_pre_scan_still_exposes_its_transcript() {
    let runner = FakeRunner::new(0, true);
    let outcome = job::run_job(&runner, &plain(), &host(true));
    assert_eq!(CopyJobStage::Failed, outcome.stage);
    assert_eq!("fake.log", outcome.log_path);
    assert!(outcome.error.is_some());
    assert_eq!(0, *runner.start_calls.lock().unwrap());
}

#[test]
fn declined_confirmation_still_exposes_its_transcript() {
    let runner = FakeRunner::new(1, false);
    let request = request(r"C:\S", r"D:\D", vec![CopyOptionValue::new("Purge")]);
    let outcome = job::run_job(&runner, &request, &host(false));
    assert_eq!(CopyJobStage::Canceled, outcome.stage);
    assert_eq!("Copy canceled before any changes were made", outcome.status);
    assert_eq!("fake.log", outcome.log_path);
    assert_eq!(0, *runner.start_calls.lock().unwrap());
}

#[test]
fn non_destructive_job_runs_the_copy_without_confirmation() {
    let runner = FakeRunner::new(5, false);
    let host = host(false);
    let outcome = job::run_job(&runner, &plain(), &host);
    assert_eq!(CopyJobStage::Completed, outcome.stage);
    assert_eq!("All selected files were copied successfully.", outcome.status);
    assert!(host.confirmations.borrow().is_empty());
    assert_eq!(1, *runner.start_calls.lock().unwrap());
}
