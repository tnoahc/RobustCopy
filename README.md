# RobustCopy

<p align="center">
  <img src="src/RobustCopy/Assets/RobustCopyIcon.png" alt="RobustCopy app icon" width="160">
</p>

A native Windows interface for `robocopy.exe`. It keeps Robocopy's reliability while adding safe option selection, descriptive help, pre-scan totals, live progress, Pause/Resume/Stop controls, command preview, and persistent logs.

RobustCopy is released in two lines, side by side:

| Line | Versions | Built with | Source |
| --- | --- | --- | --- |
| **RobustCopy 2** | 2.x | Rust and [egui](https://github.com/emilk/egui); a single 15 MB executable | [`rust/`](rust/README.md) |
| **RobustCopy 1** | 1.x | .NET 10 WPF; a self-contained 62 MB executable | [`src/RobustCopy`](src/RobustCopy) |

Both have the same features. RobustCopy 2 is the recommended download; RobustCopy 1 stays available and receives fixes as 1.x releases.

The interface design references are kept in [`docs/mockups`](docs/mockups/README.md).

## Download

Both lines are published by **Kaleb Creative Studio** for Windows 10 and Windows 11 x64. All releases are listed on the [GitHub releases page](https://github.com/tnoahc/RobustCopy/releases).

### RobustCopy 2.0.0 (recommended)

- [Download RobustCopy.exe](https://github.com/tnoahc/RobustCopy/releases/download/v2.0.0/RobustCopy.exe)
- [Download the SHA-256 checksum](https://github.com/tnoahc/RobustCopy/releases/download/v2.0.0/RobustCopy.exe.sha256)
- [View the GitHub release](https://github.com/tnoahc/RobustCopy/releases/tag/v2.0.0)

A single 15.0 MB executable with no runtime to install. The [Rust port README](rust/README.md#differences-from-the-wpf-version) lists the small differences in behaviour from RobustCopy 1.

### RobustCopy 1.0.2

- [Download RobustCopy.exe](https://github.com/tnoahc/RobustCopy/releases/download/v1.0.2/RobustCopy.exe)
- [Download the SHA-256 checksum](https://github.com/tnoahc/RobustCopy/releases/download/v1.0.2/RobustCopy.exe.sha256)
- [View the GitHub release](https://github.com/tnoahc/RobustCopy/releases/tag/v1.0.2)

The original .NET WPF edition. The 62.1 MB executable is self-contained; no separate .NET installation is required.

### Verify a download

Each release publishes its SHA-256 checksum as `RobustCopy.exe.sha256` next to the executable. To verify a download, compare the checksum with:

```powershell
Get-FileHash .\RobustCopy.exe -Algorithm SHA256
```

- [Browse the GitHub repository](https://github.com/tnoahc/RobustCopy)
- [Read the privacy policy](PRIVACY.md)

### What's new in 2.0.0

**A new RobustCopy**

- RobustCopy 2 is a rewrite of RobustCopy in Rust. It ships as a single 15 MB executable, down from 62 MB, with no .NET or other runtime to install.
- It keeps everything RobustCopy 1.0.2 does: the option catalog and conflict handling, validation, command preview, list-only pre-scan, confirmation for destructive modes, live progress, Pause/Resume/Stop, UTF-8 transcripts with 90-day retention, and crash reports.

**Changes from RobustCopy 1**

- Dialogs (errors, destructive confirmation, close confirmation) are drawn in the app's own theme; the folder picker is still the native Windows dialog.
- An unexpected error on the GUI thread writes a crash report and closes the app. Errors on the copy thread are reported and the app keeps running.
- Stop returns immediately and the run finishes in the background, so the header shows `STOPPING` until Robocopy has exited.
- Pausing right after Start waits for Robocopy to launch instead of failing.

**RobustCopy 1**

- [RobustCopy 1.0.2](https://github.com/tnoahc/RobustCopy/releases/tag/v1.0.2), the .NET WPF version, stays available, and fixes for it will continue as 1.x releases.

### What's new in 1.0.2

**Safety**

- Destructive switches (`/MIR`, `/PURGE`, `/MOV`, `/MOVE`) can no longer bypass the confirmation prompt when typed with a `-` prefix, in the file-pattern field, or in the exclude lists.
- Pressing Stop while a job is starting now reliably prevents Robocopy from launching.
- A Robocopy process that is terminated unexpectedly is reported as a failure instead of a success.
- Move-only jobs no longer claim that destination items will be deleted.

**Reliability**

- Unexpected errors write a crash report to the log folder instead of closing the app silently.
- File names with non-ASCII characters are decoded with the system OEM code page, so they display correctly.
- Copy log works with very large and still-running transcripts (the last 5 MB is copied).
- Open log and Copy log are available after a failed or declined pre-scan.
- Low free-space mode values are checked before a run starts.
- Copied command previews keep paths that end in a backslash intact.

**Performance and usability**

- Transcripts no longer contain a line for every progress percentage, and the live view updates smoothly during large transfers.
- Selecting an option now turns off options it conflicts with instead of being blocked; for example, Mirror is selectable straight away.
- Logs older than 90 days are removed automatically at startup.

## Features

- Choose local, mapped-drive, and UNC source/destination folders through the Windows folder picker.
- Swap source and destination in one click.
- Select Robocopy switches from categorized, two-column strategy cards with descriptions; selecting an option turns off any option it conflicts with.
- Enter additional supported switches in the Advanced field.
- Preview the exact command while configuring a job.
- Pre-scan safely with `/L` for planned files, bytes, and destination deletions.
- Show overall/current-file progress, transfer speed, ETA, file counts, and live output.
- Pause and resume the live Robocopy process, or stop it permanently.
- Require confirmation for Mirror, Purge, and Move modes.
- Save UTF-8 run transcripts under `%LocalAppData%\RobustCopy\Logs`; logs older than 90 days are removed automatically at startup.
- Preserve logs from earlier installations by copying them into the RobustCopy log directory on first use.
- Write a crash report to the log directory if the app hits an unexpected error.
- Run Robocopy without `cmd.exe`; paths and switches are passed through `ProcessStartInfo.ArgumentList`.

Robocopy console output is decoded using the system OEM code page (the one Robocopy writes in, which follows the Windows system locale) and then written as UTF-8. The GUI reserves `/UNICODE` because the installed Robocopy build emits a mixed BOM/console stream for that switch when stdout is redirected, which cannot be parsed reliably for live progress.

## Interface

The application uses a dark console-inspired three-panel layout:

- **Transfer locations** contains the source, destination, file pattern, and command preview.
- **Transfer strategy** provides scrollable option cards using the original Folders, Reliability, Metadata, Filters, Performance, and Destructive Operations groups.
- **Live transfer overview** shows progress, current-file details, transfer metrics, and the saved run transcript.

The Start, Pause, Resume, and Stop controls remain visible in the header, alongside safety guidance and the current run state. The interactive HTML source and rendered visual reference are documented in [`docs/mockups`](docs/mockups/README.md).

## Requirements

- Windows 10 or Windows 11, x64.
- The .NET 10 SDK is required only to build from source.
- The published self-contained executable does not require a separately installed .NET runtime.
- RobustCopy 2 (Rust) builds with the stable [Rust toolchain](https://rustup.rs) for MSVC and the Visual Studio Build Tools (C++ workload with the Windows SDK).

## Build and run

Open PowerShell in this repository:

```powershell
dotnet build RobustCopy.slnx --configuration Release
dotnet run --project src\RobustCopy\RobustCopy.csproj
```

If an installer has just added the SDK and the current terminal has not refreshed its `PATH`, start a new PowerShell window or invoke `C:\Program Files\dotnet\dotnet.exe` directly.

To build and run RobustCopy 2 (Rust) instead:

```powershell
cd rust
cargo run --release
```

The release build is `rust\target\release\RobustCopy.exe`. Run `cargo test` in `rust` for its tests; see the [Rust port README](rust/README.md) for details.

## Website

The download page is [`index.php`](index.php), written in plain PHP 8 with no framework or dependencies. It renders a single self-contained page: [`site/styles.css`](site/styles.css), [`site/script.js`](site/script.js), the logo, and the favicon (`src/RobustCopy/Assets/RobustCopy.ico`) are inlined into it.

- [`site/releases.php`](site/releases.php) holds the version, tag, size, and SHA-256 of the current release and of RobustCopy 1. Download links are built from the tag.
- [`site/content.php`](site/content.php) holds the repeated content: navigation, features, steps, safeguards, and requirements.

To preview it locally with XAMPP's PHP:

```powershell
C:\xampp\php\php.exe -S localhost:8000
```

Then open <http://localhost:8000>. To serve it with Apache instead, place the repository (or `index.php`, `site/`, and `src/RobustCopy/Assets/RobustCopy.ico` with the same layout) under `C:\xampp\htdocs`.

## Tests

The repository uses a dependency-free executable test project so it can be verified without downloading a test framework. It covers command construction, validation, parsing, exit codes, and safe Robocopy integration in temporary folders, including Pause/Resume and Stop/Restart.

```powershell
dotnet run --project tests\RobustCopy.Tests\RobustCopy.Tests.csproj --configuration Release
```

## Publish a portable executable

```powershell
dotnet publish src\RobustCopy\RobustCopy.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output artifacts\publish
```

The main deliverable is `artifacts\publish\RobustCopy.exe`.

The `artifacts/` directory contains generated build outputs and visual verification files and is intentionally excluded from version control. Run the publish command whenever the distributable executable needs to be refreshed.

## Continuous integration

The GitLab pipeline in [`.gitlab-ci.yml`](.gitlab-ci.yml) runs on merge requests, the default branch, and tags:

- **build** runs `dotnet build RobustCopy.slnx --configuration Release`, and **build-rust** runs `cargo build --release` for the Rust app.
- **test** runs the test project above, and **test-rust** runs `cargo test`; any failing test fails the job.
- **release** runs on version tags only (`v1.2.3`, or `v1.2.3-preview.4` for a pre-release), after the tests pass, using [`ci/release.ps1`](ci/release.ps1). The tag's major version picks the app: `v1.*` releases RobustCopy 1 (WPF) and `v2.*` releases RobustCopy 2 (Rust). The job:
  1. Checks that the tag matches the app's version, then builds `RobustCopy.exe` and writes `RobustCopy.exe.sha256` in the checksum format shown above.
  2. Pushes the tag to GitHub and creates the GitHub release `RobustCopy <version>` with `RobustCopy.exe`, `RobustCopy.exe.sha256`, and `RobustCopy.pdb`. The release is created as a draft and published only after every file has uploaded. Its notes come from the README's "What's new in <version>" section (for a preview, "What's new in 2.1.0 Preview 1"), followed by the checksum.
  3. Marks the release as GitHub's Latest only if it is the highest stable version, so a 1.x maintenance release never replaces 2.x as Latest. Preview tags become pre-releases and are never Latest.
  4. Creates the GitLab release with the same notes and download links that point to the GitHub files. The executables are not uploaded to GitLab, because the GitLab instance's proxy cannot accept uploads that large.

  The job fails with a clear message if the tag does not match the app's version, if a release for the tag already exists, or if an upload fails (the GitHub release is then left as a draft).

The jobs need a Windows runner using the shell executor with PowerShell, because WPF and the Robocopy integration tests cannot run in Docker and require a real `C:\Windows\System32\robocopy.exe`. The .NET 10 SDK is expected to be preinstalled on the runner; a commented-out `dotnet-install.ps1` fallback is kept in `.gitlab-ci.yml` for runners without it. The Rust jobs, and releases of RobustCopy 2, also need the stable [Rust toolchain](https://rustup.rs) for MSVC and the Visual Studio Build Tools C++ workload with the Windows SDK. Rust build output is kept in `CARGO_TARGET_DIR`, outside the project directory, so later pipelines only rebuild what changed. Jobs select the runner by the `windows` tag, which is set once under `default: tags:`.

Releasing requires a `GITHUB_TOKEN` CI variable that is masked and protected: a fine-grained GitHub token with **Contents: read and write** on `tnoahc/RobustCopy`. Version tags (`v*`) must be protected so that tag pipelines can read it.

To release a new version:

1. Bump the app's version: `<Version>` in `src/RobustCopy/RobustCopy.csproj` and the version in `src/RobustCopy/app.manifest` for RobustCopy 1, or `version` in `rust/Cargo.toml` for RobustCopy 2 (its manifest version follows automatically).
2. Update the version and links in this README's Download section and add a "What's new in <version>" section. After the release job has run, update `site/releases.php` with the new version, size, and the SHA-256 it printed.
3. Merge, then tag the merge commit (for example `git tag -a v1.0.3 -m "RobustCopy 1.0.3"` or `git tag -a v2.0.0 -m "RobustCopy 2.0.0"`) and push the tag to GitLab.

To check a release without publishing it, run the script locally with `RELEASE_DRY_RUN` set; it builds the app and prints the release notes:

```powershell
$env:CI_COMMIT_TAG = 'v2.0.0'; $env:RELEASE_DRY_RUN = '1'
powershell -NoProfile -ExecutionPolicy Bypass -File ci\release.ps1
```

## Safety notes

- `/MIR` and `/PURGE` may delete destination content.
- `/MOV` and `/MOVE` delete successfully copied source content.
- The GUI performs a list-only pre-scan and shows a mandatory confirmation before these operations.
- Source and destination cannot be the same folder or contain one another.
- Backup modes may require Windows privileges that the current account does not have; the app does not auto-elevate.
- Stop terminates the current Robocopy process. Restartable mode (`/Z`) is enabled by default, so a later run can continue supported partial transfers.
