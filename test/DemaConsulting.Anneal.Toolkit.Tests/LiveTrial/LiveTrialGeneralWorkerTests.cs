using System;
using System.Linq;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     One real, end-to-end live trial of <c>GeneralWorker</c> proving the capability-complete Large worker can
///     still handle an obvious Small-Fix-shaped request without firing heavyweight obligations it did not need.
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
    public async Task LiveTrial_GeneralWorkerHandlesAnObviousSmallFixWithoutHeavyObligations_Succeeds()
    {
        // Arrange: skip unless the live-trial gate is explicitly set - this test makes real model calls
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        var cancellationToken = TestContext.Current.CancellationToken;

        await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken);

        // Arrange: a tiny, one-file repository with an obvious, narrowly-scoped defect and no contract or
        // architecture surface that would justify the heavier obligations.
        fixture.WriteFile(
            "Arithmetic.txt",
            """
            This file states a simple arithmetic fact.

            Two plus two equals 5.
            """);
        await fixture.CommitAllAsync("seed: add Arithmetic.txt with a wrong sum", cancellationToken);

        // Act: run the capability-complete Large worker directly on a plainly Small-Fix-shaped request.
        var (result, steps) = await fixture.RunGeneralWorkerAsync(
            "Fix the arithmetic in Arithmetic.txt - \"Two plus two equals 5\" should read " +
            "\"Two plus two equals 4\".",
            ["Arithmetic.txt"],
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

        // Assert: the deterministic step trace shows the cheap path fired - code-only preflight, no planning,
        // no documentation authoring, no contract check, and no absorbed architecture-agreement pass.
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
            () => Assert.IsType<WorkerRunResult.Completed>(result.Finding),
            () => Assert.Contains(steps, line => line.Contains("\"Preflight:CodeOnly\"")),
            () => Assert.Contains(steps, line => line.Contains("\"Developer\"")),
            () => Assert.Contains(steps, line => line.Contains("\"DeterministicCheck:build.ps1\"")),
            () => Assert.Contains(steps, line => line.Contains("\"DiffCheck\"")),
            () => Assert.Contains(steps, line => line.Contains("\"Verifier\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"Planner\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"DocumentAuthor\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"DeterministicCheck:check-contracts\"")),
            () => Assert.DoesNotContain(steps, line => line.Contains("\"ArchDocAgreementGate\"")),
            () => Assert.Equal(1, steps.Count(line => line.Contains("\"Developer\""))));

        // Act: grade the observed outcome against the stated expectation with a real model-backed oracle. The
        // '.anneal/' directory is the Toolkit's own transcript bookkeeping, an expected side effect of any real
        // invocation, and the process-step trace is supplied explicitly so the oracle can see that the heavier
        // obligations did not fire.
        var verdict = await fixture.GradeAsync(
            "Arithmetic.txt states \"Two plus two equals 4\"; the general worker completed successfully; and no " +
            "file changed other than Arithmetic.txt and the Toolkit's own '.anneal/' records and transcript " +
            "bookkeeping.",
            $"general worker outcome: {result.Outcome}\n" +
            $"general worker finding: {DescribeFinding(result.Finding)}\n" +
            $"general worker notes:\n{DescribeNotes(result)}\n" +
            $"process steps:\n{string.Join("\n", steps)}\n" +
            $"git status:\n{status}\n" +
            $"git diff:\n{diff}",
            cancellationToken);

        Console.WriteLine($"grading verdict: evidence={verdict.HasSufficientEvidence}; passed={verdict.Passed}");
        Console.WriteLine($"grading reasoning: {verdict.Reasoning}");

        // Assert: the oracle had enough evidence and judged the run correct
        Assert.Multiple(
            () => Assert.True(verdict.HasSufficientEvidence, $"oracle had insufficient evidence: {verdict.Reasoning}"),
            () => Assert.True(verdict.Passed, $"oracle judged the trial failed: {verdict.Reasoning}"));
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
