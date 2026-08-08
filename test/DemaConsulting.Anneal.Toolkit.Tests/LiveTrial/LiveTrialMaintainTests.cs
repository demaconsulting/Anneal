using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     One real, end-to-end live trial of <c>maintain</c> proving the new operation actually works against a
///     real model: a tiny fixture repo, an obvious in-bound Small-Fix-shaped work item, a real run, and a real
///     grading oracle.
/// </summary>
/// <remarks>
///     Interior test, not a contract test: it lives beside the harness rather than under <c>Contract/</c>, names
///     no clause, and is never linked by <c>check-contracts</c>. It is gated behind
///     <see cref="LiveTrialFixture.GateEnvironmentVariable" /> and skips by default - a plain <c>dotnet test</c>
///     or <c>pwsh ./build.ps1</c> never reaches a real model or a real process here.
/// </remarks>
public sealed class LiveTrialMaintainTests
{
    [Fact]
    public async Task LiveTrial_MaintainHandlesAnObviousInBoundSmallFix_Succeeds()
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

        // Act: run maintain against the real SmallFixWorker with a declared bound covering the one file this
        // Maintenance work item should ever need to touch.
        var (exitCode, output) = await fixture.RunMaintainAsync(
            "Fix the arithmetic in Arithmetic.txt - \"Two plus two equals 5\" should read " +
            "\"Two plus two equals 4\". This is bounded Maintenance work: touch only Arithmetic.txt.",
            ["Arithmetic.txt"],
            cancellationToken);

        var status = await fixture.GitStatusAsync(cancellationToken);
        var diff = await fixture.GitDiffAsync(cancellationToken);

        // Act: grade the observed outcome against the stated expectation with a real model-backed oracle. The
        // '.anneal/' directory is the Toolkit's own transcript bookkeeping, an expected side effect of any real
        // invocation, and is called out explicitly so the oracle does not read it as an unrelated change.
        var verdict = await fixture.GradeAsync(
            "Arithmetic.txt states \"Two plus two equals 4\", the maintain run reported completing (not " +
            "escalating or failing), and no file changed other than that one and the Toolkit's own '.anneal/' " +
            "transcript bookkeeping.",
            $"maintain exit code: {exitCode}\nmaintain output:\n{output}\ngit status:\n{status}\ngit diff:\n{diff}",
            cancellationToken);

        // Assert: the oracle had enough evidence and judged the fix correct
        Assert.Multiple(
            () => Assert.True(verdict.HasSufficientEvidence, $"oracle had insufficient evidence: {verdict.Reasoning}"),
            () => Assert.True(verdict.Passed, $"oracle judged the trial failed: {verdict.Reasoning}"));
    }
}
