//! Integration tests that drive the real `C:\Windows\System32\robocopy.exe` in temporary folders.
#![cfg(windows)]

use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex, OnceLock};
use std::thread;
use std::time::{Duration, Instant};

use robustcopy::core::app_identity;
use robustcopy::core::models::*;
use robustcopy::services::output_parser;
use robustcopy::services::runner::{CopyRunner, RobocopyRunner, RunnerEvent};

struct Fixture {
    _root: tempfile::TempDir,
    source: PathBuf,
    destination: PathBuf,
    logs: PathBuf,
}

fn fixture() -> Fixture {
    let root = tempfile::tempdir().unwrap();
    let source = root.path().join("source");
    let destination = root.path().join("destination");
    fs::create_dir_all(&source).unwrap();
    fs::create_dir_all(&destination).unwrap();
    let logs = root.path().join("logs");
    Fixture { source, destination, logs, _root: root }
}

#[derive(Default)]
struct Recorded {
    output: Vec<String>,
    progress: Vec<CopyJobProgress>,
}

fn runner(fixture: &Fixture) -> (Arc<RobocopyRunner>, Arc<Mutex<Recorded>>) {
    let recorded = Arc::new(Mutex::new(Recorded::default()));
    let sink_recorded = Arc::clone(&recorded);
    let runner = RobocopyRunner::new(
        fixture.logs.clone(),
        Arc::new(move |event| {
            let mut recorded = sink_recorded.lock().unwrap();
            match event {
                RunnerEvent::Output(line) => recorded.output.push(line),
                RunnerEvent::Progress(progress) => recorded.progress.push(progress),
            }
        }),
    );
    (Arc::new(runner), recorded)
}

fn request(fixture: &Fixture, extra: Vec<CopyOptionValue>) -> CopyJobRequest {
    let mut options = vec![
        CopyOptionValue::new("EmptySubdirectories"),
        CopyOptionValue::new("Restartable"),
        CopyOptionValue::with_value("Retries", "1"),
        CopyOptionValue::with_value("Wait", "0"),
    ];
    options.extend(extra);
    CopyJobRequest {
        source_path: fixture.source.to_string_lossy().into_owned(),
        destination_path: fixture.destination.to_string_lossy().into_owned(),
        file_patterns: "*.*".into(),
        options,
        advanced_options: String::new(),
    }
}

fn sized_file(path: &Path, length: u64) {
    fs::File::create_new(path).unwrap().set_len(length).unwrap();
}

fn ipg(milliseconds: &str) -> Vec<CopyOptionValue> {
    vec![CopyOptionValue::with_value("InterPacketGap", milliseconds)]
}

fn wait_for_stage(runner: &RobocopyRunner, expected: CopyJobStage) {
    let deadline = Instant::now() + Duration::from_secs(5);
    while runner.stage() != expected && Instant::now() < deadline {
        thread::sleep(Duration::from_millis(25));
    }

    assert_eq!(expected, runner.stage());
}

fn succeeded(result: &CopyJobResult) -> bool {
    matches!(result.outcome, CopyOutcome::Success | CopyOutcome::SuccessWithWarnings)
}

#[test]
fn robocopy_scan_and_copy() {
    let fixture = fixture();
    let content = "R".repeat(64 * 1024);
    fs::write(fixture.source.join("copy.txt"), &content).unwrap();
    let (runner, recorded) = runner(&fixture);
    let request = request(&fixture, vec![]);
    let scan = runner.scan(&request).unwrap();
    assert_eq!(1, scan.file_count, "{:?}", recorded.lock().unwrap().output);
    assert!(scan.total_bytes >= content.len() as u64);
    let result = runner.start(&request, &scan).unwrap();
    assert!(succeeded(&result), "{}", result.summary);
    assert_eq!(content, fs::read_to_string(fixture.destination.join("copy.txt")).unwrap());
    assert!(Path::new(&result.log_path).is_file());
}

