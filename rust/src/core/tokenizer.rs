/// Splits a space-separated argument list, treating double-quoted runs as one token.
pub fn parse(text: &str) -> Result<Vec<String>, String> {
    let mut result = Vec::new();
    let mut current = String::new();
    let mut quoted = false;

    for character in text.chars() {
        if character == '"' {
            quoted = !quoted;
            continue;
        }

        if character.is_whitespace() && !quoted {
            if !current.is_empty() {
                result.push(std::mem::take(&mut current));
            }

            continue;
        }

        current.push(character);
    }

    if quoted {
        return Err("An option contains an unmatched quotation mark.".to_owned());
    }

    if !current.is_empty() {
        result.push(current);
    }

    Ok(result)
}

pub fn is_switch(token: &str) -> bool {
    token.starts_with('/') || token.starts_with('-')
}

pub fn normalize_switch(token: &str) -> String {
    match token.strip_prefix('-') {
        Some(rest) => format!("/{rest}"),
        None => token.to_owned(),
    }
}
