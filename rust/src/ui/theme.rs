//! The dark console-inspired palette and typography from the original WPF window.

use std::sync::Arc;

use egui::{Color32, FontData, FontDefinitions, FontFamily, FontId, Stroke, TextStyle};

pub const BG: Color32 = Color32::from_rgb(0x14, 0x17, 0x1A);
pub const BG_RAISED: Color32 = Color32::from_rgb(0x19, 0x1D, 0x21);
pub const PANEL: Color32 = Color32::from_rgb(0x1C, 0x21, 0x25);
pub const PANEL2: Color32 = Color32::from_rgb(0x21, 0x26, 0x2B);
pub const BORDER: Color32 = Color32::from_rgb(0x2B, 0x32, 0x38);
pub const BORDER_SOFT: Color32 = Color32::from_rgb(0x23, 0x29, 0x2E);
pub const TEXT_LIGHT: Color32 = Color32::from_rgb(0xE8, 0xEB, 0xED);
pub const TEXT_DIM: Color32 = Color32::from_rgb(0x9A, 0xA3, 0xAB);
pub const TEXT_FAINT: Color32 = Color32::from_rgb(0x5B, 0x64, 0x69);
pub const GREEN: Color32 = Color32::from_rgb(0x35, 0xD6, 0xA8);
pub const GREEN_DIM: Color32 = Color32::from_rgb(0x1E, 0x8F, 0x6F);
pub const AMBER: Color32 = Color32::from_rgb(0xF0, 0xB4, 0x37);
pub const AMBER_DIM: Color32 = Color32::from_rgb(0x8A, 0x6A, 0x22);
pub const RED: Color32 = Color32::from_rgb(0xEA, 0x52, 0x57);
pub const RED_DIM: Color32 = Color32::from_rgb(0x7A, 0x32, 0x34);
pub const BLUE: Color32 = Color32::from_rgb(0x5B, 0x9C, 0xE0);
pub const CONSOLE: Color32 = Color32::from_rgb(0x0F, 0x12, 0x14);
pub const CHECK_TEXT: Color32 = Color32::from_rgb(0x0B, 0x1F, 0x1A);

pub fn mono(size: f32) -> FontId {
    FontId::new(size, FontFamily::Monospace)
}

pub fn body(size: f32) -> FontId {
    FontId::new(size, FontFamily::Proportional)
}

/// The semibold face when Segoe UI Semibold is installed, else the regular face.
pub fn strong(size: f32) -> FontId {
    FontId::new(size, FontFamily::Name("strong".into()))
}

pub fn apply(ctx: &egui::Context) {
    ctx.set_fonts(fonts());

    let mut style = (*ctx.global_style()).clone();
    style.text_styles = [
        (TextStyle::Heading, body(20.0)),
        (TextStyle::Body, body(12.5)),
        (TextStyle::Button, body(12.0)),
        (TextStyle::Small, body(10.5)),
        (TextStyle::Monospace, mono(11.0)),
    ]
    .into();
    style.spacing.item_spacing = egui::vec2(6.0, 6.0);
    style.spacing.button_padding = egui::vec2(8.0, 4.0);

    let visuals = &mut style.visuals;
    *visuals = egui::Visuals::dark();
    visuals.panel_fill = BG;
    visuals.window_fill = PANEL;
    visuals.window_stroke = Stroke::new(1.0, BORDER);
    visuals.extreme_bg_color = BG_RAISED;
    visuals.faint_bg_color = PANEL2;
    visuals.override_text_color = None;
    visuals.selection.bg_fill = GREEN_DIM;
    visuals.selection.stroke = Stroke::new(1.0, GREEN);
    visuals.hyperlink_color = BLUE;
    for (widget, fill, stroke, text) in [
        (&mut visuals.widgets.noninteractive, PANEL, BORDER_SOFT, TEXT_DIM),
        (&mut visuals.widgets.inactive, PANEL2, BORDER, TEXT_DIM),
        (&mut visuals.widgets.hovered, Color32::from_rgb(0x28, 0x2E, 0x34), TEXT_FAINT, TEXT_LIGHT),
        (&mut visuals.widgets.active, Color32::from_rgb(0x2E, 0x35, 0x3B), TEXT_DIM, TEXT_LIGHT),
        (&mut visuals.widgets.open, PANEL2, BORDER, TEXT_LIGHT),
    ] {
        widget.bg_fill = fill;
        widget.weak_bg_fill = fill;
        widget.bg_stroke = Stroke::new(1.0, stroke);
        widget.fg_stroke = Stroke::new(1.0, text);
        widget.corner_radius = 3.into();
    }

    ctx.set_global_style(style);
}

fn fonts() -> FontDefinitions {
    let mut fonts = FontDefinitions::default();
    let mut add = |name: &str, family: FontFamily, candidates: &[&str]| {
        let Some(bytes) = candidates.iter().find_map(|file| std::fs::read(system_font(file)).ok()) else {
            return false;
        };

        fonts.font_data.insert(name.to_owned(), Arc::new(FontData::from_owned(bytes)));
        fonts.families.entry(family).or_default().insert(0, name.to_owned());
        true
    };

    add("ui", FontFamily::Proportional, &["segoeui.ttf"]);
    add("mono", FontFamily::Monospace, &["CascadiaMono.ttf", "consola.ttf"]);
    let has_strong = add("strong", FontFamily::Name("strong".into()), &["seguisb.ttf"]);

    // Fall back to the proportional stack for semibold text, and keep emoji/symbol fallbacks.
    let proportional = fonts.families[&FontFamily::Proportional].clone();
    let strong = fonts.families.entry(FontFamily::Name("strong".into())).or_default();
    let skip = usize::from(has_strong);
    strong.truncate(skip);
    strong.extend(proportional);
    fonts
}

fn system_font(file: &str) -> std::path::PathBuf {
    let windows = std::env::var_os("SystemRoot").unwrap_or_else(|| "C:\\Windows".into());
    std::path::Path::new(&windows).join("Fonts").join(file)
}
