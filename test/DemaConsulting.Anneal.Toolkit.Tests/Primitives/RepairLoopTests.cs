using DemaConsulting.Anneal.Toolkit.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="RepairLoop{TState}" />'s bounded-attempt behavior and its ownership-directed
///     repair: a finding goes back through the same execute step, not a generic restart.
/// </summary>
public class RepairLoopTests
{
    [Fact]
    public async Task RunAsync_PassesOnFirstAttempt_SucceedsAfterOneExecuteAndOneVerify()
    {
        // Arrange: an execute step that always succeeds, and a verify step that passes immediately
        var executeCalls = 0;
        var verifyCalls = 0;
        var loop = new RepairLoop<string>(maxRepairAttempts: 3);

        // Act
        var result = await loop.RunAsync(
            "initial",
            (state, _, _) =>
            {
                executeCalls++;
                return Task.FromResult(new StepResult<string>(OperationOutcome.Succeeded, state, []));
            },
            (_, _) =>
            {
                verifyCalls++;
                return Task.FromResult(new StepResult<VerificationFinding>(
                    OperationOutcome.Succeeded,
                    new VerificationFinding
                    {
                        Verdict = VerificationVerdict.Passed,
                        Concerns = [],
                        AdvisoryNotes = [],
                        EvidenceSufficient = true
                    },
                    []));
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
            () => Assert.Equal(1, executeCalls),
            () => Assert.Equal(1, verifyCalls));
    }

    [Fact]
    public async Task RunAsync_ExecuteFailsOutright_ReturnsTheExecuteResultUnchanged()
    {
        // Arrange: an execute step that never succeeds, so verification is never reached
        var verifyCalls = 0;
        var loop = new RepairLoop<string>(maxRepairAttempts: 3);

        // Act
        var result = await loop.RunAsync(
            "initial",
            (_, _, _) => Task.FromResult(new StepResult<string>(
                OperationOutcome.Failed, null, [new ProcessNote("could not execute")])),
            (_, _) =>
            {
                verifyCalls++;
                return Task.FromResult(new StepResult<VerificationFinding>(
                    OperationOutcome.Succeeded,
                    new VerificationFinding
                    {
                        Verdict = VerificationVerdict.Passed,
                        Concerns = [],
                        AdvisoryNotes = [],
                        EvidenceSufficient = true
                    },
                    []));
            },
            TestContext.Current.CancellationToken);

        // Assert: the execute step's own outcome comes back, and verification was never asked
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
            () => Assert.Equal(0, verifyCalls));
    }

    [Fact]
    public async Task RunAsync_VerificationEscalates_StopsWithoutSpendingFurtherRepairBudget()
    {
        // Arrange: verification reaches a boundary only a person can resolve
        var executeCalls = 0;
        var loop = new RepairLoop<string>(maxRepairAttempts: 3);

        // Act
        var result = await loop.RunAsync(
            "initial",
            (state, _, _) =>
            {
                executeCalls++;
                return Task.FromResult(new StepResult<string>(OperationOutcome.Succeeded, state, []));
            },
            (_, _) => Task.FromResult(new StepResult<VerificationFinding>(
                OperationOutcome.Escalated, null, [new ProcessNote("needs a person")])),
            TestContext.Current.CancellationToken);

        // Assert: escalation is not a repair this loop may spend budget chasing
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Escalated, result.Outcome),
            () => Assert.Equal(1, executeCalls));
    }

    [Fact]
    public async Task RunAsync_RepairBudgetSpent_FailsAndSendsEachFindingBackToExecute()
    {
        // Arrange: verification always demands the same fix, and the loop has one repair attempt to spend
        List<IReadOnlyList<string>> fixesSeen = [];
        var loop = new RepairLoop<string>(maxRepairAttempts: 1);

        // Act
        var result = await loop.RunAsync(
            "initial",
            (state, fixes, _) =>
            {
                fixesSeen.Add(fixes);
                return Task.FromResult(new StepResult<string>(OperationOutcome.Succeeded, state, []));
            },
            (_, _) => Task.FromResult(new StepResult<VerificationFinding>(
                OperationOutcome.Succeeded,
                new VerificationFinding
                {
                    Verdict = VerificationVerdict.RepairRequired,
                    Concerns = [new VerificationConcern { Owner = VerificationOwner.Code, FixText = "fix the thing" }],
                    AdvisoryNotes = [],
                    EvidenceSufficient = true
                },
                [])),
            TestContext.Current.CancellationToken);

        // Assert: two execute attempts (the first, plus the one repair the budget allowed), the second carrying
        // the fix the first attempt's verification demanded, then Failed once the budget was spent
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
            () => Assert.Equal(2, fixesSeen.Count),
            () => Assert.Empty(fixesSeen[0]),
            () => Assert.Equal(["fix the thing"], fixesSeen[1]));
    }

    [Fact]
    public async Task RunAsync_PassesAfterOneRepair_SucceedsWithTheRepairedState()
    {
        // Arrange: verification fails once, then passes on the repaired state
        var attempt = 0;
        var loop = new RepairLoop<string>(maxRepairAttempts: 2);

        // Act
        var result = await loop.RunAsync(
            "initial",
            (_, fixes, _) => Task.FromResult(new StepResult<string>(
                OperationOutcome.Succeeded, fixes.Count == 0 ? "first attempt" : "repaired", [])),
            (state, _) =>
            {
                attempt++;
                var passed = state == "repaired";
                return Task.FromResult(new StepResult<VerificationFinding>(
                    OperationOutcome.Succeeded,
                    new VerificationFinding
                    {
                        Verdict = passed ? VerificationVerdict.Passed : VerificationVerdict.RepairRequired,
                        Concerns = passed
                            ? []
                            : [new VerificationConcern { Owner = VerificationOwner.Code, FixText = "repair it" }],
                        AdvisoryNotes = [],
                        EvidenceSufficient = true
                    },
                    []));
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
            () => Assert.Equal("repaired", result.Finding),
            () => Assert.Equal(2, attempt));
    }
}
