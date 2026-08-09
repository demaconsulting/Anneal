using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     One real, end-to-end live trial of <c>verify-change</c> proving the new operation actually works against
///     a real model: a tiny fixture repository at a committed baseline, an honest and narrowly-scoped
///     uncommitted change, a real run reading the real diff, and a real grading oracle.
/// </summary>
/// <remarks>
///     Interior test, not a contract test: it lives beside the harness rather than under <c>Contract/</c>, names
///     no clause, and is never linked by <c>check-contracts</c>. It is gated behind
///     <see cref="LiveTrialFixture.GateEnvironmentVariable" /> and skips by default - a plain <c>dotnet test</c>
///     or <c>pwsh ./build.ps1</c> never reaches a real model or a real process here.
///     <para>
///         Unlike the other live trials, this one exercises <see cref="Primitives.DiffCheck" /> for real too, not
///         only <c>Verifier</c>'s judgement pass: <c>RunVerifyChangeAsync</c> substitutes no <c>runGit</c>, so the
///         diff read is a real <c>git diff HEAD</c> against this fixture's real working tree.
///     </para>
/// </remarks>
public sealed class LiveTrialVerifyChangeTests
{
    [Fact]
    public async Task LiveTrial_VerifyChangeJudgesAnHonestSmallFix_Succeeds()
    {
        // Arrange: skip unless the live-trial gate is explicitly set - this test makes real model calls
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        var cancellationToken = TestContext.Current.CancellationToken;

        await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken);

        // Arrange: a committed baseline - a system contract document with one real, already-fulfilled clause -
        // so verify-change has a real HEAD to diff the uncommitted change below against.
        fixture.WriteFile(
            "docs/architecture/overview.md",
            """
            # Overview

            A single-system fixture repository for a live trial.

            - **Widget** - reads a configuration file and reports whether it is valid.
            """);
        fixture.WriteFile(
            "docs/architecture/widget.md",
            """
            # Widget

            Widget reads `widget.config` and validates it against the schema it owns.

            ## Contract

            ### Provides

            - **WIDGET-01** - Widget reports a configuration file valid when it parses and every required field
              is present.
              *Verified by:* `WidgetTests.ConfigurationValidityIsReported`
            """);
        fixture.WriteFile(
            "Arithmetic.txt",
            """
            This file states a simple arithmetic fact.

            Two plus two equals 5.
            """);
        await fixture.CommitAllAsync("seed: add Widget's system document and a wrong sum", cancellationToken);

        // Arrange: an honest, narrowly-scoped, uncommitted Small Fix - it touches no contract document and
        // changes nothing about what Widget promises, so verify-change's own honesty and conformance questions
        // both have an obvious "yes" answer.
        fixture.WriteFile(
            "Arithmetic.txt",
            """
            This file states a simple arithmetic fact.

            Two plus two equals 4.
            """);

        // Act: run verify-change against the real Verifier, diffing the uncommitted fix above against HEAD
        var (exitCode, output) = await fixture.RunVerifyChangeAsync(null, cancellationToken);

        var status = await fixture.GitStatusAsync(cancellationToken);
        var diff = await fixture.GitDiffAsync(cancellationToken);

        // Act: grade the observed outcome against the stated expectation with a real model-backed oracle.
        var verdict = await fixture.GradeAsync(
            "verify-change reported completing with no concerns: the build check and the strict contract check " +
            "both passed, and the verifier found no contract-conformance, scope-honesty, or architecture-tree " +
            "concern, since the uncommitted change is an honest, narrowly-scoped arithmetic fix touching only " +
            "Arithmetic.txt and no contract document.",
            $"verify-change exit code: {exitCode}\nverify-change output:\n{output}\ngit status:\n{status}\n" +
            $"git diff:\n{diff}",
            cancellationToken);

        // Assert: the oracle had enough evidence and judged the trial correct
        Assert.Multiple(
            () => Assert.True(verdict.HasSufficientEvidence, $"oracle had insufficient evidence: {verdict.Reasoning}"),
            () => Assert.True(verdict.Passed, $"oracle judged the trial failed: {verdict.Reasoning}"));
    }
}
