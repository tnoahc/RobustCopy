# RobustCopy (Rust port)

A Rust rewrite of RobustCopy using [egui/eframe](https://github.com/emilk/egui). It lives next to the
.NET WPF version in this repository until it reaches feature parity.

The port keeps the WPF version's behaviour: the same option catalog, validation rules, command preview,
list-only pre-scan, destructive-operation confirmation, live progress, Pause/Resume/Stop, UTF-8 transcripts
under `%LocalAppData%\RobustCopy\Logs` with 90-day retention, and crash reports.

## Layout

| Path | Contents |
| --- | --- |
| `src/core/` | Option catalog, tokenizer, validator, command builder, exit codes, log directory, crash reporter |
| `src/services/` | Robocopy runner, output parser, progress tracker, log tail reader, scan → confirm → copy workflow |
| `src/services/native.rs` | Win32: Job Object containment, thread suspension for Pause, OEM code page decoding |
| `src/ui/model.rs` | Option selection and conflict resolution, independent of egui |
| `src/ui/app.rs` | The egui window |
| `tests/core.rs` | Platform-independent tests ported from `tests/RobustCopy.Tests` |
| `tests/robocopy.rs` | Integration tests that drive the real `robocopy.exe` (Windows only) |

Everything except `native.rs` and the window compiles and is unit-tested on any platform.

## Build and run

On Windows 10/11 with the [Rust toolchain](https://rustup.rs) (MSVC):

```powershell
cd rust
cargo run --release
```

The release build is a single self-contained `target\release\RobustCopy.exe` with the icon, version
information, and the application manifest embedded (see `build.rs`).

## Tests

```powershell
cargo test
```

On Windows this also runs the Robocopy integration tests (copy, pause/resume, stop/restart, stop during
startup, mirror pre-scan, OEM file names). Elsewhere only the platform-independent tests run.

To type-check the Windows-only code from Linux or macOS:

```sh
rustup target add x86_64-pc-windows-gnu
cargo clippy --all-targets --target x86_64-pc-windows-gnu
```

## Differences from the WPF version

- Dialogs (errors, destructive confirmation, close confirmation) are drawn in the app's own theme instead of
  Windows message boxes. The folder picker is still the native Windows dialog.
- An unexpected error on the GUI thread writes a crash report and closes the app; WPF could recover from
  some dispatcher exceptions. Errors on the copy thread are reported and the app keeps running.
- Stop returns immediately and the run finishes in the background, so the header shows `STOPPING` until
  Robocopy has exited.
