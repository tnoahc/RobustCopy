use std::collections::VecDeque;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::sync::mpsc::{self, Receiver, Sender};
use std::thread::JoinHandle;
use std::time::Duration;

use egui::{
    Align, Align2, Button, Color32, CornerRadius, Frame, Label, Layout, Margin, Rect, RichText, ScrollArea, Sense,
    Stroke, TextEdit, Ui, UiBuilder, pos2, vec2,
};

use super::model::{OptionItem, TransferForm};
use super::theme::{self, *};
use crate::core::catalog::CATEGORIES;
use crate::core::models::{CopyJobProgress, CopyJobStage};
use crate::core::{app_identity, format, validator};
use crate::services::job::{self, JobHost, JobOutcome};
use crate::services::log_tail;
use crate::services::runner::{self, CopyRunner, RobocopyRunner, RunnerEvent};

const MAXIMUM_VISIBLE_LOG_CHARACTERS: usize = 250_000;
const UI_UPDATE_INTERVAL: Duration = Duration::from_millis(100);

/// Messages from the runner and the job worker thread to the GUI thread.
enum UiEvent {
    Runner(RunnerEvent),
    Status(String),
    Totals(u64, usize),
    Confirm { destination: String, planned_deletes: usize, moves: bool, reply: Sender<bool> },
    Finished(JobOutcome),
}

enum Modal {
    Message { title: String, message: String, is_error: bool },
    ConfirmDestructive { message: String, reply: Sender<bool> },
    ConfirmClose,
}

#[derive(Default)]
struct ProgressView {
    overall_percent: f64,
    current_file_percent: f64,
    current_file: String,
    bytes_copied: u64,
    total_bytes: u64,
    bytes_per_second: f64,
    eta: Option<Duration>,
    files_completed: usize,
    total_files: usize,
    is_estimated: bool,
}

/// The transcript shown in the GUI, trimmed to its most recent characters.
struct LogView {
    lines: VecDeque<String>,
    characters: usize,
}

impl LogView {
    fn new(initial: &str) -> Self {
        Self { lines: VecDeque::from([initial.to_owned()]), characters: initial.len() }
    }

    fn clear(&mut self) {
        self.lines.clear();
        self.characters = 0;
    }

    fn push(&mut self, line: String) {
        self.characters += line.len() + 1;
        self.lines.push_back(line);
        while self.characters > MAXIMUM_VISIBLE_LOG_CHARACTERS
            && let Some(removed) = self.lines.pop_front()
        {
            self.characters -= removed.len() + 1;
        }
    }
}

struct WorkerHost {
    events: Sender<UiEvent>,
    ctx: egui::Context,
}

impl WorkerHost {
    fn send(&self, event: UiEvent) {
        let _ = self.events.send(event);
        self.ctx.request_repaint();
    }
}

impl JobHost for WorkerHost {
    fn set_status(&self, text: &str) {
        self.send(UiEvent::Status(text.to_owned()));
    }

    fn set_totals(&self, total_bytes: u64, total_files: usize) {
        self.send(UiEvent::Totals(total_bytes, total_files));
    }

    fn confirm_destructive(&self, destination: &str, planned_deletes: usize, moves_source_items: bool) -> bool {
        let (reply, answer) = mpsc::channel();
        self.send(UiEvent::Confirm {
            destination: destination.to_owned(),
            planned_deletes,
            moves: moves_source_items,
            reply,
        });
        // A dropped reply (for example when the window closes) declines the operation.
        answer.recv().unwrap_or(false)
    }
}

pub struct RobustCopyApp {
    form: TransferForm,
    runner: Arc<RobocopyRunner>,
    events: Receiver<UiEvent>,
    sender: Sender<UiEvent>,
    worker: Option<JoinHandle<()>>,
    stage: CopyJobStage,
    status_text: String,
    progress: ProgressView,
    log: LogView,
    last_log_path: String,
    modal: Option<Modal>,
    allow_close: bool,
}

impl RobustCopyApp {
    pub fn new(cc: &eframe::CreationContext<'_>) -> Self {
        theme::apply(&cc.egui_ctx);
        #[cfg(windows)]
        use_dark_title_bar(cc);

        let (sender, events) = mpsc::channel();
        let sink_sender = sender.clone();
        let ctx = cc.egui_ctx.clone();
        let runner = RobocopyRunner::new(
            runner::default_log_directory(),
            Arc::new(move |event| {
                let _ = sink_sender.send(UiEvent::Runner(event));
                // Coalesces bursts of Robocopy output into at most ten repaints per second.
                ctx.request_repaint_after(UI_UPDATE_INTERVAL);
            }),
        );

        Self {
            form: TransferForm::default(),
            runner: Arc::new(runner),
            events,
            sender,
            worker: None,
            stage: CopyJobStage::Idle,
            status_text: "Ready to configure a copy".to_owned(),
            progress: ProgressView { current_file: "—".to_owned(), ..ProgressView::default() },
            log: LogView::new("[idle] Waiting to start…"),
            last_log_path: String::new(),
            modal: None,
            allow_close: false,
        }
    }

