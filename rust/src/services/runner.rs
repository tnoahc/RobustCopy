use std::fs::{self, File, OpenOptions};
use std::io::{BufWriter, Read, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::{Arc, Mutex, MutexGuard};
use std::thread;
use std::time::{Duration, Instant};

use super::output_parser;
use super::progress::ProgressTracker;
use crate::NEWLINE;
use crate::core::command_builder::{self, ExecutionMode};
use crate::core::exit_codes;
use crate::core::models::{
    CopyJobProgress, CopyJobRequest, CopyJobResult, CopyJobStage, CopyOutcome, RunError, ScanResult,
};
use crate::core::{app_identity, tokenizer, validator};

const LOG_FLUSH_INTERVAL: Duration = Duration::from_secs(1);

#[derive(Clone, Debug, PartialEq)]
pub enum RunnerEvent {
    Progress(CopyJobProgress),
    Output(String),
}

/// Receives runner events. It may be called from Robocopy's output threads, and it may call back
/// into the runner (for example to stop it), so the runner never holds a lock while calling it.
pub type EventSink = Arc<dyn Fn(RunnerEvent) + Send + Sync>;

/// The operations the GUI needs from a runner; implemented by fakes in tests.
pub trait CopyRunner: Send + Sync {
    fn scan(&self, request: &CopyJobRequest) -> Result<ScanResult, RunError>;
    fn start(&self, request: &CopyJobRequest, scan: &ScanResult) -> Result<CopyJobResult, RunError>;
    fn pause(&self) -> Result<(), RunError>;
    fn resume(&self) -> Result<(), RunError>;
    fn stop(&self) -> Result<(), RunError>;
    fn stage(&self) -> CopyJobStage;
    fn current_log_path(&self) -> String;

    fn is_busy(&self) -> bool {
        self.stage().is_busy()
    }
}

#[derive(Default)]
struct State {
    stage: CopyJobStage,
    log_path: String,
    stopped_by_user: bool,
    child: Option<Arc<Mutex<Child>>>,
    #[cfg(windows)]
    suspender: super::native::ProcessSuspender,
}

#[derive(Default)]
struct LogState {
    writer: Option<BufWriter<File>>,
    last_flush: Option<Instant>,
}

pub struct RobocopyRunner {
    log_directory: PathBuf,
    sink: EventSink,
    state: Mutex<State>,
    log: Mutex<LogState>,
    /// Serializes output handling so tracker updates and their events stay in order.
    output: Mutex<Option<ProgressTracker>>,
    /// Serializes Pause, Resume, and Stop.
    control: Mutex<()>,
}

impl RobocopyRunner {
    pub fn new(log_directory: PathBuf, sink: EventSink) -> Self {
        Self {
            log_directory,
            sink,
            state: Mutex::default(),
            log: Mutex::default(),
            output: Mutex::default(),
            control: Mutex::default(),
        }
    }

    fn state(&self) -> MutexGuard<'_, State> {
        lock(&self.state)
    }

    fn set_stage(&self, stage: CopyJobStage) {
        self.state().stage = stage;
    }

    fn emit(&self, event: RunnerEvent) {
        (self.sink)(event);
    }

    fn ensure_idle(&self) -> Result<(), RunError> {
        if self.is_busy() {
            return Err(RunError::Failed("Another Robocopy operation is already active.".to_owned()));
        }

        Ok(())
    }

    fn run_process(&self, arguments: &[String], on_output: &(dyn Fn(&str) + Sync)) -> Result<i32, RunError> {
        let windows_directory = windows_directory();
        let robocopy_path = windows_directory.join("System32").join("robocopy.exe");
        if !robocopy_path.is_file() {
            return Err(RunError::Failed(format!(
                "Robocopy is not available on this Windows installation ({}).",
                robocopy_path.display()
            )));
        }

        if self.state().stopped_by_user {
            return Err(RunError::Canceled("The operation was stopped before Robocopy started.".to_owned()));
        }

        let mut command = Command::new(&robocopy_path);
        command
            .args(arguments)
            .current_dir(&windows_directory)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x0800_0000;
            command.creation_flags(CREATE_NO_WINDOW);
        }

        let mut child =
            command.spawn().map_err(|error| RunError::Failed(format!("Windows could not start Robocopy: {error}")))?;
        let stdout = child.stdout.take();
        let stderr = child.stderr.take();

        #[cfg(windows)]
        let _job = match super::native::ProcessJob::new().and_then(|job| job.assign(&child).map(|()| job)) {
            Ok(job) => Some(job),
            Err(message) => {
                self.write_log(&format!("[Warning] Process containment was unavailable: {message}{NEWLINE}"));
                None
            }
        };

        let child = Arc::new(Mutex::new(child));
        let stopped_during_startup = {
            let mut state = self.state();
            state.child = Some(Arc::clone(&child));
            state.stopped_by_user
        };

        if stopped_during_startup {
            // Stop ran before the process was published, so it could not kill it.
            let _ = lock(&child).kill();
        }

        thread::scope(|scope| {
            if let Some(stderr) = stderr {
                scope.spawn(|| self.pump(stderr, on_output));
            }

            if let Some(stdout) = stdout {
                self.pump(stdout, on_output);
            }
        });

        // Both pipes reached end-of-file, so the process has exited or is exiting.
        let status = lock(&child).wait();
        {
            let mut state = self.state();
            state.child = None;
            #[cfg(windows)]
            state.suspender.resume_all();
        }

        let status =
            status.map_err(|error| RunError::Failed(format!("Unable to read Robocopy's exit code: {error}")))?;
        Ok(status.code().unwrap_or(-1))
    }

    fn pump(&self, mut reader: impl Read, on_output: &(dyn Fn(&str) + Sync)) {
        let mut buffer = [0u8; 4096];
        let mut segment = Vec::new();
        loop {
            let count = match reader.read(&mut buffer) {
                Ok(0) | Err(_) => break,
                Ok(count) => count,
            };

            // Splitting bytes on CR/LF is safe for every OEM code page, including DBCS ones,
            // because those bytes never occur as trail bytes.
            for byte in &buffer[..count] {
                match byte {
                    b'\r' | b'\n' => {
                        if !segment.is_empty() {
                            self.emit_line(&decode_output(&segment), on_output);
                            segment.clear();
                        }
                    }
                    0 => {}
                    _ => segment.push(*byte),
                }
            }
        }

        if !segment.is_empty() {
            self.emit_line(&decode_output(&segment), on_output);
        }
    }

    fn emit_line(&self, line: &str, on_output: &(dyn Fn(&str) + Sync)) {
        // Percent-only segments drive current-file progress but are noise in the transcript and log view.
        let percent_only = output_parser::is_percent_only(line);
        if percent_only {
            self.flush_log_if_due();
        } else {
            self.write_log(&format!("{line}{NEWLINE}"));
            self.emit(RunnerEvent::Output(line.to_owned()));
        }

        on_output(line);
    }

    fn open_log(&self) {
        let path = self.state().log_path.clone();
        let mut log = lock(&self.log);
        if log.writer.is_none()
            && let Ok(file) = OpenOptions::new().create(true).append(true).open(path)
        {
            log.writer = Some(BufWriter::new(file));
            log.last_flush = Some(Instant::now());
        }
    }

    fn close_log(&self) {
        if let Some(mut writer) = lock(&self.log).writer.take() {
            let _ = writer.flush();
        }
    }

    fn write_log(&self, text: &str) {
        let path = self.state().log_path.clone();
        if path.trim().is_empty() {
            return;
        }

        let mut log = lock(&self.log);
        if let Some(writer) = log.writer.as_mut() {
            let _ = writer.write_all(text.as_bytes());
        } else if let Ok(mut file) = OpenOptions::new().create(true).append(true).open(&path) {
            let _ = file.write_all(text.as_bytes());
        }

        flush_if_due(&mut log);
    }

    fn flush_log_if_due(&self) {
        flush_if_due(&mut lock(&self.log));
    }

    /// Writes a timestamped transcript line and flushes it so it is readable during the run.
    fn write_event(&self, message: &str) {
        self.write_log(&format!("[{}] {message}{NEWLINE}", timestamp()));
        let mut log = lock(&self.log);
        if let Some(writer) = log.writer.as_mut() {
            let _ = writer.flush();
            log.last_flush = Some(Instant::now());
        }
    }
}

