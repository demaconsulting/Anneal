using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary test for TOOLKIT-25: <c>route</c> classifies the routed work item's Effort — Small, Medium,
///     Large, or Massive — in the same pass that selects a worker, and reports the classified value alongside
///     whatever outcome the run reaches.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> and assertions
///     are on the exit code and the written output. Nothing here reaches inside <see cref="RouteOperation" />
///     or <c>Process.Router</c>.
/// </remarks>
public class Toolkit25EffortContractTests
{
    /// <summary>
    ///     TOOLKIT-25 — the route oracle's classified Effort surfaces on both a completed run and a run that
    ///     concludes no route exists, so a caller can observe what Effort was classified regardless of which
    ///     outcome the run reaches. Verified by <c>RouteReportsClassifiedEffort</c>.
    /// </summary>
    [Fact]
    public async Task RouteReportsClassifiedEffort()
    {
        // Scenario 1: a completed run reports the classified Effort alongside its success output.
        var completedRoot = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"interior fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Medium","hasSufficientEvidence":true}""",
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"fixed it"}""");

            var operation = new RouteOperation(
                completedRoot,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "fix the bug"],
                output,
                [operation],
                completedRoot,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("effort classified as Medium", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(completedRoot, recursive: true);
        }

        // Scenario 2: a run that reaches no route still reports the classified Effort, not just a completed one.
        var noRouteRoot = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"NoRoute","why":"nothing in the catalog fits","workerKey":"","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Massive","hasSufficientEvidence":true}""");

            var operation = new RouteOperation(noRouteRoot, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "do something entirely unclassifiable"],
                output,
                [operation],
                noRouteRoot,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("effort classified as Massive", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(noRouteRoot, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-tk25-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
