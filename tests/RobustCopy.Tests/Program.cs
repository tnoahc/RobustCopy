using RobustCopy.Core;
using RobustCopy.Services;
using RobustCopy.ViewModels;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Application identity uses RobustCopy", TestAppIdentityAsync),
    ("Legacy application logs migrate to RobustCopy", TestLegacyLogMigrationAsync),
    ("Crash reporter writes a best-effort crash log", TestCrashReporterAsync),
    ("Legacy log migration runs once", TestLegacyLogMigrationRunsOnceAsync),
    ("Log retention prunes only expired application logs", TestLogRetentionAsync),
    ("Log retention skips logs that are still open", TestLogRetentionSkipsOpenLogsAsync),
    ("Argument tokenizer preserves quoted values", TestTokenizerAsync),
    ("Command builder keeps paths as individual arguments", TestCommandBuilderAsync),
    ("Command preview quotes paths so they round-trip", TestDisplayCommandQuotingAsync),
    ("Validator blocks overlapping paths", TestOverlappingPathsAsync),
    ("Validator reports option conflicts", TestOptionConflictsAsync),
    ("Option conflicts reference known ids and are symmetric", TestOptionConflictSymmetryAsync),
    ("Validator accepts valid low free-space floors", TestLowFreeSpaceValidValuesAsync),
    ("Validator rejects invalid low free-space floors", TestLowFreeSpaceInvalidValuesAsync),
    ("Option catalog preserves the original category organization", TestOptionCategoriesAsync),
    ("Selecting an option turns off conflicting options", TestOptionAutoDeselectAsync),
    ("Validator protects GUI-owned advanced flags", TestAdvancedProtectionAsync),
    ("Validator protects dash-prefixed GUI-owned advanced flags", TestAdvancedDashProtectionAsync),
    ("Validator rejects switches in file patterns", TestFilePatternSwitchesAsync),
    ("Validator rejects switches in list options", TestListOptionSwitchesAsync),
    ("Runner detects dash-prefixed multi-threading", TestDashMultiThreadedAsync),
    ("Output parser reads bytes, paths, percentages, and extras", TestOutputParserAsync),
    ("Exit codes map to Robocopy outcomes", TestExitCodesAsync),
    ("Log tail returns a small log whole", TestLogTailSmallFileAsync),
    ("Log tail truncates a large log at a line boundary", TestLogTailLargeFileAsync),
    ("Log tail reads a log that is open for writing", TestLogTailOpenForWritingAsync),
    ("Robocopy scan and copy integration", TestCopyIntegrationAsync),
    ("Robocopy pause and resume integration", TestPauseResumeIntegrationAsync),
    ("Robocopy stop and restart integration", TestStopAndRestartIntegrationAsync),
    ("Stop during copy startup prevents Robocopy launch", TestStopDuringCopyStartupAsync),
    ("Stop during scan startup prevents Robocopy launch", TestStopDuringScanStartupAsync),
    ("Stop right after start leaves no copy running", TestStopImmediatelyAfterStartAsync),
    ("Mirror pre-scan reports planned deletion", TestMirrorScanIntegrationAsync),
    ("Robocopy output decodes non-ASCII names with the system OEM code page", TestOemFileNameIntegrationAsync),
    ("Destructive confirmation reports deletions only for mirror or purge", TestDestructiveConfirmationAsync),
    ("Runner exposes the pre-scan transcript path", TestScanLogPathIntegrationAsync),
    ("Failed pre-scan still exposes its transcript", TestFailedScanLogPathAsync),
    ("Declined confirmation still exposes its transcript", TestDeclinedConfirmationLogPathAsync)
};

var failures = 0;
Console.WriteLine($"RobustCopy test runner — {tests.Length} tests");
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL  {test.Name}");
        Console.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All tests passed." : $"{failures} test(s) failed.");
return failures == 0 ? 0 : 1;

static Task TestAppIdentityAsync()
{
    Equal("RobustCopy", AppIdentity.DisplayName);
    Equal("RobustCopy", AppIdentity.LocalDataFolderName);
    Equal("RobustCopy", typeof(AppIdentity).Assembly.GetName().Name);
    Equal("RobustCopy.Core", typeof(AppIdentity).Namespace);
    Equal("RobustCopy.Tests", System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name);
    True(
        AppIdentity.LogDirectory.EndsWith(Path.Combine("RobustCopy", "Logs"), StringComparison.OrdinalIgnoreCase),
        "Expected the log directory to use the RobustCopy product name.");
    return Task.CompletedTask;
}

static Task TestLegacyLogMigrationAsync() => WithFixtureAsync(fixture =>
{
    var legacyDirectory = Path.Combine(
        fixture.Root,
        AppIdentity.LegacyLocalDataFolderName,
        "Logs");
    Directory.CreateDirectory(legacyDirectory);
    var legacyLog = Path.Combine(legacyDirectory, "robocopy-legacy.log");
    File.WriteAllText(legacyLog, "legacy transcript");

    var migratedDirectory = AppIdentity.EnsureLogDirectory(fixture.Root);
    Equal(Path.Combine(fixture.Root, AppIdentity.LocalDataFolderName, "Logs"), migratedDirectory);
    Equal("legacy transcript", File.ReadAllText(Path.Combine(migratedDirectory, "robocopy-legacy.log")));
    True(File.Exists(legacyLog), "Legacy logs should be copied without deleting the originals.");
    return Task.CompletedTask;
});

