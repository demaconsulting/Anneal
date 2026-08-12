using System;
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
        // Arrange: skip unless the live-trial gate is explicitly set - this test makes real model calls
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        var cancellationToken = TestContext.Current.CancellationToken;

        await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken);

        // Arrange: a tiny docs-only repository with an obvious typo and no Contract section to touch.
        fixture.WriteFile(
            "docs/guide.md",
            """
            # Worker Guide

            Teh worker fixes small problems carefully.
            """);
        await fixture.CommitAllAsync("seed: add docs/guide.md with a typo", cancellationToken);

        // Act: run the general worker directly at Small effort against a docs-only markdown edit.
        var (result, steps) = await fixture.RunGeneralWorkerAsync(
            "Fix the typo in docs/guide.md so \"Teh worker\" reads \"The worker\".",
            ["docs/guide.md"],
            Effort.Small,
            cancellationToken);

        var status = await fixture.GitStatusAsync(cancellationToken);
        var diff = await fixture.GitDiffAsync(cancellationToken);

        Console.WriteLine($"general worker outcome: {result.Outcome}");
        Console.WriteLine($"general worker finding: {DescribeFinding(result.Finding)}");
        Console.WriteLine("process steps:");
        Console.WriteLine(string.Join("\n", steps));
        Console.WriteLine("git status:");
        Console.WriteLine(status);
        Console.WriteLine("git diff:");
        Console.WriteLine(diff);

        // Assert: the deterministic step trace proves the docs-only verifier skip fired.
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
            () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
            () => Assert.Contains(steps, line => line.Contains("\"Preflight:CodeOnly\"")),
            () => Assert.Contains(steps, line => line.Contains("\"Developer\"")),
            () => Assert.Contains(steps, line => line.Contains("\"DeterministicCheck:build.ps1\"")),
            () => Assert.Contains(steps, line => line.Contains("\"DiffCheck\"")),
            () => Assert.Contains(steps, line => line.Contains("\"Verifier:skipped\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"Planner\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"DocumentAuthor\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"DeterministicCheck:check-contracts\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"ArchDocAgreementGate\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"Verifier\"")),
            () => Assert.Equal(1, steps.Count(line => line.Contains("\"Developer\""))));

        Assert.Contains("+The worker fixes small problems carefully.", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveTrial_GeneralWorkerMediumContractAndCodeRequestRunsDocumentPathWithoutPlanner_Succeeds()
    {
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        var cancellationToken = TestContext.Current.CancellationToken;
        var passed = false;
        var diagnostic = string.Empty;

        for (var attempt = 1; attempt <= 3 && !passed; attempt++)
        {
            await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken);

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
            await fixture.CommitAllAsync("seed: add Widget source and contract doc", cancellationToken);

            var (result, steps) = await fixture.RunGeneralWorkerAsync(
                "Update src/Widget.cs so Widget.Describe() returns \"calibrated widget\" instead of " +
                "\"old widget\", and update .anneal/architecture/widget.md in place so its prose and " +
                "WIDGET-01 contract clause match. Keep this to the existing Widget system only. Do not " +
                "create, rename, split, or merge any architecture documents.",
                ["src/Widget.cs", ".anneal/architecture/widget.md"],
                Effort.Medium,
                cancellationToken);

            var status = await fixture.GitStatusAsync(cancellationToken);
            var diff = await fixture.GitDiffAsync(cancellationToken);

            passed =
                result.Outcome == OperationOutcome.Succeeded &&
                result.Finding is WorkerRunResult.Completed &&
                steps.Any(line => line.Contains("\"Preflight:Document\"")) &&
                steps.Any(line => line.Contains("\"DocumentAuthor\"")) &&
                steps.Any(line => line.Contains("\"Developer\"")) &&
                steps.Any(line => line.Contains("\"DeterministicCheck:build.ps1\"")) &&
                steps.Any(line => line.Contains("\"DiffCheck\"")) &&
                steps.Any(line => line.Contains("\"DeterministicCheck:check-contracts\"")) &&
                steps.Any(line => line.Contains("\"ArchDocAgreementGate\"")) &&
                steps.Any(line => line.Contains("\"Verifier\"")) &&
                !steps.Any(line => line.Contains("\"Planner\"")) &&
                steps.Count(line => line.Contains("\"DocumentAuthor\"")) == 1 &&
                steps.Count(line => line.Contains("\"Developer\"")) == 1 &&
                diff.Contains("+    public string Describe() => \"calibrated widget\";", StringComparison.Ordinal) &&
                diff.Contains("+Widget exposes the description string \"calibrated widget\" for callers.", StringComparison.Ordinal) &&
                diff.Contains("+- **WIDGET-01** — Widget returns the description string \"calibrated widget\".", StringComparison.Ordinal);

            diagnostic =
                $"attempt={attempt}; outcome={result.Outcome}; finding={DescribeFinding(result.Finding)}\n" +
                $"steps:\n{string.Join("\n", steps)}\nstatus:\n{status}\ndiff:\n{diff}";
        }

        Assert.True(passed, diagnostic);
    }

    [Fact]
    public async Task LiveTrial_GeneralWorkerLargeStructuralRequestStillRunsVerifier_Succeeds()
    {
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        var cancellationToken = TestContext.Current.CancellationToken;

        await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken);

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
        await fixture.CommitAllAsync("seed: add Widget source and architecture docs", cancellationToken);

        var (result, steps) = await fixture.RunGeneralWorkerAsync(
            "This structural change renames the public type inside src/Widget.cs from Widget to " +
            "CalculationWidget, keeps the file path src/Widget.cs unchanged, and updates " +
            ".anneal/architecture/overview.md plus .anneal/architecture/widget.md in place to match. " +
            "Do not create or rename architecture documents.",
            ["src/Widget.cs", ".anneal/architecture/overview.md", ".anneal/architecture/widget.md"],
            Effort.Large,
            cancellationToken);

        var status = await fixture.GitStatusAsync(cancellationToken);
        var diff = await fixture.GitDiffAsync(cancellationToken);

        Console.WriteLine($"general worker outcome: {result.Outcome}");
        Console.WriteLine($"general worker finding: {DescribeFinding(result.Finding)}");
        Console.WriteLine("process steps:");
        Console.WriteLine(string.Join("\n", steps));
        Console.WriteLine("git status:");
        Console.WriteLine(status);
        Console.WriteLine("git diff:");
        Console.WriteLine(diff);

        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
            () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
            () => Assert.Contains(steps, line => line.Contains("\"Preflight:PlanAndDocument\"")),
            () => Assert.Contains(steps, line => line.Contains("\"Planner\"")),
            () => Assert.Contains(steps, line => line.Contains("\"DocumentAuthor\"")),
            () => Assert.Contains(steps, line => line.Contains("\"Developer\"")),
            () => Assert.Contains(steps, line => line.Contains("\"DeterministicCheck:build.ps1\"")),
            () => Assert.Contains(steps, line => line.Contains("\"DiffCheck\"")),
            () => Assert.Contains(steps, line => line.Contains("\"ArchDocAgreementGate\"")),
            () => Assert.Contains(steps, line => line.Contains("\"Verifier\"")));
        Assert.Multiple(
            () => Assert.Contains("+public sealed class CalculationWidget", diff, StringComparison.Ordinal),
            () => Assert.DoesNotContain("calculation-widget.md", diff, StringComparison.OrdinalIgnoreCase),
            () => Assert.Contains("diff --git a/.anneal/architecture/overview.md", diff, StringComparison.Ordinal),
            () => Assert.Contains("diff --git a/.anneal/architecture/widget.md", diff, StringComparison.Ordinal),
            () => Assert.Contains("CalculationWidget", diff, StringComparison.Ordinal));
    }

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

    private static string DescribeNotes(WorkerExecutionResult result) =>
        result.Notes.Count == 0 ? "(none)" : string.Join("\n", result.Notes.Select(note => note.Text));
}
