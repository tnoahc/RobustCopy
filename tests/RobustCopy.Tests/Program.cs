using RobustCopy.Core;
using RobustCopy.Services;
using RobustCopy.ViewModels;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Application identity uses RobustCopy", TestAppIdentityAsync),
    ("Legacy application logs migrate to RobustCopy", TestLegacyLogMigrationAsync),
    ("Argument tokenizer preserves quoted values", TestTokenizerAsync),
    ("Command builder keeps paths as individual arguments", TestCommandBuilderAsync),
    ("Command preview quotes paths so they round-trip", TestDisplayCommandQuotingAsync),
    ("Validator blocks overlapping paths", TestOverlappingPathsAsync),
    ("Validator reports option conflicts", TestOptionConflictsAsync),
    ("Option catalog preserves the original category organization", TestOptionCategoriesAsync),
    ("Validator protects GUI-owned advanced flags", TestAdvancedProtectionAsync),
    ("Validator protects dash-prefixed GUI-owned advanced flags", TestAdvancedDashProtectionAsync),
    ("Validator rejects switches in file patterns", TestFilePatternSwitchesAsync),
    ("Validator rejects switches in list options", TestListOptionSwitchesAsync),
    ("Runner detects dash-prefixed multi-threading", TestDashMultiThreadedAsync),
    ("Output parser reads bytes, paths, percentages, and extras", TestOutputParserAsync),
    ("Exit codes map to Robocopy outcomes", TestExitCodesAsync),
    ("Robocopy scan and copy integration", TestCopyIntegrationAsync),
    ("Robocopy pause and resume integration", TestPauseResumeIntegrationAsync),
    ("Robocopy stop and restart integration", TestStopAndRestartIntegrationAsync),
    ("Stop during copy startup prevents Robocopy launch", TestStopDuringCopyStartupAsync),
    ("Stop during scan startup prevents Robocopy launch", TestStopDuringScanStartupAsync),
    ("Stop right after start leaves no copy running", TestStopImmediatelyAfterStartAsync),
    ("Mirror pre-scan reports planned deletion", TestMirrorScanIntegrationAsync)
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
