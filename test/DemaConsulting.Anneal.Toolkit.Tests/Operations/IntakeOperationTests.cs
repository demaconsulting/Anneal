using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Operations;

/// <summary>
///     Interior tests for <see cref="IntakeOperation" />'s own composition: one oracle decision followed by a
///     register append or escalation, with no Router, Developer, or DocumentAuthor in the path.
/// </summary>
public class IntakeOperationTests
{
    [Fact]
    public async Task ExecuteAsync_RefusedDecision_ReturnsRefusedWithoutWriting()
    {
        using var repository = CreateRepository();
        var backlogPath = Path.Combine(repository.Root, ".anneal", "work", "backlog.md");
        var before = File.ReadAllText(backlogPath);

        var endpoint = new QueuedEndpoint(
            """{"kind":"Constraint","why":"the wording is too vague to classify honestly","bulletText":"","constraintSection":"None","hasSufficientEvidence":false}""");
        var operation = new IntakeOperation(repository.Root, endpointFor: _ => endpoint);

        var output = new StringWriter();
        var result = await operation.ExecuteAsync(
            ["maybe improve startup"], output, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Refused, result.Outcome),
            () => Assert.Equal(string.Empty, result.FindingAs<IntakeReport>()!.TargetFile),
            () => Assert.Equal(before, File.ReadAllText(backlogPath)),
            () => Assert.Contains("refused", output.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_ConstraintWithNoSection_Fails()
    {
        using var repository = CreateRepository();
        var endpoint = new QueuedEndpoint(
            """{"kind":"Constraint","why":"this is a constraint but the section was omitted","bulletText":"**Keep startup under one second.**","constraintSection":"None","hasSufficientEvidence":true}""");
        var operation = new IntakeOperation(repository.Root, endpointFor: _ => endpoint);

        var output = new StringWriter();
        var result = await operation.ExecuteAsync(
            ["keep startup under one second"], output, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
            () => Assert.Contains("named no target section", output.ToString(), StringComparison.Ordinal));
    }

    private static TemporaryRepository CreateRepository()
    {
        var repository = new TemporaryRepository();
        repository.Write(
            ".anneal/work/backlog.md",
            """
            # Backlog

            Wanted, not yet scheduled.
            """);
        repository.Write(
            ".anneal/governance/assumptions.md",
            """
            # Assumptions

            Curated, descriptive truths the design rests on, disprovable but not chosen.
            """);
        repository.Write(
            ".anneal/work/constraints.md",
            """
            # Constraints

            ## Satisfied
            """);
        return repository;
    }
}
