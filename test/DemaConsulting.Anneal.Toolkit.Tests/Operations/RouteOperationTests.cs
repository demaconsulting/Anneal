using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Operations;

/// <summary>
///     Interior tests for <see cref="RouteOperation" />: mapping a real <c>Process.Router</c> run, over the
///     production three-worker catalog, back onto <see cref="OperationResult" />.
/// </summary>
/// <remarks>
///     Every model call this operation's whole run makes — the route oracle, any research pass, and every
///     primitive the selected worker composes — is driven through one shared <see cref="QueuedEndpoint" />, the
///     same pattern <c>ContractChangeWorkerTests</c> and <c>StructuralChangeWorkerTests</c> already use for their
///     own single-endpoint fixtures: role is ignored and replies are consumed strictly in call order, so the
///     queue's own order is the whole of the test's arrangement.
/// </remarks>
public class RouteOperationTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesToSmallFixAndCompletes_ReportsSucceededWithFilesChanged()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                // Route oracle: a single schema-last probe, no free-form pass.
                SelectWorkerJson("small-fix", "this is a small, interior fix"),
                // Developer: free-form pass, then the schema.
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"fixed the bug"}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["fix the off-by-one bug"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(["src/Foo.cs"], result.FindingAs<RouteReport>()!.FilesChanged),
                () => Assert.Equal("fixed the bug", result.FindingAs<RouteReport>()!.Summary),
                () => Assert.Contains("src/Foo.cs", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RoutesToContractChangeAndCompletes_ReportsSucceededWithBothFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                SelectWorkerJson("contract-change", "this adds a contract clause"),
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["add a new contract clause for the widget"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(
                    ["docs/architecture/toolkit.md", "src/Foo.cs"], result.FindingAs<RouteReport>()!.FilesChanged));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RoutesToStructuralChangeAndCompletes_ReportsSucceededWithAllFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                SelectWorkerJson("structural-change", "this moves a system boundary"),
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["update overview.md","update the system doc"]}""",
                "I updated the contract documents.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md","docs/architecture/toolkit.md"],"summary":"split the system"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["split this system into two"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(
                    ["docs/architecture/overview.md", "docs/architecture/toolkit.md", "src/Foo.cs"],
                    result.FindingAs<RouteReport>()!.FilesChanged));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RouteOracleCannotClassify_ReportsFailedWithWhatWasTried()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(NoRouteJson("nothing in the catalog fits", humanOnlyNextStep: ""));

            var operation = new RouteOperation(root, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["do something entirely unclassifiable"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.NotEmpty(result.FindingAs<RouteReport>()!.WhatWasTried),
                () => Assert.Contains("failed", output.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RouteOracleNamesAHumanOnlyNextStep_ReportsEscalated()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                NoRouteJson("this needs an approved migration stage", humanOnlyNextStep: "this is a Migration proposal"));

            var operation = new RouteOperation(root, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["propose a migration stage"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Escalated, result.Outcome),
                () => Assert.Equal(
                    "this is a Migration proposal", result.FindingAs<RouteReport>()!.RecommendedNextStep),
                () => Assert.Contains("escalated", output.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoWorkItemGiven_ReportsUsageError()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var operation = new RouteOperation(root);

            var result = await operation.ExecuteAsync([], TextWriter.Null, TestContext.Current.CancellationToken);

            Assert.Equal(OperationOutcome.UsageError, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BlankWorkItemGiven_ReportsUsageError()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var operation = new RouteOperation(root);

            var result = await operation.ExecuteAsync(
                ["   "], TextWriter.Null, TestContext.Current.CancellationToken);

            Assert.Equal(OperationOutcome.UsageError, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string SelectWorkerJson(string workerKey, string why) =>
        $$"""
          {"kind":"SelectWorker","why":"{{why}}","workerKey":"{{workerKey}}","question":"","researchScope":"Narrow","humanOnlyNextStep":"","hasSufficientEvidence":true}
          """;

    private static string NoRouteJson(string why, string humanOnlyNextStep) =>
        $$"""
          {"kind":"NoRoute","why":"{{why}}","workerKey":"","question":"","researchScope":"Narrow","humanOnlyNextStep":"{{humanOnlyNextStep}}","hasSufficientEvidence":true}
          """;

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-route-op-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
