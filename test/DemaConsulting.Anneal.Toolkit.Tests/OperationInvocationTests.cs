using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests;

/// <summary>
///     Interior tests for how an invocation carries its caller's signal and its finding.
/// </summary>
/// <remarks>
///     Disposable, and deliberately narrower than the contract tests beside them: these pin down the mechanics
///     the clauses leave interior — that the token arrives unchanged rather than merely arriving, and that an
///     invocation withdrawn before it starts spends nothing at all.
/// </remarks>
public class OperationInvocationTests
{
    /// <summary>
    ///     Validates that the token the dispatcher hands an operation is the caller's own, not a copy of a
    ///     different source.
    /// </summary>
    [Fact]
    public async Task AnnealTool_RunAsync_TokenSupplied_ReachesTheOperationUnchanged()
    {
        // Arrange: a caller's signal, and an operation that reports back what it was handed
        using var cancellation = new CancellationTokenSource();
        var operation = new RecordingOperation();

        // Act: dispatch through the command surface
        await AnnealTool.RunAsync(["recording"], TextWriter.Null, [operation], cancellation.Token);

        // Assert: the same signal, still live, still able to stop what it was given to
        Assert.Multiple(
            () => Assert.Equal(cancellation.Token, operation.Received),
            () => Assert.True(operation.Received.CanBeCanceled),
            () => Assert.False(operation.Received.IsCancellationRequested));
    }

    /// <summary>
    ///     Validates that a probe invoked under an already-cancelled signal consults no model at all.
    /// </summary>
    [Fact]
    public async Task ProbeRuleOwnerOperation_ExecuteAsync_AlreadyCancelled_ConsultsNoModel()
    {
        // Arrange: a repository, a counting endpoint, and a caller who has already withdrawn
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new CountingEndpoint();
            IOperation operation = new ProbeRuleOwnerOperation(root, _ => endpoint);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            // Act / Assert: the invocation stops before it spends anything, and reaches no outcome
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => operation.ExecuteAsync(["a rule"], TextWriter.Null, cancellation.Token));

            Assert.Equal(0, endpoint.Calls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Validates that the deterministic check, too, refuses to start under an already-cancelled signal.
    /// </summary>
    [Fact]
    public async Task VerifyEvidenceOperation_ExecuteAsync_AlreadyCancelled_ReadsNothing()
    {
        // Arrange: a report that would verify cleanly, and a caller who has already withdrawn
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllLines(Path.Combine(root, "subject.txt"), ["first line", "the promise this cites"]);
            File.WriteAllLines(
                Path.Combine(root, "report.md"),
                ["**Result**: SUCCEEDED", "", "`subject.txt:2` - \"the promise this cites\""]);

            IOperation operation = new VerifyEvidenceOperation(root);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var output = new StringWriter();

            // Act / Assert: nothing is rendered, because nothing was checked
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => operation.ExecuteAsync(["report.md"], output, cancellation.Token));

            Assert.Equal(string.Empty, output.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Validates that a typed read of a finding of another type answers null rather than throwing.
    /// </summary>
    [Fact]
    public void OperationResult_FindingAs_OtherType_ReturnsNull()
    {
        // Arrange: a result carrying a finding of one type
        var answer = new RuleOwnerAnswer
        {
            Ownership = RuleOwnership.SingleOwner,
            OwningFile = "owner.md",
            Evidence = "I read the files."
        };
        var carried = new OperationResult(OperationOutcome.Succeeded, answer);
        var empty = new OperationResult(OperationOutcome.Succeeded);

        // Act / Assert: the expected type comes back, anything else and an absent finding come back null
        Assert.Multiple(
            () => Assert.Same(answer, carried.FindingAs<RuleOwnerAnswer>()),
            () => Assert.Null(carried.FindingAs<string>()),
            () => Assert.Null(empty.FindingAs<RuleOwnerAnswer>()),
            () => Assert.Null(empty.Finding));
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-invocation-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RecordingOperation : IOperation
    {
        public CancellationToken Received { get; private set; }

        public string Name => "recording";

        public OperationCategory Category => OperationCategory.Research;

        public ModelRole? RequiredRole => null;

        public string Summary => "Records the signal it was handed";

        public string Usage => "usage: dotnet anneal recording - takes no arguments";

        public Task<OperationResult> ExecuteAsync(
            IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
        {
            Received = cancellationToken;
            return Task.FromResult(new OperationResult(OperationOutcome.Succeeded));
        }
    }

    private sealed class CountingEndpoint : IChatEndpoint
    {
        public int Calls { get; private set; }

        public Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ChatTurnResult("{}"));
        }
    }
}
