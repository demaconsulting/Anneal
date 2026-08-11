using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="DeterministicCheck" />'s basic shape and outcome mapping. The one primitive
///     with no model call, so these tests inject only a substituted <see cref="RunRepositoryScript" />.
/// </summary>
public class DeterministicCheckTests
{
    [Fact]
    public async Task RunAsync_BlankName_ReportsUsageError()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var check = new DeterministicCheck(root, runScript: (_, _) => Task.FromResult(new ScriptRun(0, "")));

            // Act
            var result = await check.RunAsync(
                "  ", "build.ps1", null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.UsageError, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_BlankScript_ReportsUsageError()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var check = new DeterministicCheck(root, runScript: (_, _) => Task.FromResult(new ScriptRun(0, "")));

            // Act
            var result = await check.RunAsync(
                "build", string.Empty, null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.UsageError, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ScriptExitsZero_Succeeds()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var check = new DeterministicCheck(
                root, runScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            // Act
            var result = await check.RunAsync(
                "build", "build.ps1", null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.True(result.Finding?.Passed),
                () => Assert.Equal(0, result.Finding?.ExitCode));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ScriptExitsNonZero_Fails()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var check = new DeterministicCheck(
                root, runScript: (_, _) => Task.FromResult(new ScriptRun(1, "it broke")));

            // Act
            var result = await check.RunAsync(
                "build", "build.ps1", "some-selector", TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.False(result.Finding?.Passed),
                () => Assert.Equal(1, result.Finding?.ExitCode),
                () => Assert.Contains("some-selector", result.Finding!.EvidenceRefs));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ScriptOutlivesItsTimeout_ReportsFailed()
    {
        // Arrange: a script that never finishes within a very short timeout
        var root = CreateTemporaryDirectory();
        try
        {
            var check = new DeterministicCheck(
                root,
                timeout: TimeSpan.FromMilliseconds(20),
                runScript: async (_, cancellationToken) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    return new ScriptRun(0, "should never get here");
                });

            // Act
            var result = await check.RunAsync(
                "build", "build.ps1", null, TestContext.Current.CancellationToken);

            // Assert: a timeout is a failed check, not a withdrawal
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.False(result.Finding?.Passed));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NullScript_ReportsSucceededWithATruthfulSkippedFinding()
    {
        // Arrange: a repository that configures no script for this check (ScriptConfiguration) is not a
        // failure to diagnose - the finding must still read as truthfully passed, not merely absent, so
        // downstream evidence lists and report fields that read Finding?.Passed do not silently read as failed.
        var root = CreateTemporaryDirectory();
        try
        {
            var ran = false;
            var check = new DeterministicCheck(root, runScript: (_, _) =>
            {
                ran = true;
                return Task.FromResult(new ScriptRun(0, "should never run"));
            });

            // Act
            var result = await check.RunAsync(
                "build", null, null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.True(result.Finding?.Passed),
                () => Assert.False(ran));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_OutputExceedsBudget_WritesFullOutputFileAndSummaryContainsPath()
    {
        // Arrange: output that exceeds the 2000-character summarization budget
        var root = CreateTemporaryDirectory();
        try
        {
            var largeOutput = new string('x', 2001);
            var check = new DeterministicCheck(
                root, runScript: (_, _) => Task.FromResult(new ScriptRun(0, largeOutput)));

            // Act
            var result = await check.RunAsync(
                "my-check", "build.ps1", null, TestContext.Current.CancellationToken);

            // Assert: the summary references a log file whose content is the untruncated output
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
            var summary = result.Finding!.Summary;
            Assert.Contains("Full output:", summary);

            var logPathLine = summary.Split('\n')
                .First(l => l.StartsWith("Full output:", StringComparison.Ordinal));
            var logRelPath = logPathLine["Full output: ".Length..].Trim();

            Assert.StartsWith(".anneal/logs/checks/check-output-my-check-", logRelPath, StringComparison.Ordinal);
            Assert.EndsWith(".txt", logRelPath, StringComparison.Ordinal);

            var fullPath = Path.Combine(root, logRelPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(fullPath), $"Expected log file at {fullPath}");
            Assert.Equal(largeOutput, await File.ReadAllTextAsync(fullPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_OutputWithinBudget_NoLogFileWrittenAndSummaryLacksFullOutputReference()
    {
        // Arrange: output that fits within the 2000-character summarization budget
        var root = CreateTemporaryDirectory();
        try
        {
            var shortOutput = new string('x', 2000);
            var check = new DeterministicCheck(
                root, runScript: (_, _) => Task.FromResult(new ScriptRun(0, shortOutput)));

            // Act
            var result = await check.RunAsync(
                "my-check", "build.ps1", null, TestContext.Current.CancellationToken);

            // Assert: no log file written, no 'Full output:' reference in summary
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
            Assert.DoesNotContain("Full output:", result.Finding!.Summary, StringComparison.Ordinal);

            var logsDir = Path.Combine(root, ".anneal", "logs");
            Assert.False(Directory.Exists(logsDir) && Directory.EnumerateFiles(logsDir).Any(),
                "No log files should be written when output fits within the budget");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_LogWriteFails_OutcomeIsUnaffectedAndNoExceptionThrown()
    {
        // Arrange: a repository root that does not exist so Directory.CreateDirectory will fail
        // on a path that cannot be created (root is a file, not a directory).
        var tempFile = Path.GetTempFileName();
        // Use the temp file itself as the "repository root": .anneal/logs cannot be created under a file.
        try
        {
            var largeOutput = new string('x', 2001);
            var check = new DeterministicCheck(
                tempFile, runScript: (_, _) => Task.FromResult(new ScriptRun(0, largeOutput)));

            // Act: must not throw even though the log write will fail
            var result = await check.RunAsync(
                "my-check", "build.ps1", null, TestContext.Current.CancellationToken);

            // Assert: outcome is still reported correctly despite the write failure
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.True(result.Finding?.Passed));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunAsync_TouchedFileHintGiven_MiddleLineWithTouchedFileNameSurvivesTruncation()
    {
        // Arrange: build output that exceeds the budget; the critical warning sits in the middle and
        // would be dropped by plain head+tail, but the touched-file hint names the file it mentions.
        var root = CreateTemporaryDirectory();
        try
        {
            var head = new string('a', 900) + "\n";
            var middle = "WARNING: src/Foo/Bar.cs has an unresolved reference\n";
            var tail = new string('b', 900) + "\n";
            // Pad with filler so total clearly exceeds the 2000-character budget
            var filler = new string('c', 300);
            var largeOutput = head + filler + middle + filler + tail;
            var check = new DeterministicCheck(
                root, runScript: (_, _) => Task.FromResult(new ScriptRun(1, largeOutput)));

            // Act
            var result = await check.RunAsync(
                "build", "build.ps1", null, TestContext.Current.CancellationToken,
                touchedFiles: ["src/Foo/Bar.cs"]);

            // Assert: the touched-file warning from the middle of the output survives in the summary
            Assert.Contains("Bar.cs has an unresolved reference", result.Finding!.Summary,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NoTouchedFileHint_FallsBackToPlainHeadTailTruncation()
    {
        // Arrange: same oversized output as above, but no touched-file hint; the middle line must
        // not appear in the summary so the caller sees unmodified head+tail behavior.
        var root = CreateTemporaryDirectory();
        try
        {
            var head = new string('a', 1000);
            var middle = "\nWARNING: src/Foo/Bar.cs has an unresolved reference\n";
            var tail = new string('b', 1000);
            var filler = new string('c', 500);
            var largeOutput = head + filler + middle + filler + tail;
            var check = new DeterministicCheck(
                root, runScript: (_, _) => Task.FromResult(new ScriptRun(1, largeOutput)));

            // Act: no touchedFiles argument — default (null) path
            var result = await check.RunAsync(
                "build", "build.ps1", null, TestContext.Current.CancellationToken);

            // Assert: summary is pure head+tail — the middle warning is not there
            Assert.DoesNotContain("Bar.cs has an unresolved reference", result.Finding!.Summary,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-check-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
