use std::collections::HashSet;
use std::time::{Duration, Instant};

use super::output_parser;
use crate::core::models::{CopyJobProgress, CopyJobStage, ScanResult};

/// Turns Robocopy's per-file output into overall progress against the pre-scan totals.
pub struct ProgressTracker {
    scan_total_bytes: u64,
    scan_file_count: usize,
    estimated: bool,
    started_at: Instant,
    completed_paths: HashSet<String>,
    paused_at: Option<Instant>,
    paused_duration: Duration,
    current_path: String,
    current_size: u64,
    current_percent: f64,
    completed_bytes: u64,
}

impl ProgressTracker {
    pub fn new(scan: &ScanResult, estimated: bool) -> Self {
        Self {
            scan_total_bytes: scan.total_bytes,
            scan_file_count: scan.file_count,
            estimated,
            started_at: Instant::now(),
            completed_paths: HashSet::new(),
            paused_at: None,
            paused_duration: Duration::ZERO,
            current_path: String::new(),
            current_size: 0,
            current_percent: 0.0,
            completed_bytes: 0,
        }
    }

    pub fn update(&mut self, line: &str, stage: CopyJobStage) -> CopyJobProgress {
        let record = output_parser::parse(line);
        if record.has_file() && !record.is_extra && !record.is_directory {
            if !self.current_path.is_empty() && !record.path.eq_ignore_ascii_case(&self.current_path) {
                self.complete_current_file();
            }

            self.current_path = record.path;
            self.current_size = record.bytes;
            self.current_percent = 0.0;
        }

        if let Some(percent) = record.percent
            && !self.current_path.is_empty()
        {
            self.current_percent = self.current_percent.max(percent);
            if self.current_percent >= 100.0 {
                self.complete_current_file();
            }
        }

        self.snapshot(stage, false)
    }

    pub fn pause(&mut self) -> CopyJobProgress {
        self.paused_at.get_or_insert_with(Instant::now);
        self.snapshot(CopyJobStage::Paused, false)
    }

    pub fn resume(&mut self) -> CopyJobProgress {
        if let Some(paused_at) = self.paused_at.take() {
            self.paused_duration += paused_at.elapsed();
        }

        self.snapshot(CopyJobStage::Running, false)
    }

    pub fn complete(&mut self, success: bool) -> CopyJobProgress {
        if success {
            self.complete_current_file();
        }

        self.snapshot(if success { CopyJobStage::Completed } else { CopyJobStage::Failed }, success)
    }

    fn complete_current_file(&mut self) {
        if self.current_path.is_empty() || !self.completed_paths.insert(self.current_path.to_uppercase()) {
            return;
        }

        self.completed_bytes += self.current_size;
        self.current_percent = 100.0;
    }

    fn snapshot(&self, stage: CopyJobStage, is_final: bool) -> CopyJobProgress {
        let current_contribution = if self.current_percent >= 100.0 {
            0
        } else {
            (self.current_size as f64 * (self.current_percent / 100.0)) as u64
        };
        let copied = self.completed_bytes + current_contribution;
        let denominator = self.scan_total_bytes.max(copied);
        let overall = if denominator == 0 { 0.0 } else { copied as f64 * 100.0 / denominator as f64 };
        let overall = if is_final { 100.0 } else { overall.clamp(0.0, 99.0) };

        let now = self.paused_at.unwrap_or_else(Instant::now);
        let active = now.saturating_duration_since(self.started_at).saturating_sub(self.paused_duration).as_secs_f64();
        let speed = if active <= 0.0 { 0.0 } else { copied as f64 / active };
        let estimated_remaining = (speed > 0.0 && self.scan_total_bytes > copied)
            .then(|| Duration::from_secs_f64((self.scan_total_bytes - copied) as f64 / speed));

        CopyJobProgress {
            stage,
            overall_percent: overall,
            current_file_percent: self.current_percent,
            current_file: self.current_path.clone(),
            bytes_copied: if is_final { self.scan_total_bytes.max(copied) } else { copied },
            total_bytes: denominator,
            bytes_per_second: speed,
            estimated_remaining,
            files_completed: self.completed_paths.len(),
            total_files: self.scan_file_count,
            is_estimated: self.estimated,
        }
    }
}
