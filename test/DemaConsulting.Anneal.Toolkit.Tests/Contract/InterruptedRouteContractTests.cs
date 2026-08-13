using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-23's expanded promise: on an Escalated or Failed outcome, if the selected
///     worker had already written files before stopping, <c>route</c> reports those files and a summary
///     separately from the completion fields, writes a <c>.anneal/logs/snapshots/interrupted-*.patch</c> snapshot file,
///     and prints <c>pre-triage snapshot written to &lt;path&gt;</c>. A normal Succeeded run produces no patch
///     file. The snapshot step is silent when git is unavailable; the reported outcome is unaffected.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> and assertions
///     are on the exit code and the written output. Nothing here reaches inside <see cref="RouteOperation" />
///     or any worker.
/// </remarks>
public class InterruptedRouteContractTests
{
    /// <summary>
    ///     TOOLKIT-23 (interrupted-change promise) — when the selected worker stops on Failed after writing files,
    ///     <c>route</c> prints those files and a summary alongside the failure output, so a caller can see what is
    ///     already on disk without inspecting the working tree manually. On a successful completion, those same
    ///     fields carry nothing, so they act as a reliable "something was interrupted" signal rather than always
    ///     carrying data. Verified by <c>RouteReportsFilesWrittenBeforeStopping</c>.
    /// </summary>
    [Fact]
    public async Task RouteReportsFilesWrittenBeforeStopping()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: oracle selects general; developer writes a file (Completed state); build check fails
            // on both the initial attempt and the one local repair GeneralWorker's default Small-effort budget
            // allows, so the worker returns Failed with the last Completed state as Interrupted. Each Developer
            // authoring turn consumes two replies (a free-text turn, then the forced structured decision),
            // so two full rounds need four replies, plus the preflight oracle reply.
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"interior fix","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I edited a file before the build failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""",
                "I repaired the file, but the build still failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(1, "build failed")),
                runGit: CodeOnlyDiff("src/Written.cs"));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "fix something"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            // Assert: the exit code is still ExitSuccess - route's Authoring category never gates a Failed
            // outcome, unrelated to this clause and true regardless of it - but the output now names both the
            // file already on disk and the "before stopping" summary, which is the promise this expanded
            // clause adds.
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("route: failed", written, StringComparison.Ordinal),
                () => Assert.Contains("src/Written.cs", written, StringComparison.Ordinal),
                () => Assert.Contains("before stopping", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        // Second scenario, same clause: a successful completion carries no interrupted-change data, so the
        // fields act as a reliable "something was interrupted" signal rather than always carrying data.
        var successRoot = CreateTemporaryDirectory();
        try
        {
            // Arrange: oracle selects general; developer completes; build passes — normal success path.
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"simple fix","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"fixed it"}""",
                PassedVerifierJson());

            var operation = new RouteOperation(
                successRoot,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: CodeOnlyDiff("src/Foo.cs"));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "fix the bug"],
                output,
                [operation],
                successRoot,
                TestContext.Current.CancellationToken);

            // Assert: success, and the output does not carry the interrupted-change lines
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.DoesNotContain("before stopping", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(successRoot, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-23 (snapshot promise) — when the selected worker stops with files already on disk, <c>route</c>
    ///     writes a <c>.anneal/logs/snapshots/interrupted-*.patch</c> file containing the <c>git diff HEAD</c> for those files
    ///     and prints <c>pre-triage snapshot written to</c> so a caller can recover the pre-triage state.
    ///     Verified by <c>RouteWritesSnapshotPatchOnInterruptedOutcome</c>.
    /// </summary>
    [Fact]
    public async Task RouteWritesSnapshotPatchOnInterruptedOutcome()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: a real git repository with a HEAD commit so git diff HEAD is meaningful.
            // The file the fake worker will claim it changed must actually differ from HEAD.
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

            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"interior fix","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I edited a file before the build failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""",
                "I repaired the file, but the build still failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(1, "build failed")),
                runGit: CodeOnlyDiff("src/Written.cs"));

            var output = new StringWriter();

            // Act
            await AnnealTool.RunAsync(
                ["route", "fix something"],
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
            // Git marks its internal object files read-only on Windows; clear the attribute
            // recursively before deleting so Directory.Delete does not throw UnauthorizedAccessException.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-23 (snapshot promise — negative) — a normal Succeeded run never produces a patch file under
    ///     <c>.anneal/logs/snapshots/</c>. Verified by <c>SucceededRunProducesNoSnapshotPatch</c>.
    /// </summary>
    [Fact]
    public async Task SucceededRunProducesNoSnapshotPatch()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: oracle selects general; developer completes; build passes — normal success path.
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"simple fix","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"fixed it"}""",
                PassedVerifierJson());

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: CodeOnlyDiff("src/Foo.cs"));

            var output = new StringWriter();

            // Act
            await AnnealTool.RunAsync(
                ["route", "fix the bug"],
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
    ///     TOOLKIT-23 (snapshot promise — no-git resilience) — when there is no git repository in the temp root the
    ///     snapshot step's <c>git diff HEAD</c> call fails silently; <c>route</c> still reports the Escalated/Failed
    ///     outcome correctly and does not throw. Verified by <c>SnapshotFailureDoesNotMaskReportedOutcome</c>.
    /// </summary>
    [Fact]
    public async Task SnapshotFailureDoesNotMaskReportedOutcome()
    {
        // Arrange: plain non-git temp folder — same shape as the existing RouteReportsFilesWrittenBeforeStopping
        // scenario. The snapshot step will call git in a directory with no .git and get a non-zero exit, which
        // the operation must absorb without throwing or altering the reported outcome.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"interior fix","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I edited a file before the build failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""",
                "I repaired the file, but the build still failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(1, "build failed")),
                runGit: CodeOnlyDiff("src/Written.cs"));

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["route", "fix something"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            // Assert: the escalation/failure is reported normally; no patch file (git failed silently);
            // no exception was thrown (the call returned instead of propagating).
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("route: failed", written, StringComparison.Ordinal),
                () => Assert.Contains("src/Written.cs", written, StringComparison.Ordinal),
                () => Assert.DoesNotContain("pre-triage snapshot written to", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-54 (triage JSON companion) — when <c>route</c> writes a patch file on a Failed or Escalated
    ///     outcome, it also writes a companion <c>.json</c> file next to the patch containing the triage narrative.
    ///     Verified by <c>RouteWritesTriageContextJsonAlongsidePatch</c>.
    /// </summary>
    [Fact]
    public async Task RouteWritesTriageContextJsonAlongsidePatch()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: a real git repository with a HEAD commit so git diff HEAD produces output.
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

            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"interior fix","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I edited a file before the build failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""",
                "I repaired the file, but the build still failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(1, "build failed")),
                runGit: CodeOnlyDiff("src/Written.cs"));

            var output = new StringWriter();

            // Act
            await AnnealTool.RunAsync(
                ["route", "fix something"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            // Assert: a JSON companion file exists next to the patch, and the output announces it.
            var snapshotsDir = Path.Combine(root, ".anneal", "logs", "snapshots");
            var jsonFiles = Directory.Exists(snapshotsDir)
                ? Directory.GetFiles(snapshotsDir, "interrupted-*.json")
                : [];

            Assert.NotEmpty(jsonFiles);
            var jsonContent = File.ReadAllText(jsonFiles[0]);
            Assert.Multiple(
                () => Assert.Contains("\"outcome\"", jsonContent, StringComparison.Ordinal),
                () => Assert.Contains("triage context written to", written, StringComparison.Ordinal));
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string PassedVerifierJson() =>
        """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""";

    private static RunGitCommand CodeOnlyDiff(params string[] files) =>
        (_, _) => Task.FromResult(new ScriptRun(0, BuildDiff(files)));

    private static string BuildDiff(IReadOnlyList<string> files) =>
        string.Join(
            "\n",
            files.Select(file =>
                $"diff --git a/{file} b/{file}\n--- a/{file}\n+++ b/{file}\n@@ -1 +1 @@\n-old\n+new"));

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-tk23-" + Guid.NewGuid().ToString("N")[..12]);
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
