// Hide the console window in release builds; debug builds keep it for logs.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::time::SystemTime;

use robustcopy::core::{app_identity, crash_reporter};
use robustcopy::ui::app::RobustCopyApp;

fn main() -> eframe::Result {
    install_crash_reporter();

    // Retention runs once per launch, off the UI thread, and never fails.
    std::thread::spawn(|| {
        app_identity::prune_old_logs(
            &app_identity::log_directory(),
            SystemTime::now(),
            app_identity::LOG_RETENTION_DAYS,
        )
    });

    let icon = eframe::icon_data::from_png_bytes(include_bytes!("../../src/RobustCopy/Assets/RobustCopyIcon.png"))
        .expect("the bundled icon is a valid PNG");
    let options = eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_title(app_identity::DISPLAY_NAME)
            .with_inner_size([1200.0, 680.0])
            .with_min_inner_size([1000.0, 620.0])
            .with_icon(icon),
        centered: true,
        ..Default::default()
    };
    eframe::run_native(app_identity::DISPLAY_NAME, options, Box::new(|cc| Ok(Box::new(RobustCopyApp::new(cc)))))
}

/// Writes a crash report for any panic, then tells the user where it is when the GUI thread panicked.
fn install_crash_reporter() {
    let default_hook = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        let thread = std::thread::current();
        let source = format!("Thread '{}'", thread.name().unwrap_or("unnamed"));
        let details = format!("{info}\n{}", std::backtrace::Backtrace::force_capture());
        let report = crash_reporter::write(&details, &source, None);
        default_hook(info);

        if thread.name() == Some("main") {
            let detail = match report {
                Some(path) => format!("Details were saved to:\n{}", path.display()),
                None => "A crash report could not be written.".to_owned(),
            };
            let _ = rfd::MessageDialog::new()
                .set_level(rfd::MessageLevel::Error)
                .set_title("Unexpected error")
                .set_description(format!(
                    "{} hit an unexpected error and must close.\n\n{detail}",
                    app_identity::DISPLAY_NAME
                ))
                .show();
        }
    }));
}
