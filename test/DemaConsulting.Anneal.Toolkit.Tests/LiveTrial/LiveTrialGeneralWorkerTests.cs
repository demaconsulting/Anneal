using System;
using System.Collections.Generic;
using System.Linq;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     Real, end-to-end live trials of <c>GeneralWorker</c> covering both the new docs-only cheap floor and the
///     unchanged Large structural path.
/// </summary>
/// <remarks>
///     Interior test, not a contract test: it lives beside the harness rather than under <c>Contract/</c>, names
///     no clause, and is never linked by <c>check-contracts</c>. It is gated behind
///     <see cref="LiveTrialFixture.GateEnvironmentVariable" /> and skips by default - a plain <c>dotnet test</c>
///     or <c>pwsh ./build.ps1</c> never reaches a real model or a real process here.
/// </remarks>
public sealed class LiveTrialGeneralWorkerTests
{
    [Fact]
    public async Task LiveTrial_GeneralWorkerSkipsVerifierForDocsOnlyMarkdownEdit_Succeeds()
    {
        // Arrange: a tiny docs-only repository with an obvious typo and no Contract section to touch.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: run the general worker directly at Small effort against a docs-only markdown edit.
        var trial = await RunGeneralWorkerTrialAsync(
                fixture => fixture.WriteFile(
                    "docs/guide.md",
                    """
                    # Worker Guide

                    Teh worker fixes small problems carefully.
                    """),
                "seed: add docs/guide.md with a typo",
                "Fix the typo in docs/guide.md so \"Teh worker\" reads \"The worker\".",
                ["docs/guide.md"],
                Effort.Small,
                cancellationToken);

        // Assert: the deterministic step trace proves the docs-only verifier skip fired.
        AssertCompletedRunIncludesCommonSteps(trial);
        Assert.Multiple(
            () => AssertContainsStep(trial.Steps, "Preflight:CodeOnly"),
            () => AssertContainsStep(trial.Steps, "Verifier:skipped"),
            () => AssertDoesNotContainStep(trial.Steps, "Planner"),
            () => AssertDoesNotContainStep(trial.Steps, "DocumentAuthor"),
            () => AssertDoesNotContainStep(trial.Steps, "DeterministicCheck:check-contracts"),
            () => AssertDoesNotContainStep(trial.Steps, "ArchDocAgreementGate"),
            () => AssertDoesNotContainStep(trial.Steps, "Verifier"),
            () => Assert.Equal(1, CountStep(trial.Steps, "Developer")));

        Assert.Contains("+The worker fixes small problems carefully.", trial.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveTrial_GeneralWorkerMediumContractAndCodeRequestRunsDocumentPathWithoutPlanner_Succeeds()
    {
        // Arrange: retry the same live trial because this path depends on a real model reaching a narrow edit.
        var cancellationToken = TestContext.Current.CancellationToken;
        var passed = false;
        var diagnostic = string.Empty;

        // Act: run the document-first medium path, preserving the existing three-attempt allowance.
        for (var attempt = 1; attempt <= 3 && !passed; attempt++)
        {
            var trial = await RunGeneralWorkerTrialAsync(
                    SeedWidgetWithContract,
                    "seed: add Widget source and contract doc",
                    "Update src/Widget.cs so Widget.Describe() returns \"calibrated widget\" instead of " +
                    "\"old widget\", and update .anneal/architecture/widget.md in place so its prose and " +
                    "WIDGET-01 contract clause match. Keep this to the existing Widget system only. Do not " +
                    "create, rename, split, or merge any architecture documents.",
                    ["src/Widget.cs", ".anneal/architecture/widget.md"],
                    Effort.Medium,
                    cancellationToken);

            passed = MediumDocumentPathSucceeded(trial);
            diagnostic = DescribeAttempt(attempt, trial);
        }

        // Assert: the medium path updated both code and contract docs without invoking the planner.
        Assert.True(passed, diagnostic);
    }

    [Fact]
    public async Task LiveTrial_GeneralWorkerLargeStructuralRequestStillRunsVerifier_Succeeds()
    {
        // Arrange: a structural architecture fixture that should still take the planned Large path.
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act: run the general worker directly at Large effort against the structural rename request.
        var trial = await RunGeneralWorkerTrialAsync(
                SeedWidgetWithOverviewAndContract,
                "seed: add Widget source and architecture docs",
                "This structural change renames the public type inside src/Widget.cs from Widget to " +
                "CalculationWidget, keeps the file path src/Widget.cs unchanged, and updates " +
                ".anneal/architecture/overview.md plus .anneal/architecture/widget.md in place to match. " +
                "Do not create or rename architecture documents.",
                ["src/Widget.cs", ".anneal/architecture/overview.md", ".anneal/architecture/widget.md"],
                Effort.Large,
                cancellationToken);

        // Assert: the Large structural path still plans, verifies, and updates existing documents in place.
        AssertCompletedRunIncludesCommonSteps(trial);
        Assert.Multiple(
            () => AssertContainsStep(trial.Steps, "Preflight:PlanAndDocument"),
            () => AssertContainsStep(trial.Steps, "Planner"),
            () => AssertContainsStep(trial.Steps, "DocumentAuthor"),
            () => AssertContainsStep(trial.Steps, "ArchDocAgreementGate"),
            () => AssertContainsStep(trial.Steps, "Verifier"));
        Assert.Multiple(
            () => Assert.Contains("+public sealed class CalculationWidget", trial.Diff, StringComparison.Ordinal),
            () => Assert.DoesNotContain("calculation-widget.md", trial.Diff, StringComparison.OrdinalIgnoreCase),
            () => Assert.Contains("diff --git a/.anneal/architecture/overview.md", trial.Diff, StringComparison.Ordinal),
            () => Assert.Contains("diff --git a/.anneal/architecture/widget.md", trial.Diff, StringComparison.Ordinal),
            () => Assert.Contains("CalculationWidget", trial.Diff, StringComparison.Ordinal));
    }

    private static async Task<LiveTrialRun> RunGeneralWorkerTrialAsync(
        Action<LiveTrialFixture> seedRepository,
        string seedCommitMessage,
        string workItem,
        IReadOnlyList<string> changedFileHints,
        Effort effort,
        CancellationToken cancellationToken)
    {
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken).ConfigureAwait(false);
        seedRepository(fixture);
        await fixture.CommitAllAsync(seedCommitMessage, cancellationToken).ConfigureAwait(false);

        var (result, steps) = await fixture.RunGeneralWorkerAsync(workItem, changedFileHints, effort, cancellationToken)
            .ConfigureAwait(false);
        var status = await fixture.GitStatusAsync(cancellationToken).ConfigureAwait(false);
        var diff = await fixture.GitDiffAsync(cancellationToken).ConfigureAwait(false);

        var trial = new LiveTrialRun(result, steps, status, diff);
        WriteDiagnostics(trial);
        return trial;
    }

    private static void SeedWidgetWithContract(LiveTrialFixture fixture)
    {
        fixture.WriteFile(
            "src/Widget.cs",
            """
            namespace Trial;

            public sealed class Widget
            {
                public string Describe() => "old widget";
            }
            """);
        fixture.WriteFile(
            ".anneal/architecture/widget.md",
            """
            ---
            covers:
              - src/Widget.cs
            ---

            # Widget

            Widget exposes the description string "old widget" for callers.

            ## Contract

            ### Provides

            - **WIDGET-01** — Widget returns the description string "old widget".
              *Verified by:* `TODO.WidgetDescriptionIsStable`
            """);
    }

    private static void SeedWidgetWithOverviewAndContract(LiveTrialFixture fixture)
    {
        fixture.WriteFile(
            "src/Widget.cs",
            """
            namespace Trial;

            public sealed class Widget
            {
                public string Describe() => "old widget";
            }
            """);
        fixture.WriteFile(
            ".anneal/architecture/overview.md",
            """
            # Overview

            A single-system fixture repository for a live trial.

            - **Widget** - exposes a simple description string.
            """);
        fixture.WriteFile(
            ".anneal/architecture/widget.md",
            """
            ---
            covers:
              - src/Widget.cs
            ---

            # Widget

            Widget is the repository's sample type for this live trial.

            ## Contract

            ### Provides

            - **WIDGET-01** — The sample type exposes a stable description string.
              *Verified by:* `TODO.WidgetDescriptionIsStable`
            """);
    }

    private static void AssertCompletedRunIncludesCommonSteps(LiveTrialRun trial) =>
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Succeeded, trial.Result.Outcome),
            () => Assert.IsType<WorkerRunResult.Completed>(trial.Result.Finding),
            () => AssertContainsStep(trial.Steps, "Developer"),
            () => AssertContainsStep(trial.Steps, "DeterministicCheck:build.ps1"),
            () => AssertContainsStep(trial.Steps, "DiffCheck"));

    private static bool MediumDocumentPathSucceeded(LiveTrialRun trial) =>
        trial.Result.Outcome == OperationOutcome.Succeeded &&
        trial.Result.Finding is WorkerRunResult.Completed &&
        HasStep(trial.Steps, "Preflight:Document") &&
        HasStep(trial.Steps, "DocumentAuthor") &&
        HasStep(trial.Steps, "Developer") &&
        HasStep(trial.Steps, "DeterministicCheck:build.ps1") &&
        HasStep(trial.Steps, "DiffCheck") &&
        HasStep(trial.Steps, "DeterministicCheck:check-contracts") &&
        HasStep(trial.Steps, "ArchDocAgreementGate") &&
        HasStep(trial.Steps, "Verifier") &&
        !HasStep(trial.Steps, "Planner") &&
        CountStep(trial.Steps, "DocumentAuthor") == 1 &&
        CountStep(trial.Steps, "Developer") == 1 &&
        trial.Diff.Contains("+    public string Describe() => \"calibrated widget\";", StringComparison.Ordinal) &&
        trial.Diff.Contains("+Widget exposes the description string \"calibrated widget\" for callers.", StringComparison.Ordinal) &&
        trial.Diff.Contains("+- **WIDGET-01** — Widget returns the description string \"calibrated widget\".", StringComparison.Ordinal);

    private static void AssertContainsStep(IReadOnlyList<string> steps, string step) =>
        Assert.Contains(steps, line => line.Contains($"\"{step}\""));

    private static void AssertDoesNotContainStep(IReadOnlyList<string> steps, string step) =>
        Assert.DoesNotContain(steps, line => line.Contains($"\"{step}\""));

    private static bool HasStep(IReadOnlyList<string> steps, string step) =>
        steps.Any(line => line.Contains($"\"{step}\""));

    private static int CountStep(IReadOnlyList<string> steps, string step) =>
        steps.Count(line => line.Contains($"\"{step}\""));

    private static void WriteDiagnostics(LiveTrialRun trial)
    {
        Console.WriteLine($"general worker outcome: {trial.Result.Outcome}");
        Console.WriteLine($"general worker finding: {DescribeFinding(trial.Result.Finding)}");
        Console.WriteLine("process steps:");
        Console.WriteLine(string.Join("\n", trial.Steps));
        Console.WriteLine("git status:");
        Console.WriteLine(trial.Status);
        Console.WriteLine("git diff:");
        Console.WriteLine(trial.Diff);
    }

    private static string DescribeAttempt(int attempt, LiveTrialRun trial) =>
        $"attempt={attempt}; outcome={trial.Result.Outcome}; finding={DescribeFinding(trial.Result.Finding)}\n" +
        $"steps:\n{string.Join("\n", trial.Steps)}\nstatus:\n{trial.Status}\ndiff:\n{trial.Diff}";

    private static string DescribeFinding(WorkerRunResult? finding) =>
        finding switch
        {
            WorkerRunResult.Completed completed =>
                $"{completed.Summary.Summary} | files: {string.Join(", ", completed.Summary.FilesChanged)}",
            WorkerRunResult.Reroute reroute =>
                $"reroute: {reroute.Why} | suggested worker: {reroute.SuggestedWorker ?? "(none)"}",
            null => "(none)",
            _ => finding.ToString() ?? "(unknown)"
        };

    private sealed record LiveTrialRun(
        WorkerExecutionResult Result,
        IReadOnlyList<string> Steps,
        string Status,
        string Diff);
}
