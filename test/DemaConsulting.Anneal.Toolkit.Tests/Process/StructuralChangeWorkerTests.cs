using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for <see cref="StructuralChangeWorker" />'s own composition: a single-shot
///     <see cref="Planner" /> → <see cref="DocumentAuthor" /> → <see cref="Developer" /> → two
///     <see cref="DeterministicCheck" /> steps → a model-backed <see cref="Verifier" />, then ownership-directed
///     repair against three independent one-shot budgets - documentation, code, and re-plan.
/// </summary>
public class StructuralChangeWorkerTests
{
    [Fact]
    public async Task RunAsync_PlanThenEverythingPassesFirstTry_CompletesWithoutRepairing()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["update overview.md","update the system doc"]}""",
                "I updated the contract documents.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md","docs/architecture/toolkit.md"],"summary":"split the system"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","requiredFixes":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var buildCalls = 0;
            var contractCalls = 0;
            var worker = new StructuralChangeWorker(
                root,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) =>
                {
                    buildCalls++;
                    return Task.FromResult(new ScriptRun(0, "all good"));
                },
                contractCheckRunScript: (_, _) =>
                {
                    contractCalls++;
                    return Task.FromResult(new ScriptRun(0, "43/43"));
                });

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
                () => Assert.Equal(
                    ["docs/architecture/overview.md", "docs/architecture/toolkit.md", "src/Foo.cs"],
                    ((WorkerRunResult.Completed)result.Finding!).Summary.FilesChanged),
                () => Assert.Equal(1, buildCalls),
                () => Assert.Equal(1, contractCalls),
                () => Assert.Equal(6, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ComposesInstructions_SplitsStandardsAcrossPlannerDocumentAuthorAndDeveloper()
    {
        // Arrange: S12 - Planner gets change-classification.md (it's the one place this worker decides scope/plan
        // shape), DocumentAuthor gets the documentation standards, Developer gets the code/testing standards.
        var root = CreateTemporaryDirectory();
        try
        {
            WriteStandard(root, "change-classification.md", "MARKER-CLASSIFICATION");
            WriteStandard(root, "architecture-documentation.md", "MARKER-ARCH-DOC");
            WriteStandard(root, "system-contracts.md", "MARKER-SYSTEM-CONTRACTS");
            WriteStandard(root, "coding-principles.md", "MARKER-CODING-PRINCIPLE");
            WriteStandard(root, "csharp-language.md", "MARKER-CSHARP-LANGUAGE");
            WriteStandard(root, "testing-principles.md", "MARKER-TESTING-PRINCIPLE");
            WriteStandard(root, "csharp-testing.md", "MARKER-CSHARP-TESTING");

            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["update overview.md","update the system doc"]}""",
                "I updated the contract documents.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md","docs/architecture/toolkit.md"],"summary":"split the system"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","requiredFixes":[],"advisoryNotes":[],"evidenceSufficient":true}""");

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
            await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            var plannerText = string.Join("\n", endpoint.Requests[0].Messages.Select(m => m.Text));
            var documentAuthorText = string.Join("\n", endpoint.Requests[1].Messages.Select(m => m.Text));
            var developerText = string.Join("\n", endpoint.Requests[3].Messages.Select(m => m.Text));
            Assert.Multiple(
                () => Assert.Contains("MARKER-CLASSIFICATION", plannerText),
                () => Assert.DoesNotContain("MARKER-ARCH-DOC", plannerText),
                () => Assert.Contains("MARKER-ARCH-DOC", documentAuthorText),
                () => Assert.Contains("MARKER-SYSTEM-CONTRACTS", documentAuthorText),
                () => Assert.DoesNotContain("MARKER-CODING-PRINCIPLE", documentAuthorText),
                () => Assert.Contains("MARKER-CODING-PRINCIPLE", developerText),
                () => Assert.Contains("MARKER-CSHARP-LANGUAGE", developerText),
                () => Assert.Contains("MARKER-TESTING-PRINCIPLE", developerText),
                () => Assert.Contains("MARKER-CSHARP-TESTING", developerText),
                () => Assert.DoesNotContain("MARKER-ARCH-DOC", developerText));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_StandardFileMissing_DegradesGracefullyWithoutThrowing()
    {
        // Arrange: S12 - a repository that has not installed a given standard must not fail the worker.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["update overview.md","update the system doc"]}""",
                "I updated the contract documents.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md","docs/architecture/toolkit.md"],"summary":"split the system"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","requiredFixes":[],"advisoryNotes":[],"evidenceSufficient":true}""");

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
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_PlanWithTenStepsUnderRaisedDefaultBudget_StillCompletes()
    {
        // Arrange: S11's live routing trials found a genuinely-scoped structural change (one new system
        // plus its two existing neighbors) produces a real model plan of exactly 10 steps, which the
        // un-widened Planner default of 8 refused outright with no repair path. This regression-tests the
        // fix: StructuralChangeWorker's own default (12) must comfortably admit a 10-step plan.
        var root = CreateTemporaryDirectory();
        try
        {
            var tenStepPlan =
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["step 1","step 2","step 3","step 4","step 5","step 6","step 7","step 8","step 9","step 10"]}""";

            var endpoint = new QueuedEndpoint(
                tenStepPlan,
                "I updated the contract documents.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md"],"summary":"split the system"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","requiredFixes":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new StructuralChangeWorker(
                root, "planner charter", "document charter", "developer charter", "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_MaxPlanStepsOverride_StillFailsClosedWhenPlanExceedsIt()
    {
        // Arrange: the raised default must still act as a budget - an explicit, smaller override proves a
        // plan exceeding it is still refused and the worker still fails closed, exactly as
        // documentAuthorTargetFileCountBudget's own budget already does for DocumentAuthor.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["step 1","step 2","step 3","step 4"]}""");

            var worker = new StructuralChangeWorker(
                root, "planner charter", "document charter", "developer charter", "verifier charter",
                maxPlanSteps: 3,
                endpointFor: _ => endpoint);

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_PlannerReroutes_ReturnsRerouteWithoutRunningAnyOtherPrimitive()
    {
        // Arrange: the planner itself concludes this work does not belong to a structural worker
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Reroute","why":"this is a narrow interior fix, not a structural change","planSummary":"","planSteps":[]}""");

            var checkCalls = 0;
            var worker = new StructuralChangeWorker(
                root,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) =>
                {
                    checkCalls++;
                    return Task.FromResult(new ScriptRun(0, "all good"));
                });

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: no DocumentAuthor, Developer, or check ran at all
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Reroute>(result.Finding),
                () => Assert.Equal(0, checkCalls),
                () => Assert.Equal(1, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_PlannerPrefersDirectExecution_ProceedsWithoutConsumingAPlan()
    {
        // Arrange: DirectExecutionIsBetter still proceeds through DocumentAuthor -> Developer -> checks -> Verifier
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"DirectExecutionIsBetter","why":"this is small enough to just do","planSummary":"","planSteps":[]}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","requiredFixes":[],"advisoryNotes":[],"evidenceSufficient":true}""");

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
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
                () => Assert.Equal(6, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DocumentationRepairRequiredOnce_RepairsThenPasses_LeavingCodeAndReplanBudgetsUntouched()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["update the docs"]}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/toolkit.md"],"summary":"first draft"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"DocumentationRepairRequired","requiredFixes":["clause wording is ambiguous"],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I fixed the wording.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/toolkit.md"],"summary":"clarified wording"}""",
                "I re-synced the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"re-synced"}""",
                """{"verdict":"Passed","requiredFixes":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var recordStore = new RecordStore(root);
            var worker = new StructuralChangeWorker(
                root,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")),
                recordStore: recordStore);

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            var steps = File.ReadAllLines(RecordStore.ProcessStepsPathFor(root));
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
                () => Assert.Contains(steps, line => line.Contains("\"DocumentAuthor:repair\"")),
                () => Assert.Contains(steps, line => line.Contains("\"Developer:resync\"")),
                () => Assert.DoesNotContain(steps, line => line.Contains("\"Developer:repair\"")),
                () => Assert.DoesNotContain(steps, line => line.Contains("\"Planner:replan\"")),
                () => Assert.Equal(1, steps.Count(line => line.Contains("\"Planner\""))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_StrategyRevisionRequiredOnce_ReplansOnceThenRerunsWholeBodyAndPasses()
    {
        // Arrange: one StrategyRevisionRequired verdict spends the re-plan budget, which triggers a second
        // Planner.PlanAsync call informed by the verifier's finding, then restarts DocumentAuthor -> Developer ->
        // checks -> Verifier from the top, with the documentation/code repair budgets left untouched
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["step one"]}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md"],"summary":"first draft"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"StrategyRevisionRequired","requiredFixes":["the wrong system was split"],"advisoryNotes":[],"evidenceSufficient":true}""",
                """{"kind":"Plan","why":"","planSummary":"split the correct system","planSteps":["step one revised"]}""",
                "I updated the contract document again.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md"],"summary":"revised draft"}""",
                "I implemented the change again.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"revised attempt"}""",
                """{"verdict":"Passed","requiredFixes":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var recordStore = new RecordStore(root);
            var worker = new StructuralChangeWorker(
                root,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")),
                recordStore: recordStore);

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            var steps = File.ReadAllLines(RecordStore.ProcessStepsPathFor(root));
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
                () => Assert.Equal(12, endpoint.Calls),
                () => Assert.Equal(1, steps.Count(line => line.Contains("\"Planner\""))),
                () => Assert.Equal(1, steps.Count(line => line.Contains("\"Planner:replan\""))),
                () => Assert.DoesNotContain(steps, line => line.Contains("\"DocumentAuthor:repair\"")),
                () => Assert.DoesNotContain(steps, line => line.Contains("\"Developer:repair\"")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_StrategyRevisionRequiredTwice_FailsOnceTheReplanBudgetIsSpent()
    {
        // Arrange: the re-plan budget (default 1) is spent on the first StrategyRevisionRequired verdict, and a
        // second one must not attempt a third Planner.PlanAsync call
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["step one"]}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md"],"summary":"first draft"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"StrategyRevisionRequired","requiredFixes":["the wrong system was split"],"advisoryNotes":[],"evidenceSufficient":true}""",
                """{"kind":"Plan","why":"","planSummary":"split a different system","planSteps":["step one revised"]}""",
                "I updated the contract document again.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/overview.md"],"summary":"revised draft"}""",
                "I implemented the change again.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"revised attempt"}""",
                """{"verdict":"StrategyRevisionRequired","requiredFixes":["still the wrong system"],"advisoryNotes":[],"evidenceSufficient":true}""");

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
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: failed, and no thirteenth (third-planner) call was ever made
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding),
                () => Assert.Equal(12, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DocumentationRepairRequiredTwice_FailsOnceItsBudgetIsSpent()
    {
        // Arrange: the documentation budget (default 1) is spent on the first finding, and a second
        // documentation-repair verdict must not repair again
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["step one"]}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/toolkit.md"],"summary":"first draft"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"DocumentationRepairRequired","requiredFixes":["clause wording is ambiguous"],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I tried to fix the wording.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/toolkit.md"],"summary":"tried again"}""",
                "I re-synced the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"re-synced"}""",
                """{"verdict":"DocumentationRepairRequired","requiredFixes":["still ambiguous"],"advisoryNotes":[],"evidenceSufficient":true}""");

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
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: failed, not rerouted and not re-planned - a spent repair budget is not a strategy finding
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_VerifierEscalatesRerouteRequired_Reroutes()
    {
        // Arrange: the verifier concludes this change does not belong to a structural worker
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["step one"]}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"RerouteRequired","requiredFixes":["this is actually a narrow interior fix"],"advisoryNotes":[],"evidenceSufficient":true}""");

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
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Reroute>(result.Finding),
                () => Assert.Contains(
                    "narrow interior fix", ((WorkerRunResult.Reroute)result.Finding!).Why));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_VerifierEvidenceInsufficient_Fails()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["step one"]}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":["docs/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"Passed","requiredFixes":[],"advisoryNotes":[],"evidenceSufficient":false}""");

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
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NoModelAvailable_Fails()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var worker = new StructuralChangeWorker(
                root, "planner charter", "document charter", "developer charter", "verifier charter",
                endpointFor: _ => endpoint);

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkerBrief MakeBrief() =>
        new("parent-1", "split this system into two", "structural change", [], [], "this moves a system boundary", []);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-structural-change-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteStandard(string root, string fileName, string content)
    {
        var directory = Path.Combine(root, ".github", "standards");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }
}
