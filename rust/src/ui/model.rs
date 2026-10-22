//! GUI state that does not depend on egui, so selection rules can be unit-tested.

use crate::core::catalog::{self, OptionArgumentStyle, OptionValueKind, RobocopyOptionDefinition};
use crate::core::command_builder::{self, ExecutionMode};
use crate::core::models::{CopyJobRequest, CopyOptionValue};
use crate::core::validator;

pub struct OptionItem {
    pub definition: &'static RobocopyOptionDefinition,
    pub selected: bool,
    pub value: String,
    pub conflict_message: String,
}

impl OptionItem {
    fn new(definition: &'static RobocopyOptionDefinition) -> Self {
        Self {
            definition,
            selected: definition.default_selected,
            value: definition.default_value.to_owned(),
            conflict_message: String::new(),
        }
    }

    pub fn id(&self) -> &'static str {
        self.definition.id
    }

    pub fn label(&self) -> &'static str {
        match self.id() {
            "Mirror" => "Mirror",
            "Purge" => "Purge",
            "MoveTree" => "Move",
            "EmptySubdirectories" => "Include subfolders",
            "MultiThreaded" => "Multithreaded",
            "ExcludeOlder" => "Skip older files",
            "ExcludeNewer" => "Skip newer files",
            _ => self.definition.label,
        }
    }

    pub fn description(&self) -> &'static str {
        match self.id() {
            "Mirror" => "Destination matches source exactly, deleting extras.",
            "Purge" => "Removes destination files no longer in source.",
            "MoveTree" => "Moves files and deletes them from source.",
            "EmptySubdirectories" => "Copies all subfolders, including empty ones.",
            "Restartable" => "Resumes cleanly after a network interruption.",
            "MultiThreaded" => "Copies with multiple threads for faster transfers.",
            "ExcludeOlder" => "Only copies files newer than the destination.",
            "ExcludeNewer" => "Leaves newer destination files untouched.",
            "Retries" => "Attempts a failed file again before skipping it.",
            "Wait" => "Pause between retries on a failed file.",
            _ => self.definition.description,
        }
    }

    pub fn display_description(&self) -> String {
        if self.conflict_message.is_empty() {
            self.description().to_owned()
        } else {
            format!("{}  {}", self.description(), self.conflict_message)
        }
    }

    pub fn flag_text(&self) -> String {
        let flag = self.definition.flag;
        let has_value = !self.value.trim().is_empty();
        match self.definition.argument_style {
            OptionArgumentStyle::ColonValue => format!("{flag}:{}", self.value),
            OptionArgumentStyle::OptionalColonValue if has_value => format!("{flag}:{}", self.value),
            OptionArgumentStyle::SeparateList if has_value => format!("{flag} {}", self.value),
            _ => flag.to_owned(),
        }
    }

    pub fn has_value(&self) -> bool {
        self.definition.value_kind != OptionValueKind::None
    }

    pub fn to_selection(&self) -> CopyOptionValue {
        CopyOptionValue { id: self.id().to_owned(), value: self.has_value().then(|| self.value.clone()) }
    }
}

pub struct TransferForm {
    pub source_path: String,
    pub destination_path: String,
    pub file_patterns: String,
    pub advanced_options: String,
    pub options: Vec<OptionItem>,
}

impl Default for TransferForm {
    fn default() -> Self {
        let mut form = Self {
            source_path: String::new(),
            destination_path: String::new(),
            file_patterns: "*.*".to_owned(),
            advanced_options: String::new(),
            options: catalog::ALL.iter().map(OptionItem::new).collect(),
        };
        form.refresh_conflict_messages();
        form
    }
}

impl TransferForm {
    pub fn option(&self, id: &str) -> &OptionItem {
        self.options.iter().find(|option| option.id() == id).expect("known option id")
    }

    /// Selects or clears an option. Selecting one turns off every option it conflicts with.
    pub fn set_selected(&mut self, id: &str, selected: bool) {
        let Some(index) = self.options.iter().position(|option| option.id() == id) else {
            return;
        };

        self.options[index].selected = selected;
        if selected {
            let changed = self.options[index].definition;
            for option in &mut self.options {
                if option.selected && option.definition.conflicts_with(changed) {
                    option.selected = false;
                }
            }
        }

        self.refresh_conflict_messages();
    }

    fn refresh_conflict_messages(&mut self) {
        let selected: Vec<(&'static RobocopyOptionDefinition, &'static str)> = self
            .options
            .iter()
            .filter(|option| option.selected)
            .map(|option| (option.definition, option.label()))
            .collect();
        for option in &mut self.options {
            let turns_off: Vec<String> = if option.selected {
                Vec::new()
            } else {
                selected
                    .iter()
                    .filter(|(definition, _)| definition.conflicts_with(option.definition))
                    .map(|(_, label)| format!("“{label}”"))
                    .collect()
            };
            option.conflict_message = if turns_off.is_empty() {
                String::new()
            } else {
                format!("Selecting this turns off {}.", turns_off.join(", "))
            };
        }
    }

    pub fn swap_paths(&mut self) {
        std::mem::swap(&mut self.source_path, &mut self.destination_path);
    }

    pub fn clear_paths(&mut self) {
        self.source_path.clear();
        self.destination_path.clear();
    }

    pub fn build_request(&self) -> CopyJobRequest {
        CopyJobRequest {
            source_path: self.source_path.trim().to_owned(),
            destination_path: self.destination_path.trim().to_owned(),
            file_patterns: self.file_patterns.clone(),
            options: self.options.iter().filter(|option| option.selected).map(OptionItem::to_selection).collect(),
            advanced_options: self.advanced_options.clone(),
        }
    }

    /// The command preview and the validation summary shown while configuring a job.
    pub fn preview(&self) -> (String, String) {
        let request = self.build_request();
        let command = command_builder::build_display_command(&request, ExecutionMode::Copy);
        let validation = validator::validate(&request, false).join("  ");
        (command, validation)
    }
}
