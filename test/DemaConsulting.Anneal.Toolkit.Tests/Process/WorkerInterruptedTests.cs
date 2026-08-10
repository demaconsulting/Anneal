using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for the <see cref="ChangeSetBeforeStopping" /> (Interrupted) field on
///     <see cref="WorkerExecutionResult" /> returned by each compiled worker: that the field is populated when a
///     worker stopped with real file changes on disk, and null when no changes were ever made.
/// </summary>
public class WorkerInterruptedTests
{
    [Fact]
    public async Task SmallFixWorker_BuildNeverPasses_InterruptedCarriesFilesFromLastDeveloperState()
    {
        // Arrange: Developer produced a Completed state (files on disk) but the build check failed with zero
        // repair budget — Interrupted must carry those files so the caller can see what is already on disk.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "partial attempt"}""");

            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken) =>
                Task.FromResult(new ScriptRun(1, "still failing"));

            var worker = new SmallFixWorker(root, "a charter", maxRepairAttempts: 0, endpointFor: _ => endpoint, runScript: BuildCheck);

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
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
    public async Task SmallFixWorker_NoModelAvailable_InterruptedIsNull()
    {
        // Arrange: no model means Developer never produced any state — Interrupted must be null.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var worker = new SmallFixWorker(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Interrupted));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ContractChangeWorker_CodeRepairBudgetExhausted_InterruptedMergesDocumentationAndCode()
    {
        // Arrange: code-repair budget is spent; documentation and code state exist in scope — Interrupted must
        // carry the merged file list.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Code","fixText":"null check is missing"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I tried to fix it.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"tried again"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Code","fixText":"still missing"}],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            // Act
            var result = await worker.RunAsync(MakeContractBrief(), TestContext.Current.CancellationToken);

            // Assert: failed, and Interrupted merges documentation and code files already on disk
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
    public async Task ContractChangeWorker_NoModelAvailable_InterruptedIsNull()
    {
        // Arrange: no model available — no authoring state was ever reached.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var worker = new ContractChangeWorker(
                root, "document charter", "developer charter", "verifier charter", endpointFor: _ => endpoint);

            // Act
            var result = await worker.RunAsync(MakeContractBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Interrupted));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StructuralChangeWorker_CodeRepairBudgetExhausted_InterruptedMergesDocumentationAndCode()
    {
        // Arrange: Planner uses DirectExecutionIsBetter; then code-repair budget is spent — Interrupted must
        // carry the merged documentation and code file list.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"DirectExecutionIsBetter","why":"simple enough","planSummary":"","planSteps":[]}""",
                "I updated the docs.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated docs"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Code","fixText":"null check missing"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I tried to fix it.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"tried again"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Code","fixText":"still missing"}],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new StructuralChangeWorker(
                root,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            // Act
            var result = await worker.RunAsync(MakeStructuralBrief(), TestContext.Current.CancellationToken);

            // Assert: failed, and Interrupted merges documentation and code files already on disk
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
    public async Task StructuralChangeWorker_NoModelAvailable_InterruptedIsNull()
    {
        // Arrange: no model available — no authoring state was ever reached.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var worker = new StructuralChangeWorker(
                root,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint);

            // Act
            var result = await worker.RunAsync(MakeStructuralBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Interrupted));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkerBrief MakeBrief() =>
        new("parent-1", "fix the flaky test", "small fix", [], [], "this looks small", [], []);

    private static WorkerBrief MakeContractBrief() =>
        new("parent-1", "add a contract clause", "contract change", [], [], "this touches a contract", [], []);

    private static WorkerBrief MakeStructuralBrief() =>
        new("parent-1", "move a system boundary", "structural change", [], [], "this moves a boundary", [], []);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-interrupted-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
