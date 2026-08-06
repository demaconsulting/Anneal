using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Recording;
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

            Assert.Multiple(
                () => Assert.Equal(0, endpoint.Calls),
                () => Assert.Equal(0, endpoint.Enumerations));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Validates that bare "help" points a caller toward "help &lt;action&gt;" for detail on one action, on
    ///     top of the listing TOOLKIT-12 requires. This is interior, not contract: TOOLKIT-12 only promises the
    ///     listing and the success exit; the hint is a discoverability nicety layered over that, so widening or
    ///     rewording it is not a contract change and must not be pinned down by a contract test.
    /// </summary>
    [Fact]
    public async Task AnnealTool_RunAsync_Help_HintsAtHelpAction()
    {
        // Arrange
        var output = new StringWriter();

        // Act: "dotnet anneal help", with no action to describe
        await AnnealTool.RunAsync(["help"], output, TestContext.Current.CancellationToken);

        // Assert: the listing points a caller toward "help <action>" for more detail on one action
        Assert.Contains("help <action>", output.ToString(), StringComparison.Ordinal);
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

    /// <summary>
    ///     Validates that a deterministic action makes no provider call at all — not even the availability
    ///     enquiry that role resolution depends on.
    /// </summary>
    /// <remarks>
    ///     This is the property that lets a deterministic check gate a build: a gate must not depend on a
    ///     network. The model-backed operation is registered alongside the deterministic one and holds the
    ///     endpoint, so "nothing was asked" is a fact about what the dispatched run did rather than about which
    ///     objects the test happened to build.
    /// </remarks>
    [Fact]
    public async Task AnnealTool_RunAsync_DeterministicAction_EnquiresAboutNoModel()
    {
        // Arrange: a report that verifies cleanly, and a model-backed operation standing by but not invoked
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllLines(Path.Combine(root, "subject.txt"), ["first line", "the promise this cites"]);
            File.WriteAllLines(
                Path.Combine(root, "report.md"),
                ["**Result**: SUCCEEDED", "", "`subject.txt:2` - \"the promise this cites\""]);

            var endpoint = new CountingEndpoint();

            // Act: run the deterministic check
            var exitCode = await AnnealTool.RunAsync(
                ["verify-evidence", "report.md"],
                TextWriter.Null,
                [new VerifyEvidenceOperation(root), new ProbeRuleOwnerOperation(root, _ => endpoint)],
                root,
                TestContext.Current.CancellationToken);

            // Assert: it ran, and it asked the provider nothing whatsoever
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Equal(0, endpoint.Enumerations),
                () => Assert.Equal(0, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Validates that an availability enquiry which fails does not fail the resolution: the role falls back
    ///     to its first configured candidate and the call goes on to succeed or fail on its own terms.
    /// </summary>
    [Fact]
    public async Task ModelRoles_ResolveModelAsync_EnquiryFails_TakesTheFirstCandidate()
    {
        // Arrange: a repository naming two candidates, and an endpoint that cannot say what it offers
        var root = CreateTemporaryDirectory();
        try
        {
            WriteCandidates(root, "the-preferred-light-model", "a-rearguard-light-model");
            var endpoint = new CountingEndpoint { EnquiryFails = true };
            var roles = new ModelRoles(root, _ => endpoint);

            // Act
            var resolved = await roles.ResolveModelAsync(ModelRole.Light, TestContext.Current.CancellationToken);

            // Assert: the enquiry was made, its failure was not a gate, and the guess is the stated preference
            Assert.Multiple(
                () => Assert.Equal(1, endpoint.Enumerations),
                () => Assert.Equal("the-preferred-light-model", resolved));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Validates that a caller who withdraws during the availability enquiry is reported as a withdrawal,
    ///     rather than being mistaken for a provider that could not answer and folded into the first-candidate
    ///     fallback.
    /// </summary>
    [Fact]
    public async Task ModelRoles_ResolveModelAsync_EnquiryCancelled_PropagatesTheWithdrawal()
    {
        // Arrange: a repository naming two candidates, and an enquiry the caller withdraws from mid-flight
        var root = CreateTemporaryDirectory();
        try
        {
            WriteCandidates(root, "the-preferred-light-model", "a-rearguard-light-model");
            using var cancellation = new CancellationTokenSource();
            var endpoint = new CountingEndpoint { WithdrawDuringEnquiry = cancellation };
            var roles = new ModelRoles(root, _ => endpoint);

            // Act / Assert: the withdrawal comes out as a withdrawal, not as a fallback to the first candidate
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => roles.ResolveModelAsync(ModelRole.Light, cancellation.Token));

            Assert.Equal(1, endpoint.Enumerations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Validates that a role whose every candidate has been retired fails before any model is consulted, and
    ///     so writes no transcript: the capture guarantee covers interactions, and this is not one.
    /// </summary>
    [Fact]
    public async Task ModelSession_RunAsync_EveryCandidateRetired_WritesNoTranscript()
    {
        // Arrange: a repository whose only candidates the provider does not offer
        var root = CreateTemporaryDirectory();
        try
        {
            WriteCandidates(root, "a-retired-light-model");
            var endpoint = new CountingEndpoint { Offers = ["some-other-model"] };
            var session = new ModelSession(new ModelRoles(root, _ => endpoint), "a charter");

            // Act / Assert: the resolution fails, and it fails without reaching the endpoint
            await Assert.ThrowsAsync<ModelUnavailableException>(
                () => session.RunAsync("a question", ModelRole.Light, TestContext.Current.CancellationToken));

            Assert.Multiple(
                () => Assert.Equal(0, endpoint.Calls),
                () => Assert.False(File.Exists(RecordStore.TranscriptsPathFor(root))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Validates that a role is resolved once and then remembered, so a multi-turn conversation pays for one
    ///     enquiry rather than one per turn.
    /// </summary>
    [Fact]
    public async Task ModelRoles_ResolveModelAsync_CalledRepeatedly_EnquiresOnce()
    {
        // Arrange: a repository whose leading candidate has been retired, and a provider that says so
        var root = CreateTemporaryDirectory();
        try
        {
            WriteCandidates(root, "a-retired-light-model", "a-surviving-light-model");
            var endpoint = new CountingEndpoint { Offers = ["a-surviving-light-model"] };
            var roles = new ModelRoles(root, _ => endpoint);

            // Act
            var first = await roles.ResolveModelAsync(ModelRole.Light, TestContext.Current.CancellationToken);
            var second = await roles.ResolveModelAsync(ModelRole.Light, TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal("a-surviving-light-model", first),
                () => Assert.Equal("a-surviving-light-model", second),
                () => Assert.Equal(1, endpoint.Enumerations));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteCandidates(string root, params string[] light)
    {
        var path = Path.Combine(root, ModelConfiguration.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var names = string.Join(", ", light.Select(name => $"\"{name}\""));
        File.WriteAllText(path, "{\"models\": {\"light\": [" + names + "]}}");
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

        public int Enumerations { get; private set; }

        /// <summary>
        ///     What this endpoint says its account is offered. Empty is "nothing stated", which is the answer a
        ///     test gives when it is not the availability behavior under test.
        /// </summary>
        public IReadOnlyCollection<string> Offers { get; init; } = [];

        /// <summary>
        ///     Whether the availability enquiry throws rather than answering, standing in for a provider that
        ///     could not be reached to be asked.
        /// </summary>
        public bool EnquiryFails { get; init; }

        /// <summary>
        ///     A source the availability enquiry cancels as it runs, standing in for a caller who withdraws
        ///     while the provider is being asked what it offers.
        /// </summary>
        public CancellationTokenSource? WithdrawDuringEnquiry { get; init; }

        public Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ChatTurnResult("{}"));
        }

        public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken)
        {
            Enumerations++;

            if (WithdrawDuringEnquiry is not null)
            {
                WithdrawDuringEnquiry.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return EnquiryFails
                ? Task.FromException<IReadOnlyCollection<string>>(
                    new InvalidOperationException("the model list could not be read"))
                : Task.FromResult(Offers);
        }
    }
}
