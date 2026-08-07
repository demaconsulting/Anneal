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

            // Assert
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
    public async Task AuthorAsync_AuthoredOverBudget_Fails()
    {
        // Arrange: a one-file budget, but the reply reports three files touched
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
                """);
            var author = new DocumentAuthor(root, "a charter", targetFileCountBudget: 1, endpointFor: _ => endpoint);

            // Act
            var result = await author.AuthorAsync("clarify this", TestContext.Current.CancellationToken);

            // Assert: the change grew past this primitive's bound
            Assert.Equal(OperationOutcome.Failed, result.Outcome);
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
