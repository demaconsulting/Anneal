using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-29, TOOLKIT-30, and TOOLKIT-31: how <c>maintain</c> runs a declared Maintenance
///     work item directly against <c>GeneralWorker</c> with no routing oracle, and mechanically enforces the
///     declared file-scope bound and the protected-path prohibition against the worker's actual output.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> and assertions
///     are on the exit code and the written output. Nothing here reaches inside <see cref="MaintainOperation" />
///     or <c>Process.GeneralWorker</c>.
/// </remarks>
public class MaintainContractTests
{
    /// <summary>
    ///     TOOLKIT-29 — <c>maintain</c> takes a Maintenance work item and a declared file-scope bound and runs it
    ///     directly against <c>GeneralWorker</c> at Small effort, asking no routing oracle: only the worker's own
    ///     replies are queued, so if <c>maintain</c> asked a route oracle first, the endpoint would report itself
    ///     unavailable and the run would fail for the wrong reason instead of completing. Naming no file-scope
    ///     entry is a usage error. Verified by <c>MaintainRunsDeclaredBoundDirectlyThroughGeneralWorker</c>.
    /// </summary>
    [Fact]
    public async Task MaintainRunsDeclaredBoundDirectlyThroughGeneralWorker()
    {
        // Scenario 1: a declared work item and bound runs directly against Small-effort GeneralWorker and completes, with
        // no routing-oracle reply queued at all.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I made the change.",
                CompletedJson(["src/a.cs"], "tidied the interior helper"),
                PassedVerifierJson());

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: CodeOnlyDiff("src/a.cs"));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "tidy the interior helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("maintain: completed", written, StringComparison.Ordinal),
                () => Assert.Contains("src/a.cs", written, StringComparison.Ordinal),
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        // Scenario 2: naming no file-scope entry is a usage error - unbounded Maintenance work has no bound to
        // declare, and no model call is ever made.
        var usageRoot = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var operation = new MaintainOperation(usageRoot, endpointFor: _ => endpoint);

            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "tidy something"],
                new StringWriter(),
                [operation],
                usageRoot,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitUsageError, exitCode),
                () => Assert.Equal(0, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(usageRoot, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-30 — after the worker's run, <c>maintain</c> checks the actual files it reports having changed
    ///     against the declared file-scope bound by containment: a changed file the bound did not cover forces
    ///     escalation, naming the offending file, rather than being reported as an unqualified success — even
    ///     though the worker itself completed the work. Verified by
    ///     <c>MaintainEscalatesWhenActualChangesExceedTheDeclaredBound</c>.
    /// </summary>
    [Fact]
    public async Task MaintainEscalatesWhenActualChangesExceedTheDeclaredBound()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // The worker reports it changed a file outside the declared bound (only "src/a.cs" was declared).
            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I made the change.",
                CompletedJson(["src/a.cs", "src/out-of-bounds.cs"], "tidied more than declared"),
                PassedVerifierJson());

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: CodeOnlyDiff("src/a.cs", "src/out-of-bounds.cs"));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "tidy the interior helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
                () => Assert.Contains("maintain: escalated", written, StringComparison.Ordinal),
                () => Assert.Contains("src/out-of-bounds.cs", written, StringComparison.Ordinal),
                () => Assert.Contains("falls outside the declared bound", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-31 — after the worker's run, <c>maintain</c> runs <c>ProtectedPathTripwire</c> against the
    ///     worker's actual changed-file list and forces escalation whenever it trips, naming the tripped path,
    ///     regardless of what the containment check (TOOLKIT-30) concludes for the same run: the declared bound
    ///     here explicitly includes the protected file, so containment alone would pass, and only the tripwire
    ///     fires. Verified by <c>MaintainEscalatesWhenActualChangesTripTheProtectedPathCheck</c>.
    /// </summary>
    [Fact]
    public async Task MaintainEscalatesWhenActualChangesTripTheProtectedPathCheck()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // The worker reports it changed .anneal/work/backlog.md - a protected path per change-classification.md's
            // "Maintenance may never edit ... .anneal/work/backlog.md" rule - and the declared bound explicitly names it too,
            // so the containment check (TOOLKIT-30) alone would clear this run; only the tripwire must fire.
            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I made the change.",
                CompletedJson(["src/a.cs", ".anneal/work/backlog.md"], "tidied and updated the backlog"),
                PassedVerifierJson());

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: CodeOnlyDiff("src/a.cs", ".anneal/work/backlog.md"));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "tidy the interior helper", "src/a.cs", ".anneal/work/backlog.md"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
                () => Assert.Contains("maintain: escalated", written, StringComparison.Ordinal),
                () => Assert.Contains(".anneal/work/backlog.md", written, StringComparison.Ordinal),
                () => Assert.Contains("protected path", written, StringComparison.Ordinal));
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
        var root = Path.Combine(Path.GetTempPath(), "anneal-tk293031-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), "");
        return root;
    }
}