static Task TestCrashReporterAsync() => WithFixtureAsync(fixture =>
{
    var exception = new InvalidOperationException("crash-report-marker");
    var reportPath = CrashReporter.Write(exception, "Test", fixture.Logs);

    True(reportPath is not null, "Expected the crash report to be written.");
    Equal(fixture.Logs, Path.GetDirectoryName(reportPath));
    var fileName = Path.GetFileName(reportPath!);
    True(
        fileName.StartsWith("crash-", StringComparison.Ordinal) && fileName.EndsWith(".log", StringComparison.Ordinal),
        $"Unexpected crash report name '{fileName}'.");
    var report = File.ReadAllText(reportPath!);
    True(report.Contains(exception.ToString(), StringComparison.Ordinal), "Expected the report to contain the exception text.");
    True(report.Contains("Time (UTC):", StringComparison.Ordinal), "Expected the report to contain a UTC timestamp.");
    True(report.Contains("Version:", StringComparison.Ordinal), "Expected the report to contain the application version.");

    // Reporting is best-effort: an unusable directory yields null instead of throwing.
    Equal<string?>(null, CrashReporter.Write(exception, "Test", "invalid\0directory"));
    return Task.CompletedTask;
});

static Task TestLegacyLogMigrationRunsOnceAsync() => WithFixtureAsync(fixture =>
{
    var legacyDirectory = Path.Combine(fixture.Root, AppIdentity.LegacyLocalDataFolderName, "Logs");
    Directory.CreateDirectory(legacyDirectory);
    File.WriteAllText(Path.Combine(legacyDirectory, "robocopy-first.log"), "first");

    var logDirectory = AppIdentity.EnsureLogDirectory(fixture.Root);
    True(File.Exists(Path.Combine(logDirectory, "robocopy-first.log")), "The first pass should migrate legacy logs.");
    True(
        File.Exists(Path.Combine(fixture.Root, AppIdentity.LocalDataFolderName, AppIdentity.LegacyMigrationMarkerName)),
        "A successful migration should leave a marker.");

    File.WriteAllText(Path.Combine(legacyDirectory, "robocopy-second.log"), "second");
    var legacySnapshot = Directory.GetFiles(legacyDirectory).Order().ToArray();
    AppIdentity.EnsureLogDirectory(fixture.Root);
    True(!File.Exists(Path.Combine(logDirectory, "robocopy-second.log")), "Migration should not run a second time.");
    Equal(string.Join("|", legacySnapshot), string.Join("|", Directory.GetFiles(legacyDirectory).Order()));

    AppIdentity.PruneOldLogs(logDirectory, DateTime.UtcNow.AddYears(10));
    Equal("first", File.ReadAllText(Path.Combine(legacyDirectory, "robocopy-first.log")));
    Equal("second", File.ReadAllText(Path.Combine(legacyDirectory, "robocopy-second.log")));
    return Task.CompletedTask;
});

static Task TestLogRetentionAsync() => WithFixtureAsync(fixture =>
{
    var logDirectory = Path.Combine(fixture.Root, "Logs");
    Directory.CreateDirectory(logDirectory);
    var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
    var expired = now.AddDays(-AppIdentity.LogRetentionDays - 1);
    var recent = now.AddDays(-AppIdentity.LogRetentionDays + 1);

    var files = new (string Name, DateTime LastWrite, bool Kept)[]
    {
        ("robocopy-old.log", expired, false),
        ("crash-old.log", expired, false),
        ("robocopy-new.log", recent, true),
        ("crash-new.log", recent, true),
        ("notes.txt", expired, true),
        (".legacy-migrated", expired, true),
        ("other-old.log", expired, true),
        ("robocopy-old.log.bak", expired, true)
    };
    foreach (var file in files)
    {
        var path = Path.Combine(logDirectory, file.Name);
        File.WriteAllText(path, file.Name);
        File.SetLastWriteTimeUtc(path, file.LastWrite);
    }

    var nested = Path.Combine(logDirectory, "nested");
    Directory.CreateDirectory(nested);
    var nestedLog = Path.Combine(nested, "robocopy-nested.log");
    File.WriteAllText(nestedLog, "nested");
    File.SetLastWriteTimeUtc(nestedLog, expired);

    Equal(2, AppIdentity.PruneOldLogs(logDirectory, now));
    foreach (var file in files)
    {
        Equal(file.Kept, File.Exists(Path.Combine(logDirectory, file.Name)));
    }

    True(File.Exists(nestedLog), "Retention should not recurse into subdirectories.");
    Equal(0, AppIdentity.PruneOldLogs(Path.Combine(fixture.Root, "missing"), now));
    return Task.CompletedTask;
});

