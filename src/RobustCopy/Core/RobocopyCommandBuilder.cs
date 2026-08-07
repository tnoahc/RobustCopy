using System.Text;

namespace RobustCopy.Core;

public enum RobocopyExecutionMode
{
    Scan,
    Copy
}

public static class RobocopyCommandBuilder
{
    private static readonly string[] ScanArguments = ["/L", "/BYTES", "/FP", "/NJH", "/NJS", "/R:0", "/W:0"];
    private static readonly string[] CopyArguments = ["/BYTES", "/FP", "/NDL", "/NJH", "/NJS", "/ETA"];

    public static IReadOnlyList<string> BuildArguments(CopyJobRequest request, RobocopyExecutionMode mode)
    {
        var arguments = new List<string> { request.SourcePath, request.DestinationPath };
        var patterns = ArgumentTokenizer.Parse(request.FilePatterns);
        arguments.AddRange(patterns.Count == 0 ? ["*.*"] : patterns);

        var selected = request.Options.ToDictionary(option => option.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var definition in RobocopyOptionCatalog.All)
        {
            if (!selected.TryGetValue(definition.Id, out var selection))
            {
                continue;
            }

            AddOption(arguments, definition, selection.Value);
        }

        arguments.AddRange(ArgumentTokenizer.Parse(request.AdvancedOptions));
        arguments.AddRange(mode == RobocopyExecutionMode.Scan ? ScanArguments : CopyArguments);
        return arguments;
    }

    public static string BuildDisplayCommand(CopyJobRequest request, RobocopyExecutionMode mode = RobocopyExecutionMode.Copy)
    {
        return "robocopy.exe " + string.Join(' ', BuildArguments(request, mode).Select(QuoteForDisplay));
    }

    private static void AddOption(List<string> arguments, RobocopyOptionDefinition definition, string? value)
    {
        switch (definition.ArgumentStyle)
        {
            case OptionArgumentStyle.Switch:
                arguments.Add(definition.Flag);
                break;
            case OptionArgumentStyle.ColonValue:
                arguments.Add($"{definition.Flag}:{value}");
                break;
            case OptionArgumentStyle.OptionalColonValue:
                arguments.Add(string.IsNullOrWhiteSpace(value) ? definition.Flag : $"{definition.Flag}:{value}");
                break;
            case OptionArgumentStyle.SeparateList:
                arguments.Add(definition.Flag);
                arguments.AddRange(ArgumentTokenizer.Parse(value));
                break;
            default:
                throw new InvalidOperationException($"Unsupported argument style {definition.ArgumentStyle}.");
        }
    }

    private static string QuoteForDisplay(string value)
    {
        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\\\"")}\"";
    }
}
