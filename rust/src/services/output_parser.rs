use std::sync::LazyLock;

use regex::Regex;

use crate::core::models::ScanResult;

#[derive(Clone, Debug, PartialEq)]
pub struct OutputRecord {
    pub path: String,
    pub bytes: u64,
    pub percent: Option<f64>,
    pub is_extra: bool,
    pub is_directory: bool,
}

impl OutputRecord {
    pub fn has_file(&self) -> bool {
        !self.path.trim().is_empty()
    }
}

static FILE_LINE: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"^\s*(?<status>.*?)\s+(?<bytes>-?\d+)\s+(?<path>(?:[A-Za-z]:\\|\\\\).+?)\s*$").unwrap()
});
static PERCENT: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"(?<percent>\d+(?:[\.,]\d+)?)\s*%").unwrap());
static PERCENT_ONLY: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"^\s*\d+(?:[\.,]\d+)?\s*%\s*$").unwrap());

pub fn is_percent_only(line: &str) -> bool {
    PERCENT_ONLY.is_match(line)
}

pub fn parse(line: &str) -> OutputRecord {
    let mut record =
        OutputRecord { path: String::new(), bytes: 0, percent: None, is_extra: false, is_directory: false };
    if let Some(captures) = FILE_LINE.captures(line) {
        record.path = captures["path"].trim().to_owned();
        record.bytes = captures["bytes"].parse::<i64>().unwrap_or(0).max(0) as u64;
        let status = captures["status"].to_ascii_lowercase();
        record.is_extra = status.contains("extra");
        record.is_directory = status.contains("dir") || record.path.ends_with('\\') || record.path.ends_with('/');
    }

    if let Some(captures) = PERCENT.captures(line) {
        record.percent =
            captures["percent"].replace(',', ".").parse::<f64>().ok().map(|percent| percent.clamp(0.0, 100.0));
    }

    record
}

pub fn parse_scan<'a>(lines: impl IntoIterator<Item = &'a str>) -> ScanResult {
    let mut result = ScanResult::default();
    for line in lines {
        let record = parse(line);
        if !record.has_file() {
            continue;
        }

        if record.is_extra {
            result.planned_delete_count += 1;
        } else if !record.is_directory {
            result.total_bytes += record.bytes;
            result.file_count += 1;
        }

        if result.preview_lines.len() < 200 {
            result.preview_lines.push(line.trim().to_owned());
        }
    }

    result
}