static Task TestLogRetentionSkipsOpenLogsAsync() => WithFixtureAsync(fixture =>
{
    var logDirectory = Path.Combine(fixture.Root, "Logs");
    Directory.CreateDirectory(logDirectory);
    var now = DateTime.UtcNow;
    var openLog = Path.Combine(logDirectory, "robocopy-open.log");
    var closedLog = Path.Combine(logDirectory, "robocopy-closed.log");
    foreach (var path in new[] { openLog, closedLog })
    {
        File.WriteAllText(path, "transcript");
        File.SetLastWriteTimeUtc(path, now.AddDays(-AppIdentity.LogRetentionDays - 10));
    }

    using (new FileStream(openLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
    {
        Equal(1, AppIdentity.PruneOldLogs(logDirectory, now));
    }

    True(File.Exists(openLog), "An open log should be skipped.");
    True(!File.Exists(closedLog), "Other expired logs should still be pruned.");
    return Task.CompletedTask;
});

static Task TestTokenizerAsync()
{
    var tokens = ArgumentTokenizer.Parse("*.txt \"annual report*.pdf\" /FFT");
    Equal(3, tokens.Count);
    Equal("annual report*.pdf", tokens[1]);
    return Task.CompletedTask;
}

static Task TestCommandBuilderAsync()
{
    var request = Request(@"C:\Source Folder", @"D:\Destination Folder",
        new("EmptySubdirectories"), new("Restartable"), new("Retries", "3"), new("Wait", "5"));
    var arguments = RobocopyCommandBuilder.BuildArguments(request, RobocopyExecutionMode.Copy);
    Equal(@"C:\Source Folder", arguments[0]);
    Equal(@"D:\Destination Folder", arguments[1]);
    True(arguments.Contains("/E"), "Expected /E.");
    True(arguments.Contains("/R:3"), "Expected /R:3.");
    True(arguments.Contains("/BYTES"), "Expected GUI-owned byte output.");
    True(!arguments.Any(argument => argument.Contains('"')), "ArgumentList values must not contain shell quotes.");
    return Task.CompletedTask;
}

static Task TestDisplayCommandQuotingAsync()
{
    (string Source, string Destination, string Expected)[] cases =
    [
        (@"D:\My Backups\", @"D:\", @"robocopy.exe ""D:\My Backups\\"" D:\ *.* "),
        (@"C:\Source", @"D:\Destination", @"robocopy.exe C:\Source D:\Destination *.* "),
        (@"C:\Odd""Name", @"C:\Odd\""Name\", @"robocopy.exe ""C:\Odd\""Name"" ""C:\Odd\\\""Name\\"" *.* ")
    ];

    foreach (var (source, destination, expected) in cases)
    {
        var command = RobocopyCommandBuilder.BuildDisplayCommand(Request(source, destination));
        True(command.StartsWith(expected, StringComparison.Ordinal), $"Expected '{expected}...', found '{command}'.");

        var parsed = SplitCommandLine(command);
        Equal(source, parsed[1]);
        Equal(destination, parsed[2]);
    }

    return Task.CompletedTask;
}

static string[] SplitCommandLine(string commandLine)
{
    var argv = CommandLineToArgvW(commandLine, out var count);
    if (argv == IntPtr.Zero) throw new InvalidOperationException("CommandLineToArgvW failed.");
    try
    {
        var result = new string[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = System.Runtime.InteropServices.Marshal.PtrToStringUni(
                System.Runtime.InteropServices.Marshal.ReadIntPtr(argv, index * IntPtr.Size))!;
        }

        return result;
    }
    finally
    {
        LocalFree(argv);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);
}

static Task TestOverlappingPathsAsync()
{
    var request = Request(@"C:\Data", @"C:\Data\Backup");
    var errors = CopyJobValidator.Validate(request, requireExistingSource: false);
    True(errors.Any(error => error.Contains("contain", StringComparison.OrdinalIgnoreCase)), "Expected overlapping-path error.");
    return Task.CompletedTask;
}

static Task TestOptionConflictsAsync()
{
    var request = Request(@"C:\Source", @"D:\Destination", new("Subdirectories"), new("EmptySubdirectories"));
    var errors = CopyJobValidator.Validate(request, requireExistingSource: false);
    True(errors.Any(error => error.Contains("cannot be combined", StringComparison.OrdinalIgnoreCase)), "Expected conflict error.");
    return Task.CompletedTask;
}

static Task TestOptionConflictSymmetryAsync()
{
    var definitions = RobocopyOptionCatalog.All.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    foreach (var definition in RobocopyOptionCatalog.All)
    {
        foreach (var conflict in definition.ConflictsWith)
        {
            True(definitions.TryGetValue(conflict, out var other), $"'{definition.Id}' conflicts with unknown option '{conflict}'.");
            True(!conflict.Equals(definition.Id, StringComparison.OrdinalIgnoreCase), $"'{definition.Id}' must not conflict with itself.");
            True(other!.ConflictsWith.Contains(definition.Id, StringComparer.OrdinalIgnoreCase),
                $"'{definition.Id}' conflicts with '{conflict}', but '{conflict}' does not list '{definition.Id}'.");
        }
    }
    return Task.CompletedTask;
}

static Task TestLowFreeSpaceValidValuesAsync()
{
    foreach (var value in new[] { "", " ", "0", "100", "2G", "2g", "512M", "512m", "10K", "007M", "18446744073709551615" })
    {
        var errors = CopyJobValidator.Validate(Request(@"C:\Source", @"D:\Destination", new CopyOptionValue("LowFreeSpace", value)), requireExistingSource: false);
        True(errors.Count == 0, $"Expected /LFSM value '{value}' to be valid, found: {string.Join(" | ", errors)}");
    }
    return Task.CompletedTask;
}

static Task TestLowFreeSpaceInvalidValuesAsync()
{
    foreach (var value in new[] { "1.5G", "1.5", ".5G", "2.G", "1,5G", "2GB", "2T", "abc", "-1G", " 2G", "2 G", "18446744073709551616", "١٢G" })
    {
        var errors = CopyJobValidator.Validate(Request(@"C:\Source", @"D:\Destination", new CopyOptionValue("LowFreeSpace", value)), requireExistingSource: false);
        True(errors.Any(error => error.Contains("Low free-space floor", StringComparison.Ordinal)), $"Expected /LFSM value '{value}' to be rejected.");
    }
    return Task.CompletedTask;
}

static Task TestOptionCategoriesAsync()
{
    var expectedCounts = new Dictionary<string, int>
    {
        ["Folders"] = 6,
        ["Reliability"] = 5,
        ["Metadata"] = 4,
        ["Filters"] = 9,
        ["Performance"] = 5,
        ["Destructive operations"] = 4
    };
    var options = RobocopyOptionCatalog.All
        .Select(definition => new OptionItemViewModel(definition, _ => { }))
        .ToList();

    Equal(33, options.Count);
    Equal(33, options.Select(option => option.Id).Distinct(StringComparer.Ordinal).Count());
    foreach (var expected in expectedCounts)
    {
        Equal(expected.Value, options.Count(option => option.Category == expected.Key));
    }
    True(options.All(option => option.Category == option.Definition.Category), "UI categories must match the catalog categories.");
    return Task.CompletedTask;
}

static async Task TestOptionAutoDeselectAsync()
{
    var desktop = new DesktopServices();
    await using var viewModel = new MainViewModel(
        new RobocopyRunner(Path.Combine(Path.GetTempPath(), "RobustCopyTests", "issue15-logs")),
        desktop,
        desktop,
        desktop);
    OptionItemViewModel Option(string id) => viewModel.Options.Single(option => option.Id == id);
    void NoConflictErrors()
    {
        var request = Request(@"C:\Source", @"D:\Destination",
            viewModel.Options.Where(option => option.IsSelected).Select(option => option.ToSelection()).ToArray());
        var errors = CopyJobValidator.Validate(request, requireExistingSource: false);
        True(!errors.Any(error => error.Contains("cannot be combined", StringComparison.OrdinalIgnoreCase)), string.Join(" ", errors));
    }

    True(Option("EmptySubdirectories").IsSelected && Option("Restartable").IsSelected, "Expected the default selections.");
    True(viewModel.Options.All(option => option.IsAvailable), "Conflicts must no longer disable options.");
    Equal("Selecting this turns off “Include subfolders”.", Option("Mirror").ConflictMessage);
    Equal(string.Empty, Option("EmptySubdirectories").ConflictMessage);
    NoConflictErrors();

    Option("Mirror").IsSelected = true;
    True(Option("Mirror").IsSelected, "Mirror should stay selected.");
    True(!Option("EmptySubdirectories").IsSelected, "Mirror should turn off Include subfolders.");
    Equal(string.Empty, Option("Mirror").ConflictMessage);
    Equal("Selecting this turns off “Mirror”.", Option("EmptySubdirectories").ConflictMessage);
    NoConflictErrors();

    Option("Backup").IsSelected = true;
    True(Option("Backup").IsSelected && !Option("Restartable").IsSelected, "Backup should turn off Restartable.");
    NoConflictErrors();

    Option("EmptySubdirectories").IsSelected = true;
    True(Option("EmptySubdirectories").IsSelected && !Option("Mirror").IsSelected, "Include subfolders should turn off Mirror.");
    True(viewModel.Options.All(option => option.IsAvailable), "Conflicts must no longer disable options.");
    NoConflictErrors();
}

static Task TestAdvancedProtectionAsync()
{
    var request = Request(@"C:\Source", @"D:\Destination") with { AdvancedOptions = "/FFT /LOG:unsafe.txt /MIR" };
    var errors = CopyJobValidator.Validate(request, requireExistingSource: false);
    True(errors.Count(error => error.Contains("managed by the GUI", StringComparison.OrdinalIgnoreCase)) == 2, "Expected /LOG and /MIR to be protected.");
    return Task.CompletedTask;
}

static Task TestAdvancedDashProtectionAsync()
{
    var request = Request(@"C:\Source", @"D:\Destination") with { AdvancedOptions = "-FFT -MIR -purge -LOG:unsafe.txt -MOV" };
    var errors = CopyJobValidator.Validate(request, requireExistingSource: false);
    Equal(4, errors.Count(error => error.Contains("managed by the GUI", StringComparison.OrdinalIgnoreCase)));
    True(errors.Any(error => error.Contains("'/MIR'", StringComparison.Ordinal)), "Expected -MIR to be reported as /MIR.");
    return Task.CompletedTask;
}

static Task TestFilePatternSwitchesAsync()
{
    foreach (var patterns in new[] { "*.* /MIR", "*.txt -PURGE", "\"/MOVE\"" })
    {
        var request = Request(@"C:\Source", @"D:\Destination") with { FilePatterns = patterns };
        var errors = CopyJobValidator.Validate(request, requireExistingSource: false);
        True(errors.Any(error => error.Contains("Advanced field", StringComparison.Ordinal)), $"Expected '{patterns}' to be rejected.");
    }

    var valid = Request(@"C:\Source", @"D:\Destination") with { FilePatterns = "*.txt \"annual report*.pdf\"" };
    Equal(0, CopyJobValidator.Validate(valid, requireExistingSource: false).Count);
    return Task.CompletedTask;
}

static Task TestListOptionSwitchesAsync()
{
    foreach (var id in new[] { "ExcludeFiles", "ExcludeDirectories" })
    {
        foreach (var value in new[] { "foo.tmp /PURGE", "cache -MIR" })
        {
            var request = Request(@"C:\Source", @"D:\Destination", new CopyOptionValue(id, value));
            var errors = CopyJobValidator.Validate(request, requireExistingSource: false);
            True(errors.Any(error => error.Contains("Advanced field", StringComparison.Ordinal)), $"Expected {id} '{value}' to be rejected.");
        }

        var valid = Request(@"C:\Source", @"D:\Destination", new CopyOptionValue(id, "foo.tmp \"build output\""));
        Equal(0, CopyJobValidator.Validate(valid, requireExistingSource: false).Count);
    }

    return Task.CompletedTask;
}

static Task TestDashMultiThreadedAsync()
{
    var request = Request(@"C:\Source", @"D:\Destination");
    True(!RobocopyRunner.IsMultiThreaded(request), "Expected a single-threaded request.");
    True(RobocopyRunner.IsMultiThreaded(request with { AdvancedOptions = "/MT:8" }), "Expected /MT:8 to be multi-threaded.");
    True(RobocopyRunner.IsMultiThreaded(request with { AdvancedOptions = "-MT:8" }), "Expected -MT:8 to be multi-threaded.");
    True(RobocopyRunner.IsMultiThreaded(request with { AdvancedOptions = "-mt" }), "Expected -mt to be multi-threaded.");
    return Task.CompletedTask;
}

static Task TestOutputParserAsync()
{
    var file = RobocopyOutputParser.Parse(@"    New File              10299    C:\Source Folder\pasted-text.txt");
    True(file.HasFile, "Expected a file record.");
    Equal(10299L, file.Bytes);
    Equal(@"C:\Source Folder\pasted-text.txt", file.Path);

    var percent = RobocopyOutputParser.Parse("        47.5%");
    Equal(47.5, percent.Percent ?? -1);

    var scan = RobocopyOutputParser.ParseScan([
        @"    New File              100    C:\Source\one.bin",
        @"    *EXTRA File           50     D:\Destination\old.bin",
        @"    *EXTRA Dir            -1     D:\Destination\old-folder\"
    ]);
    Equal(100L, scan.TotalBytes);
    Equal(1, scan.FileCount);
    Equal(2, scan.PlannedDeleteCount);
    return Task.CompletedTask;
}

static Task TestExitCodesAsync()
{
    Equal(CopyOutcome.Success, ExitCodeInterpreter.Interpret(0, "log").Outcome);
    Equal(CopyOutcome.Success, ExitCodeInterpreter.Interpret(1, "log").Outcome);
    foreach (var warningCode in new[] { 2, 3, 4, 5, 6, 7 })
    {
        Equal(CopyOutcome.SuccessWithWarnings, ExitCodeInterpreter.Interpret(warningCode, "log").Outcome);
    }
    foreach (var failureCode in new[] { 8, 9, 16 })
    {
        Equal(CopyOutcome.Failure, ExitCodeInterpreter.Interpret(failureCode, "log").Outcome);
    }
    foreach (var abnormalCode in new[] { -1, -2, int.MinValue })
    {
        var abnormal = ExitCodeInterpreter.Interpret(abnormalCode, "log");
        Equal(CopyOutcome.Failure, abnormal.Outcome);
        Equal($"Robocopy terminated unexpectedly (exit code {abnormalCode}).", abnormal.Summary);
    }
    Equal(CopyOutcome.Canceled, ExitCodeInterpreter.Interpret(-1, "log", true).Outcome);
    return Task.CompletedTask;
}

static Task TestLogTailSmallFileAsync() => WithFixtureAsync(fixture =>
{
    var path = Path.Combine(fixture.Root, "small.log");
    var content = "first line\r\nsecond line — ünïcode\r\n";
    File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));

    var tail = LogTailReader.Read(path);
    True(!tail.IsTruncated, "A small log must not be truncated.");
    Equal(content, tail.Text);
    return Task.CompletedTask;
});

static Task TestLogTailLargeFileAsync() => WithFixtureAsync(fixture =>
{
    var path = Path.Combine(fixture.Root, "large.log");
    var builder = new System.Text.StringBuilder();
    var lineNumber = 0;
    while (builder.Length < 6 * 1024 * 1024)
    {
        builder.Append($"line {++lineNumber:D7} é漢字🙂\r\n");
    }
    var content = builder.ToString();
    File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
    var fileBytes = new FileInfo(path).Length;

    var tail = LogTailReader.Read(path);
    True(tail.IsTruncated, "A log over 5 MB must be truncated.");
    Equal(fileBytes, tail.FileBytes);
    var headerEnd = tail.Text.IndexOf(Environment.NewLine, StringComparison.Ordinal);
    var header = tail.Text[..headerEnd];
    var body = tail.Text[(headerEnd + Environment.NewLine.Length)..];
    True(header.StartsWith("[Truncated: showing the last 5 MB of ", StringComparison.Ordinal), $"Unexpected header: {header}");
    True(header.EndsWith("Open the log for the full transcript.]", StringComparison.Ordinal), $"Unexpected header: {header}");
    True(body.StartsWith("line ", StringComparison.Ordinal), "The tail must start at a line boundary.");
    True(!body.Contains('�'), "The tail must not split a multibyte character.");
    True(content.EndsWith(body, StringComparison.Ordinal), "The tail must be the end of the log.");
    True(System.Text.Encoding.UTF8.GetByteCount(body) <= LogTailReader.DefaultMaximumBytes, "The tail must fit in 5 MB.");
    return Task.CompletedTask;
});

static Task TestLogTailOpenForWritingAsync() => WithFixtureAsync(fixture =>
{
    var path = Path.Combine(fixture.Root, "open.log");
    using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false)))
    {
        writer.Write("still being written\r\n");
        writer.Flush();

        var tail = LogTailReader.Read(path);
        Equal("still being written\r\n", tail.Text);
        True(!tail.IsTruncated, "A small open log must not be truncated.");
    }

    return Task.CompletedTask;
});