    fn is_busy(&self) -> bool {
        self.stage.is_busy() || self.worker.is_some()
    }

    fn apply_events(&mut self) {
        while let Ok(event) = self.events.try_recv() {
            match event {
                UiEvent::Runner(RunnerEvent::Progress(progress)) => self.apply_progress(progress),
                UiEvent::Runner(RunnerEvent::Output(line)) => self.log.push(line),
                UiEvent::Status(text) => self.status_text = text,
                UiEvent::Totals(total_bytes, total_files) => {
                    self.progress.total_bytes = total_bytes;
                    self.progress.total_files = total_files;
                }
                UiEvent::Confirm { destination, planned_deletes, moves, reply } => {
                    let message = job::destructive_confirmation_message(&destination, planned_deletes, moves);
                    self.modal = Some(Modal::ConfirmDestructive { message, reply });
                }
                UiEvent::Finished(outcome) => {
                    if let Some(worker) = self.worker.take() {
                        let _ = worker.join();
                    }

                    self.stage = outcome.stage;
                    self.status_text = outcome.status;
                    self.last_log_path = outcome.log_path;
                    if let Some(message) = outcome.error {
                        self.show_message(app_identity::DISPLAY_NAME, &message, true);
                    }
                }
            }
        }
    }

    fn apply_progress(&mut self, progress: CopyJobProgress) {
        self.stage = progress.stage;
        let view = &mut self.progress;
        view.overall_percent = progress.overall_percent;
        view.current_file_percent = progress.current_file_percent;
        if !progress.current_file.trim().is_empty() {
            view.current_file = progress.current_file;
        }

        view.bytes_copied = progress.bytes_copied;
        view.total_bytes = progress.total_bytes;
        view.bytes_per_second = progress.bytes_per_second;
        view.eta = progress.estimated_remaining;
        view.files_completed = progress.files_completed;
        view.total_files = progress.total_files;
        view.is_estimated = progress.is_estimated;
    }

    fn show_message(&mut self, title: &str, message: &str, is_error: bool) {
        self.modal = Some(Modal::Message { title: title.to_owned(), message: message.to_owned(), is_error });
    }

