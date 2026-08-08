using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary test for TOOLKIT-23's expanded promise: on an Escalated or Failed outcome, if the selected
///     worker had already written files before stopping, <c>route</c> reports those files and a summary
///     separately from the completion fields, and reports neither when nothing was interrupted.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> and assertions
///     are on the exit code and the written output. Nothing here reaches inside <see cref="RouteOperation" />
///     or any worker.
/// </remarks>
public class Toolkit23InterruptedRouteContractTests
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
            // Arrange: oracle selects small-fix; developer writes a file (Completed state); build check fails
            // on both the initial attempt and the one local repair SmallFixWorker's default budget allows, so
            // RepairLoop's own budget-exhaustion path returns Failed with the last Completed state, which
            // SmallFixWorker turns into Interrupted. Each Developer authoring turn consumes two replies (a
            // free-text turn, then the forced structured decision), so two full rounds need four replies.
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"interior fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","hasSufficientEvidence":true}""",
                "I edited a file before the build failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""",
                "I repaired the file, but the build still failed.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Written.cs"],"summary":"partial edit before stopping"}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                // Build fails on both attempts SmallFixWorker's default one-repair budget allows: RepairLoop
                // exhausts that budget and returns Failed with the last DevelopmentResult.Completed state,
                // which SmallFixWorker turns into Interrupted.
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(1, "build failed")));

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
            // Arrange: oracle selects small-fix; developer completes; build passes — normal success path.
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"simple fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","hasSufficientEvidence":true}""",
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"fixed it"}""");

            var operation = new RouteOperation(
                successRoot,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

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

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-tk23-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
