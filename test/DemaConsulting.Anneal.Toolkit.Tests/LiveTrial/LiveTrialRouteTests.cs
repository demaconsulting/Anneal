using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     One real, end-to-end live trial of <c>route</c> proving <see cref="LiveTrialFixture" /> actually works:
///     a tiny fixture repo, an obvious Small Fix work item, a real routed run, and a real grading oracle.
/// </summary>
/// <remarks>
///     Interior test, not a contract test: it lives beside the harness rather than under <c>Contract/</c>, names
///     no clause, and is never linked by <c>check-contracts</c>. It is gated behind
///     <see cref="LiveTrialFixture.GateEnvironmentVariable" /> and skips by default - a plain <c>dotnet test</c>
///     or <c>pwsh ./build.ps1</c> never reaches a real model or a real process here.
/// </remarks>
public sealed class LiveTrialRouteTests
{
    [Fact]
    public async Task LiveTrial_RouteHandlesAnObviousSmallFix_Succeeds()
    {
        // Arrange: skip unless the live-trial gate is explicitly set - this test makes real model calls
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        var cancellationToken = TestContext.Current.CancellationToken;

        await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken);

        // Arrange: a tiny, one-file repository with an obvious, narrowly-scoped defect
        fixture.WriteFile(
            "Arithmetic.txt",
            """
            This file states a simple arithmetic fact.

            Two plus two equals 5.
            """);
        await fixture.CommitAllAsync("seed: add Arithmetic.txt with a wrong sum", cancellationToken);

        // Act: route an obvious Small Fix work item against the real Router and real workers
        var (exitCode, output) = await fixture.RunRouteAsync(
            "Fix the arithmetic in Arithmetic.txt - \"Two plus two equals 5\" should read " +
            "\"Two plus two equals 4\".",
            ["Arithmetic.txt"],
            cancellationToken);

        var status = await fixture.GitStatusAsync(cancellationToken);
        var diff = await fixture.GitDiffAsync(cancellationToken);

        // Act: grade the observed outcome against the stated expectation with a real model-backed oracle. The
        // '.anneal/' directory is the Toolkit's own transcript bookkeeping, an expected side effect of any real
        // invocation, and is called out explicitly so the oracle does not read it as an unrelated change.
        var verdict = await fixture.GradeAsync(
            "Arithmetic.txt states \"Two plus two equals 4\", and no file changed other than that one and the " +
            "Toolkit's own '.anneal/' transcript bookkeeping.",
            $"route exit code: {exitCode}\nroute output:\n{output}\ngit status:\n{status}\ngit diff:\n{diff}",
            cancellationToken);

        // Assert: the oracle had enough evidence and judged the fix correct
        Assert.Multiple(
            () => Assert.True(verdict.HasSufficientEvidence, $"oracle had insufficient evidence: {verdict.Reasoning}"),
            () => Assert.True(verdict.Passed, $"oracle judged the trial failed: {verdict.Reasoning}"));
    }
}