    fn start(&mut self, ctx: &egui::Context) {
        let request = self.form.build_request();
        let errors = validator::validate(&request, true);
        if !errors.is_empty() {
            self.show_message("Check copy settings", &errors.join("\n"), true);
            return;
        }

        self.progress = ProgressView { current_file: "Waiting for Robocopy…".to_owned(), ..ProgressView::default() };
        self.log.clear();
        self.status_text = "Scanning source and destination…".to_owned();
        self.stage = CopyJobStage::Scanning;

        let runner = Arc::clone(&self.runner);
        let host = WorkerHost { events: self.sender.clone(), ctx: ctx.clone() };
        let spawned = std::thread::Builder::new().name("robocopy-job".to_owned()).spawn(move || {
            let outcome =
                std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| job::run_job(&*runner, &request, &host)))
                    .unwrap_or_else(|_| JobOutcome {
                        stage: CopyJobStage::Failed,
                        status: "The operation failed".to_owned(),
                        log_path: runner.current_log_path(),
                        error: Some(
                            "An unexpected error stopped the operation. A crash report was saved to the log folder."
                                .to_owned(),
                        ),
                    });
            host.send(UiEvent::Finished(outcome));
        });

        match spawned {
            Ok(worker) => self.worker = Some(worker),
            Err(error) => {
                self.stage = CopyJobStage::Failed;
                self.status_text = "The operation failed".to_owned();
                self.show_message(app_identity::DISPLAY_NAME, &format!("Unable to start the copy: {error}"), true);
            }
        }
    }

    fn control(&mut self, action: fn(&RobocopyRunner) -> Result<(), crate::core::models::RunError>) {
        if let Err(error) = action(&self.runner) {
            self.show_message(app_identity::DISPLAY_NAME, &error.to_string(), true);
        }
    }

    fn stop(&mut self) {
        if self.stage.is_busy() {
            self.stage = CopyJobStage::Stopping;
        }

        self.control(RobocopyRunner::stop);
    }

    /// Stops any active job and waits for the worker so Robocopy never outlives the window.
    fn shut_down(&mut self) {
        // Dropping a pending confirmation declines it, so the worker cannot wait on the GUI.
        self.modal = None;
        let _ = self.runner.stop();
        let Some(worker) = self.worker.take() else { return };
        while !worker.is_finished() {
            // A confirmation request may still be queued (or arrive after a stop that found the
            // runner idle between the scan and the copy); dropping it declines it too.
            while self.events.try_recv().is_ok() {}
            std::thread::sleep(Duration::from_millis(10));
        }

        let _ = worker.join();
    }

    fn pick_folder(&mut self, title: &str, destination: bool) {
        let current = if destination { &self.form.destination_path } else { &self.form.source_path };
        let mut dialog = rfd::FileDialog::new().set_title(title);
        if Path::new(current).is_dir() {
            dialog = dialog.set_directory(current);
        }

        if let Some(folder) = dialog.pick_folder() {
            let folder = folder.to_string_lossy().into_owned();
            if destination {
                self.form.destination_path = folder;
            } else {
                self.form.source_path = folder;
            }
        }
    }

    fn copy_log(&mut self, ctx: &egui::Context) {
        match log_tail::read(Path::new(&self.last_log_path), log_tail::DEFAULT_MAXIMUM_BYTES) {
            Ok(tail) => {
                ctx.copy_text(tail.text);
                let message = if tail.is_truncated {
                    format!(
                        "The transcript is {}, so only the last {} is on the clipboard. Open the log for the full transcript.",
                        format::megabytes(tail.file_bytes),
                        format::megabytes(tail.maximum_bytes)
                    )
                } else {
                    "The complete Robocopy transcript is on the clipboard.".to_owned()
                };
                self.show_message("Log copied", &message, false);
            }
            Err(error) => self.show_message("Could not copy the log", &error.to_string(), true),
        }
    }

    fn open_path(&mut self, path: &Path) {
        if let Err(error) = open_in_shell(path) {
            self.show_message(app_identity::DISPLAY_NAME, &format!("Unable to open {}: {error}", path.display()), true);
        }
    }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────

    fn header(&mut self, ui: &mut Ui) {
        ui.horizontal(|ui| {
            ui.vertical(|ui| {
                ui.horizontal(|ui| {
                    let (dot, _) = ui.allocate_exact_size(vec2(6.0, 6.0), Sense::hover());
                    ui.painter().circle_filled(dot.center(), 3.0, GREEN);
                    ui.label(RichText::new("WINDOWS FILE TRANSFER UTILITY").font(mono(10.0)).color(GREEN));
                });
                ui.label(RichText::new("ROBUSTCOPY").font(strong(26.0)).color(TEXT_LIGHT));
                ui.label(
                    RichText::new("Reliable Windows file transfers, without command-line guesswork")
                        .font(body(12.5))
                        .color(TEXT_DIM),
                );
            });

            ui.with_layout(Layout::top_down(Align::Max), |ui| {
                ui.label(
                    RichText::new("Review the command before using Mirror, Purge, or Move.")
                        .font(strong(12.5))
                        .color(AMBER),
                );
                ui.label(
                    RichText::new("Stop ends the current run. Pause keeps Robocopy in memory for Resume.")
                        .font(body(11.5))
                        .color(TEXT_FAINT),
                );
                self.stage_pill(ui);
                self.run_controls(ui);
            });
        });
    }

    fn stage_pill(&self, ui: &mut Ui) {
        let (dot, border) = match self.stage {
            CopyJobStage::Scanning | CopyJobStage::Running => (GREEN, GREEN_DIM),
            CopyJobStage::Paused => (AMBER, AMBER_DIM),
            CopyJobStage::Failed | CopyJobStage::Canceled => (RED, RED_DIM),
            _ => (TEXT_FAINT, BORDER),
        };
        // Painted by hand: a Frame in this right-aligned column would lay its content out right to left.
        let galley = ui.painter().layout_no_wrap(self.stage.as_upper().to_owned(), mono(10.5), TEXT_DIM);
        let size = vec2(12.0 + 7.0 + 8.0 + galley.size().x + 12.0, galley.size().y + 10.0);
        let (rect, _) = ui.allocate_exact_size(size, Sense::hover());
        let painter = ui.painter();
        painter.rect(rect, CornerRadius::same(18), PANEL, Stroke::new(1.0, border), egui::StrokeKind::Inside);
        painter.circle_filled(egui::pos2(rect.left() + 15.5, rect.center().y), 3.5, dot);
        painter.galley(egui::pos2(rect.left() + 27.0, rect.center().y - galley.size().y / 2.0), galley, TEXT_DIM);
    }

    fn run_controls(&mut self, ui: &mut Ui) {
        let can_start = !self.is_busy();
        // (label, enabled, fill, text, disabled fill)
        let buttons = [
            ("START", can_start, GREEN, CHECK_TEXT, Color32::from_rgb(0x1E, 0x5C, 0x4B)),
            ("PAUSE", self.stage == CopyJobStage::Running, AMBER, CHECK_TEXT, Color32::from_rgb(0x5C, 0x4C, 0x24)),
            ("RESUME", self.stage == CopyJobStage::Paused, BLUE, CHECK_TEXT, Color32::from_rgb(0x2A, 0x34, 0x40)),
            ("STOP", self.stage.is_busy(), RED, Color32::WHITE, Color32::from_rgb(0x5A, 0x2C, 0x2E)),
        ];
        let mut clicked = None;
        ui.horizontal(|ui| {
            ui.spacing_mut().item_spacing.x = 1.0;
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                for (label, enabled, fill, text, disabled_fill) in buttons.into_iter().rev() {
                    let fill = if enabled { fill } else { disabled_fill };
                    let text = if enabled { text } else { TEXT_DIM };
                    let button = Button::new(RichText::new(label).font(strong(12.0)).color(text))
                        .fill(fill)
                        .stroke(Stroke::NONE)
                        .corner_radius(0)
                        .min_size(vec2(114.0, 34.0));
                    if ui.add_enabled(enabled, button).clicked() {
                        clicked = Some(label);
                    }
                }
            });
        });

        match clicked {
            Some("START") => self.start(ui.ctx()),
            Some("PAUSE") => self.control(RobocopyRunner::pause),
            Some("RESUME") => self.control(RobocopyRunner::resume),
            Some("STOP") => self.stop(),
            _ => {}
        }
    }

    fn locations_panel(&mut self, ui: &mut Ui) {
        let busy = self.is_busy();
        panel_header(ui, "TRANSFER LOCATIONS", "Choose what to copy and where it should go", |ui| {
            if ui.add_enabled(!busy, small_button("Clear")).clicked() {
                self.form.clear_paths();
            }
        });

        // Validation is pinned to the bottom so it stays visible; the fields above it scroll.
        let (command, validation) = self.form.preview();
        ui.with_layout(Layout::bottom_up(Align::Min), |ui| {
            if !validation.is_empty() {
                Frame::new()
                    .fill(Color32::from_rgb(0x2A, 0x1C, 0x1E))
                    .stroke(Stroke::new(1.0, RED_DIM))
                    .corner_radius(3)
                    .inner_margin(Margin::symmetric(10, 6))
                    .show(ui, |ui| {
                        ui.set_width(ui.available_width());
                        ui.add(Label::new(RichText::new(validation).font(body(11.5)).color(RED)).wrap());
                    });
            }

            ScrollArea::vertical().id_salt("locations").auto_shrink([false, false]).show(ui, |ui| {
                ui.with_layout(Layout::top_down(Align::Min), |ui| self.location_fields(ui, busy, &command));
            });
        });
    }

    fn location_fields(&mut self, ui: &mut Ui, busy: bool, command: &str) {
        ui.add_space(6.0);
        mono_label(ui, "SOURCE");
        if path_button(ui, &self.form.source_path, "Source directory here", !busy) {
            self.pick_folder("Choose source folder", false);
        }

        ui.add_space(4.0);
        ui.horizontal(|ui| {
            mono_label(ui, "DESTINATION");
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                let swap = Button::new(RichText::new("⇅").size(13.0)).min_size(vec2(26.0, 22.0));
                if ui.add_enabled(!busy, swap).on_hover_text("Swap source and destination").clicked() {
                    self.form.swap_paths();
                }
            });
        });
        if path_button(ui, &self.form.destination_path, "Destination directory here", !busy) {
            self.pick_folder("Choose destination folder", true);
        }

        ui.add_space(10.0);
        mono_label(ui, "FILES");
        ui.add_enabled(
            !busy,
            TextEdit::singleline(&mut self.form.file_patterns)
                .font(mono(11.5))
                .hint_text("*.*")
                .desired_width(f32::INFINITY),
        );

        ui.add_space(12.0);
        ui.horizontal(|ui| {
            ui.label(RichText::new("Review command before running").font(body(11.5)).color(TEXT_DIM));
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                if ui.add_enabled(!command.is_empty(), small_button("copy")).clicked() {
                    ui.ctx().copy_text(command.to_owned());
                }
            });
        });
        Frame::new()
            .fill(CONSOLE)
            .stroke(Stroke::new(1.0, BORDER_SOFT))
            .corner_radius(4)
            .inner_margin(Margin::same(10))
            .show(ui, |ui| {
                ui.set_width(ui.available_width());
                ScrollArea::vertical().id_salt("command").max_height(150.0).show(ui, |ui| {
                    ui.add(Label::new(RichText::new(command).font(mono(11.0)).color(GREEN)).wrap());
                });
            });
    }

    fn strategy_panel(&mut self, ui: &mut Ui) {
        let busy = self.is_busy();
        panel_header(
            ui,
            "CHOOSE A TRANSFER STRATEGY",
            "Click an option card to include its switch. Selecting one turns off options it conflicts with.",
            |_| {},
        );

        let advanced_height = 40.0;
        let mut toggled = None;
        ScrollArea::vertical()
            .id_salt("strategy")
            .max_height((ui.available_height() - advanced_height).max(100.0))
            .auto_shrink([false, false])
            .show(ui, |ui| {
                for category in CATEGORIES {
                    ui.add_space(10.0);
                    mono_label(ui, &category.to_uppercase());
                    let indices: Vec<usize> = (0..self.form.options.len())
                        .filter(|index| self.form.options[*index].definition.category == category)
                        .collect();
                    for pair in indices.chunks(2) {
                        ui.horizontal(|ui| {
                            ui.spacing_mut().item_spacing.x = 8.0;
                            let width = ((ui.available_width() - 8.0) / 2.0).floor();
                            for index in pair {
                                if option_card(ui, &mut self.form.options[*index], width, !busy) {
                                    toggled = Some(*index);
                                }
                            }
                        });
                    }
                }
            });

        if let Some(index) = toggled {
            let option = &self.form.options[index];
            let (id, selected) = (option.id(), !option.selected);
            self.form.set_selected(id, selected);
        }

        ui.add_space(8.0);
        ui.horizontal(|ui| {
            mono_label(ui, "ADVANCED");
            ui.add_enabled(
                !busy,
                TextEdit::singleline(&mut self.form.advanced_options)
                    .font(mono(11.0))
                    .hint_text("Additional switches, e.g. /FFT /XA:H")
                    .desired_width(f32::INFINITY),
            );
        });
    }

    fn overview_panel(&mut self, ui: &mut Ui) {
        panel_header(ui, "LIVE TRANSFER OVERVIEW", &self.status_text.clone(), |_| {});
        let view = &self.progress;

        ui.add_space(10.0);
        let overall_label = if view.is_estimated { "Overall progress (estimated)" } else { "Overall progress" };
        ui.horizontal(|ui| {
            ui.label(RichText::new(overall_label).font(strong(12.0)).color(TEXT_LIGHT));
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                ui.label(RichText::new(format!("{:.0}%", view.overall_percent)).font(strong(15.0)).color(GREEN));
            });
        });
        progress_bar(ui, view.overall_percent, 8.0, GREEN);
        ui.label(
            RichText::new(format!("{} / {}", format::bytes(view.bytes_copied), format::bytes(view.total_bytes)))
                .font(mono(10.5))
                .color(TEXT_FAINT),
        );

        ui.add_space(8.0);
        mono_label(ui, "CURRENT FILE");
        ui.horizontal(|ui| {
            let percent = format!("{:.0}%", view.current_file_percent);
            let percent_width = 40.0;
            ui.allocate_ui_with_layout(
                vec2(ui.available_width() - percent_width, 18.0),
                Layout::left_to_right(Align::Center),
                |ui| {
                    ui.add(Label::new(RichText::new(&view.current_file).font(mono(11.0)).color(TEXT_DIM)).truncate())
                        .on_hover_text(&view.current_file);
                },
            );
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                ui.label(RichText::new(percent).font(mono(11.0)).color(TEXT_LIGHT));
            });
        });
        progress_bar(ui, view.current_file_percent, 4.0, BLUE);

        ui.add_space(10.0);
        let speed = if view.bytes_per_second <= 0.0 {
            "0 MB/s".to_owned()
        } else {
            format!("{}/s", format::bytes(view.bytes_per_second as u64))
        };
        let files = format!("{} / {}", format::grouped(view.files_completed), format::grouped(view.total_files));
        let metrics =
            [("SPEED", speed, GREEN), ("TIME LEFT", format::eta(view.eta), BLUE), ("FILES", files, TEXT_LIGHT)];
        ui.horizontal(|ui| {
            ui.spacing_mut().item_spacing.x = 1.0;
            let width = (ui.available_width() - 2.0) / 3.0;
            for (label, value, color) in metrics {
                Frame::new().fill(PANEL2).inner_margin(Margin::symmetric(11, 8)).show(ui, |ui| {
                    ui.vertical(|ui| {
                        ui.set_width(width - 22.0);
                        ui.spacing_mut().item_spacing.y = 2.0;
                        ui.label(RichText::new(label).font(mono(9.5)).color(TEXT_FAINT));
                        ui.add(Label::new(RichText::new(value).font(strong(14.0)).color(color)).truncate());
                    });
                });
            }
        });

        ui.add_space(10.0);
        let has_log = Path::new(&self.last_log_path).is_file();
        let mut action = None;
        ui.horizontal(|ui| {
            ui.label(RichText::new("Run transcript").font(strong(12.0)).color(TEXT_LIGHT))
                .on_hover_text("Saved to %LocalAppData%\\RobustCopy\\Logs");
            ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                for (label, enabled) in [("DIR", true), ("OPEN", has_log), ("COPY", has_log)] {
                    if ui.add_enabled(enabled, small_button(label)).clicked() {
                        action = Some(label);
                    }
                }
            });
        });

        match action {
            Some("COPY") => self.copy_log(ui.ctx()),
            Some("OPEN") => self.open_path(&PathBuf::from(&self.last_log_path)),
            Some("DIR") => match app_identity::ensure_log_directory(None) {
                Ok(directory) => self.open_path(&directory),
                Err(error) => self.show_message(app_identity::DISPLAY_NAME, &error.to_string(), true),
            },
            _ => {}
        }

        // The console fills exactly the space left in the panel.
        let console = ui.available_rect_before_wrap();
        ui.painter().rect(
            console,
            CornerRadius::same(4),
            CONSOLE,
            Stroke::new(1.0, BORDER_SOFT),
            egui::StrokeKind::Inside,
        );
        let mut inner =
            ui.new_child(UiBuilder::new().max_rect(console.shrink(8.0)).layout(Layout::top_down(Align::Min)));
        let font = mono(10.5);
        let row_height = inner.fonts_mut(|fonts| fonts.row_height(&font));
        ScrollArea::both().id_salt("transcript").stick_to_bottom(true).auto_shrink([false, false]).show_rows(
            &mut inner,
            row_height,
            self.log.lines.len(),
            |ui, rows| {
                for line in self.log.lines.range(rows) {
                    ui.add(Label::new(RichText::new(line).font(font.clone()).color(TEXT_DIM)).extend());
                }
            },
        );
    }

    fn modal(&mut self, ctx: &egui::Context) {
        let Some(modal) = &self.modal else { return };
        let (title, message, accent) = match modal {
            Modal::Message { title, message, is_error } => {
                (title.clone(), message.clone(), if *is_error { RED } else { GREEN })
            }
            Modal::ConfirmDestructive { message, .. } => {
                ("Confirm destructive operation".to_owned(), message.clone(), AMBER)
            }
            Modal::ConfirmClose => (
                "Copy in progress".to_owned(),
                format!("A scan or copy is still active. Stop it and close {}?", app_identity::DISPLAY_NAME),
                AMBER,
            ),
        };
        let has_choice = !matches!(modal, Modal::Message { .. });

        let mut answer = None;
        egui::Modal::new(egui::Id::unique("robustcopy-modal"))
            .frame(
                Frame::popup(&ctx.global_style())
                    .fill(PANEL)
                    .stroke(Stroke::new(1.0, accent))
                    .inner_margin(Margin::same(18)),
            )
            .show(ctx, |ui| {
                ui.set_max_width(460.0);
                ui.label(RichText::new(title).font(strong(15.0)).color(TEXT_LIGHT));
                ui.add_space(8.0);
                ui.add(Label::new(RichText::new(message).font(body(12.5)).color(TEXT_DIM)).wrap());
                ui.add_space(14.0);
                ui.with_layout(Layout::right_to_left(Align::Center), |ui| {
                    if has_choice {
                        // No is first in the layout and focused, so Enter never confirms by accident.
                        let no = ui.add(Button::new("No").min_size(vec2(72.0, 26.0)));
                        if no.clicked() || ui.input(|input| input.key_pressed(egui::Key::Escape)) {
                            answer = Some(false);
                        }

                        let yes = Button::new(RichText::new("Yes").color(accent)).min_size(vec2(72.0, 26.0));
                        if ui.add(yes).clicked() {
                            answer = Some(true);
                        }

                        if !no.has_focus() && answer.is_none() && ui.memory(|memory| memory.focused().is_none()) {
                            no.request_focus();
                        }
                    } else {
                        let ok = ui.add(Button::new("OK").min_size(vec2(72.0, 26.0)));
                        if ok.clicked()
                            || ui.input(|input| {
                                input.key_pressed(egui::Key::Escape) || input.key_pressed(egui::Key::Enter)
                            })
                        {
                            answer = Some(true);
                        }
                    }
                });
            });

        let Some(answer) = answer else { return };
        match self.modal.take() {
            Some(Modal::ConfirmDestructive { reply, .. }) => {
                let _ = reply.send(answer);
            }
            Some(Modal::ConfirmClose) if answer => {
                self.shut_down();
                self.allow_close = true;
                ctx.send_viewport_cmd(egui::ViewportCommand::Close);
            }
            _ => {}
        }
    }
}