static async Task TestCopyIntegrationAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        var content = new string('R', 64 * 1024);
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, "copy.txt"), content);
        await using var runner = new RobocopyRunner(fixture.Logs);
        var output = new List<string>();
        runner.OutputReceived += (_, line) => output.Add(line);
        var request = DefaultRequest(fixture);
        var scan = await runner.ScanAsync(request);
        True(scan.FileCount == 1, $"Expected one scan file, found {scan.FileCount}. Output: {string.Join(" | ", output)}");
        True(scan.TotalBytes >= content.Length, "Expected scan byte total.");
        var result = await runner.StartAsync(request, scan);
        True(result.Outcome is CopyOutcome.Success or CopyOutcome.SuccessWithWarnings, result.Summary);
        Equal(content, await File.ReadAllTextAsync(Path.Combine(fixture.Destination, "copy.txt")));
        True(File.Exists(result.LogPath), "Expected a saved transcript.");
    });
}

static async Task TestPauseResumeIntegrationAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        await CreateSizedFileAsync(Path.Combine(fixture.Source, "slow.bin"), 24 * 1024 * 1024);
        await using var runner = new RobocopyRunner(fixture.Logs);
        var request = DefaultRequest(fixture, new CopyOptionValue("InterPacketGap", "20"));
        var scan = await runner.ScanAsync(request);
        var copyTask = runner.StartAsync(request, scan);
        await WaitForStageAsync(runner, CopyJobStage.Running, copyTask);
        await Task.Delay(250);
        await runner.PauseAsync();
        Equal(CopyJobStage.Paused, runner.Stage);
        await Task.Delay(150);
        await runner.ResumeAsync();
        var result = await copyTask;
        True(result.Outcome is CopyOutcome.Success or CopyOutcome.SuccessWithWarnings, result.Summary);
        True(File.Exists(Path.Combine(fixture.Destination, "slow.bin")), "Expected resumed file.");
    });
}

static async Task TestStopAndRestartIntegrationAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        await CreateSizedFileAsync(Path.Combine(fixture.Source, "restart.bin"), 16 * 1024 * 1024);
        await using var runner = new RobocopyRunner(fixture.Logs);
        var slowRequest = DefaultRequest(fixture, new CopyOptionValue("InterPacketGap", "30"));
        var scan = await runner.ScanAsync(slowRequest);
        var copyTask = runner.StartAsync(slowRequest, scan);
        await WaitForStageAsync(runner, CopyJobStage.Running, copyTask);
        await Task.Delay(200);
        await runner.StopAsync();
        var stopped = await copyTask;
        Equal(CopyOutcome.Canceled, stopped.Outcome);

        var restartRequest = DefaultRequest(fixture);
        var restartScan = await runner.ScanAsync(restartRequest);
        var restarted = await runner.StartAsync(restartRequest, restartScan);
        True(restarted.Outcome is CopyOutcome.Success or CopyOutcome.SuccessWithWarnings, restarted.Summary);
        True(File.Exists(Path.Combine(fixture.Destination, "restart.bin")), "Expected restarted copy to finish.");
    });
}

static async Task TestStopDuringCopyStartupAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        for (var index = 0; index < 8; index++)
        {
            await CreateSizedFileAsync(Path.Combine(fixture.Source, $"startup-{index}.bin"), 4 * 1024 * 1024);
        }

        await using var runner = new RobocopyRunner(fixture.Logs);
        var request = DefaultRequest(fixture);
        var scan = await runner.ScanAsync(request);
        var outputLines = 0;
        runner.OutputReceived += (_, _) => Interlocked.Increment(ref outputLines);
        Task? stopTask = null;
        runner.ProgressChanged += (_, progress) =>
        {
            // Raised synchronously by StartAsync after Stage = Running and before Robocopy is launched.
            if (stopTask is null && progress.Stage == CopyJobStage.Running)
            {
                stopTask = runner.StopAsync();
            }
        };

        var result = await runner.StartAsync(request, scan);
        True(stopTask is not null, "Expected Stop to be requested during startup.");
        await stopTask!;
        Equal(CopyOutcome.Canceled, result.Outcome);
        Equal(CopyJobStage.Canceled, runner.Stage);
        Equal(0, Volatile.Read(ref outputLines));
        True(!Directory.EnumerateFileSystemEntries(fixture.Destination).Any(), "Robocopy must not copy anything after Stop.");
    });
}

static async Task TestStopDuringScanStartupAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, "scan.txt"), "scan");
        await using var runner = new RobocopyRunner(fixture.Logs);
        var outputLines = 0;
        runner.OutputReceived += (_, _) => Interlocked.Increment(ref outputLines);
        Task? stopTask = null;
        runner.ProgressChanged += (_, progress) =>
        {
            if (stopTask is null && progress.Stage == CopyJobStage.Scanning)
            {
                stopTask = runner.StopAsync();
            }
        };

        var canceled = false;
        try
        {
            await runner.ScanAsync(DefaultRequest(fixture));
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        True(stopTask is not null, "Expected Stop to be requested during startup.");
        await stopTask!;
        True(canceled, "Expected the pre-scan to be canceled.");
        Equal(CopyJobStage.Canceled, runner.Stage);
        Equal(0, Volatile.Read(ref outputLines));
    });
}

static async Task TestStopImmediatelyAfterStartAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        await CreateSizedFileAsync(Path.Combine(fixture.Source, "large.bin"), 64 * 1024 * 1024);
        await using var runner = new RobocopyRunner(fixture.Logs);
        // A full copy takes about 50 seconds, so a Robocopy process that outlives Stop is easy to detect.
        var request = DefaultRequest(fixture, new CopyOptionValue("InterPacketGap", "50"));
        var scan = await runner.ScanAsync(request);
        var copyTask = runner.StartAsync(request, scan);
        await runner.StopAsync();
        var finished = await Task.WhenAny(copyTask, Task.Delay(TimeSpan.FromSeconds(10)));
        True(finished == copyTask, "Robocopy kept running after Stop.");
        Equal(CopyOutcome.Canceled, (await copyTask).Outcome);
        Equal(CopyJobStage.Canceled, runner.Stage);
    });
}

static async Task TestMirrorScanIntegrationAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, "keep.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(fixture.Destination, "keep.txt"), "keep");
        await File.WriteAllTextAsync(Path.Combine(fixture.Destination, "extra.txt"), "delete only during a real mirror");
        Directory.CreateDirectory(Path.Combine(fixture.Destination, "extra-folder"));
        await using var runner = new RobocopyRunner(fixture.Logs);
        var output = new List<string>();
        runner.OutputReceived += (_, line) => output.Add(line);
        var request = Request(fixture.Source, fixture.Destination,
            new("Restartable"), new("Retries", "1"), new("Wait", "0"), new("Mirror"));
        var scan = await runner.ScanAsync(request);
        True(scan.PlannedDeleteCount >= 1, $"Expected the mirror dry run to find the extra destination file. Output: {string.Join(" | ", output)}");
        True(File.Exists(Path.Combine(fixture.Destination, "extra.txt")), "A dry run must not delete anything.");
    });
}

