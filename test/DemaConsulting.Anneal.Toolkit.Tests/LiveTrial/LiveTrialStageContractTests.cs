using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     One real, end-to-end live trial of <c>stage-contract</c> proving the new operation actually works against
///     a real model: a tiny fixture repository with an existing system contract document, an obvious new
///     capability to stage a clause for, a real run, and a real grading oracle.
/// </summary>
/// <remarks>
///     Interior test, not a contract test: it lives beside the harness rather than under <c>Contract/</c>, names
///     no clause, and is never linked by <c>check-contracts</c>. It is gated behind
///     <see cref="LiveTrialFixture.GateEnvironmentVariable" /> and skips by default - a plain <c>dotnet test</c>
///     or <c>pwsh ./build.ps1</c> never reaches a real model or a real process here.
///     <para>
///         This is the live-trial validation the "declare → build → live-validate → retire" discipline requires
///         before <c>architecture-update.agent.md</c> can retire (see <c>MIGRATION.md</c>'s S17 entry): a compiled
///         path existing and passing fake-endpoint unit tests is not the same claim as it holding up against a
///         real model's real reasoning and real tool calls.
///     </para>
/// </remarks>
public sealed class LiveTrialStageContractTests
{
    [Fact]
    public async Task LiveTrial_StageContractStagesAWellFormedTodoClause_Succeeds()
    {
        // Arrange: skip unless the live-trial gate is explicitly set - this test makes real model calls
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        var cancellationToken = TestContext.Current.CancellationToken;

        await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken);

        // Arrange: a tiny fixture repository with one existing system contract document, deliberately missing
        // a clause for a capability its own prose already describes - an obvious in-bound "stage a clause"
        // work item.
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
              *Verified by:* `TODO.ConfigurationValidityIsReported`
            """);
        await fixture.CommitAllAsync("seed: add Widget's system document, missing its rejection clause", cancellationToken);

        // Act: run stage-contract against the real DocumentAuthor with an obvious, narrow work item: stage one
        // new clause, in the TODO. planned-obligation form, for a capability the prose already names but the
        // Contract section does not yet promise.
        var (exitCode, output) = await fixture.RunStageContractAsync(
            "Widget also reports a configuration file invalid, naming the first reason, when it fails to parse " +
            "or is missing a required field - but this is not yet implemented. Stage a new contract clause for " +
            "this in docs/architecture/widget.md, using system-contracts.md's TODO. planned-obligation form for " +
            "its verifier, since no test exists yet. Do not touch any file outside docs/architecture/.",
            cancellationToken);

        var status = await fixture.GitStatusAsync(cancellationToken);
        var diff = await fixture.GitDiffAsync(cancellationToken);

        // Act: grade the observed outcome against the stated expectation with a real model-backed oracle. The
        // '.anneal/' directory is the Toolkit's own transcript bookkeeping, an expected side effect of any real
        // invocation, and is called out explicitly so the oracle does not read it as an unrelated change.
        var verdict = await fixture.GradeAsync(
            "stage-contract reported completing (not escalating or failing); docs/architecture/widget.md gained " +
            "a new contract clause for reporting an invalid configuration and naming the first reason; that " +
            "clause's *Verified by:* line names a verifier whose text begins with the literal characters " +
            "'TODO.' or 'TODO_' (case-sensitive) - the specific word or words following that prefix do not " +
            "matter, since system-contracts.md's rule is only about the prefix, not whether the rest of the " +
            "name looks like a real test; and no file changed other than that one and the Toolkit's own " +
            "'.anneal/' transcript bookkeeping.",
            $"stage-contract exit code: {exitCode}\nstage-contract output:\n{output}\ngit status:\n{status}\n" +
            $"git diff:\n{diff}",
            cancellationToken);

        // Assert: the oracle had enough evidence and judged the staged clause correct
        Assert.Multiple(
            () => Assert.True(verdict.HasSufficientEvidence, $"oracle had insufficient evidence: {verdict.Reasoning}"),
            () => Assert.True(verdict.Passed, $"oracle judged the trial failed: {verdict.Reasoning}"));
    }
}
