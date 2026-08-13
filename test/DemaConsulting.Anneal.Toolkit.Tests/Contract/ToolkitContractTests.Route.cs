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
///     Boundary tests for the route action, TOOLKIT-23.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{

    /// <summary>
    ///     TOOLKIT-23 — `route`, dispatched through the same command surface a caller has, drives a real
    ///     `Process.Router` over the production three-worker catalog and runs whichever compiled worker the
    ///     routing oracle selects, reporting the completed change as data.
    /// </summary>
    /// <remarks>
    ///     Driven through <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, IReadOnlyList{IOperation}, string, CancellationToken)" />
    ///     itself, exactly as every other action's own boundary test is, so what this proves is the registered
    ///     action reaching a real worker rather than <see cref="RouteOperation" /> in isolation - the latter is
    ///     already covered in depth by <c>RouteOperationTests</c>.
    /// </remarks>
    [Fact]
    public async Task RouteRunsTheSelectedCompiledWorker()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "build.ps1"), "");

            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"this is a small, interior fix","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"fixed the bug"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: (_, _) => Task.FromResult(new ScriptRun(0, "diff --git a/src/Foo.cs b/src/Foo.cs\n--- a/src/Foo.cs\n+++ b/src/Foo.cs\n@@ -1 +1 @@\n-old\n+new")));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "fix the off-by-one bug"], output, [operation], root, TestContext.Current.CancellationToken);
            var written = output.ToString();
            var routeOracleText = string.Join("\n", endpoint.Requests[0].Messages.Select(message => message.Text));

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("src/Foo.cs", written, StringComparison.Ordinal),
                () => Assert.DoesNotContain("unknown action", written, StringComparison.Ordinal),
                () => Assert.Contains("Work item: fix the off-by-one bug", routeOracleText, StringComparison.Ordinal),
                () => Assert.DoesNotContain("Keyword implication:", routeOracleText, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
