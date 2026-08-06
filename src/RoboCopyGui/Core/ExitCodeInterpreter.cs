namespace RoboCopyGui.Core;

public static class ExitCodeInterpreter
{
    public static CopyJobResult Interpret(int exitCode, string logPath, bool canceled = false)
    {
        if (canceled)
        {
            return new(exitCode, CopyOutcome.Canceled, "The copy was stopped by the user.", logPath);
        }

        if (exitCode >= 8)
        {
            return new(exitCode, CopyOutcome.Failure, "Robocopy reported one or more copy failures.", logPath);
        }

        var copied = (exitCode & 1) != 0;
        var extras = (exitCode & 2) != 0;
        var mismatches = (exitCode & 4) != 0;
        var outcome = extras || mismatches ? CopyOutcome.SuccessWithWarnings : CopyOutcome.Success;
        var summary = exitCode switch
        {
            0 => "Everything is already up to date.",
            1 => "All selected files were copied successfully.",
            2 => "No files were copied; extra items exist in the destination.",
            3 => "Files were copied; extra items also exist in the destination.",
            4 => "No files were copied; mismatched items were detected.",
            5 => "Files were copied; mismatched items were detected.",
            6 => "Extra and mismatched items were detected; no files were copied.",
            7 => "Files were copied; extra and mismatched items were detected.",
            _ when copied => "The copy completed successfully.",
            _ => "Robocopy completed without a copy failure."
        };
        return new(exitCode, outcome, summary, logPath);
    }
}
