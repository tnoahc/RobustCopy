use std::path::Path;
use std::sync::LazyLock;

use regex::Regex;

use super::catalog::{self, OptionValueKind, RobocopyOptionDefinition};
use super::models::{CopyJobRequest, CopyOptionValue};
use super::tokenizer;

const GUI_OWNED_FLAGS: [&str; 24] = [
    "/L", "/LOG", "/LOG+", "/UNILOG", "/UNILOG+", "/TEE", "/BYTES", "/FP", "/NDL", "/NJH", "/NJS", "/UNICODE", "/NP",
    "/ETA", "/JOB", "/SAVE", "/QUIT", "/NOSD", "/NODD", "/IF", "/MIR", "/PURGE", "/MOV", "/MOVE",
];

static FREE_SPACE_FLOOR: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"(?i)^(?<number>[0-9]+)[KMG]?$").unwrap());

pub fn validate(request: &CopyJobRequest, require_existing_source: bool) -> Vec<String> {
    let mut errors = Vec::new();
    if request.source_path.trim().is_empty() {
        errors.push("Choose a source folder.".to_owned());
    } else if require_existing_source && !Path::new(&request.source_path).is_dir() {
        errors.push("The source folder does not exist or is not accessible.".to_owned());
    }

    if request.destination_path.trim().is_empty() {
        errors.push("Choose a destination folder.".to_owned());
    }

    if errors.is_empty() {
        validate_path_relationship(&request.source_path, &request.destination_path, &mut errors);
    }

    validate_options(&request.options, &mut errors);
    validate_token_list(&request.file_patterns, "file pattern", &mut errors);
    validate_advanced_options(&request.advanced_options, &mut errors);
    errors
}

pub fn is_destructive(request: &CopyJobRequest) -> bool {
    request
        .options
        .iter()
        .any(|selection| catalog::get(&selection.id).is_ok_and(|definition| definition.is_destructive))
}

fn validate_path_relationship(source: &str, destination: &str, errors: &mut Vec<String>) {
    let (source_path, destination_path) = match (normalize_path(source), normalize_path(destination)) {
        (Ok(source), Ok(destination)) => (source, destination),
        (Err(message), _) | (_, Err(message)) => {
            errors.push(format!("A folder path is invalid: {message}"));
            return;
        }
    };

    if eq_ignore_case(&source_path, &destination_path) {
        errors.push("Source and destination must be different folders.".to_owned());
        return;
    }

    if is_inside(&source_path, &destination_path) || is_inside(&destination_path, &source_path) {
        errors.push("Source and destination cannot contain one another.".to_owned());
    }
}

/// Resolves a path to its full Windows form with backslash separators and no trailing separator
/// (except on a root), mirroring .NET's `Path.GetFullPath`.
fn normalize_path(path: &str) -> Result<String, String> {
    let trimmed = path.trim();
    if trimmed.contains('\0') {
        return Err("Illegal characters in path.".to_owned());
    }

    let full = full_path(trimmed)?;
    let root_length = root_length(&full);
    let without_trailing = full.trim_end_matches('\\');
    Ok(if without_trailing.len() > root_length { without_trailing.to_owned() } else { full })
}

#[cfg(windows)]
fn full_path(path: &str) -> Result<String, String> {
    // GetFullPathNameW, like Path.GetFullPath, resolves relative segments and the current drive.
    std::path::absolute(path).map(|full| full.to_string_lossy().into_owned()).map_err(|error| error.to_string())
}

/// A lexical stand-in for GetFullPathNameW so path rules can be unit-tested off Windows.
#[cfg(not(windows))]
fn full_path(path: &str) -> Result<String, String> {
    let path = path.replace('/', "\\");
    let root_length = root_length(&path);
    if root_length == 0 {
        return Err(format!("The path '{path}' is not rooted."));
    }

    let (root, rest) = path.split_at(root_length);
    let mut segments: Vec<&str> = Vec::new();
    for segment in rest.split('\\') {
        match segment {
            "" | "." => {}
            ".." => {
                segments.pop();
            }
            other => segments.push(other),
        }
    }

    let mut result = root.to_owned();
    if !result.ends_with('\\') {
        result.push('\\');
    }

    result.push_str(&segments.join("\\"));
    if path.ends_with('\\') && !segments.is_empty() {
        result.push('\\');
    }

    Ok(result)
}

/// Length of a `C:\` or `\\server\share` root, or zero for a relative path.
fn root_length(path: &str) -> usize {
    let bytes = path.as_bytes();
    if bytes.len() >= 2 && bytes[0].is_ascii_alphabetic() && bytes[1] == b':' {
        return if bytes.get(2).is_some_and(|byte| *byte == b'\\' || *byte == b'/') { 3 } else { 2 };
    }

    if path.starts_with("\\\\") {
        // \\server\share\ — the root ends after the share name.
        let mut separators = 0;
        for (index, byte) in bytes.iter().enumerate().skip(2) {
            if *byte == b'\\' {
                separators += 1;
                if separators == 2 {
                    return index;
                }
            }
        }

        return bytes.len();
    }

    0
}

