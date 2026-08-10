using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Providers;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the lint-fix action, TOOLKIT-19 and TOOLKIT-41.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{

    /// <summary>
    ///     TOOLKIT-19 — `lint-fix` drives the repository to a clean lint or reports why it could not: succeeding
    ///     when lint exits zero, escalating when a repair needs a protected file, and failing when its budget is
    ///     exhausted.
    /// </summary>
    /// <remarks>
    ///     The repository's scripts are substituted so all three state flows are reachable in a test, because the
    ///     alternative — rebuilding and re-linting a real repository per case — would exercise PowerShell rather
    ///     than the operation.
    /// </remarks>
    [Fact]
    public async Task LintFixDrivesTheRepositoryCleanOrReportsWhyNot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "present.md"), "a line to read");
            File.WriteAllText(Path.Combine(root, "fix.ps1"), "");
            File.WriteAllText(Path.Combine(root, "lint.ps1"), "");

            // Arrange: lint fails once and then passes, and a worker that edits ordinary files
            var attempts = 0;
            var repaired = await RunLintFix(
                root,
                (script, _) => Task.FromResult(
                    script == "lint.ps1" && attempts++ > 0
                        ? new ScriptRun(0, string.Empty)
                        : new ScriptRun(script == "lint.ps1" ? 1 : 0, "present.md:1 MD013 line too long")),
                new ScriptedEndpoint("I wrapped the line."));

            // Arrange: lint keeps reporting the same thing, and the only repair the worker attempts is refused
            var escalated = await RunLintFix(
                root,
                (script, _) => Task.FromResult(
                    new ScriptRun(script == "lint.ps1" ? 1 : 0, "build artifacts are being linted")),
                new ToolCallingEndpoint(
                    ("create_file", new Dictionary<string, object?>
                    {
                        ["path"] = ".cspell.yaml",
                        ["content"] = "words: []"
                    })));

            // Arrange: lint keeps failing and nothing is ever refused, so the budget runs out
            var exhausted = await RunLintFix(
                root,
                (script, _) => Task.FromResult(
                    new ScriptRun(script == "lint.ps1" ? 1 : 0, "present.md:1 MD013 line too long")),
                new ScriptedEndpoint("I could not fix that."));

            // Arrange: the same, except the worker's one refusal is a path outside the repository - its own
            // mistake to correct, and nothing the user has to decide
            var outOfBounds = await RunLintFix(
                root,
                (script, _) => Task.FromResult(
                    new ScriptRun(script == "lint.ps1" ? 1 : 0, "build artifacts are being linted")),
                new ToolCallingEndpoint(
                    ("read_file", new Dictionary<string, object?>
                    {
                        ["path"] = "../something",
                        ["start"] = 1,
                        ["max"] = 10
                    })));

            Assert.Multiple(
                // Clean lint is success.
                () => Assert.Equal(OperationOutcome.Succeeded, repaired.Result.Outcome),

                // A repair that needs a protected file is escalation, naming what was refused - not failure.
                () => Assert.Equal(OperationOutcome.Escalated, escalated.Result.Outcome),
                () => Assert.Contains(
                    ".cspell.yaml", escalated.Output, StringComparison.Ordinal),
                () => Assert.NotEmpty(escalated.Result.FindingAs<LintFixReport>()!.RefusedWrites),

                // An exhausted budget is failure, and it is bounded rather than open-ended.
                () => Assert.Equal(OperationOutcome.Failed, exhausted.Result.Outcome),
                () => Assert.Equal(
                    LintFixOperation.MaxIterations,
                    exhausted.Result.FindingAs<LintFixReport>()!.Iterations),
                () => Assert.Contains(
                    "MD013", exhausted.Result.FindingAs<LintFixReport>()!.RemainingOutput, StringComparison.Ordinal),

                // A refusal that was never about a protected file is not escalation: it exhausts the budget like
                // any other repair that did not work, because telling the user a protected file needs their
                // approval when none does would be false.
                () => Assert.Equal(OperationOutcome.Failed, outOfBounds.Result.Outcome),
                () => Assert.Equal(
                    LintFixOperation.MaxIterations,
                    outOfBounds.Result.FindingAs<LintFixReport>()!.Iterations),
                () => Assert.Empty(outOfBounds.Result.FindingAs<LintFixReport>()!.RefusedWrites));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-41 — a repository's own `.anneal/config.json` names the fix, build, and lint scripts, or a
    ///     name is defaulted from what exists on disk, and `lint-fix` adapts: no lint script configured
    ///     succeeds immediately with nothing to drive clean, and no fix script configured (but a lint script
    ///     present) skips straight to the repair loop rather than calling a script that was never named.
    /// </summary>
    [Fact]
    public async Task LintFixHonorsConfiguredScriptsAndSkipsAbsentOnes()
    {
        // Scenario 1: neither script exists on disk and neither is configured - nothing to drive clean.
        var neitherRoot = CreateTemporaryDirectory();
        try
        {
            var calls = new List<string>();
            var neither = await RunLintFix(
                neitherRoot,
                (script, _) =>
                {
                    calls.Add(script);
                    return Task.FromResult(new ScriptRun(0, ""));
                },
                new QueuedEndpoint());

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, neither.Result.Outcome),
                () => Assert.Empty(calls),
                () => Assert.Contains("no lint script", neither.Output, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(neitherRoot, recursive: true);
        }

        // Scenario 2: a configured lint script but no fix script (explicitly emptied) skips straight to the
        // repair loop, never calling a fix script, and still drives lint clean.
        var lintOnlyRoot = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(lintOnlyRoot, ".anneal"));
            File.WriteAllText(
                Path.Combine(lintOnlyRoot, ".anneal", "config.json"),
                """{"scripts":{"fix":"","lint":"tools/lint.sh"}}""");

            var fixCalled = false;
            var attempts = 0;
            var lintOnly = await RunLintFix(
                lintOnlyRoot,
                (script, _) =>
                {
                    if (script == "tools/lint.sh")
                        return Task.FromResult(
                            attempts++ > 0 ? new ScriptRun(0, "") : new ScriptRun(1, "present.md:1 MD013"));

                    fixCalled = true;
                    return Task.FromResult(new ScriptRun(0, ""));
                },
                new ScriptedEndpoint("I wrapped the line."));

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, lintOnly.Result.Outcome),
                () => Assert.False(fixCalled));
        }
        finally
        {
            Directory.Delete(lintOnlyRoot, recursive: true);
        }
    }
}
