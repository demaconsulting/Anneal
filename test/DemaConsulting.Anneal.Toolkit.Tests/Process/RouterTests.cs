using System.Text.Json;
using System.Text.Json.Serialization;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for <see cref="Router" />'s bounded routing algorithm: how it composes the route oracle,
///     research, and a worker catalog, and how its two independent budgets — research iterations and worker
///     reroutes — are spent and fail closed.
/// </summary>
public class RouterTests
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task RunAsync_SelectWorkerThenCompleted_SucceedsAndRecordsBothSteps()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(SelectWorkerJson("small-fix", "this is a small fix"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            var runnerCalls = 0;
            WorkerRunner runner = (_, _) =>
            {
                runnerCalls++;
                return Task.FromResult(new WorkerExecutionResult(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Completed(new ChangeSetSummary(["a.cs"], "fixed it")),
                    null,
                    []));
            };

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("fix the bug", null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<RouterOutcome.Completed>(result.Finding),
                () => Assert.Equal(1, runnerCalls));

            var records = ReadRecords(root);
            Assert.Multiple(
                () => Assert.Equal(2, records.Count),
                () => Assert.Equal("RouteOracle", records[0].Step),
                () => Assert.Equal("Worker:small-fix", records[1].Step),
                () => Assert.Equal(records[0].ParentInvocationId, records[1].ParentInvocationId),
                () => Assert.False(string.IsNullOrWhiteSpace(records[0].ParentInvocationId)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WorkerReroutesThenCompletes_LoopsBackToTheOracleAndSucceeds()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(
                SelectWorkerJson("small-fix", "looks small"),
                SelectWorkerJson("small-fix", "still the right worker"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            var runnerCalls = 0;
            WorkerRunner runner = (_, _) =>
            {
                runnerCalls++;
                return Task.FromResult(
                    runnerCalls == 1
                        ? new WorkerExecutionResult(
                            OperationOutcome.Succeeded,
                            new WorkerRunResult.Reroute("needs a contract change", [], "contract-change"),
                            null,
                            [])
                        : new WorkerExecutionResult(
                            OperationOutcome.Succeeded,
                            new WorkerRunResult.Completed(new ChangeSetSummary(["a.cs"], "fixed it")),
                            null,
                            []));
            };

            var router = BuildRouter(
                root, recordStore, oracleEndpoint, researchEndpoint, runner, maxWorkerReroutes: 2);

            // Act
            var result = await router.RunAsync("fix the bug", null, TestContext.Current.CancellationToken);

            // Assert: two route-oracle asks and two worker runs, ending in success
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<RouterOutcome.Completed>(result.Finding),
                () => Assert.Equal(2, runnerCalls));

            var records = ReadRecords(root);
            Assert.Equal(4, records.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NeedsResearchThenSelectsWorker_RunsResearchAndSucceeds()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            // Both the route oracle's own question and Research's schema-last extraction probe run at
            // ModelRole.Light (an oracle-style narrow judgement), so both draw from the same fake endpoint, in
            // the exact order those calls happen: route ask, research extraction, route ask again. Only
            // Research's free-form look-around pass runs at ModelRole.Medium, on the separate endpoint.
            var oracleEndpoint = new QueuedEndpoint(
                NeedResearchJson("what changed recently?", "not enough context yet"),
                ResearchFindingJson("what changed recently?", "nothing unusual", sufficientForNextDecision: true),
                SelectWorkerJson("small-fix", "now it is clear"));
            var researchEndpoint = new QueuedEndpoint("Looking around.");
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => Task.FromResult(new WorkerExecutionResult(
                OperationOutcome.Succeeded,
                new WorkerRunResult.Completed(new ChangeSetSummary(["a.cs"], "fixed it")),
                null,
                []));

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("fix the bug", null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);

            var records = ReadRecords(root);
            Assert.Multiple(
                () => Assert.Equal(4, records.Count),
                () => Assert.Equal("RouteOracle", records[0].Step),
                () => Assert.Equal("Research", records[1].Step),
                () => Assert.Equal("RouteOracle", records[2].Step),
                () => Assert.Equal("Worker:small-fix", records[3].Step));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NeedsResearchReportsInsufficientEvidence_StillRunsResearchAndSucceeds()
    {
        // Arrange: NeedResearch is the one decision kind where HasSufficientEvidence: false is the honest,
        // expected reply - not a refusal to answer at all - per RouteCharter's own instruction to ask for a
        // bounded look-around rather than guess. A live run against a real model produced exactly this
        // shape and, before this test's own fix, it made Router.RunAsync fail closed immediately instead of
        // spending its research budget as designed.
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(
                NeedResearchJson("what changed recently?", "not enough context yet", hasSufficientEvidence: false),
                ResearchFindingJson("what changed recently?", "nothing unusual", sufficientForNextDecision: true),
                SelectWorkerJson("small-fix", "now it is clear"));
            var researchEndpoint = new QueuedEndpoint("Looking around.");
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => Task.FromResult(new WorkerExecutionResult(
                OperationOutcome.Succeeded,
                new WorkerRunResult.Completed(new ChangeSetSummary(["a.cs"], "fixed it")),
                null,
                []));

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("fix the bug", null, TestContext.Current.CancellationToken);

            // Assert: research actually ran rather than the run failing closed on the first ask
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<RouterOutcome.Completed>(result.Finding),
                () => Assert.Equal(1, researchEndpoint.Calls));

            var records = ReadRecords(root);
            Assert.Multiple(
                () => Assert.Equal(4, records.Count),
                () => Assert.Equal("RouteOracle", records[0].Step),
                () => Assert.Equal("Research", records[1].Step),
                () => Assert.Equal("RouteOracle", records[2].Step),
                () => Assert.Equal("Worker:small-fix", records[3].Step));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_SelectWorkerReportsInsufficientEvidence_FailsClosedWithoutRunningTheWorker()
    {
        // Arrange: SelectWorker with HasSufficientEvidence: false is a genuinely contradictory reply - the
        // oracle named a worker while itself saying it lacks the evidence to commit to that answer - unlike
        // NeedResearch, where the same flag is the expected, honest signal. This must still fail closed.
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(
                SelectWorkerJson("small-fix", "maybe this", hasSufficientEvidence: false));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => throw new InvalidOperationException("no worker should run");

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("fix the bug", null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.IsType<RouterOutcome.Report>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ResearchBudgetAlreadyExhausted_FailsWithoutRunningResearch()
    {
        // Arrange: a research budget of zero means the very first NeedResearch fails closed
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(NeedResearchJson("what changed?", "unclear"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => throw new InvalidOperationException("no worker should run");

            var router = BuildRouter(
                root, recordStore, oracleEndpoint, researchEndpoint, runner, maxResearchIterations: 0);

            // Act
            var result = await router.RunAsync("fix the bug", null, TestContext.Current.CancellationToken);

            // Assert: failed closed, and research was never actually invoked
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.IsType<RouterOutcome.Report>(result.Finding),
                () => Assert.Equal(0, researchEndpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_RerouteBudgetAlreadyExhausted_FailsClosedWithoutASecondOracleAsk()
    {
        // Arrange: a reroute budget of zero means the first Reroute fails closed rather than looping
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(SelectWorkerJson("small-fix", "looks small"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            var runnerCalls = 0;
            WorkerRunner runner = (_, _) =>
            {
                runnerCalls++;
                return Task.FromResult(new WorkerExecutionResult(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Reroute("needs a contract change", [], "contract-change"),
                    null,
                    []));
            };

            var router = BuildRouter(
                root, recordStore, oracleEndpoint, researchEndpoint, runner, maxWorkerReroutes: 0);

            // Act
            var result = await router.RunAsync("fix the bug", null, TestContext.Current.CancellationToken);

            // Assert: exactly one oracle ask and one worker run, then a closed failure
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Equal(1, runnerCalls),
                () => Assert.Equal(1, oracleEndpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NoRouteWithNoHumanStep_Fails()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(NoRouteJson("nothing in the catalog fits", humanOnlyNextStep: ""));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => throw new InvalidOperationException("no worker should run");

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("do something odd", null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.IsType<RouterOutcome.Report>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_NoRouteWithHumanOnlyNextStep_Escalates()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(
                NoRouteJson("this changes a contract", humanOnlyNextStep: "this is a Migration proposal"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => throw new InvalidOperationException("no worker should run");

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("propose a migration stage", null, TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Escalated, result.Outcome),
                () => Assert.IsType<RouterOutcome.Report>(result.Finding),
                () => Assert.Equal(
                    "this is a Migration proposal", ((RouterOutcome.Report)result.Finding!).FailureReport.RecommendedNextStep));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_TwoEntryCatalog_RoutesToContractChangeWithoutDisturbingSmallFix()
    {
        // Arrange: a catalog with both worker keys pass 3 introduces, confirming the second entry does not
        // shadow or otherwise disturb small-fix's own existing routing
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(SelectWorkerJson("contract-change", "this touches a contract"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            var smallFixCalls = 0;
            WorkerRunner smallFixRunner = (_, _) =>
            {
                smallFixCalls++;
                return Task.FromResult(new WorkerExecutionResult(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Completed(new ChangeSetSummary(["a.cs"], "fixed it")),
                    null,
                    []));
            };

            var contractChangeCalls = 0;
            WorkerRunner contractChangeRunner = (_, _) =>
            {
                contractChangeCalls++;
                return Task.FromResult(new WorkerExecutionResult(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Completed(new ChangeSetSummary(["docs/architecture/toolkit.md"], "updated the contract")),
                    null,
                    []));
            };

            WorkerCatalogEntry smallFix = new(new WorkerDescriptor("small-fix", "the cheap path"), smallFixRunner);
            WorkerCatalogEntry contractChange =
                new(new WorkerDescriptor("contract-change", "contract clause changes"), contractChangeRunner);

            var router = new Router(
                root,
                "route charter",
                "research charter",
                [smallFix, contractChange],
                recordStore,
                maxResearchIterations: 3,
                maxWorkerReroutes: 2,
                endpointFor: role => role == ModelRole.Medium ? researchEndpoint : oracleEndpoint);

            // Act
            var result = await router.RunAsync("add a contract clause", null, TestContext.Current.CancellationToken);

            // Assert: routed to the newly-added worker, and small-fix's own runner was never invoked
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<RouterOutcome.Completed>(result.Finding),
                () => Assert.Equal(1, contractChangeCalls),
                () => Assert.Equal(0, smallFixCalls));

            var records = ReadRecords(root);
            Assert.Contains(records, record => record.Step == "Worker:contract-change");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WorkerEscalatesWithInterruptedChange_ChangeBeforeStoppingFlowsIntoReport()
    {
        // Arrange: a worker that escalates but carries files it already wrote
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(SelectWorkerJson("small-fix", "simple fix"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => Task.FromResult(new WorkerExecutionResult(
                OperationOutcome.Escalated,
                null,
                new ChangeSetBeforeStopping(["protected-file.cs"], "partial edit before escalation"),
                [new ProcessNote("a protected path was reached")]));

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("fix something", null, TestContext.Current.CancellationToken);

            // Assert: escalated outcome and the interrupted change flows into the report
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Escalated, result.Outcome),
                () => Assert.IsType<RouterOutcome.Report>(result.Finding));

            var report = ((RouterOutcome.Report)result.Finding!).FailureReport;
            Assert.Multiple(
                () => Assert.NotNull(report.ChangeBeforeStopping),
                () => Assert.Contains("protected-file.cs", report.ChangeBeforeStopping!.FilesChanged),
                () => Assert.Equal("partial edit before escalation", report.ChangeBeforeStopping!.Summary));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WorkerFailsWithInterruptedChange_ChangeBeforeStoppingFlowsIntoReport()
    {
        // Arrange: a worker that fails but carries files it already wrote
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(SelectWorkerJson("small-fix", "simple fix"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => Task.FromResult(new WorkerExecutionResult(
                OperationOutcome.Failed,
                null,
                new ChangeSetBeforeStopping(["a.cs", "b.cs"], "two files edited before budget expired"),
                []));

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("fix something", null, TestContext.Current.CancellationToken);

            // Assert: failed outcome and the interrupted change flows into the report
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.IsType<RouterOutcome.Report>(result.Finding));

            var report = ((RouterOutcome.Report)result.Finding!).FailureReport;
            Assert.Multiple(
                () => Assert.NotNull(report.ChangeBeforeStopping),
                () => Assert.Equal(2, report.ChangeBeforeStopping!.FilesChanged.Count),
                () => Assert.Equal("two files edited before budget expired", report.ChangeBeforeStopping!.Summary));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WorkerFailsWithNoInterruptedChange_ChangeBeforeStoppingIsNull()
    {
        // Arrange: a worker that fails with no file-write state (e.g. no model reachable)
        var root = CreateTemporaryDirectory();
        try
        {
            var oracleEndpoint = new QueuedEndpoint(SelectWorkerJson("small-fix", "simple fix"));
            var researchEndpoint = new QueuedEndpoint();
            var recordStore = new RecordStore(root);

            WorkerRunner runner = (_, _) => Task.FromResult(new WorkerExecutionResult(
                OperationOutcome.Failed, null, null, [new ProcessNote("no model reachable")]));

            var router = BuildRouter(root, recordStore, oracleEndpoint, researchEndpoint, runner);

            // Act
            var result = await router.RunAsync("fix something", null, TestContext.Current.CancellationToken);

            // Assert: failed outcome and no interrupted change
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.IsType<RouterOutcome.Report>(result.Finding));

            var report = ((RouterOutcome.Report)result.Finding!).FailureReport;
            Assert.Null(report.ChangeBeforeStopping);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Router BuildRouter(
        string root,
        RecordStore recordStore,
        QueuedEndpoint oracleEndpoint,
        QueuedEndpoint researchEndpoint,
        WorkerRunner runner,
        int maxResearchIterations = 3,
        int maxWorkerReroutes = 2)
    {
        WorkerCatalogEntry entry = new(new WorkerDescriptor("small-fix", "the cheap path"), runner);

        return new Router(
            root,
            "route charter",
            "research charter",
            [entry],
            recordStore,
            maxResearchIterations,
            maxWorkerReroutes,
            endpointFor: role => role == ModelRole.Medium ? researchEndpoint : oracleEndpoint);
    }

    private static string SelectWorkerJson(string workerKey, string why, bool hasSufficientEvidence = true) =>
        $$"""
          {"kind":"SelectWorker","why":"{{why}}","workerKey":"{{workerKey}}","question":"","researchScope":"Narrow","humanOnlyNextStep":"","hasSufficientEvidence":{{(hasSufficientEvidence ? "true" : "false")}}}
          """;

    private static string NeedResearchJson(string question, string why, bool hasSufficientEvidence = true) =>
        $$"""
          {"kind":"NeedResearch","why":"{{why}}","workerKey":"","question":"{{question}}","researchScope":"Narrow","humanOnlyNextStep":"","hasSufficientEvidence":{{(hasSufficientEvidence ? "true" : "false")}}}
          """;

    private static string NoRouteJson(string why, string humanOnlyNextStep) =>
        $$"""
          {"kind":"NoRoute","why":"{{why}}","workerKey":"","question":"","researchScope":"Narrow","humanOnlyNextStep":"{{humanOnlyNextStep}}","hasSufficientEvidence":true}
          """;

    private static string ResearchFindingJson(string question, string answer, bool sufficientForNextDecision) =>
        $$"""
          {"question":"{{question}}","answer":"{{answer}}","evidenceRefs":[],"implications":"nothing to act on","sufficientForNextDecision":{{(sufficientForNextDecision ? "true" : "false")}}}
          """;

    private static IReadOnlyList<ProcessStepRecord> ReadRecords(string root)
    {
        var path = RecordStore.ProcessStepsPathFor(root);
        return [.. File.ReadAllLines(path).Select(
            line => JsonSerializer.Deserialize<ProcessStepRecord>(line, ReadOptions)!)];
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-router-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