static async Task TestOemFileNameIntegrationAsync()
{
    // Robocopy writes redirected output in the system OEM code page (GetOEMCP), so a name is only
    // expected to survive the round trip when that code page can represent it.
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    var systemOem = System.Text.Encoding.GetEncoding(
        (int)NativeMethods.GetOEMCP(),
        System.Text.EncoderFallback.ExceptionFallback,
        System.Text.DecoderFallback.ExceptionFallback);
    var fileName = new[] { "café.txt", "naïve.txt", "Ünïcödé.txt" }.FirstOrDefault(name =>
    {
        try
        {
            return systemOem.GetString(systemOem.GetBytes(name)) == name;
        }
        catch (System.Text.EncoderFallbackException)
        {
            return false;
        }
    });
    if (fileName is null)
    {
        Console.WriteLine($"      (skipped: system OEM code page {systemOem.CodePage} cannot represent the sample names)");
        return;
    }

    await WithFixtureAsync(async fixture =>
    {
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, fileName), "oem");
        await using var runner = new RobocopyRunner(fixture.Logs);
        var output = new List<string>();
        runner.OutputReceived += (_, line) => output.Add(line);
        var request = DefaultRequest(fixture);
        var scan = await runner.ScanAsync(request);
        True(scan.FileCount == 1, $"Expected one scan file, found {scan.FileCount}. Output: {string.Join(" | ", output)}");
        var result = await runner.StartAsync(request, scan);
        True(result.Outcome is CopyOutcome.Success or CopyOutcome.SuccessWithWarnings, result.Summary);
        True(File.Exists(Path.Combine(fixture.Destination, fileName)), $"Expected {fileName} to be copied.");
        True(output.Any(line => line.Contains(fileName, StringComparison.Ordinal)),
            $"Expected Robocopy output to contain '{fileName}' (system OEM code page {systemOem.CodePage}). Output: {string.Join(" | ", output)}");
        var transcript = await File.ReadAllTextAsync(result.LogPath);
        True(transcript.Contains(fileName, StringComparison.Ordinal), $"Expected the transcript to contain '{fileName}'.");
    });
}

static Task TestDestructiveConfirmationAsync() => WithFixtureAsync(async fixture =>
{
    var cases = new (string[] Options, int ExpectedDeletes, bool ExpectedMoves)[]
    {
        (["MoveFiles"], 0, true),
        (["MoveTree"], 0, true),
        (["Mirror"], 3, false),
        (["Purge"], 3, false),
        (["Purge", "MoveFiles"], 3, true)
    };

    foreach (var testCase in cases)
    {
        var dialogs = new FakeDialogs();
        await using var viewModel = new MainViewModel(new FakeRunner(plannedDeletes: 3), new FakeFolderPicker(), dialogs, new FakeLauncher())
        {
            SourcePath = fixture.Source,
            DestinationPath = fixture.Destination
        };
        foreach (var option in viewModel.Options)
        {
            option.IsSelected = testCase.Options.Contains(option.Id);
        }

        viewModel.StartCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.Stage == CopyJobStage.Canceled);
        var label = string.Join("+", testCase.Options);
        True(dialogs.Confirmations.Count == 1, $"{label}: expected one confirmation, found {dialogs.Confirmations.Count}. Errors: {string.Join(" | ", dialogs.Errors)}");
        True(dialogs.Confirmations[0].PlannedDeletes == testCase.ExpectedDeletes, $"{label}: expected {testCase.ExpectedDeletes} planned deletes, found {dialogs.Confirmations[0].PlannedDeletes}.");
        True(dialogs.Confirmations[0].MovesSourceItems == testCase.ExpectedMoves, $"{label}: expected moves={testCase.ExpectedMoves}.");
    }
});

static async Task WaitUntilAsync(Func<bool> condition)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!condition() && DateTime.UtcNow < deadline)
    {
        await Task.Delay(10);
    }

    True(condition(), "Timed out waiting for the expected state.");
}

static async Task TestScanLogPathIntegrationAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        await File.WriteAllTextAsync(Path.Combine(fixture.Source, "scan.txt"), "scan");
        await using var runner = new RobocopyRunner(fixture.Logs);
        Equal(string.Empty, runner.CurrentLogPath);
        await runner.ScanAsync(DefaultRequest(fixture));
        True(File.Exists(runner.CurrentLogPath), "Expected the pre-scan transcript to exist after the scan.");
        True(runner.CurrentLogPath.StartsWith(fixture.Logs, StringComparison.OrdinalIgnoreCase), "Expected the transcript in the runner log directory.");
    });
}

static async Task TestFailedScanLogPathAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        var runner = new FakeRobocopyRunner(Path.Combine(fixture.Root, "failed-scan.log"), failScan: true);
        var dialogs = new FakeDialogs(confirm: true);
        await using var viewModel = CreateViewModel(fixture, runner, dialogs);
        var openLogChanged = 0;
        viewModel.OpenLogCommand.CanExecuteChanged += (_, _) => openLogChanged++;

        await ExecuteStartAsync(viewModel);

        Equal(CopyJobStage.Failed, viewModel.Stage);
        Equal(runner.CurrentLogPath, viewModel.LastLogPath);
        True(viewModel.OpenLogCommand.CanExecute(null), "Open log should be enabled after a failed pre-scan.");
        True(viewModel.CopyLogCommand.CanExecute(null), "Copy log should be enabled after a failed pre-scan.");
        True(openLogChanged > 0, "Expected the Open log command to be refreshed.");
        Equal(1, dialogs.Errors.Count);
        Equal(0, runner.StartCalls);
    });
}

static async Task TestDeclinedConfirmationLogPathAsync()
{
    await WithFixtureAsync(async fixture =>
    {
        var runner = new FakeRobocopyRunner(Path.Combine(fixture.Root, "declined.log"), failScan: false);
        var dialogs = new FakeDialogs(confirm: false);
        await using var viewModel = CreateViewModel(fixture, runner, dialogs);
        viewModel.Options.First(option => option.Id == "Purge").IsSelected = true;

        await ExecuteStartAsync(viewModel);

        Equal(CopyJobStage.Canceled, viewModel.Stage);
        Equal(1, dialogs.Confirmations.Count);
        Equal(runner.CurrentLogPath, viewModel.LastLogPath);
        True(viewModel.OpenLogCommand.CanExecute(null), "Open log should be enabled after declining confirmation.");
        True(viewModel.CopyLogCommand.CanExecute(null), "Copy log should be enabled after declining confirmation.");
        Equal(0, runner.StartCalls);
    });
}

