using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Operations;

/// <summary>
///     Interior tests for <see cref="VerifyChangeOperation" />'s own composition:
///     <c>Primitives.DiffCheck</c> → two deterministic checks → a model-backed <c>Primitives.Verifier</c>, with no
///     <c>Process.Router</c>, <c>Primitives.DocumentAuthor</c>, or <c>Primitives.Developer</c> in the path at all.
/// </summary>
public class VerifyChangeOperationTests
{
    [Fact]
    public async Task ExecuteAsync_TooManyArguments_ReportsUsageError()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var operation = new VerifyChangeOperation(root);

            var result = await operation.ExecuteAsync(
                ["main", "extra"], new StringWriter(), TestContext.Current.CancellationToken);

            Assert.Equal(OperationOutcome.UsageError, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_BuildFails_HardFailsWithNoModelConsulted()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint("should never be reached");

            var operation = new VerifyChangeOperation(
                root,
                endpointFor: _ => endpoint,
                runGit: (_, _) => Task.FromResult(new ScriptRun(0, "")),
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(1, "it broke")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "  43/43 clauses checked.")));

            var output = new StringWriter();
            var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.False(result.FindingAs<VerifyChangeReport>()!.BuildPassed),
                () => Assert.Equal(0, endpoint.Calls),
                () => Assert.Contains("FAIL", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnrelatedUnfulfilledObligation_IsSetAsideAsAdvisoryNotBlocking()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // The diff touches only toolkit.md; the obligation error names a different document (process.md), so
            // it must be classified pre-existing/advisory rather than blocking, per scope-check.agent.md's own
            // exception - and the verifier must still be reached and asked to judge.
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
                    "  error: process.md: clause PROCESS-05 has an unfulfilled test obligation 'TODO.Something'")));

            var result = await operation.ExecuteAsync([], new StringWriter(), TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.True(result.FindingAs<VerifyChangeReport>()!.ContractConformancePassed),
                () => Assert.Single(result.FindingAs<VerifyChangeReport>()!.AdvisoryNotes),
                () => Assert.Equal(1, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnfulfilledObligationInTouchedSystem_RemainsBlocking()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint("should never be reached");

            var patch =
                """
                diff --git a/.anneal/architecture/process.md b/.anneal/architecture/process.md
                index 1111111..2222222 100644
                --- a/.anneal/architecture/process.md
                +++ b/.anneal/architecture/process.md
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
                    "  error: process.md: clause PROCESS-05 has an unfulfilled test obligation 'TODO.Something'")));

            var result = await operation.ExecuteAsync([], new StringWriter(), TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.False(result.FindingAs<VerifyChangeReport>()!.ContractConformancePassed),
                () => Assert.Equal(0, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DiffUnavailable_NeverAppliesTheAdvisoryException()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint("should never be reached");

            var operation = new VerifyChangeOperation(
                root,
                endpointFor: _ => endpoint,
                runGit: (_, _) => Task.FromResult(new ScriptRun(128, "fatal: not a git repository")),
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(
                    1,
                    "  error: process.md: clause PROCESS-05 has an unfulfilled test obligation 'TODO.Something'")));

            var result = await operation.ExecuteAsync([], new StringWriter(), TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.False(result.FindingAs<VerifyChangeReport>()!.DiffAvailable),
                () => Assert.False(result.FindingAs<VerifyChangeReport>()!.ContractConformancePassed),
                () => Assert.Equal(0, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_VerifierFindsConcern_ReportsFailedWithConcernText()
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

            var result = await operation.ExecuteAsync([], new StringWriter(), TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Contains(
                    "Documentation: update the clause", result.FindingAs<VerifyChangeReport>()!.Concerns));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_EverythingPasses_ReportsSucceeded()
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
            var result = await operation.ExecuteAsync(
                ["main"], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.True(result.FindingAs<VerifyChangeReport>()!.BuildPassed),
                () => Assert.True(result.FindingAs<VerifyChangeReport>()!.ContractConformancePassed),
                () => Assert.Empty(result.FindingAs<VerifyChangeReport>()!.Concerns),
                () => Assert.Contains("no concerns found", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-verify-change-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), "");
        return root;
    }
}