fn eq_ignore_case(left: &str, right: &str) -> bool {
    left.to_uppercase() == right.to_uppercase()
}

fn is_inside(candidate: &str, parent: &str) -> bool {
    let prefix = if parent.ends_with('\\') { parent.to_uppercase() } else { format!("{}\\", parent.to_uppercase()) };
    candidate.to_uppercase().starts_with(&prefix)
}

fn validate_options(options: &[CopyOptionValue], errors: &mut Vec<String>) {
    let selected: Vec<String> = options.iter().map(|option| option.id.to_ascii_lowercase()).collect();
    for selection in options {
        let definition = match catalog::get(&selection.id) {
            Ok(definition) => definition,
            Err(message) => {
                errors.push(message);
                continue;
            }
        };

        for conflict in definition.conflicts_with {
            if selected.contains(&conflict.to_ascii_lowercase())
                && definition.id.to_ascii_uppercase() < conflict.to_ascii_uppercase()
            {
                let other = catalog::get(conflict).map(|other| other.label).unwrap_or(conflict);
                errors.push(format!("'{}' cannot be combined with '{other}'.", definition.label));
            }
        }

        validate_option_value(definition, selection.value.as_deref(), errors);
    }
}

fn validate_option_value(definition: &RobocopyOptionDefinition, value: Option<&str>, errors: &mut Vec<String>) {
    let blank = value.is_none_or(|value| value.trim().is_empty());
    if matches!(definition.value_kind, OptionValueKind::None | OptionValueKind::OptionalText) && blank {
        return;
    }

    let value = match value {
        Some(value) if !blank => value,
        _ => {
            errors.push(format!("Enter a value for '{}'.", definition.label));
            return;
        }
    };

    if definition.value_kind == OptionValueKind::Integer {
        let Some(number) = parse_whole_number(value) else {
            errors.push(format!("'{}' requires a non-negative whole number.", definition.label));
            return;
        };

        if definition.id == "Levels" && number < 1 {
            errors.push("Depth limit must be at least 1.".to_owned());
        } else if definition.id == "MultiThreaded" && !(1..=128).contains(&number) {
            errors.push("Multi-threaded copy must use between 1 and 128 threads.".to_owned());
        }
    } else if definition.id == "FileMetadata" && !only_flags(value, "DATSOUX") {
        errors.push("File metadata can only contain D, A, T, S, O, U, and X.".to_owned());
    } else if definition.id == "DirectoryMetadata" && !only_flags(value, "DATEX") {
        errors.push("Directory metadata can only contain D, A, T, E, and X.".to_owned());
    } else if definition.id == "LowFreeSpace" && !is_valid_free_space_floor(value) {
        errors.push("Low free-space floor must be a whole number, optionally followed by K, M, or G.".to_owned());
    } else if definition.value_kind == OptionValueKind::List {
        validate_token_list(value, definition.label, errors);
    }
}

/// Parses ASCII digits only — no sign, whitespace, or separators — into a signed 64-bit number.
fn parse_whole_number(value: &str) -> Option<i64> {
    if value.is_empty() || !value.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }

    value.parse().ok()
}

fn only_flags(value: &str, allowed: &str) -> bool {
    value.chars().all(|character| allowed.contains(character.to_ascii_uppercase()))
}

fn is_valid_free_space_floor(value: &str) -> bool {
    // Robocopy accepts /LFSM:n[KMG] where n is an unsigned 64-bit integer; decimals are rejected.
    FREE_SPACE_FLOOR.captures(value).is_some_and(|captures| captures["number"].parse::<u64>().is_ok())
}

fn validate_advanced_options(text: &str, errors: &mut Vec<String>) {
    let tokens = match tokenizer::parse(text) {
        Ok(tokens) => tokens,
        Err(message) => {
            errors.push(message);
            return;
        }
    };

    for token in tokens {
        if !tokenizer::is_switch(&token) {
            errors.push(format!("Advanced argument '{token}' must be a self-contained Robocopy switch."));
            continue;
        }

        let flag = tokenizer::normalize_switch(token.split(':').next().unwrap_or(&token));
        if GUI_OWNED_FLAGS.iter().any(|owned| owned.eq_ignore_ascii_case(&flag)) {
            errors.push(format!("Advanced argument '{flag}' is managed by the GUI and cannot be overridden."));
        }
    }
}

fn validate_token_list(text: &str, label: &str, errors: &mut Vec<String>) {
    let tokens = match tokenizer::parse(text) {
        Ok(tokens) => tokens,
        Err(message) => {
            errors.push(format!("The {label} list is invalid: {message}"));
            return;
        }
    };

    for token in tokens.iter().filter(|token| tokenizer::is_switch(token)) {
        errors.push(format!(
            "The {label} list cannot contain the switch '{token}'. Enter Robocopy switches in the Advanced field."
        ));
    }
}
