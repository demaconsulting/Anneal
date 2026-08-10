using DemaConsulting.Anneal.Toolkit.Primitives;
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

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-doc-author-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