#[test]
fn transcript_and_output_omit_percent_only_segments() {
    let fixture = fixture();
    sized_file(&fixture.source.join("progress.bin"), 32 * 1024 * 1024);
    let (runner, recorded) = runner(&fixture);
    let request = request(&fixture, vec![]);
    let scan = runner.scan(&request).unwrap();
    let result = runner.start(&request, &scan).unwrap();
    assert!(succeeded(&result), "{}", result.summary);

    let transcript = fs::read_to_string(&result.log_path).unwrap();
    assert!(transcript.lines().any(|line| line.to_lowercase().contains("progress.bin")));
    assert!(transcript.contains("Exit code"));
    assert!(!transcript.lines().any(output_parser::is_percent_only));
    let recorded = recorded.lock().unwrap();
    assert!(!recorded.output.iter().any(|line| output_parser::is_percent_only(line)));
    assert!(
        recorded
            .progress
            .iter()
            .any(|progress| progress.current_file_percent > 0.0 && progress.current_file_percent < 100.0)
    );
    assert_eq!(100.0, recorded.progress.last().unwrap().current_file_percent);
}

#[test]
fn transcript_is_readable_and_flushed_during_a_run() {
    let fixture = fixture();
    sized_file(&fixture.source.join("readable.bin"), 16 * 1024 * 1024);
    let (runner, _) = runner(&fixture);
    let request = request(&fixture, ipg("30"));
    let scan = runner.scan(&request).unwrap();
    let copy = thread::spawn({
        let runner = Arc::clone(&runner);
        move || runner.start(&request, &scan)
    });
    wait_for_stage(&runner, CopyJobStage::Running);
    runner.pause().unwrap();
    let log_path = runner.current_log_path();
    let transcript = fs::read_to_string(&log_path).unwrap();
    assert!(transcript.contains("Copy started"));
    assert!(transcript.contains("Copy paused by user."));
    runner.stop().unwrap();
    assert_eq!(CopyOutcome::Canceled, copy.join().unwrap().unwrap().outcome);
    assert!(fs::read_to_string(&log_path).unwrap().contains("Operation stopped by user."));
}

#[test]
fn robocopy_pause_and_resume() {
    let fixture = fixture();
    sized_file(&fixture.source.join("slow.bin"), 24 * 1024 * 1024);
    let (runner, _) = runner(&fixture);
    let request = request(&fixture, ipg("20"));
    let scan = runner.scan(&request).unwrap();
    let copy = thread::spawn({
        let runner = Arc::clone(&runner);
        move || runner.start(&request, &scan)
    });
    wait_for_stage(&runner, CopyJobStage::Running);
    thread::sleep(Duration::from_millis(250));
    runner.pause().unwrap();
    assert_eq!(CopyJobStage::Paused, runner.stage());
    thread::sleep(Duration::from_millis(150));
    runner.resume().unwrap();
    let result = copy.join().unwrap().unwrap();
    assert!(succeeded(&result), "{}", result.summary);
    assert!(fixture.destination.join("slow.bin").exists());
}

#[test]
fn robocopy_stop_and_restart() {
    let fixture = fixture();
    sized_file(&fixture.source.join("restart.bin"), 16 * 1024 * 1024);
    let (runner, _) = runner(&fixture);
    let slow = request(&fixture, ipg("30"));
    let scan = runner.scan(&slow).unwrap();
    let copy = thread::spawn({
        let runner = Arc::clone(&runner);
        move || runner.start(&slow, &scan)
    });
    wait_for_stage(&runner, CopyJobStage::Running);
    thread::sleep(Duration::from_millis(200));
    runner.stop().unwrap();
    assert_eq!(CopyOutcome::Canceled, copy.join().unwrap().unwrap().outcome);

    let restart = request(&fixture, vec![]);
    let scan = runner.scan(&restart).unwrap();
    let result = runner.start(&restart, &scan).unwrap();
    assert!(succeeded(&result), "{}", result.summary);
    assert!(fixture.destination.join("restart.bin").exists());
}