impl eframe::App for RobustCopyApp {
    fn logic(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.apply_events();

        if ctx.input(|input| input.viewport().close_requested()) && !self.allow_close && self.is_busy() {
            ctx.send_viewport_cmd(egui::ViewportCommand::CancelClose);
            if !matches!(self.modal, Some(Modal::ConfirmClose)) {
                // Replacing a pending confirmation declines it.
                self.modal = Some(Modal::ConfirmClose);
            }
        }

        if self.is_busy() {
            // Keeps speed and ETA ticking even when Robocopy is quiet.
            ctx.request_repaint_after(Duration::from_millis(500));
        }
    }

    fn ui(&mut self, ui: &mut Ui, _frame: &mut eframe::Frame) {
        let ctx = ui.ctx().clone();
        let full = ui.max_rect();
        ui.painter().rect_filled(full, 0.0, BG);
        let content = full.shrink(18.0);

        let mut header = ui.new_child(UiBuilder::new().max_rect(content).layout(Layout::top_down(Align::Min)));
        self.header(&mut header);
        let rule = header.min_rect().bottom() + 12.0;
        ui.painter().hline(content.x_range(), rule, Stroke::new(1.0, BORDER_SOFT));

        // Fixed, clipped columns so no panel's content can push its neighbours around.
        let top = rule + 14.0;
        let gap = 18.0;
        let weights = [1.05, 1.1, 1.0];
        let total: f32 = weights.iter().sum();
        let unit = (content.width() - gap * 2.0) / total;
        let mut left = content.left();
        for (index, weight) in weights.into_iter().enumerate() {
            let rect = Rect::from_min_max(pos2(left, top), pos2(left + unit * weight, content.bottom()));
            left = rect.right() + gap;
            panel(ui, rect, |ui| match index {
                0 => self.locations_panel(ui),
                1 => self.strategy_panel(ui),
                _ => self.overview_panel(ui),
            });
        }

        self.modal(&ctx);
    }

