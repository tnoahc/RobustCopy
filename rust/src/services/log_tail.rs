use std::fs::File;
use std::io::{self, Read, Seek, SeekFrom};
use std::path::Path;

use crate::NEWLINE;
use crate::core::format;

pub const DEFAULT_MAXIMUM_BYTES: u64 = 5 * 1024 * 1024;
const UTF8_PREAMBLE: &[u8] = &[0xEF, 0xBB, 0xBF];

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LogTail {
    pub text: String,
    pub is_truncated: bool,
    pub file_bytes: u64,
    pub maximum_bytes: u64,
}

/// Reads a transcript for the clipboard: the whole file when it is small, otherwise the last
/// `maximum_bytes` starting at a line boundary. Works on logs that are still being written.
pub fn read(path: &Path, maximum_bytes: u64) -> io::Result<LogTail> {
    assert!(maximum_bytes > 0, "maximum_bytes must be positive");

    // std opens files with FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE on Windows.
    let mut file = File::open(path)?;
    let length = file.metadata()?.len();
    if length <= maximum_bytes {
        let whole = read_exactly(&mut file, 0, length)?;
        return Ok(LogTail { text: decode(&whole, 0), is_truncated: false, file_bytes: length, maximum_bytes });
    }

    // Read one extra byte before the window so a window that already starts on a line boundary is kept intact.
    let buffer = read_exactly(&mut file, length - maximum_bytes - 1, maximum_bytes + 1)?;
    let start = match buffer.iter().position(|byte| *byte == b'\n') {
        Some(index) => index + 1,
        None => {
            // No line break in the window: skip UTF-8 continuation bytes so a multibyte character is never split.
            let mut start = 1;
            while start < buffer.len() && buffer[start] & 0xC0 == 0x80 {
                start += 1;
            }

            start
        }
    };

    let header = format!(
        "[Truncated: showing the last {} of {}. Open the log for the full transcript.]",
        format::megabytes(maximum_bytes),
        format::megabytes(length)
    );
    let text = format!("{header}{NEWLINE}{}", decode(&buffer, start));
    Ok(LogTail { text, is_truncated: true, file_bytes: length, maximum_bytes })
}

fn read_exactly(file: &mut File, offset: u64, count: u64) -> io::Result<Vec<u8>> {
    file.seek(SeekFrom::Start(offset))?;
    let mut buffer = Vec::with_capacity(count as usize);
    file.take(count).read_to_end(&mut buffer)?;
    Ok(buffer)
}

fn decode(buffer: &[u8], start: usize) -> String {
    let start = if start == 0 && buffer.starts_with(UTF8_PREAMBLE) { UTF8_PREAMBLE.len() } else { start };
    String::from_utf8_lossy(&buffer[start..]).into_owned()
}
