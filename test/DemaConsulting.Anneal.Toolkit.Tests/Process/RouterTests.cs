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
                return Task.FromResult(new StepResult<WorkerRunResult>(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Completed(new ChangeSetSummary(["a.cs"], "fixed it")),
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
                        ? new StepResult<WorkerRunResult>(
                            OperationOutcome.Succeeded,
                            new WorkerRunResult.Reroute("needs a contract change", [], "contract-change"),
                            [])
                        : new StepResult<WorkerRunResult>(
                            OperationOutcome.Succeeded,
                            new WorkerRunResult.Completed(new ChangeSetSummary(["a.cs"], "fixed it")),
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

            WorkerRunner runner = (_, _) => Task.FromResult(new StepResult<WorkerRunResult>(
                OperationOutcome.Succeeded,
                new WorkerRunResult.Completed(new ChangeSetSummary(["a.cs"], "fixed it")),
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
                return Task.FromResult(new StepResult<WorkerRunResult>(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Reroute("needs a contract change", [], "contract-change"),
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

    private static string SelectWorkerJson(string workerKey, string why) =>
        $$"""
          {"kind":"SelectWorker","why":"{{why}}","workerKey":"{{workerKey}}","question":"","researchScope":"Narrow","humanOnlyNextStep":"","hasSufficientEvidence":true}
          """;

    private static string NeedResearchJson(string question, string why) =>
        $$"""
          {"kind":"NeedResearch","why":"{{why}}","workerKey":"","question":"{{question}}","researchScope":"Narrow","humanOnlyNextStep":"","hasSufficientEvidence":true}
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
