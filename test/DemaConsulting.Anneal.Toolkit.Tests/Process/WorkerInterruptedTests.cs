using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for the <see cref="ChangeSetBeforeStopping" /> (Interrupted) field on
///     <see cref="WorkerExecutionResult" /> returned by <see cref="GeneralWorker" />: that the field is populated
///     when the worker stopped with real file changes on disk, and null when no changes were ever made.
/// </summary>
public class WorkerInterruptedTests
{
    [Fact]
    public async Task GeneralWorker_SmallBuildNeverPasses_InterruptedCarriesFilesFromLastDeveloperState()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["a.cs"],"summary":"partial attempt"}""");

            var worker = new GeneralWorker(
                root,
                Effort.Small,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                maxCodeRepairAttempts: 0,
                runArchDocAgreementGate: false,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(1, "still failing")),
                runGit: CodeOnlyDiff);

            var result = await worker.RunAsync(
                MakeBrief("fix the flaky test", Effort.Small, ["a.cs"]),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding),
                () => Assert.NotNull(result.Interrupted),
                () => Assert.Contains("a.cs", result.Interrupted!.FilesChanged),
                () => Assert.Equal("partial attempt", result.Interrupted!.Summary));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorker_MediumCodeRepairBudgetExhausted_InterruptedMergesDocumentationAndCode()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Code","fixText":"null check is missing"}],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new GeneralWorker(
                root,
                Effort.Medium,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                maxCodeRepairAttempts: 0,
                runArchDocAgreementGate: false,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")),
                runGit: ContractTouchingDiff);

            var result = await worker.RunAsync(
                MakeBrief(
                    "add a contract clause and implement the behavior",
                    Effort.Medium,
                    [".anneal/architecture/toolkit.md", "src/Foo.cs"]),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding),
                () => Assert.NotNull(result.Interrupted),
                () => Assert.Contains(".anneal/architecture/toolkit.md", result.Interrupted!.FilesChanged),
                () => Assert.Contains("src/Foo.cs", result.Interrupted!.FilesChanged));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorker_NoModelAvailable_InterruptedIsNull()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var worker = new GeneralWorker(
                root,
                Effort.Small,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                runArchDocAgreementGate: false,
                endpointFor: _ => endpoint);

            var result = await worker.RunAsync(
                MakeBrief("fix the flaky test", Effort.Small, []),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Interrupted));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkerBrief MakeBrief(string workItem, Effort effort, IReadOnlyList<string> changedFileHints) =>
        new("parent-1", workItem, effort, "general", [], [], "the route selected general", [], [], changedFileHints);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-interrupted-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), "");
        return root;
    }

    private static Task<ScriptRun> CodeOnlyDiff(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        _ = arguments;
        _ = cancellationToken;

        const string patch =
            """
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -1 +1 @@
            -class A {}
            +class A { int Value => 1; }
            """;

        return Task.FromResult(new ScriptRun(0, patch));
    }

    private static Task<ScriptRun> ContractTouchingDiff(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        _ = arguments;
        _ = cancellationToken;

        const string patch =
            """
            diff --git a/.anneal/architecture/toolkit.md b/.anneal/architecture/toolkit.md
            --- a/.anneal/architecture/toolkit.md
            +++ b/.anneal/architecture/toolkit.md
            @@ -1,5 +1,5 @@
             ## Contract
             
             ### Provides
             
            -- **TOOLKIT-01** - Accepts records.
            +- **TOOLKIT-01** - Accepts records and reports status.
            diff --git a/src/Foo.cs b/src/Foo.cs
            --- a/src/Foo.cs
            +++ b/src/Foo.cs
            @@ -1 +1 @@
            -class Foo {}
            +class Foo { int Status => 1; }
            """;

        return Task.FromResult(new ScriptRun(0, patch));
    }
}
