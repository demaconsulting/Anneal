using DemaConsulting.Anneal.Toolkit.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="Research" />'s basic shape and outcome mapping.
/// </summary>
public class ResearchTests
{
    [Fact]
    public async Task InvestigateAsync_SufficientOnFirstTurn_Succeeds()
    {
        // Arrange: one look-around turn, then a finding that reports itself sufficient.
        // The QueuedEndpoint makes no read-tool calls, so corroboration removes all self-reported refs.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I looked at the files.",
                """
                {
                    "question": "what owns this?",
                    "answer": "the toolkit does",
                    "evidenceRefs": ["a.md", "b.md", "c.md"],
                    "implications": "nothing else needs to change",
                    "sufficientForNextDecision": true
                }
                """);
            var research = new Research(root, "a charter", maxTurns: 3, evidenceBudget: 2, endpointFor: _ => endpoint);

            // Act
            var result = await research.InvestigateAsync("what owns this?", TestContext.Current.CancellationToken);

            // Assert: succeeded after a single round; corroboration empties evidence because no tool was called
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Empty(result.Finding?.EvidenceRefs ?? []),
                () => Assert.Equal(2, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvestigateAsync_NeverSufficientWithinBudget_Refuses()
    {
        // Arrange: a finding that never reports itself sufficient, exhausting the one-turn budget
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I looked, but could not find enough.",
                """
                {
                    "question": "what owns this?",
                    "answer": "unclear",
                    "evidenceRefs": [],
                    "implications": "more research is needed",
                    "sufficientForNextDecision": false
                }
                """);
            var research = new Research(root, "a charter", maxTurns: 1, endpointFor: _ => endpoint);

            // Act
            var result = await research.InvestigateAsync("what owns this?", TestContext.Current.CancellationToken);

            // Assert: an honest refusal, carrying the insufficient finding
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Refused, result.Outcome),
                () => Assert.False(result.Finding?.SufficientForNextDecision));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvestigateAsync_NoModelAvailable_Fails()
    {
        // Arrange: nothing queued, standing in for a provider that cannot be reached
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var research = new Research(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await research.InvestigateAsync("what owns this?", TestContext.Current.CancellationToken);

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
    public async Task InvestigateAsync_ModelOverClaimsEvidence_TrimsToCorroboratedRefs()
    {
        // Arrange: the model reports three evidence refs but the QueuedEndpoint makes no real tool calls,
        // so none of those paths were actually read — the corroboration step should remove all three
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I reviewed the files.",
                """
                {
                    "question": "what owns this?",
                    "answer": "the toolkit does",
                    "evidenceRefs": ["a.md", "b.md", "c.md"],
                    "implications": "nothing else needs to change",
                    "sufficientForNextDecision": true
                }
                """);
            var research = new Research(root, "a charter", maxTurns: 1, endpointFor: _ => endpoint);

            // Act
            var result = await research.InvestigateAsync("what owns this?", TestContext.Current.CancellationToken);

            // Assert: hallucinated evidence refs are removed; the finding is still otherwise valid
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Empty(result.Finding?.EvidenceRefs ?? []));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-research-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
