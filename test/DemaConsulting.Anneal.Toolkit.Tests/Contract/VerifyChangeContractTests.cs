using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-35, TOOLKIT-36, and TOOLKIT-37: how <c>verify-change</c> runs
///     <c>DiffCheck</c>, two deterministic checks, and a model-backed <c>Primitives.Verifier</c>, sets aside
///     an unfulfilled test obligation naming an untouched system as advisory, and never gates a build
///     regardless of outcome.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, IReadOnlyList{IOperation}, string, CancellationToken)" />
///     and assertions are on the exit code and the written output. Nothing here reaches inside
///     <see cref="VerifyChangeOperation" /> or <c>Primitives.DiffCheck</c>/<c>Primitives.Verifier</c>.
/// </remarks>
public class VerifyChangeContractTests
{
    /// <summary>
    ///     TOOLKIT-35 — <c>verify-change</c> reads the diff, runs <c>build.ps1</c> and a strict
    ///     <c>check-contracts</c> pass, and asks a verifier to judge contract conformance, scope honesty, and
    ///     architecture-tree accuracy from the diff as evidence, succeeding when both checks and the verifier
    ///     pass. Verified by <c>VerifyChangeRunsBuildAndStrictContractCheckThenAsksAVerifier</c>.
    /// </summary>
    [Fact]
    public async Task VerifyChangeRunsBuildAndStrictContractCheckThenAsksAVerifier()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new VerifyChangeOperation(
                root,
                endpointFor: _ => endpoint,
                runGit: (_, _) => Task.FromResult(new ScriptRun(0, "")),
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "  43/43 clauses checked.")));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["verify-change"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("verify-change: running build.ps1", written, StringComparison.Ordinal),
                () => Assert.Contains(
                    "verify-change: running check-contracts -Strict", written, StringComparison.Ordinal),
                () => Assert.Contains("no concerns found", written, StringComparison.Ordinal),
                () => Assert.Equal(1, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-36 — an unfulfilled test-obligation error naming a document the diff did not touch is set
    ///     aside as advisory rather than blocking, and the verifier is still reached and asked to judge.
    ///     Verified by <c>VerifyChangeSetsAsideAnUnfulfilledObligationInAnUntouchedSystem</c>.
    /// </summary>
    [Fact]
    public async Task VerifyChangeSetsAsideAnUnfulfilledObligationInAnUntouchedSystem()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var patch =
                """
                diff --git a/.anneal/architecture/toolkit.md b/.anneal/architecture/toolkit.md
                index 1111111..2222222 100644
                --- a/.anneal/architecture/toolkit.md
                +++ b/.anneal/architecture/toolkit.md
                @@ -1 +1 @@
                -old
                +new
                """;

            var operation = new VerifyChangeOperation(
                root,
                endpointFor: _ => endpoint,
                runGit: (_, _) => Task.FromResult(new ScriptRun(0, patch)),
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(
                    1,
                    "  error: process.md: clause PROCESS-07 has an unfulfilled test obligation 'TODO.Something'")));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["verify-change"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains(
                    "verify-change: contract conformance PASS", written, StringComparison.Ordinal),
                () => Assert.Contains("advisory:", written, StringComparison.Ordinal),
                () => Assert.Equal(1, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-37 — <c>verify-change</c> declares <see cref="OperationCategory.Advisory" />: even when the
    ///     verifier reports a concern (a <see cref="OperationOutcome.Failed" /> outcome), the process exit code
    ///     stays <see cref="AnnealTool.ExitSuccess" /> and the wrapper's own "does not gate" line accompanies
    ///     the failure message, the same non-gating shape every other Advisory action already has. Verified by
    ///     <c>VerifyChangeNeverGatesRegardlessOfOutcome</c>.
    /// </summary>
    [Fact]
    public async Task VerifyChangeNeverGatesRegardlessOfOutcome()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """
                {"verdict":"RepairRequired","concerns":[{"owner":"Documentation","fixText":"update the clause"}],
                "advisoryNotes":[],"evidenceSufficient":true}
                """);

            var operation = new VerifyChangeOperation(
                root,
                endpointFor: _ => endpoint,
                runGit: (_, _) => Task.FromResult(new ScriptRun(0, "")),
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "  43/43 clauses checked.")));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["verify-change"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Equal(OperationCategory.Advisory, operation.Category),
                () => Assert.Contains("does not gate", written, StringComparison.Ordinal),
                () => Assert.Contains("Documentation: update the clause", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-verify-change-contract-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), "");
        return root;
    }
}
