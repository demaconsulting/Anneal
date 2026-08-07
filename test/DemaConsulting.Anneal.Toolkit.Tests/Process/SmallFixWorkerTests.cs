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
    public async Task RunAsync_ComposesInstruction_IncludesCodingAndTestingStandardsContent()
    {
        // Arrange: S12 - a compiled worker now injects its own fixed standards list into the Developer prompt.
        // SmallFixWorker's own remit is code and tests (its deterministic check runs build.ps1's full test
        // suite), so it carries coding, csharp-language, testing, and csharp-testing standards.
        var root = CreateTemporaryDirectory();
        try
        {
            WriteStandard(root, "coding-principles.md", "MARKER-CODING-PRINCIPLE");
            WriteStandard(root, "csharp-language.md", "MARKER-CSHARP-LANGUAGE");
            WriteStandard(root, "testing-principles.md", "MARKER-TESTING-PRINCIPLE");
            WriteStandard(root, "csharp-testing.md", "MARKER-CSHARP-TESTING");

            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "fixed it"}""");

            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken) =>
                Task.FromResult(new ScriptRun(0, "all good"));

            var worker = new SmallFixWorker(root, "a charter", endpointFor: _ => endpoint, runScript: BuildCheck);

            // Act
            await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: the first (only) Developer call's user message carries every standard's content verbatim.
            var userText = string.Join("\n", endpoint.Requests[0].Messages.Select(m => m.Text));
            Assert.Multiple(
                () => Assert.Contains("MARKER-CODING-PRINCIPLE", userText),
                () => Assert.Contains("MARKER-CSHARP-LANGUAGE", userText),
                () => Assert.Contains("MARKER-TESTING-PRINCIPLE", userText),
                () => Assert.Contains("MARKER-CSHARP-TESTING", userText));
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
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "fixed it"}""");

            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken) =>
                Task.FromResult(new ScriptRun(0, "all good"));

            var worker = new SmallFixWorker(root, "a charter", endpointFor: _ => endpoint, runScript: BuildCheck);

            // Act
            var result = await worker.RunAsync(MakeBrief(), TestContext.Current.CancellationToken);

            // Assert: no standards were installed under root, and the worker still completes normally.
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
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

    private static void WriteStandard(string root, string fileName, string content)
    {
        var directory = Path.Combine(root, ".github", "standards");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }
}
