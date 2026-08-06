using System.Globalization;
using System.IO;

namespace RoboCopyGui.Core;

public static class CopyJobValidator
{
    private static readonly HashSet<string> GuiOwnedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "/L", "/LOG", "/LOG+", "/UNILOG", "/UNILOG+", "/TEE", "/BYTES", "/FP", "/NDL", "/NJH", "/NJS",
        "/UNICODE", "/NP", "/ETA", "/JOB", "/SAVE", "/QUIT", "/NOSD", "/NODD", "/IF",
        "/MIR", "/PURGE", "/MOV", "/MOVE"
    };

    public static IReadOnlyList<string> Validate(CopyJobRequest request, bool requireExistingSource = true)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.SourcePath))
        {
            errors.Add("Choose a source folder.");
        }
        else if (requireExistingSource && !Directory.Exists(request.SourcePath))
        {
            errors.Add("The source folder does not exist or is not accessible.");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            errors.Add("Choose a destination folder.");
        }

        if (errors.Count == 0)
        {
            ValidatePathRelationship(request.SourcePath, request.DestinationPath, errors);
        }

        ValidateOptions(request.Options, errors);
        ValidateTokenList(request.FilePatterns, "file pattern", errors);
        ValidateAdvancedOptions(request.AdvancedOptions, errors);
        return errors;
    }

    public static bool IsDestructive(CopyJobRequest request) => request.Options.Any(selection => RobocopyOptionCatalog.Get(selection.Id).IsDestructive);

    private static void ValidatePathRelationship(string source, string destination, List<string> errors)
    {
        try
        {
            var sourcePath = NormalizePath(source);
            var destinationPath = NormalizePath(destination);
            if (sourcePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Source and destination must be different folders.");
                return;
            }

            if (IsInside(sourcePath, destinationPath) || IsInside(destinationPath, sourcePath))
            {
                errors.Add("Source and destination cannot contain one another.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"A folder path is invalid: {exception.Message}");
        }
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(fullPath);
        return fullPath.Length > (root?.Length ?? 0) ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : fullPath;
    }

    private static bool IsInside(string candidate, string parent)
    {
        var prefix = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateOptions(IReadOnlyList<CopyOptionValue> options, List<string> errors)
    {
        var selected = new HashSet<string>(options.Select(option => option.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var selection in options)
        {
            RobocopyOptionDefinition definition;
            try
            {
                definition = RobocopyOptionCatalog.Get(selection.Id);
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
                continue;
            }

            foreach (var conflict in definition.ConflictsWith.Where(selected.Contains))
            {
                if (string.Compare(definition.Id, conflict, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    errors.Add($"'{definition.Label}' cannot be combined with '{RobocopyOptionCatalog.Get(conflict).Label}'.");
                }
            }

            ValidateOptionValue(definition, selection.Value, errors);
        }
    }

    private static void ValidateOptionValue(RobocopyOptionDefinition definition, string? value, List<string> errors)
    {
        if (definition.ValueKind is OptionValueKind.None or OptionValueKind.OptionalText && string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Enter a value for '{definition.Label}'.");
            return;
        }

        if (definition.ValueKind == OptionValueKind.Integer)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0)
            {
                errors.Add($"'{definition.Label}' requires a non-negative whole number.");
                return;
            }

            if (definition.Id == "Levels" && number < 1)
            {
                errors.Add("Depth limit must be at least 1.");
            }
            else if (definition.Id == "MultiThreaded" && number is < 1 or > 128)
            {
                errors.Add("Multi-threaded copy must use between 1 and 128 threads.");
            }
        }
        else if (definition.Id == "FileMetadata" && value.Any(character => !"DATSOUX".Contains(char.ToUpperInvariant(character))))
        {
            errors.Add("File metadata can only contain D, A, T, S, O, U, and X.");
        }
        else if (definition.Id == "DirectoryMetadata" && value.Any(character => !"DATEX".Contains(char.ToUpperInvariant(character))))
        {
            errors.Add("Directory metadata can only contain D, A, T, E, and X.");
        }
        else if (definition.ValueKind == OptionValueKind.List)
        {
            ValidateTokenList(value, definition.Label, errors);
        }
    }

    private static void ValidateAdvancedOptions(string text, List<string> errors)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = ArgumentTokenizer.Parse(text);
        }
        catch (ArgumentException exception)
        {
            errors.Add(exception.Message);
            return;
        }

        foreach (var token in tokens)
        {
            if (!token.StartsWith('/') && !token.StartsWith('-'))
            {
                errors.Add($"Advanced argument '{token}' must be a self-contained Robocopy switch.");
                continue;
            }

            var separator = token.IndexOf(':');
            var flag = separator >= 0 ? token[..separator] : token;
            if (GuiOwnedFlags.Contains(flag))
            {
                errors.Add($"Advanced argument '{flag}' is managed by the GUI and cannot be overridden.");
            }
        }
    }

    private static void ValidateTokenList(string? text, string label, List<string> errors)
    {
        try
        {
            _ = ArgumentTokenizer.Parse(text);
        }
        catch (ArgumentException exception)
        {
            errors.Add($"The {label} list is invalid: {exception.Message}");
        }
    }
}
