using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for <see cref="ContractChangeWorker" />'s own composition: <see cref="DocumentAuthor" /> →
///     <see cref="Developer" /> → two <see cref="DeterministicCheck" /> steps → a model-backed
///     <see cref="Verifier" />, then ownership-directed repair against two independent one-shot budgets.
/// </summary>
public class ContractChangeWorkerTests
{
    [Fact]
    public async Task RunAsync_EverythingPassesFirstTry_CompletesWithoutRepairing()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var buildCalls = 0;
            var contractCalls = 0;
            var worker = new ContractChangeWorker(
                root,
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
                    [".anneal/architecture/toolkit.md", "src/Foo.cs"],
                    ((WorkerRunResult.Completed)result.Finding!).Summary.FilesChanged),
                () => Assert.Equal(1, buildCalls),
                () => Assert.Equal(1, contractCalls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ComposesInstructions_SplitsStandardsBetweenDocumentAuthorAndDeveloper()
    {
        // Arrange: S12 - DocumentAuthor gets the documentation standards, Developer gets the code/testing
        // standards, matching this worker's own documentation/code split.
        var root = CreateTemporaryDirectory();
        try
        {
            WriteStandard(root, "architecture-documentation.md", "MARKER-ARCH-DOC");
            WriteStandard(root, "system-contracts.md", "MARKER-SYSTEM-CONTRACTS");
            WriteStandard(root, "coding-principles.md", "MARKER-CODING-PRINCIPLE");
            WriteStandard(root, "csharp-language.md", "MARKER-CSHARP-LANGUAGE");
            WriteStandard(root, "testing-principles.md", "MARKER-TESTING-PRINCIPLE");
            WriteStandard(root, "csharp-testing.md", "MARKER-CSHARP-TESTING");

            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            // Act
            await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            var documentAuthorText = string.Join("\n", endpoint.Requests[0].Messages.Select(m => m.Text));
            var developerText = string.Join("\n", endpoint.Requests[2].Messages.Select(m => m.Text));
            Assert.Multiple(
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
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented it"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
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
    public async Task RunAsync_DocumentAuthorReroutes_ReturnsRerouteWithoutRunningDeveloperOrChecks()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "This belongs elsewhere.",
                """{"kind":"Reroute","why":"this is a structural boundary move","filesChanged":[],"summary":""}""");

            var checkCalls = 0;
            var worker = new ContractChangeWorker(
                root,
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

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Reroute>(result.Finding),
                () => Assert.Equal(0, checkCalls),
                () => Assert.Equal(2, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DeveloperReroutes_ReturnsRerouteWithoutRunningChecks()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "This belongs elsewhere.",
                """{"kind":"Reroute","why":"needs a structural change","suggestedWorker":"structural-change","filesChanged":[],"summary":""}""");

            var checkCalls = 0;
            var worker = new ContractChangeWorker(
                root,
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

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Reroute>(result.Finding),
                () => Assert.Equal(
                    "structural-change", ((WorkerRunResult.Reroute)result.Finding!).SuggestedWorker),
                () => Assert.Equal(0, checkCalls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DeterministicEvidenceFails_RepairsCodeWithNoModelCallToVerify()
    {
        // Arrange: build.ps1 fails first pass, so the verifier decides CodeRepairRequired deterministically -
        // no model is consulted for that first verification pass at all
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                "I repaired the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"repaired"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var buildCalls = 0;
            var worker = new ContractChangeWorker(
                root,
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) =>
                {
                    buildCalls++;
                    return Task.FromResult(buildCalls == 1 ? new ScriptRun(1, "a test failed") : new ScriptRun(0, "all good"));
                },
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: the repair was spent against the code budget, and completed on the second try
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
                () => Assert.Equal(2, buildCalls),
                () => Assert.Equal(7, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_DocumentationRepairRequired_RepairsDocumentAndResyncsCodeThenPasses()
    {
        // Arrange: the verifier asks for one documentation repair, which this worker always follows with an
        // unconditional Developer re-run to stay in sync, spent from the documentation budget alone
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"first draft"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Documentation","fixText":"clause wording is ambiguous"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I fixed the wording.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"clarified wording"}""",
                "I re-synced the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"re-synced"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var recordStore = new RecordStore(root);
            var worker = new ContractChangeWorker(
                root,
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
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
                () => Assert.Contains(
                    "clarified wording", ((WorkerRunResult.Completed)result.Finding!).Summary.Summary));

            var steps = File.ReadAllLines(RecordStore.ProcessStepsPathFor(root));
            Assert.Contains(steps, line => line.Contains("\"DocumentAuthor:repair\""));
            Assert.Contains(steps, line => line.Contains("\"Developer:resync\""));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CodeRepairRequired_RepairsCodeAloneThenPasses()
    {
        // Arrange: the verifier asks for a code repair only - no documentation pass is re-run
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Code","fixText":"null check is missing"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I fixed the null check.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"repaired"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
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
                () => Assert.Equal(8, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_BothRepairsRequired_RepairsDocumentationFirstThenCode()
    {
        // Arrange: both are needed at once; documentation repairs first (spending only its own budget), the
        // resync-then-reverify then finds a remaining code-only issue, which spends the independent code budget
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"first draft"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Documentation","fixText":"clause wording is ambiguous"},{"owner":"Code","fixText":"null check is missing"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I fixed the wording.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"clarified wording"}""",
                "I re-synced the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"re-synced"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Code","fixText":"null check is missing"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I fixed the null check.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"repaired"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
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
                () => Assert.Equal(13, endpoint.Calls));
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
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"first draft"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Documentation","fixText":"clause wording is ambiguous"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I tried to fix the wording.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"tried again"}""",
                "I re-synced the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"re-synced"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Documentation","fixText":"still ambiguous"}],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: failed, not rerouted - a spent budget is not evidence of misclassification
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
    public async Task RunAsync_CodeRepairRequiredTwice_FailsOnceItsBudgetIsSpent()
    {
        // Arrange: the code budget (default 1) is spent on the first finding, and a second code-repair verdict
        // must not repair again
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
    public async Task RunAsync_TenetRepairRequired_RepairsThroughDeveloperThenPasses()
    {
        // Arrange: the verifier asks for a tenet repair only - a tenet finding also routes through Developer,
        // the same primitive a code finding uses, since fixing a tenet violation is still a code-shaped fix
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Tenet","fixText":"src/Foo.cs reaches across a system boundary .anneal/work/constraints.md forbids"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I removed the boundary crossing.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"repaired"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
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
                () => Assert.Equal(8, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_TenetRepairRequiredTwice_FailsNamingTheTenetConcernOnceItsBudgetIsSpent()
    {
        // Arrange: the tenet budget (default 1) is spent on the first finding, and a second tenet-repair verdict
        // must not repair again - the failure names the tenet-repair budget specifically
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Tenet","fixText":"crosses a forbidden boundary"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I tried to fix it.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"tried again"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Tenet","fixText":"still crosses the boundary"}],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")));

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: failed, not rerouted - a spent budget is not evidence of misclassification, and the
            // failure note names the tenet-repair budget specifically
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding),
                () => Assert.Contains(
                    result.Notes, note => note.Text.Contains("tenet-repair budget", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CodeAndTenetRepairsRequired_DispatchesBothAgainstIndependentBudgets()
    {
        // Arrange: both a code and a tenet concern arrive together; code repairs first (tenet is checked after
        // documentation and code in this worker's fixed dispatch order), then the next full re-verify finds the
        // remaining tenet-only issue, which spends the independent tenet budget
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"first attempt"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Code","fixText":"null check is missing"},{"owner":"Tenet","fixText":"crosses a forbidden boundary"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I fixed the null check.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"repaired code"}""",
                """{"verdict":"RepairRequired","concerns":[{"owner":"Tenet","fixText":"crosses a forbidden boundary"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                "I removed the boundary crossing.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"repaired tenet"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
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
                () => Assert.Equal(11, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_VerifierConcludesScopeMisclassification_Reroutes()
    {
        // Arrange: reroute trigger 1 - the verifier concludes this should have been Structural Change
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"RerouteRequired","concerns":[],"advisoryNotes":["this actually moves a system boundary; it needed Structural Change"],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
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
                    "Structural Change", ((WorkerRunResult.Reroute)result.Finding!).Why));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_VerifierFindsReadmeAssumptionContradiction_Reroutes()
    {
        // Arrange: reroute trigger 2 - the verifier's reasoning surfaces a README Assumption contradiction that
        // implies a re-cut or Migration-scale work, reaching the same RerouteRequired verdict for a different
        // reason
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"RerouteRequired","concerns":[],"advisoryNotes":["this contradicts the README Assumption about build-time network access; the repository needs a re-cut"],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
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
                    "README Assumption", ((WorkerRunResult.Reroute)result.Finding!).Why));
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
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":false}""");

            var worker = new ContractChangeWorker(
                root,
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
            var worker = new ContractChangeWorker(
                root, "document charter", "developer charter", "verifier charter", endpointFor: _ => endpoint);

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
    public async Task RunAsync_DefaultContractCheckRunner_CallsCheckContractsOperationInProcess()
    {
        // Arrange: no contractCheckRunScript supplied, so the default must run CheckContractsOperation in
        // process against this repository rather than shelling out to a check-contracts.ps1 file - this
        // repository, like every downstream repository once template-installed, carries no such file at its
        // root, and the defect this default replaced always false-failed this step here for exactly that
        // reason. A fake build script is still supplied so this test never spawns a real build.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: the temporary repository has no .anneal/architecture tree, so the in-process check reports
            // "nothing to check" and passes - a real verdict, not a pwsh usage banner or a missing-file failure
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkerBrief MakeBrief() =>
        new("parent-1", "add a contract clause for the new action", "contract change", [], [], "this touches a contract", [], []);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-contract-change-" + Guid.NewGuid().ToString("N")[..12]);
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
