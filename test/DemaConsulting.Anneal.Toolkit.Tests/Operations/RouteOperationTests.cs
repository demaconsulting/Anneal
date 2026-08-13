using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Operations;

/// <summary>
///     Interior tests for <see cref="RouteOperation" />: mapping a real <c>Process.Router</c> run, over the
///     production single-worker catalog, back onto <see cref="OperationResult" />.
/// </summary>
/// <remarks>
///     Every model call this operation's whole run makes — the route oracle, any research pass, and every
///     primitive the selected worker composes — is driven through one shared <see cref="QueuedEndpoint" />, the
///     queue's own order is the whole of the test's arrangement.
/// </remarks>
public class RouteOperationTests
{
    [Fact]
    public async Task ExecuteAsync_RoutesToGeneralSmallAndCompletes_ReportsSucceededWithFilesChanged()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                SelectWorkerJson("general", "this is a small, interior fix", effort: "Small"),
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"fixed the bug"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: CodeOnlyDiff("src/Foo.cs"));

            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["fix the off-by-one bug"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(["src/Foo.cs"], result.FindingAs<RouteReport>()!.FilesChanged),
                () => Assert.Equal("fixed the bug", result.FindingAs<RouteReport>()!.Summary),
                () => Assert.Equal("Small", result.FindingAs<RouteReport>()!.Effort),
                () => Assert.Contains("src/Foo.cs", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RoutesToGeneralMediumAndCompletes_ReportsSucceededWithBothFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                SelectWorkerJson("general", "this adds a contract clause", effort: "Medium"),
                """{"scope":"Docs","conclusion":"Proceed"}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")),
                runGit: ProgressiveDiff(
                    [".anneal/architecture/toolkit.md"],
                    [".anneal/architecture/toolkit.md", "src/Foo.cs"]));

            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["add a new contract clause for the widget"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(
                    [".anneal/architecture/toolkit.md", "src/Foo.cs"], result.FindingAs<RouteReport>()!.FilesChanged),
                () => Assert.Equal("Medium", result.FindingAs<RouteReport>()!.Effort));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RoutesToGeneralLargeAndCompletes_ReportsSucceededWithAllFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Docs scope with 0 file hints + Large effort: Planner runs before DocumentAuthor.
            var endpoint = new QueuedEndpoint(
                SelectWorkerJson("general", "this moves a system boundary", effort: "Large"),
                """{"scope":"Docs","conclusion":"Proceed"}""",
                """{"kind":"Plan","why":"","planSummary":"split the boundary","planSteps":["update overview","update toolkit","implement code"]}""",
                "I updated the contract documents.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/overview.md",".anneal/architecture/toolkit.md"],"summary":"split the system"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")),
                runGit: ProgressiveDiff(
                    [".anneal/architecture/overview.md", ".anneal/architecture/toolkit.md"],
                    [".anneal/architecture/overview.md", ".anneal/architecture/toolkit.md", "src/Foo.cs"]));

            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["structural change: split the system and move a system boundary"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(
                    [".anneal/architecture/overview.md", ".anneal/architecture/toolkit.md", "src/Foo.cs"],
                    result.FindingAs<RouteReport>()!.FilesChanged),
                () => Assert.Equal("Large", result.FindingAs<RouteReport>()!.Effort));
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

    private static string SelectWorkerJson(string workerKey, string why, string effort = "Small") =>
        $$"""
          {"kind":"SelectWorker","why":"{{why}}","workerKey":"{{workerKey}}","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"{{effort}}","hasSufficientEvidence":true}
          """;

    private static RunGitCommand CodeOnlyDiff(params string[] files) =>
        (_, _) => Task.FromResult(new ScriptRun(0, BuildDiff(files)));

    private static RunGitCommand ProgressiveDiff(
        IReadOnlyList<string> firstDiffFiles,
        IReadOnlyList<string> laterDiffFiles)
    {
        var diffCallCount = 0;
        return (_, _) =>
        {
            var files = diffCallCount++ == 0 ? firstDiffFiles : laterDiffFiles;
            return Task.FromResult(new ScriptRun(0, BuildDiff(files)));
        };
    }

    private static string BuildDiff(IReadOnlyList<string> files) =>
        string.Join(
            "\n",
            files.Select(file =>
            {
                var body = file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && file.Contains(".anneal")
                    ? "@@ -1,5 +1,5 @@\n ## Contract\n \n ### Provides\n \n-- old\n++ new"
                    : "@@ -1 +1 @@\n-old\n+new";
                return $"diff --git a/{file} b/{file}\n--- a/{file}\n+++ b/{file}\n{body}";
            }));

    private static string NoRouteJson(string why, string humanOnlyNextStep, string effort = "Small") =>
        $$"""
          {"kind":"NoRoute","why":"{{why}}","workerKey":"","question":"","researchScope":"Narrow","humanOnlyNextStep":"{{humanOnlyNextStep}}","effort":"{{effort}}","hasSufficientEvidence":true}
          """;

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-route-op-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), "");
        return root;
    }
}