/// Builds a runner whose sink stops it the first time it reports `stage`.
fn runner_stopping_on(fixture: &Fixture, stage: CopyJobStage) -> (Arc<RobocopyRunner>, Arc<Mutex<usize>>) {
    let slot: Arc<OnceLock<std::sync::Weak<RobocopyRunner>>> = Arc::new(OnceLock::new());
    let output_lines = Arc::new(Mutex::new(0));
    let (sink_slot, sink_lines) = (Arc::clone(&slot), Arc::clone(&output_lines));
    let stopped = Mutex::new(false);
    let runner = Arc::new(RobocopyRunner::new(
        fixture.logs.clone(),
        Arc::new(move |event| match event {
            RunnerEvent::Output(_) => *sink_lines.lock().unwrap() += 1,
            RunnerEvent::Progress(progress) if progress.stage == stage => {
                let mut stopped = stopped.lock().unwrap();
                if !*stopped && let Some(runner) = sink_slot.get().and_then(std::sync::Weak::upgrade) {
                    *stopped = true;
                    runner.stop().unwrap();
                }
            }
            RunnerEvent::Progress(_) => {}
        }),
    ));
    slot.set(Arc::downgrade(&runner)).unwrap();
    (runner, output_lines)
}

#[test]
fn stop_during_copy_startup_prevents_robocopy_launch() {
    let fixture = fixture();
    for index in 0..8 {
        sized_file(&fixture.source.join(format!("startup-{index}.bin")), 4 * 1024 * 1024);
    }

    let (runner, output_lines) = runner_stopping_on(&fixture, CopyJobStage::Running);
    let request = request(&fixture, vec![]);
    let scan = runner.scan(&request).unwrap();
    *output_lines.lock().unwrap() = 0;
    let result = runner.start(&request, &scan).unwrap();
    assert_eq!(CopyOutcome::Canceled, result.outcome);
    assert_eq!(CopyJobStage::Canceled, runner.stage());
    assert_eq!(0, *output_lines.lock().unwrap());
    assert_eq!(0, fs::read_dir(&fixture.destination).unwrap().count(), "Robocopy must not copy anything after Stop.");
}

#[test]
fn stop_during_scan_startup_prevents_robocopy_launch() {
    let fixture = fixture();
    fs::write(fixture.source.join("scan.txt"), "scan").unwrap();
    let (runner, output_lines) = runner_stopping_on(&fixture, CopyJobStage::Scanning);
    let result = runner.scan(&request(&fixture, vec![]));
    assert!(matches!(result, Err(RunError::Canceled(_))), "{result:?}");
    assert_eq!(CopyJobStage::Canceled, runner.stage());
    assert_eq!(0, *output_lines.lock().unwrap());
}

#[test]
fn stop_right_after_start_leaves_no_copy_running() {
    let fixture = fixture();
    sized_file(&fixture.source.join("large.bin"), 64 * 1024 * 1024);
    let (runner, _) = runner(&fixture);
    // A full copy takes about 50 seconds, so a Robocopy process that outlives Stop is easy to detect.
    let request = request(&fixture, ipg("50"));
    let scan = runner.scan(&request).unwrap();
    let copy = thread::spawn({
        let runner = Arc::clone(&runner);
        move || runner.start(&request, &scan)
    });
    wait_for_stage(&runner, CopyJobStage::Running);
    runner.stop().unwrap();
    let deadline = Instant::now() + Duration::from_secs(10);
    while !copy.is_finished() && Instant::now() < deadline {
        thread::sleep(Duration::from_millis(25));
    }

    assert!(copy.is_finished(), "Robocopy kept running after Stop.");
    assert_eq!(CopyOutcome::Canceled, copy.join().unwrap().unwrap().outcome);
    assert_eq!(CopyJobStage::Canceled, runner.stage());
}

