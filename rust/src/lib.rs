//! RobustCopy: a native Windows interface for `robocopy.exe`.
//!
//! The `core` and `services` modules hold everything that does not depend on the GUI toolkit,
//! so they can be unit-tested on any platform. Only the Win32 process-control pieces are
//! Windows-specific, and they are compiled out elsewhere.

pub mod core;
pub mod services;
pub mod ui;

/// The platform line terminator, matching .NET's `Environment.NewLine` in logs and reports.
#[cfg(windows)]
pub const NEWLINE: &str = "\r\n";
#[cfg(not(windows))]
pub const NEWLINE: &str = "\n";
