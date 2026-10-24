use super::catalog::{self, OptionArgumentStyle, RobocopyOptionDefinition};
use super::models::CopyJobRequest;
use super::tokenizer;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ExecutionMode {
    Scan,
    Copy,
}

const SCAN_ARGUMENTS: [&str; 7] = ["/L", "/BYTES", "/FP", "/NJH", "/NJS", "/R:0", "/W:0"];
const COPY_ARGUMENTS: [&str; 6] = ["/BYTES", "/FP", "/NDL", "/NJH", "/NJS", "/ETA"];

/// Builds the argument list passed to Robocopy, one path or switch per entry and never shell-quoted.
/// Token lists that fail to parse contribute nothing; the validator reports them before any run.
pub fn build_arguments(request: &CopyJobRequest, mode: ExecutionMode) -> Vec<String> {
    let mut arguments = vec![request.source_path.clone(), request.destination_path.clone()];
    let patterns = tokenizer::parse(&request.file_patterns).unwrap_or_default();
    if patterns.is_empty() {
        arguments.push("*.*".to_owned());
    } else {
        arguments.extend(patterns);
    }

    for definition in &catalog::ALL {
        if let Some(selection) = request.options.iter().find(|option| option.id.eq_ignore_ascii_case(definition.id)) {
            add_option(&mut arguments, definition, selection.value.as_deref());
        }
    }

    arguments.extend(tokenizer::parse(&request.advanced_options).unwrap_or_default());
    let mode_arguments: &[&str] = match mode {
        ExecutionMode::Scan => &SCAN_ARGUMENTS,
        ExecutionMode::Copy => &COPY_ARGUMENTS,
    };
    arguments.extend(mode_arguments.iter().map(|argument| (*argument).to_owned()));
    arguments
}

pub fn build_display_command(request: &CopyJobRequest, mode: ExecutionMode) -> String {
    let arguments: Vec<String> =
        build_arguments(request, mode).iter().map(|argument| quote_for_display(argument)).collect();
    format!("robocopy.exe {}", arguments.join(" "))
}

fn add_option(arguments: &mut Vec<String>, definition: &RobocopyOptionDefinition, value: Option<&str>) {
    let value = value.unwrap_or_default();
    match definition.argument_style {
        OptionArgumentStyle::Switch => arguments.push(definition.flag.to_owned()),
        OptionArgumentStyle::ColonValue => arguments.push(format!("{}:{value}", definition.flag)),
        OptionArgumentStyle::OptionalColonValue if value.trim().is_empty() => {
            arguments.push(definition.flag.to_owned())
        }
        OptionArgumentStyle::OptionalColonValue => arguments.push(format!("{}:{value}", definition.flag)),
        OptionArgumentStyle::SeparateList => {
            arguments.push(definition.flag.to_owned());
            arguments.extend(tokenizer::parse(value).unwrap_or_default());
        }
    }
}

// Quotes using the CommandLineToArgvW / MSVCRT rules so the preview round-trips when pasted:
// backslashes are only special when they precede a quote, so those runs are doubled.
fn quote_for_display(value: &str) -> String {
    if !value.is_empty() && !value.chars().any(char::is_whitespace) && !value.contains('"') {
        return value.to_owned();
    }

    let mut builder = String::with_capacity(value.len() + 2);
    builder.push('"');
    let mut backslashes = 0;
    for character in value.chars() {
        if character == '\\' {
            backslashes += 1;
            continue;
        }

        let count = if character == '"' { backslashes * 2 + 1 } else { backslashes };
        builder.extend(std::iter::repeat_n('\\', count));
        backslashes = 0;
        builder.push(character);
    }

    builder.extend(std::iter::repeat_n('\\', backslashes * 2));
    builder.push('"');
    builder
}
