using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-32, TOOLKIT-33, and TOOLKIT-34: how <c>stage-contract</c> runs a work item
///     directly against <c>DocumentAuthor</c> with no routing oracle and no <c>Developer</c>/<c>Verifier</c>
///     pass, and mechanically enforces that the actual changes stay under <c>docs/architecture/</c> and that
///     the staged clause is well-formed.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> and assertions
///     are on the exit code and the written output. Nothing here reaches inside
///     <see cref="StageContractOperation" /> or <c>Primitives.DocumentAuthor</c>.
/// </remarks>
public class Toolkit323334StageContractContractTests
{
    private const string WellFormedTodoClause = """
                                                 ## Contract

                                                 ### Provides

                                                 - **EXAMPLE-01** - Does a thing.
                                                   *Verified by:* `TODO.DoesAThing`
                                                 """;

    private const string MissingContractSection = """
                                                    # Example System

                                                    Some prose with no Contract heading at all.
                                                    """;

    /// <summary>
    ///     TOOLKIT-32 — <c>stage-contract</c> takes a work item and runs it directly against
    ///     <c>DocumentAuthor</c>, asking no routing oracle and running no <c>Developer</c>/<c>Verifier</c> pass:
    ///     only <c>DocumentAuthor</c>'s own two replies (a free-text authoring turn, then its structured
    ///     decision) are queued, so if <c>stage-contract</c> asked a route oracle first or ran a second pass
    ///     afterward, the endpoint would report itself unavailable and the run would fail for the wrong reason
    ///     instead of completing. A missing work item is a usage error. Verified by
    ///     <c>StageContractRunsWorkItemDirectlyThroughDocumentAuthor</c>.
    /// </summary>
    [Fact]
    public async Task StageContractRunsWorkItemDirectlyThroughDocumentAuthor()
    {
        // Scenario 1: a declared work item runs directly against DocumentAuthor and completes, with the
        // staged clause already well-formed on disk, and no routing-oracle reply queued at all.
        using (var repository = new TemporaryRepository())
        {
            repository.WriteDocument("example.md", WellFormedTodoClause);

            var endpoint = new QueuedEndpoint(
                "I staged the clause.",
                CompletedJson(["docs/architecture/example.md"], "staged EXAMPLE-01 as a planned obligation"));

            var operation = new StageContractOperation(repository.Root, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["stage-contract", "stage a clause for the example system"],
                output,
                [operation],
                repository.Root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("stage-contract: completed", written, StringComparison.Ordinal),
                () => Assert.Contains("docs/architecture/example.md", written, StringComparison.Ordinal),
                () => Assert.Equal(2, endpoint.Calls));
        }

        // Scenario 2: a missing work item is a usage error - no model call is ever made.
        using (var repository = new TemporaryRepository())
        {
            var endpoint = new QueuedEndpoint();
            var operation = new StageContractOperation(repository.Root, endpointFor: _ => endpoint);

            var exitCode = await AnnealTool.RunAsync(
                ["stage-contract"],
                new StringWriter(),
                [operation],
                repository.Root,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitUsageError, exitCode),
                () => Assert.Equal(0, endpoint.Calls));
        }
    }

    /// <summary>
    ///     TOOLKIT-33 — after <c>DocumentAuthor</c>'s run, <c>stage-contract</c> checks the actual files it
    ///     reports having changed and forces escalation, naming the offending file, when any of them falls
    ///     outside <c>docs/architecture/</c> — the mirror image of <c>ProtectedPathTripwire</c>'s rule for
    ///     Maintenance, since this action's whole job is to touch the architecture tree and nothing else.
    ///     Verified by <c>StageContractEscalatesWhenActualChangesReachOutsideTheArchitectureTree</c>.
    /// </summary>
    [Fact]
    public async Task StageContractEscalatesWhenActualChangesReachOutsideTheArchitectureTree()
    {
        using var repository = new TemporaryRepository();

        // DocumentAuthor reports a file outside docs/architecture/ - a stop condition regardless of what it
        // claims to have accomplished.
        var endpoint = new QueuedEndpoint(
            "I made a code change too.",
            CompletedJson(["docs/architecture/example.md", "src/Something.cs"], "touched code as well"));

        var operation = new StageContractOperation(repository.Root, endpointFor: _ => endpoint);

        var output = new StringWriter();
        var exitCode = await AnnealTool.RunAsync(
            ["stage-contract", "stage a clause for the example system"],
            output,
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);
        var written = output.ToString();

        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
            () => Assert.Contains("stage-contract: escalated", written, StringComparison.Ordinal),
            () => Assert.Contains("src/Something.cs", written, StringComparison.Ordinal),
            () => Assert.Contains("falls outside docs/architecture/", written, StringComparison.Ordinal));
    }

    /// <summary>
    ///     TOOLKIT-34 — after the architecture-tree check clears, <c>stage-contract</c> runs a non-strict
    ///     <c>check-contracts</c> pass against the repository and fails, rather than reporting an unqualified
    ///     success, when the staged clause is not well-formed (here, the document is missing its
    ///     <c>## Contract</c> section entirely) — proven even though no test yet exists for the clause, since a
    ///     malformed document fails closed under <c>check-contracts</c> regardless of <c>-Strict</c>. Verified
    ///     by <c>StageContractFailsWhenTheStagedClauseIsNotWellFormed</c>.
    /// </summary>
    [Fact]
    public async Task StageContractFailsWhenTheStagedClauseIsNotWellFormed()
    {
        using var repository = new TemporaryRepository();
        repository.WriteDocument("example.md", MissingContractSection);

        var endpoint = new QueuedEndpoint(
            "I staged the clause.",
            CompletedJson(["docs/architecture/example.md"], "staged EXAMPLE-01 as a planned obligation"));

        var operation = new StageContractOperation(repository.Root, endpointFor: _ => endpoint);

        var output = new StringWriter();
        var exitCode = await AnnealTool.RunAsync(
            ["stage-contract", "stage a clause for the example system"],
            output,
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);
        var written = output.ToString();

        // Failed does not gate for an Authoring-category operation - the same non-gating shape every other
        // Authoring action already has - so the process exit is still ExitSuccess, and the wrapper's own
        // "does not gate" line accompanies this operation's own failure message.
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
            () => Assert.Contains("stage-contract: failed", written, StringComparison.Ordinal),
            () => Assert.Contains("did not pass after staging", written, StringComparison.Ordinal),
            () => Assert.Contains("does not gate", written, StringComparison.Ordinal));
    }

    private static string CompletedJson(IReadOnlyList<string> filesChanged, string summary) =>
        $$"""
          {"kind":"Authored","why":"","filesChanged":[{{string.Join(",", filesChanged.Select(file => $"\"{file}\""))}}],"summary":"{{summary}}"}
          """;
}
