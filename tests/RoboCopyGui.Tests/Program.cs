using RoboCopyGui.Core;
using RoboCopyGui.Services;
using RoboCopyGui.ViewModels;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Argument tokenizer preserves quoted values", TestTokenizerAsync),
    ("Command builder keeps paths as individual arguments", TestCommandBuilderAsync),
    ("Validator blocks overlapping paths", TestOverlappingPathsAsync),
    ("Validator reports option conflicts", TestOptionConflictsAsync),
    ("Option catalog preserves the original category organization", TestOptionCategoriesAsync),
    ("Validator protects GUI-owned advanced flags", TestAdvancedProtectionAsync),
    ("Output parser reads bytes, paths, percentages, and extras", TestOutputParserAsync),
    ("Exit codes map to Robocopy outcomes", TestExitCodesAsync),
    ("Robocopy scan and copy integration", TestCopyIntegrationAsync),
    ("Robocopy pause and resume integration", TestPauseResumeIntegrationAsync),
    ("Robocopy stop and restart integration", TestStopAndRestartIntegrationAsync),
    ("Mirror pre-scan reports planned deletion", TestMirrorScanIntegrationAsync)
};

var failures = 0;
Console.WriteLine($"RoboCopy GUI test runner — {tests.Length} tests");
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
    var root = Path.Combine(Path.GetTempPath(), "RoboCopyGuiTests", Guid.NewGuid().ToString("N"));
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
