use std::time::Duration;

/// Formats `value` with at most `decimals` fractional digits, dropping trailing zeros (.NET's `0.##`).
pub fn trimmed_decimal(value: f64, decimals: usize) -> String {
    let text = format!("{value:.decimals$}");
    if text.contains('.') { text.trim_end_matches('0').trim_end_matches('.').to_owned() } else { text }
}

pub fn bytes(bytes: u64) -> String {
    const UNITS: [&str; 5] = ["B", "KB", "MB", "GB", "TB"];
    let mut display = bytes as f64;
    let mut unit = 0;
    while display >= 1024.0 && unit < UNITS.len() - 1 {
        display /= 1024.0;
        unit += 1;
    }

    format!("{} {}", trimmed_decimal(display, 2), UNITS[unit])
}

pub fn megabytes(bytes: u64) -> String {
    format!("{} MB", trimmed_decimal(bytes as f64 / (1024.0 * 1024.0), 1))
}

/// Formats a count with thousands separators, like .NET's `N0`.
pub fn grouped(value: usize) -> String {
    let digits = value.to_string();
    let mut result = String::with_capacity(digits.len() + digits.len() / 3);
    for (index, digit) in digits.chars().enumerate() {
        if index > 0 && (digits.len() - index).is_multiple_of(3) {
            result.push(',');
        }

        result.push(digit);
    }

    result
}

pub fn eta(remaining: Option<Duration>) -> String {
    let Some(remaining) = remaining else {
        return "—".to_owned();
    };

    let seconds = remaining.as_secs();
    let (hours, minutes, seconds) = (seconds / 3600, seconds / 60 % 60, seconds % 60);
    if hours >= 1 { format!("{hours:02}:{minutes:02}:{seconds:02}") } else { format!("{minutes:02}:{seconds:02}") }
}
