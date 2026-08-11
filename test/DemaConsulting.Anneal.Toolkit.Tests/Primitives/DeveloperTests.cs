using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="Developer" />'s basic shape and outcome mapping, including the optional
///     build-check repair loop.
/// </summary>
/// <remarks>
///     As with <see cref="DocumentAuthorTests" />, the protected-write escalation path is not exercised here for
///     the same reason: it needs the provider's own tool-invocation loop, which a queued-reply endpoint does not
///     drive.
/// </remarks>
public class DeveloperTests
{
    [Fact]
    public async Task DevelopAsync_CompletedWithNoBuildCheck_Succeeds()
    {
        // Arrange: no build check configured, so the first authoring pass is reported as-is
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "added a method"}""");
            var developer = new Developer(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<DevelopmentResult.Completed>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DevelopAsync_Reroute_Succeeds()
    {
        // Arrange: a better owner was named for this change
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "This belongs elsewhere.",
                """{"kind": "Reroute", "why": "structural change needed", "suggestedWorker": "structural-change", "filesChanged": [], "summary": ""}""");
            var developer = new Developer(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert: Succeeded - naming a better owner is this primitive successfully answering its own
            // question, carrying the suggested worker along
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(
                    "structural-change", ((DevelopmentResult.Reroute)result.Finding!).SuggestedWorker));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DevelopAsync_RecoveredMidTaskThenCompleted_Succeeds()
    {
        // Arrange: the run reply itself represents recovery after a mid-task problem (a tool call that
        // failed and was then corrected later in the same transcript), followed by a probe reply reporting
        // Completed with real filesChanged/summary values. QueuedEndpoint replays canned replies regardless
        // of prompt content, so this test guards the mapping/wiring layer against regressing on this
        // transcript shape; it cannot itself exercise real model judgment against the new probe wording,
        // which is why the fix is prompt text reviewed for correctness rather than something a fake-endpoint
        // test can prove by itself.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "A tool call failed partway through, but I corrected it and finished the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs", "b.cs"], "summary": "recovered from a failed edit and completed the change"}""");
            var developer = new Developer(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert: self-recovery mid-transcript is not evidence of incompleteness
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<DevelopmentResult.Completed>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DevelopAsync_BuildCheckPassesFirstTry_SucceedsWithoutRepairing()
    {
        // Arrange: a build check that reports passing on the first try
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "added a method"}""");
            var checkCalls = 0;
            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken)
            {
                checkCalls++;
                return Task.FromResult(new ScriptRun(0, "all good"));
            }

            var developer = new Developer(root, "a charter", endpointFor: _ => endpoint, buildCheck: BuildCheck);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(1, checkCalls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DevelopAsync_BuildCheckNeverPasses_FailsWhenRepairBudgetSpent()
    {
        // Arrange: a build check that never passes, and no repair budget to spend chasing it
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint("I tried to fix it.", "I tried again.");
            Task<ScriptRun> BuildCheck(string script, CancellationToken cancellationToken) =>
                Task.FromResult(new ScriptRun(1, "still failing"));

            var developer = new Developer(
                root, "a charter", maxRepairAttempts: 0, endpointFor: _ => endpoint, buildCheck: BuildCheck);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert: the budget was spent with the check still failing
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
    public async Task DevelopAsync_NoModelAvailable_Fails()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var developer = new Developer(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Failed, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DevelopAsync_IntervalNotReached_SkipsScopeDriftCheck()
    {
        // Arrange: interval=5, no tools actually invoked (count=0 < 5) — the drift check never fires.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "done"}""");
            var developer = new Developer(
                root, "a charter", scopeDriftCheckInterval: 5, endpointFor: _ => endpoint);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert: succeeds with only 2 endpoint calls (run + probe); no scope-check call was made
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(2, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DevelopAsync_IntervalCrossedAndDriftDetected_FailsWithDriftReason()
    {
        // Arrange: interval=1 and the run turn invokes one edit tool, so count reaches 1 and the drift check fires.
        // The oracle reports clear scope drift, so DevelopAsync aborts before reaching the post-run probe.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new ScopeCheckEndpoint(
                driftReply: """{"aligned": false, "why": "the pass went beyond its declared scope", "hasSufficientEvidence": true}""");
            var developer = new Developer(
                root, "a charter", scopeDriftCheckInterval: 1, endpointFor: _ => endpoint);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert: aborted by the mid-run scope-drift check; the oracle's reason is surfaced
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding),
                () => Assert.Contains(
                    result.Notes,
                    n => n.Text.Contains("went beyond its declared scope", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DevelopAsync_IntervalCrossedButAligned_ContinuesNormally()
    {
        // Arrange: interval=1 and the run invokes one edit tool, so the drift check fires — but the oracle
        // reports aligned, so execution continues to the post-run probe and the pass succeeds.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new ScopeCheckEndpoint(
                driftReply: """{"aligned": true, "why": "", "hasSufficientEvidence": true}""",
                probeReply: """{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["a.cs"], "summary": "done"}""");
            var developer = new Developer(
                root, "a charter", scopeDriftCheckInterval: 1, endpointFor: _ => endpoint);

            // Act
            var result = await developer.DevelopAsync("add a method", TestContext.Current.CancellationToken);

            // Assert: aligned oracle → pass continues and succeeds
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<DevelopmentResult.Completed>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-developer-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    ///     A fake endpoint that invokes the real <c>create_file</c> tool during the run call so the session's
    ///     <see cref="ModelSession.SuccessfulEditCallCount" /> reaches the scope-drift check interval, then answers
    ///     the scope-check probe with a canned drift judgement, and optionally a post-drift probe reply.
    /// </summary>
    private sealed class ScopeCheckEndpoint(
        string driftReply,
        string? probeReply = null) : IChatEndpoint
    {
        private int _calls;

        public async Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
        {
            _calls++;

            if (_calls == 1)
            {
                // Simulate the SDK tool loop: invoke create_file so SuccessfulEditCallCount reaches 1
                var createFile = request.Tools.OfType<AIFunction>()
                    .FirstOrDefault(t => t.Name == "create_file");
                if (createFile is not null)
                {
                    var args = new AIFunctionArguments(
                        new Dictionary<string, object?> { ["path"] = "scope-check-file.txt", ["content"] = "x" });
                    try
                    {
                        await createFile.InvokeAsync(args, cancellationToken).ConfigureAwait(false);
                    }
                    catch { /* refusals are recorded, not thrown to callers */ }
                }

                return new ChatTurnResult("I made the change.");
            }

            if (_calls == 2)
                return new ChatTurnResult(driftReply);

            return probeReply is not null
                ? new ChatTurnResult(probeReply)
                : throw new ModelUnavailableException("no further replies queued");
        }

        public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }
}