impl CopyRunner for RobocopyRunner {
    fn scan(&self, request: &CopyJobRequest) -> Result<ScanResult, RunError> {
        self.ensure_idle()?;
        self.state().log_path.clear();
        let errors = validator::validate(request, true);
        if !errors.is_empty() {
            return Err(RunError::Failed(errors.join(NEWLINE)));
        }

        fs::create_dir_all(&self.log_directory)
            .map_err(|error| RunError::Failed(format!("Unable to create the log folder: {error}")))?;
        let log_path = self.log_directory.join(format!(
            "robocopy-{}-{}.log",
            chrono::Local::now().format("%Y%m%d-%H%M%S"),
            uuid::Uuid::new_v4().simple()
        ));
        {
            let mut state = self.state();
            state.log_path = log_path.to_string_lossy().into_owned();
            state.stopped_by_user = false;
            state.stage = CopyJobStage::Scanning;
        }

        self.emit(RunnerEvent::Progress(CopyJobProgress::status(
            CopyJobStage::Scanning,
            "Scanning source…",
            0,
            0,
            false,
        )));
        self.open_log();
        self.write_log(&format!(
            "[{}] Pre-scan{NEWLINE}{}{NEWLINE}",
            timestamp(),
            command_builder::build_display_command(request, ExecutionMode::Scan)
        ));

        let lines = Mutex::new(Vec::new());
        let arguments = command_builder::build_arguments(request, ExecutionMode::Scan);
        let exit_code = self.run_process(&arguments, &|line| lock(&lines).push(line.to_owned()));
        let result = match exit_code {
            Ok(_) if self.state().stopped_by_user => {
                self.set_stage(CopyJobStage::Canceled);
                Err(RunError::Canceled("The pre-scan was stopped.".to_owned()))
            }
            Ok(code) if code >= 8 => {
                self.set_stage(CopyJobStage::Failed);
                Err(RunError::Failed(format!(
                    "Robocopy pre-scan failed with exit code {code}. Review {}.",
                    log_path.display()
                )))
            }
            Ok(_) => {
                let lines = lines.into_inner().unwrap_or_else(|poisoned| poisoned.into_inner());
                let scan = output_parser::parse_scan(lines.iter().map(String::as_str));
                self.set_stage(CopyJobStage::Idle);
                self.emit(RunnerEvent::Progress(CopyJobProgress::status(
                    CopyJobStage::Idle,
                    "Ready",
                    scan.total_bytes,
                    scan.file_count,
                    false,
                )));
                Ok(scan)
            }
            Err(error @ RunError::Canceled(_)) => {
                self.set_stage(CopyJobStage::Canceled);
                Err(error)
            }
            Err(error) => {
                self.set_stage(CopyJobStage::Failed);
                Err(error)
            }
        };

        self.close_log();
        result
    }