#[test]
fn mirror_pre_scan_reports_planned_deletion() {
    let fixture = fixture();
    fs::write(fixture.source.join("keep.txt"), "keep").unwrap();
    fs::write(fixture.destination.join("keep.txt"), "keep").unwrap();
    fs::write(fixture.destination.join("extra.txt"), "delete only during a real mirror").unwrap();
    fs::create_dir_all(fixture.destination.join("extra-folder")).unwrap();
    let (runner, recorded) = runner(&fixture);
    let mut request = request(&fixture, vec![CopyOptionValue::new("Mirror")]);
    request.options.retain(|option| option.id != "EmptySubdirectories");
    let scan = runner.scan(&request).unwrap();
    assert!(scan.planned_delete_count >= 1, "{:?}", recorded.lock().unwrap().output);
    assert!(fixture.destination.join("extra.txt").exists(), "A dry run must not delete anything.");
}

#[test]
fn robocopy_output_decodes_non_ascii_names_with_the_oem_code_page() {
    // A name only survives the round trip when the system OEM code page can represent it;
    // café, naïve, and Ünïcödé are all representable in the common OEM code pages 437 and 850.
    let oem = unsafe { windows_oem_code_page() };
    if oem != 437 && oem != 850 && oem != 858 {
        eprintln!("skipped: system OEM code page {oem} is not known to represent the sample names");
        return;
    }

    let fixture = fixture();
    let name = "café.txt";
    fs::write(fixture.source.join(name), "oem").unwrap();
    let (runner, recorded) = runner(&fixture);
    let request = request(&fixture, vec![]);
    let scan = runner.scan(&request).unwrap();
    assert_eq!(1, scan.file_count);
    let result = runner.start(&request, &scan).unwrap();
    assert!(succeeded(&result), "{}", result.summary);
    assert!(fixture.destination.join(name).exists());
    assert!(recorded.lock().unwrap().output.iter().any(|line| line.contains(name)));
    assert!(fs::read_to_string(&result.log_path).unwrap().contains(name));
}

unsafe extern "system" {
    #[link_name = "GetOEMCP"]
    fn windows_oem_code_page() -> u32;
}

#[test]
fn runner_exposes_the_pre_scan_transcript_path() {
    let fixture = fixture();
    fs::write(fixture.source.join("scan.txt"), "scan").unwrap();
    let (runner, _) = runner(&fixture);
    assert_eq!("", runner.current_log_path());
    runner.scan(&request(&fixture, vec![])).unwrap();
    let log_path = PathBuf::from(runner.current_log_path());
    assert!(log_path.is_file());
    assert!(log_path.starts_with(&fixture.logs));
}

#[test]
fn log_retention_skips_logs_that_are_still_open() {
    use std::os::windows::fs::OpenOptionsExt;
    const FILE_SHARE_READ: u32 = 1;
    const FILE_SHARE_WRITE: u32 = 2;

    let root = tempfile::tempdir().unwrap();
    let logs = root.path().join("Logs");
    fs::create_dir_all(&logs).unwrap();
    let now = std::time::SystemTime::now();
    let expired = now - Duration::from_secs((app_identity::LOG_RETENTION_DAYS + 10) * 24 * 3600);
    let (open_log, closed_log) = (logs.join("robocopy-open.log"), logs.join("robocopy-closed.log"));
    for path in [&open_log, &closed_log] {
        fs::write(path, "transcript").unwrap();
        fs::File::options().write(true).open(path).unwrap().set_modified(expired).unwrap();
    }

    {
        // Opened without FILE_SHARE_DELETE, like a log another process is still writing.
        let _open =
            fs::File::options().read(true).share_mode(FILE_SHARE_READ | FILE_SHARE_WRITE).open(&open_log).unwrap();
        assert_eq!(1, app_identity::prune_old_logs(&logs, now, app_identity::LOG_RETENTION_DAYS));
    }

    assert!(open_log.exists());
    assert!(!closed_log.exists());
}