    fn on_exit(&mut self) {
        self.shut_down();
    }
}

// ── Widgets ─────────────────────────────────────────────────────────────────────────────────

fn panel(ui: &mut Ui, rect: Rect, content: impl FnOnce(&mut Ui)) {
    ui.painter().rect(rect, CornerRadius::same(6), PANEL, Stroke::new(1.0, BORDER), egui::StrokeKind::Inside);
    let inner = rect.shrink(15.0);
    let mut child = ui.new_child(UiBuilder::new().max_rect(inner).layout(Layout::top_down(Align::Min)));
    child.set_clip_rect(rect.shrink(1.0));
    content(&mut child);
}

fn panel_header(ui: &mut Ui, title: &str, subtitle: &str, actions: impl FnOnce(&mut Ui)) {
    ui.horizontal(|ui| {
        ui.label(RichText::new(title).font(strong(13.5)).color(TEXT_LIGHT));
        ui.with_layout(Layout::right_to_left(Align::Center), actions);
    });
    ui.add(Label::new(RichText::new(subtitle).font(body(11.5)).color(TEXT_FAINT)).wrap());
    ui.add_space(4.0);
    let rect = ui.available_rect_before_wrap();
    ui.painter().hline(rect.x_range(), rect.top(), Stroke::new(1.0, BORDER_SOFT));
    ui.add_space(6.0);
}