    fn start(&self, request: &CopyJobRequest, scan: &ScanResult) -> Result<CopyJobResult, RunError> {
        self.ensure_idle()?;
        let multi_threaded = is_multi_threaded(request);
        *lock(&self.output) = Some(ProgressTracker::new(scan, multi_threaded));
        {
            let mut state = self.state();
            state.stopped_by_user = false;
            state.stage = CopyJobStage::Running;
        }

        self.emit(RunnerEvent::Progress(CopyJobProgress::status(
            CopyJobStage::Running,
            "Starting Robocopy…",
            scan.total_bytes,
            scan.file_count,
            multi_threaded,
        )));
        self.open_log();
        self.write_log(&format!(
            "{NEWLINE}[{}] Copy started{NEWLINE}{}{NEWLINE}",
            timestamp(),
            command_builder::build_display_command(request, ExecutionMode::Copy)
        ));

        let arguments = command_builder::build_arguments(request, ExecutionMode::Copy);
        let exit_code = self.run_process(&arguments, &|line| {
            let stage = self.state().stage;
            let mut tracker = lock(&self.output);
            if let Some(tracker) = tracker.as_mut() {
                self.emit(RunnerEvent::Progress(tracker.update(line, stage)));
            }
        });

        let log_path = self.current_log_path();
        let result = match exit_code {
            Ok(code) if self.state().stopped_by_user => {
                self.set_stage(CopyJobStage::Canceled);
                let canceled = exit_codes::interpret(code, &log_path, true);
                self.write_event(&canceled.summary);
                Ok(canceled)
            }
            Ok(code) => {
                let result = exit_codes::interpret(code, &log_path, false);
                let success = result.outcome != CopyOutcome::Failure;
                self.set_stage(if success { CopyJobStage::Completed } else { CopyJobStage::Failed });
                if let Some(tracker) = lock(&self.output).as_mut() {
                    self.emit(RunnerEvent::Progress(tracker.complete(success)));
                }

                self.write_event(&format!("Exit code {code}: {}", result.summary));
                Ok(result)
            }
            Err(RunError::Canceled(_)) => {
                self.set_stage(CopyJobStage::Canceled);
                Ok(exit_codes::interpret(-1, &log_path, true))
            }
            Err(error) => {
                self.set_stage(CopyJobStage::Failed);
                Err(error)
            }
        };

        self.close_log();
        result
    }

