using DemaConsulting.Anneal.Toolkit.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="Oracle{TDecision}" />'s basic shape and outcome mapping.
/// </summary>
/// <remarks>
///     Disposable: this pass adds no contract clause, so these pin down the mapping this pass chose rather than a
///     promise the Toolkit makes to a caller outside the assembly.
/// </remarks>
public class OracleTests
{
    [Fact]
    public async Task AskAsync_SufficientEvidence_Succeeds()
    {
        // Arrange: an oracle whose decoded reply states it had enough evidence
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"hasSufficientEvidence": true, "answer": "yes"}""");
            var oracle = new Oracle<TestDecision>(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await oracle.AskAsync("is this fine?", [], TestContext.Current.CancellationToken);

            // Assert: the outcome is Succeeded and the decoded decision comes through
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal("yes", result.Finding?.Answer));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AskAsync_InsufficientEvidence_Refuses()
    {
        // Arrange: a reply that honestly states its evidence was not enough
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"hasSufficientEvidence": false, "answer": ""}""");
            var oracle = new Oracle<TestDecision>(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await oracle.AskAsync("is this fine?", [], TestContext.Current.CancellationToken);

            // Assert: refusal is reported, and the decoded (insufficient) decision still comes through
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Refused, result.Outcome),
                () => Assert.NotNull(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AskAsync_NoModelAvailable_Fails()
    {
        // Arrange: an endpoint with nothing queued, standing in for a provider that cannot be reached
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var oracle = new Oracle<TestDecision>(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await oracle.AskAsync("is this fine?", [], TestContext.Current.CancellationToken);

            // Assert: Failed, with no finding to report
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
    public async Task AskAsync_ReplyNeverDecodes_FailsAfterItsRetryBudget()
    {
        // Arrange: a reply that will never parse as the decision type, and no retries to spend chasing it
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint("not json at all");
            var oracle = new Oracle<TestDecision>(root, "a charter", maxParseRetries: 0, endpointFor: _ => endpoint);

            // Act
            var result = await oracle.AskAsync("is this fine?", [], TestContext.Current.CancellationToken);

            // Assert: Failed, having spent exactly the one attempt its budget allowed
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Equal(1, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-oracle-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>A minimal typed decision used only to exercise <see cref="Oracle{TDecision}" />.</summary>
    private sealed record TestDecision : IOracleDecision
    {
        public required bool HasSufficientEvidence { get; init; }

        public required string Answer { get; init; }
    }
}
