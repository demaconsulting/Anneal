using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
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

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-developer-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