    fn pause(&self) -> Result<(), RunError> {
        let _control = lock(&self.control);
        let not_running = || RunError::Failed("There is no running copy to pause.".to_owned());
        let child = {
            let state = self.state();
            if state.stage != CopyJobStage::Running {
                return Err(not_running());
            }

            state.child.clone().ok_or_else(not_running)?
        };

        let process_id = {
            let mut child = lock(&child);
            if !matches!(child.try_wait(), Ok(None)) {
                return Err(not_running());
            }

            child.id()
        };

        suspend(&mut self.state(), process_id)?;
        self.set_stage(CopyJobStage::Paused);
        if let Some(tracker) = lock(&self.output).as_mut() {
            self.emit(RunnerEvent::Progress(tracker.pause()));
        }

        self.write_event("Copy paused by user.");
        Ok(())
    }

    fn resume(&self) -> Result<(), RunError> {
        let _control = lock(&self.control);
        {
            let mut state = self.state();
            if state.stage != CopyJobStage::Paused {
                return Err(RunError::Failed("There is no paused copy to resume.".to_owned()));
            }

            #[cfg(windows)]
            state.suspender.resume_all();
            state.stage = CopyJobStage::Running;
        }

        if let Some(tracker) = lock(&self.output).as_mut() {
            self.emit(RunnerEvent::Progress(tracker.resume()));
        }

        self.write_event("Copy resumed by user.");
        Ok(())
    }

    /// Requests a stop and returns without waiting: the active `scan` or `start` call observes the
    /// stop, waits for Robocopy to exit, and moves the stage to `Canceled`.
    fn stop(&self) -> Result<(), RunError> {
        let _control = lock(&self.control);
        let child = {
            let mut state = self.state();
            if !state.stage.is_busy() {
                return Ok(());
            }

            state.stage = CopyJobStage::Stopping;
            state.stopped_by_user = true;
            #[cfg(windows)]
            state.suspender.resume_all();
            state.child.clone()
        };

        if let Some(child) = child {
            // The process may already have exited; that is not an error.
            let _ = lock(&child).kill();
        }

        self.write_event("Operation stopped by user.");
        Ok(())
    }

    fn stage(&self) -> CopyJobStage {
        self.state().stage
    }

    fn current_log_path(&self) -> String {
        self.state().log_path.clone()
    }
}

impl Drop for RobocopyRunner {
    fn drop(&mut self) {
        let _ = self.stop();
        self.close_log();
    }
}

pub fn is_multi_threaded(request: &CopyJobRequest) -> bool {
    request.options.iter().any(|option| option.id.eq_ignore_ascii_case("MultiThreaded"))
        || tokenizer::parse(&request.advanced_options)
            .unwrap_or_default()
            .iter()
            .any(|option| tokenizer::normalize_switch(option).to_ascii_uppercase().starts_with("/MT"))
}

/// The RobustCopy log directory, created (with legacy logs migrated) if needed.
pub fn default_log_directory() -> PathBuf {
    app_identity::ensure_log_directory(None).unwrap_or_else(|_| app_identity::log_directory())
}

#[cfg(windows)]
fn suspend(state: &mut State, process_id: u32) -> Result<(), RunError> {
    state.suspender.suspend(process_id).map_err(RunError::Failed)
}

#[cfg(not(windows))]
fn suspend(_state: &mut State, _process_id: u32) -> Result<(), RunError> {
    Err(RunError::Failed("Pausing Robocopy is only supported on Windows.".to_owned()))
}

#[cfg(windows)]
fn decode_output(bytes: &[u8]) -> String {
    super::native::decode_oem(bytes)
}

#[cfg(not(windows))]
fn decode_output(bytes: &[u8]) -> String {
    String::from_utf8_lossy(bytes).into_owned()
}

fn windows_directory() -> PathBuf {
    std::env::var_os("SystemRoot")
        .or_else(|| std::env::var_os("windir"))
        .map(PathBuf::from)
        .unwrap_or_else(|| Path::new("C:\\Windows").to_path_buf())
}

fn flush_if_due(log: &mut LogState) {
    let due = log.last_flush.is_none_or(|last| last.elapsed() >= LOG_FLUSH_INTERVAL);
    if due && let Some(writer) = log.writer.as_mut() {
        let _ = writer.flush();
        log.last_flush = Some(Instant::now());
    }
}

fn timestamp() -> String {
    chrono::Local::now().to_rfc3339_opts(chrono::SecondsFormat::Micros, false)
}

/// Locks a mutex, recovering the data if another thread panicked while holding it.
fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}
