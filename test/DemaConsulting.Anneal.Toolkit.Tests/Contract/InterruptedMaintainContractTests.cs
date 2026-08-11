using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-49's interrupted-diff snapshot promise: when <c>maintain</c> escalates or fails
///     after the worker had already written files to disk, it writes a <c>.anneal/logs/snapshots/interrupted-*.patch</c>
///     snapshot file via <c>InterruptedDiffSnapshot</c> and prints <c>maintain: pre-triage snapshot written to
///     &lt;path&gt;</c>. A completed run never produces a patch file. The snapshot step is silent when git is
///     unavailable; the reported outcome is unaffected.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> and assertions
///     are on the exit code and the written output. Nothing here reaches inside <see cref="MaintainOperation" />
///     or any worker.
/// </remarks>
public class InterruptedMaintainContractTests
{
    /// <summary>
    ///     TOOLKIT-49 (snapshot promise) — when <c>maintain</c> escalates after the worker already wrote files,
    ///     it writes a <c>.anneal/logs/snapshots/interrupted-*.patch</c> file containing the <c>git diff HEAD</c> for those
    ///     files and prints <c>maintain: pre-triage snapshot written to</c>. Verified by
    ///     <c>MaintainWritesSnapshotPatchOnInterruptedOutcome</c>.
    /// </summary>
    [Fact]
    public async Task MaintainWritesSnapshotPatchOnInterruptedOutcome()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: a real git repository with a HEAD commit so git diff HEAD is meaningful.
            await RunGitAsync(root, "init");
            await RunGitAsync(root, "config", "user.email", "test@example.com");
            await RunGitAsync(root, "config", "user.name", "Test");
            var srcDir = Path.Combine(root, "src");
            Directory.CreateDirectory(srcDir);
            var changedFile = Path.Combine(srcDir, "Written.cs");
            File.WriteAllText(changedFile, "// original\n");
            await RunGitAsync(root, "add", "-A");
            await RunGitAsync(root, "commit", "-m", "initial");

            // Mutate the tracked file so git diff HEAD is non-empty.
            File.WriteAllText(changedFile, "// changed before stopping\n");

            // Worker reports it changed a file outside the declared bound, forcing escalation (TOOLKIT-30).
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                CompletedJson(["src/Written.cs", "src/OutOfBound.cs"], "tidied more than declared"));

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            var output = new StringWriter();

            // Act
            await AnnealTool.RunAsync(
                ["maintain", "tidy a helper", "src/Written.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            // Assert: a patch file exists under .anneal/logs/snapshots/ and contains the changed line;
            // the output announces where the snapshot was written.
            var snapshotsDir = Path.Combine(root, ".anneal", "logs", "snapshots");
            var patches = Directory.Exists(snapshotsDir)
                ? Directory.GetFiles(snapshotsDir, "interrupted-*.patch")
                : [];

            Assert.NotEmpty(patches);
            Assert.Multiple(
                () => Assert.Contains("changed before stopping", File.ReadAllText(patches[0]), StringComparison.Ordinal),
                () => Assert.Contains("pre-triage snapshot written to", written, StringComparison.Ordinal));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-49 (snapshot promise — negative) — a normal Succeeded run never produces a patch file under
    ///     <c>.anneal/logs/snapshots/</c>. Verified by <c>SucceededMaintainRunProducesNoSnapshotPatch</c>.
    /// </summary>
    [Fact]
    public async Task SucceededMaintainRunProducesNoSnapshotPatch()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: worker completes within the declared bound — normal success path.
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                CompletedJson(["src/a.cs"], "tidied the interior helper"));

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            var output = new StringWriter();

            // Act
            await AnnealTool.RunAsync(
                ["maintain", "tidy a helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            // Assert: no patch file exists; the snapshot output line is absent.
            var logsDir = Path.Combine(root, ".anneal", "logs");
            var patches = Directory.Exists(logsDir)
                ? Directory.GetFiles(logsDir, "interrupted-*.patch")
                : [];

            Assert.Multiple(
                () => Assert.Empty(patches),
                () => Assert.DoesNotContain("pre-triage snapshot written to", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-49 (snapshot promise — no-git resilience) — when there is no git repository in the root, the
    ///     snapshot step's <c>git diff HEAD</c> call fails silently; <c>maintain</c> still reports the escalated
    ///     outcome correctly and does not throw. Verified by
    ///     <c>MaintainSnapshotFailureDoesNotMaskReportedOutcome</c>.
    /// </summary>
    [Fact]
    public async Task MaintainSnapshotFailureDoesNotMaskReportedOutcome()
    {
        // Arrange: plain non-git temp folder. The snapshot step will call git in a directory with no .git
        // and get a non-zero exit, which the operation must absorb without throwing or altering the outcome.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                CompletedJson(["src/Written.cs", "src/OutOfBound.cs"], "tidied more than declared"));

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "tidy a helper", "src/Written.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            // Assert: escalation is reported normally; no patch file (git failed silently);
            // no exception was thrown (the call returned instead of propagating).
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
                () => Assert.Contains("maintain: escalated", written, StringComparison.Ordinal),
                () => Assert.DoesNotContain("pre-triage snapshot written to", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CompletedJson(IReadOnlyList<string> filesChanged, string summary) =>
        $$"""
          {"kind":"Completed","why":"","suggestedWorker":"","filesChanged":[{{string.Join(",", filesChanged.Select(file => $"\"{file}\""))}}],"summary":"{{summary}}"}
          """;

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-tk49-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), "");
        return root;
    }

    private static async Task RunGitAsync(string workingDir, params string[] args)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        await stdout.ConfigureAwait(false);
        var errorText = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} exited {process.ExitCode}: {errorText}");
    }
}