fn mono_label(ui: &mut Ui, text: &str) {
    ui.label(RichText::new(text).font(mono(10.0)).color(TEXT_FAINT));
}

fn small_button(text: &str) -> Button<'_> {
    Button::new(RichText::new(text).font(mono(9.5))).min_size(vec2(0.0, 20.0))
}

/// A full-width field that shows a folder path and opens the folder picker when clicked.
fn path_button(ui: &mut Ui, path: &str, placeholder: &str, enabled: bool) -> bool {
    let (text, color) = if path.trim().is_empty() { (placeholder, TEXT_FAINT) } else { (path, TEXT_LIGHT) };
    let height = 34.0;
    let (rect, response) = ui.allocate_exact_size(vec2(ui.available_width(), height), Sense::click());
    let hovered = enabled && response.hovered();
    let painter = ui.painter_at(rect);
    painter.rect(
        rect,
        CornerRadius::same(4),
        BG_RAISED,
        Stroke::new(1.0, if hovered { TEXT_FAINT } else { BORDER }),
        egui::StrokeKind::Inside,
    );
    painter.text(rect.left_center() + vec2(10.0, 0.0), Align2::LEFT_CENTER, "📁", body(12.0), TEXT_DIM);
    let galley = ui.painter().layout_job(egui::text::LayoutJob {
        wrap: egui::text::TextWrapping::truncate_at_width(rect.width() - 44.0),
        ..egui::text::LayoutJob::simple_singleline(text.to_owned(), body(12.0), color)
    });
    painter.galley(rect.left_center() + vec2(32.0, -galley.size().y / 2.0), galley, color);
    let response =
        response.on_hover_text(if enabled { "Choose a folder" } else { "Unavailable while a job is running" });
    if enabled { response.on_hover_cursor(egui::CursorIcon::PointingHand).clicked() } else { false }
}

