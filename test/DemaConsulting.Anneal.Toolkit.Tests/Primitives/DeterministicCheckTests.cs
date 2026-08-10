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

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-check-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
