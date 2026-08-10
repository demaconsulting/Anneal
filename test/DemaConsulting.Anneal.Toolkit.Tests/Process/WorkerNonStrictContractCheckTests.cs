using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests verifying that <see cref="ContractChangeWorker" /> and
///     <see cref="StructuralChangeWorker" /> run their default contract-check step in non-strict mode, so
///     pre-existing staged TODO obligations unrelated to the current change do not block either worker while
///     real test failures still do.
/// </summary>
public class WorkerNonStrictContractCheckTests
{
    [Fact]
    public async Task ContractChangeWorker_DefaultContractCheckRunner_RunsNonStrictSoPreExistingTodoObligationDoesNotBlock()
    {
        // Arrange: repository has a staged TODO obligation on an unrelated clause; strict mode would promote
        // it to an error and block the run, but the default non-strict runner must leave it as a warning only
        var root = CreateTemporaryDirectory("anneal-cc-non-strict-");
        try
        {
            WriteStagedTodoClause(root);

            var endpoint = new QueuedEndpoint(
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new ContractChangeWorker(
                root,
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            // Act: no contractCheckRunScript supplied - the default must run non-strict so the TODO obligation
            // is a warning rather than an error
            var result = await worker.RunAsync(MakeContractBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StructuralChangeWorker_DefaultContractCheckRunner_RunsNonStrictSoPreExistingTodoObligationDoesNotBlock()
    {
        // Arrange: repository has a staged TODO obligation on an unrelated clause; strict mode would promote
        // it to an error and block the run, but the default non-strict runner must leave it as a warning only
        var root = CreateTemporaryDirectory("anneal-sc-non-strict-");
        try
        {
            WriteStagedTodoClause(root);

            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the system","planSteps":["update overview"]}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new StructuralChangeWorker(
                root,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            // Act: no contractCheckRunScript supplied - the default must run non-strict so the TODO obligation
            // is a warning rather than an error
            var result = await worker.RunAsync(MakeStructuralBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Writes a system document with a TODO-prefixed verifier into the temporary repository. In strict mode
    ///     this is an error; in non-strict mode it is a warning and the check still passes.
    /// </summary>
    private static void WriteStagedTodoClause(string root)
    {
        var archDir = Path.Combine(root, ".anneal", "architecture");
        Directory.CreateDirectory(archDir);
        File.WriteAllText(
            Path.Combine(archDir, "toolkit.md"),
            """
            ## Contract

            ### Provides

            - **TOOLKIT-01** - Accepts records.
              *Verified by:* `TODO.AcceptedRecordIsDurable`
            """);
    }

    private static string CreateTemporaryDirectory(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), "");
        return root;
    }

    private static WorkerBrief MakeContractBrief() =>
        new("parent-1", "add a contract clause for the new action", "contract change", [], [], "this touches a contract", [], []);

    private static WorkerBrief MakeStructuralBrief() =>
        new("parent-1", "split this system into two", "structural change", [], [], "this moves a system boundary", [], []);
}