fn progress_bar(ui: &mut Ui, percent: f64, height: f32, color: Color32) {
    let (rect, _) = ui.allocate_exact_size(vec2(ui.available_width(), height), Sense::hover());
    let painter = ui.painter_at(rect);
    let radius = CornerRadius::same((height / 2.0) as u8);
    painter.rect_filled(rect, radius, BG_RAISED);
    let fraction = (percent / 100.0).clamp(0.0, 1.0) as f32;
    if fraction > 0.0 {
        let mut filled = rect;
        filled.set_width(rect.width() * fraction);
        painter.rect_filled(filled, radius, color);
    }
}

/// Draws one strategy card. Returns true when the card was clicked to toggle the option.
fn option_card(ui: &mut Ui, option: &mut OptionItem, width: f32, enabled: bool) -> bool {
    const HEIGHT: f32 = 98.0;
    const PAD: f32 = 10.0;
    let (rect, response) = ui.allocate_exact_size(vec2(width, HEIGHT), Sense::click());
    let selected = option.selected;
    let destructive = option.definition.is_destructive;
    let hovered = enabled && response.hovered();

    let (fill, border) = match (selected, destructive) {
        (true, true) => (Color32::from_rgb(0x2A, 0x1F, 0x21), RED_DIM),
        (true, false) => (Color32::from_rgb(0x1B, 0x2A, 0x26), GREEN_DIM),
        (false, _) if hovered => (Color32::from_rgb(0x25, 0x2B, 0x30), TEXT_FAINT),
        (false, _) => (PANEL2, BORDER),
    };
    let painter = ui.painter_at(rect);
    painter.rect(rect, CornerRadius::same(4), fill, Stroke::new(1.0, border), egui::StrokeKind::Inside);

    let content = rect.shrink(PAD);
    let flag_color = match (selected, destructive) {
        (true, true) => RED,
        (true, false) => GREEN,
        _ => TEXT_DIM,
    };
    painter.text(content.left_top(), Align2::LEFT_TOP, option.flag_text(), mono(10.5), flag_color);

    let check = egui::Rect::from_min_size(egui::pos2(content.right() - 14.0, content.top()), vec2(14.0, 14.0));
    let check_fill = if selected { flag_color } else { BG_RAISED };
    let check_border = if selected { flag_color } else { BORDER };
    painter.rect(check, CornerRadius::same(2), check_fill, Stroke::new(1.0, check_border), egui::StrokeKind::Inside);
    if selected {
        painter.text(check.center(), Align2::CENTER_CENTER, "✓", strong(10.0), CHECK_TEXT);
    }

    painter.text(content.left_top() + vec2(0.0, 20.0), Align2::LEFT_TOP, option.label(), strong(11.5), TEXT_LIGHT);
    let description = ui.painter().layout(option.display_description(), body(10.5), TEXT_FAINT, content.width());
    let clip = egui::Rect::from_min_max(content.left_top() + vec2(0.0, 40.0), content.right_bottom());
    ui.painter_at(clip).galley(clip.left_top(), description, TEXT_FAINT);

    if option.has_value() {
        let field = egui::Rect::from_min_size(
            egui::pos2(content.right() - 14.0 - 8.0 - 52.0, rect.top() + 6.0),
            vec2(52.0, 22.0),
        );
        // A child Ui, unlike `ui.put`, leaves the row's cursor where the card ended.
        let mut field_ui = ui.new_child(UiBuilder::new().max_rect(field));
        field_ui.add_enabled(
            enabled && selected,
            TextEdit::singleline(&mut option.value)
                .font(mono(9.5))
                .margin(Margin::symmetric(4, 3))
                .desired_width(field.width() - 8.0),
        );
    }

    let response = response.on_hover_text(option.display_description());
    enabled && response.on_hover_cursor(egui::CursorIcon::PointingHand).clicked()
}

fn open_in_shell(path: &Path) -> std::io::Result<()> {
    #[cfg(windows)]
    let program = "explorer";
    #[cfg(target_os = "macos")]
    let program = "open";
    #[cfg(all(unix, not(target_os = "macos")))]
    let program = "xdg-open";
    std::process::Command::new(program).arg(path).spawn().map(|_| ())
}

#[cfg(windows)]
fn use_dark_title_bar(cc: &eframe::CreationContext<'_>) {
    use raw_window_handle::{HasWindowHandle, RawWindowHandle};
    use windows::Win32::Foundation::HWND;
    use windows::Win32::Graphics::Dwm::{DWMWA_USE_IMMERSIVE_DARK_MODE, DwmSetWindowAttribute};

    let Ok(handle) = cc.window_handle() else { return };
    if let RawWindowHandle::Win32(handle) = handle.as_raw() {
        let enabled: i32 = 1;
        let _ = unsafe {
            DwmSetWindowAttribute(
                HWND(handle.hwnd.get() as *mut _),
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                (&raw const enabled).cast(),
                size_of::<i32>() as u32,
            )
        };
    }
}
