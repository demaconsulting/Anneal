using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="DocumentAuthor" />'s basic shape and outcome mapping.
/// </summary>
/// <remarks>
///     The protected-write escalation path is not exercised here: triggering it needs the provider's own
///     tool-invocation loop to actually attempt a protected write, which a queued-reply endpoint does not drive.
///     That path is documented, not asserted, in this pass; see the Apply Report.
/// </remarks>
public class DocumentAuthorTests
{
    [Fact]
    public async Task AuthorAsync_AuthoredWithinBudget_Succeeds()
    {
        // Arrange: a change touching one file, well within the default three-file budget
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the document.",
                """
                {
                    "kind": "Authored",
                    "why": "",
                    "filesChanged": ["docs/example.md"],
                    "summary": "clarified a sentence"
                }
                """);
            var author = new DocumentAuthor(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("clarify this", TestContext.Current.CancellationToken);

            // Assert: within budget — no oracle call is made (only 2 replies consumed)
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<DocumentAuthoringResult.Authored>(result.Finding),
                () => Assert.Equal(2, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorAsync_AuthoredOverBudget_OracleJudgesProportionate_Succeeds()
    {
        // Arrange: a one-file budget, three files touched; oracle says the list is proportionate
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated three documents.",
                """
                {
                    "kind": "Authored",
                    "why": "",
                    "filesChanged": ["a.md", "b.md", "c.md"],
                    "summary": "a wide but justified change"
                }
                """,
                // Third reply: oracle judges proportionate
                """{"proportionate": true, "why": "", "hasSufficientEvidence": true}""");
            var author = new DocumentAuthor(root, "a charter", targetFileCountBudget: 1, endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("clarify this", TestContext.Current.CancellationToken);

            // Assert: oracle said proportionate, so the pass succeeds
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<DocumentAuthoringResult.Authored>(result.Finding),
                () => Assert.Equal(3, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorAsync_AuthoredOverBudget_OracleJudgesDisproportionate_FailsWithOracleReason()
    {
        // Arrange: a one-file budget, three files touched; oracle says the list is scope drift
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated three documents.",
                """
                {
                    "kind": "Authored",
                    "why": "",
                    "filesChanged": ["a.md", "b.md", "c.md"],
                    "summary": "a wide change"
                }
                """,
                // Third reply: oracle judges disproportionate with a reason
                """{"proportionate": false, "why": "b.md and c.md have no connection to the instruction", "hasSufficientEvidence": true}""");
            var author = new DocumentAuthor(root, "a charter", targetFileCountBudget: 1, endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("clarify this", TestContext.Current.CancellationToken);

            // Assert: failed, and the oracle's own reasoning is surfaced as the note
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding),
                () => Assert.Contains(
                    result.Notes,
                    n => n.Text.Contains("b.md and c.md have no connection", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorAsync_Reroute_Succeeds()
    {
        // Arrange: a better owner was named for this change
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "This belongs to a different owner.",
                """{"kind": "Reroute", "why": "no single owner found", "filesChanged": [], "summary": ""}""");
            var author = new DocumentAuthor(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("clarify this", TestContext.Current.CancellationToken);

            // Assert: Succeeded - naming a better owner is this primitive successfully answering its own
            // question, the same as Planner's Reroute case, not a failure to answer
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<DocumentAuthoringResult.Reroute>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorAsync_RecoveredMidTaskThenAuthored_Succeeds()
    {
        // Arrange: the run reply itself represents recovery after a mid-task problem (a tool call that
        // failed and was then corrected later in the same transcript), followed by a probe reply reporting
        // Authored with real filesChanged/summary values. QueuedEndpoint replays canned replies regardless
        // of prompt content, so this test guards the mapping/wiring layer against regressing on this
        // transcript shape; it cannot itself exercise real model judgment against the new probe wording,
        // which is why the fix is prompt text reviewed for correctness rather than something a fake-endpoint
        // test can prove by itself.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "An edit failed partway through, but I corrected it and finished the document change.",
                """
                {
                    "kind": "Authored",
                    "why": "",
                    "filesChanged": ["docs/example.md"],
                    "summary": "recovered from a failed edit and completed the change"
                }
                """);
            var author = new DocumentAuthor(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("clarify this", TestContext.Current.CancellationToken);

            // Assert: self-recovery mid-transcript is not evidence of incompleteness
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<DocumentAuthoringResult.Authored>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorAsync_NoModelAvailable_Fails()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var author = new DocumentAuthor(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("clarify this", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Failed, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorAsync_IntervalNotReached_SkipsScopeDriftCheck()
    {
        // Arrange: interval=5, no tools actually invoked (count=0 < 5) — the drift check never fires.
        // This exercises the guard condition that prevents unnecessary oracle calls early in a run.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the document.",
                """{"kind": "Authored", "why": "", "filesChanged": ["docs/a.md"], "summary": "a small update"}""");
            var author = new DocumentAuthor(root, "a charter", scopeDriftCheckInterval: 5, endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("update the document", TestContext.Current.CancellationToken);

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
    public async Task AuthorAsync_IntervalCrossedAndDriftDetected_FailsWithDriftReason()
    {
        // Arrange: interval=1 and the run turn invokes one edit tool, so count reaches 1 and the drift check fires.
        // The oracle reports clear scope drift, so AuthorAsync aborts before reaching the post-run probe.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new ScopeCheckEndpoint(
                driftReply: """{"aligned": false, "why": "the pass went outside its declared scope", "hasSufficientEvidence": true}""");
            var author = new DocumentAuthor(
                root, "a charter", scopeDriftCheckInterval: 1, endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("update the document", TestContext.Current.CancellationToken);

            // Assert: aborted by the mid-run scope-drift check; the oracle's reason is surfaced
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding),
                () => Assert.Contains(
                    result.Notes,
                    n => n.Text.Contains("went outside its declared scope", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AuthorAsync_IntervalCrossedButAligned_ContinuesNormally()
    {
        // Arrange: interval=1 and the run invokes one edit tool, so the drift check fires — but the oracle
        // reports aligned, so execution continues to the post-run probe and the pass succeeds.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new ScopeCheckEndpoint(
                driftReply: """{"aligned": true, "why": "", "hasSufficientEvidence": true}""",
                probeReply: """{"kind": "Authored", "why": "", "filesChanged": ["docs/a.md"], "summary": "updated"}""");
            var author = new DocumentAuthor(
                root, "a charter", scopeDriftCheckInterval: 1, endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("update the document", TestContext.Current.CancellationToken);

            // Assert: aligned oracle → pass continues and succeeds
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<DocumentAuthoringResult.Authored>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-doc-author-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    ///     A fake endpoint that invokes a real edit tool during the run call so the session's
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
                // Simulate the SDK tool loop invoking create_file, incrementing SuccessfulEditCallCount
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

                return new ChatTurnResult("I updated the document.");
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