static MainViewModel CreateViewModel(TestFixture fixture, IRobocopyRunner runner, IUserDialogService dialogs) =>
    new(runner, new FakeFolderPicker(), dialogs, new FakeLauncher())
    {
        SourcePath = fixture.Source,
        DestinationPath = fixture.Destination
    };

static async Task ExecuteStartAsync(MainViewModel viewModel)
{
    True(viewModel.StartCommand.CanExecute(null), "Expected Start to be enabled.");
    viewModel.StartCommand.Execute(null);
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!viewModel.StartCommand.CanExecute(null) && DateTime.UtcNow < deadline)
    {
        await Task.Delay(25);
    }

    True(viewModel.StartCommand.CanExecute(null), "Expected Start to finish.");
}

static CopyJobRequest DefaultRequest(TestFixture fixture, params CopyOptionValue[] extra)
{
    var values = new List<CopyOptionValue>
    {
        new("EmptySubdirectories"), new("Restartable"), new("Retries", "1"), new("Wait", "0")
    };
    values.AddRange(extra);
    return Request(fixture.Source, fixture.Destination, values.ToArray());
}

static CopyJobRequest Request(string source, string destination, params CopyOptionValue[] options) =>
    new(source, destination, "*.*", options, string.Empty);

static async Task WaitForStageAsync(IRobocopyRunner runner, CopyJobStage expected, Task copyTask)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (runner.Stage != expected && !copyTask.IsCompleted && DateTime.UtcNow < deadline)
    {
        await Task.Delay(25);
    }

    True(runner.Stage == expected, $"Expected stage {expected}, found {runner.Stage}.");
}

static async Task CreateSizedFileAsync(string path, long length)
{
    await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
    stream.SetLength(length);
}

static async Task WithFixtureAsync(Func<TestFixture, Task> action)
{
    var root = Path.Combine(Path.GetTempPath(), "RobustCopyTests", Guid.NewGuid().ToString("N"));
    var fixture = new TestFixture(root, Path.Combine(root, "source"), Path.Combine(root, "destination"), Path.Combine(root, "logs"));
    Directory.CreateDirectory(fixture.Source);
    Directory.CreateDirectory(fixture.Destination);
    try
    {
        await action(fixture);
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', found '{actual}'.");
    }
}

internal sealed record TestFixture(string Root, string Source, string Destination, string Logs);

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    internal static extern uint GetOEMCP();
}

internal sealed class FakeRunner(int plannedDeletes) : IRobocopyRunner
{
    public event EventHandler<CopyJobProgress>? ProgressChanged { add { } remove { } }
    public event EventHandler<string>? OutputReceived { add { } remove { } }

    public CopyJobStage Stage => CopyJobStage.Idle;
    public bool IsBusy => false;
    public string CurrentLogPath => string.Empty;

    public Task<ScanResult> ScanAsync(CopyJobRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScanResult(100, 1, plannedDeletes, []));

    public Task<CopyJobResult> StartAsync(CopyJobRequest request, ScanResult scan, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The fake runner must not start a copy.");

    public Task PauseAsync() => Task.CompletedTask;
    public Task ResumeAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeRobocopyRunner(string logPath, bool failScan) : IRobocopyRunner
{
    public event EventHandler<CopyJobProgress>? ProgressChanged;
    public event EventHandler<string>? OutputReceived;

    public CopyJobStage Stage { get; private set; } = CopyJobStage.Idle;
    public bool IsBusy => Stage is CopyJobStage.Scanning or CopyJobStage.Running or CopyJobStage.Paused or CopyJobStage.Stopping;
    public string CurrentLogPath { get; private set; } = string.Empty;
    public int StartCalls { get; private set; }

    public async Task<ScanResult> ScanAsync(CopyJobRequest request, CancellationToken cancellationToken = default)
    {
        CurrentLogPath = logPath;
        await File.WriteAllTextAsync(logPath, "pre-scan transcript", cancellationToken);
        OutputReceived?.Invoke(this, "pre-scan transcript");
        if (failScan)
        {
            Stage = CopyJobStage.Failed;
            throw new InvalidOperationException($"Robocopy pre-scan failed with exit code 16. Review {logPath}.");
        }

        Stage = CopyJobStage.Idle;
        ProgressChanged?.Invoke(this, new(Stage, 0, 0, "Ready", 0, 10, 0, null, 0, 1));
        return new ScanResult(10, 1, 1, []);
    }

    public Task<CopyJobResult> StartAsync(CopyJobRequest request, ScanResult scan, CancellationToken cancellationToken = default)
    {
        StartCalls++;
        Stage = CopyJobStage.Completed;
        return Task.FromResult(ExitCodeInterpreter.Interpret(1, CurrentLogPath));
    }

    public Task PauseAsync() => Task.CompletedTask;
    public Task ResumeAsync() => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeDialogs(bool confirm = false) : IUserDialogService
{
    public List<(string Destination, int PlannedDeletes, bool MovesSourceItems)> Confirmations { get; } = [];
    public List<string> Errors { get; } = [];

    public void ShowError(string title, string message) => Errors.Add(message);
    public void ShowInformation(string title, string message) { }

    public bool ConfirmDestructive(string destination, int plannedDeletes, bool movesSourceItems)
    {
        Confirmations.Add((destination, plannedDeletes, movesSourceItems));
        return confirm;
    }
}

internal sealed class FakeFolderPicker : IFolderPicker
{
    public string? PickFolder(string title, string initialFolder) => null;
}

internal sealed class FakeLauncher : IDesktopLauncher
{
    public void OpenPath(string path) { }
    public void CopyText(string text) { }
}
