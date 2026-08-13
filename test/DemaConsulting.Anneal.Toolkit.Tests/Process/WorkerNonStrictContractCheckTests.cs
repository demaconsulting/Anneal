using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests verifying that <see cref="GeneralWorker" /> runs its default contract-check step in
///     non-strict mode, so pre-existing staged TODO obligations unrelated to the current change do not block the
///     worker while real test failures still do.
/// </summary>
public class WorkerNonStrictContractCheckTests
{
    [Fact]
    public async Task GeneralWorker_MediumContractTouch_DefaultContractCheckRunner_RunsNonStrictSoPreExistingTodoObligationDoesNotBlock()
    {
        var root = CreateTemporaryDirectory("anneal-gw-medium-non-strict-");
        try
        {
            WriteStagedTodoClause(root);

            var endpoint = new QueuedEndpoint(
                """{"scope":"Docs","conclusion":"Proceed"}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new GeneralWorker(
                root,
                Effort.Medium,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                runArchDocAgreementGate: false,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: ContractTouchingDiff);

            var result = await worker.RunAsync(
                MakeBrief(
                    "Add a contract clause for the toolkit action and implement it.",
                    Effort.Medium,
                    [".anneal/architecture/toolkit.md", "src/Foo.cs"]),
                TestContext.Current.CancellationToken);

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
    public async Task GeneralWorker_LargeStructuralTouch_DefaultContractCheckRunner_RunsNonStrictSoPreExistingTodoObligationDoesNotBlock()
    {
        var root = CreateTemporaryDirectory("anneal-gw-large-non-strict-");
        try
        {
            WriteStagedTodoClause(root);

            var endpoint = new QueuedEndpoint(
                """{"scope":"Docs","conclusion":"Proceed"}""",
                "I updated the contract document.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"updated the contract"}""",
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                runArchDocAgreementGate: false,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: ContractTouchingDiff);

            var result = await worker.RunAsync(
                MakeBrief(
                    "This structural change reshapes the toolkit contract and implementation.",
                    Effort.Large,
                    [".anneal/architecture/toolkit.md", "src/Foo.cs"]),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<WorkerRunResult.Completed>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    private static WorkerBrief MakeBrief(string workItem, Effort effort, IReadOnlyList<string> changedFileHints) =>
        new("parent-1", workItem, effort, "general", [], [], "the route selected general", [], [], changedFileHints);

    private static Task<ScriptRun> ContractTouchingDiff(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        _ = arguments;
        _ = cancellationToken;

        const string patch =
            """
            diff --git a/.anneal/architecture/toolkit.md b/.anneal/architecture/toolkit.md
            --- a/.anneal/architecture/toolkit.md
            +++ b/.anneal/architecture/toolkit.md
            @@ -1,5 +1,5 @@
             ## Contract
             
             ### Provides
             
            -- **TOOLKIT-01** - Accepts records.
            +- **TOOLKIT-01** - Accepts records and reports status.
            diff --git a/src/Foo.cs b/src/Foo.cs
            --- a/src/Foo.cs
            +++ b/src/Foo.cs
            @@ -1 +1 @@
            -class Foo {}
            +class Foo { int Status => 1; }
            """;

        return Task.FromResult(new ScriptRun(0, patch));
    }
}
