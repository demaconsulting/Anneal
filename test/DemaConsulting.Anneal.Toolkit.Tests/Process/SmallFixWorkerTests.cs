using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for <see cref="SmallFixWorker" />'s own composition: <see cref="Developer" /> →
///     <see cref="DeterministicCheck" /> → an optional single local repair pass → finish or reroute.
/// </summary>
public class SmallFixWorkerTests
{
    [Fact]
    public async Task RunAsync_BuildPassesFirstTry_CompletesWithoutRepairing()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "fixed it"}""");

            var checkCalls = 0;
            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken)
            {
                checkCalls++;
                return Task.FromResult(new ScriptRun(0, "all good"));
            }

            var worker = new SmallFixWorker(root, "a charter", endpointFor: _ => endpoint, runScript: BuildCheck);
            var brief = MakeBrief();

            // Act
            var result = await worker.RunAsync(brief, TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
                () => Assert.Equal(1, checkCalls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_BuildFailsThenPassesAfterOneRepair_CompletesWithRepairSpent()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "first attempt"}""",
                "I repaired it.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "repaired"}""");

            var checkCalls = 0;
            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken)
            {
                checkCalls++;
                return Task.FromResult(checkCalls == 1 ? new ScriptRun(1, "one test failed") : new ScriptRun(0, "all good"));
            }

            var worker = new SmallFixWorker(root, "a charter", maxRepairAttempts: 1, endpointFor: _ => endpoint, runScript: BuildCheck);
            var brief = MakeBrief();

            // Act
            var result = await worker.RunAsync(brief, TestContext.Current.CancellationToken);

            // Assert: one repair attempt was spent, and it cleared the check
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
                () => Assert.Equal(
                    "repaired", ((WorkerRunResult.Completed)result.Finding!).Summary.Summary),
                () => Assert.Equal(2, checkCalls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_BuildNeverPasses_FailsWhenRepairBudgetSpent()
    {
        // Arrange: no repair budget, and the check never passes
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "attempt"}""");

            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken) =>
                Task.FromResult(new ScriptRun(1, "still failing"));

            var worker = new SmallFixWorker(root, "a charter", maxRepairAttempts: 0, endpointFor: _ => endpoint, runScript: BuildCheck);
            var brief = MakeBrief();

            // Act
            var result = await worker.RunAsync(brief, TestContext.Current.CancellationToken);

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
    public async Task RunAsync_DeveloperReroutes_ReturnsRerouteWithoutRunningTheBuildCheck()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "This belongs elsewhere.",
                """{"kind": "Reroute", "why": "needs a contract change", "suggestedWorker": "contract-change", "filesChanged": [], "summary": ""}""");

            var checkCalls = 0;
            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken)
            {
                checkCalls++;
                return Task.FromResult(new ScriptRun(0, "all good"));
            }

            var worker = new SmallFixWorker(root, "a charter", endpointFor: _ => endpoint, runScript: BuildCheck);
            var brief = MakeBrief();

            // Act
            var result = await worker.RunAsync(brief, TestContext.Current.CancellationToken);

            // Assert: the reroute is surfaced, and the build check is never consulted for work that isn't this worker's
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Reroute>(result.Finding),
                () => Assert.Equal(
                    "contract-change", ((WorkerRunResult.Reroute)result.Finding!).SuggestedWorker),
                () => Assert.Equal(0, checkCalls));
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
            var worker = new SmallFixWorker(root, "a charter", endpointFor: _ => endpoint);
            var brief = MakeBrief();

            // Act
            var result = await worker.RunAsync(brief, TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Failed, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkerBrief MakeBrief() =>
        new("parent-1", "fix the flaky test", "small fix", [], [], "this looks small", []);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-small-fix-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
