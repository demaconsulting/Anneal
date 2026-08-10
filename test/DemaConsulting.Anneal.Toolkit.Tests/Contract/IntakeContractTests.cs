using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-42, TOOLKIT-43, and TOOLKIT-44: how <c>intake</c> classifies one Intake item,
///     appends to the backlog or assumptions register, and escalates rather than auto-admitting a constraint or
///     silently recreating a missing register.
/// </summary>
/// <remarks>
///     These tests go through the same command surface a caller has — the action name dispatched through
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> — and assert on
///     exit code, output, and repository state. Nothing here reaches inside <see cref="IntakeOperation" />'s
///     private helpers.
/// </remarks>
public class IntakeContractTests
{
    [Fact]
    public async Task IntakeWritesBacklogAndAssumptionEntriesFromOneOracleClassification()
    {
        // Scenario 1: a completing item is filed straight into backlog from one oracle reply, with no second
        // conversation hidden behind the action.
        using (var repository = CreateRepository())
        {
            var endpoint = new QueuedEndpoint(
                DecisionJson("Backlog", "this is a discrete piece of work", "**Add a CLI smoke test** — cover the installed tool surface.", "None", true));
            var operation = new IntakeOperation(repository.Root, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["intake", "add a CLI smoke test"],
                output,
                [operation],
                repository.Root,
                TestContext.Current.CancellationToken);

            var backlog = File.ReadAllText(Path.Combine(repository.Root, ".anneal", "work", "backlog.md"));

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Equal(1, endpoint.Calls),
                () => Assert.Contains("filed in .anneal/work/backlog.md", output.ToString(), StringComparison.Ordinal),
                () => Assert.Contains("**Add a CLI smoke test**", backlog, StringComparison.Ordinal));
        }

        // Scenario 2: a disprovable standing statement is filed as an assumption.
        using (var repository = CreateRepository())
        {
            var endpoint = new QueuedEndpoint(
                DecisionJson("Assumption", "this is a disprovable environmental belief", "**Users can restore the local dotnet tool feed before invoking Anneal.**", "None", true));
            var operation = new IntakeOperation(repository.Root, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["intake", "users can restore the local dotnet tool feed before invoking Anneal"],
                output,
                [operation],
                repository.Root,
                TestContext.Current.CancellationToken);

            var assumptions = File.ReadAllText(Path.Combine(repository.Root, ".anneal", "governance", "assumptions.md"));

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Equal(1, endpoint.Calls),
                () => Assert.Contains("filed in .anneal/governance/assumptions.md", output.ToString(), StringComparison.Ordinal),
                () => Assert.Contains("Users can restore the local dotnet tool feed", assumptions, StringComparison.Ordinal));
        }

        // Scenario 3: missing input is a usage error and no oracle call is made.
        using (var repository = CreateRepository())
        {
            var endpoint = new QueuedEndpoint();
            var operation = new IntakeOperation(repository.Root, endpointFor: _ => endpoint);

            var exitCode = await AnnealTool.RunAsync(
                ["intake"],
                new StringWriter(),
                [operation],
                repository.Root,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitUsageError, exitCode),
                () => Assert.Equal(0, endpoint.Calls));
        }
    }

    [Fact]
    public async Task IntakeEscalatesConstraintInsteadOfWritingIt()
    {
        using var repository = CreateRepository();
        var constraintsPath = Path.Combine(repository.Root, ".anneal", "work", "constraints.md");
        var before = File.ReadAllText(constraintsPath);

        var endpoint = new QueuedEndpoint(
            DecisionJson("Constraint", "this is a durable condition that only a decision could change", "**Installation is by a provided script.**", "Satisfied", true));
        var operation = new IntakeOperation(repository.Root, endpointFor: _ => endpoint);

        var output = new StringWriter();
        var exitCode = await AnnealTool.RunAsync(
            ["intake", "installation is by a provided script"],
            output,
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
            () => Assert.Equal(before, File.ReadAllText(constraintsPath)),
            () => Assert.Contains("proposed constraint", output.ToString(), StringComparison.Ordinal),
            () => Assert.Contains("Satisfied", output.ToString(), StringComparison.Ordinal),
            () => Assert.Contains("Installation is by a provided script", output.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task IntakeEscalatesWhenSelectedRegisterIsMissing()
    {
        using var repository = CreateRepository(includeBacklog: false);

        var endpoint = new QueuedEndpoint(
            DecisionJson("Backlog", "this is a discrete piece of work", "**Add a smoke test** — cover the installed tool surface.", "None", true));
        var operation = new IntakeOperation(repository.Root, endpointFor: _ => endpoint);

        var output = new StringWriter();
        var exitCode = await AnnealTool.RunAsync(
            ["intake", "add a smoke test"],
            output,
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
            () => Assert.Contains(".anneal/work/backlog.md", output.ToString(), StringComparison.Ordinal),
            () => Assert.Contains("template-sync", output.ToString(), StringComparison.Ordinal),
            () => Assert.False(File.Exists(Path.Combine(repository.Root, ".anneal", "work", "backlog.md"))));
    }

    private static TemporaryRepository CreateRepository(
        bool includeBacklog = true,
        bool includeAssumptions = true,
        bool includeConstraints = true)
    {
        var repository = new TemporaryRepository();

        if (includeBacklog)
        {
            repository.Write(
                ".anneal/work/backlog.md",
                """
                # Backlog

                Wanted, not yet scheduled.
                """);
        }

        if (includeAssumptions)
        {
            repository.Write(
                ".anneal/governance/assumptions.md",
                """
                # Assumptions

                Curated, descriptive truths the design rests on, disprovable but not chosen.
                """);
        }

        if (includeConstraints)
        {
            repository.Write(
                ".anneal/work/constraints.md",
                """
                # Constraints

                ## Satisfied

                - **Existing constraint.**

                ## Not Yet Satisfied
                """);
        }

        return repository;
    }

    private static string DecisionJson(
        string kind,
        string why,
        string bulletText,
        string constraintSection,
        bool hasSufficientEvidence) =>
        $$"""
          {"kind":"{{kind}}","why":"{{why}}","bulletText":"{{bulletText}}","constraintSection":"{{constraintSection}}","hasSufficientEvidence":{{hasSufficientEvidence.ToString().ToLowerInvariant()}}}
          """;
}
