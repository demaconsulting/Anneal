using System.Reflection;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for GeneralWorker's dormant schema-enforced preflight judgement oracle.
/// </summary>
public class GeneralWorkerPreflightJudgmentTests
{
    [Theory]
    [InlineData("TenetViolation")]
    [InlineData("VisionViolation")]
    [InlineData("InsufficientSpecificity")]
    [InlineData("Proceed")]
    public async Task GeneralWorker_JudgePreflightAsync_ParsesConclusionValues(string conclusionName)
    {
        // Arrange
        var conclusion = Enum.Parse<PreflightConclusion>(conclusionName);
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(JudgmentJson(PreflightScope.Code, conclusion));
            var worker = BuildWorker(root, endpoint);

            // Act
            var judgment = await JudgePreflightAsync(worker, MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(PreflightScope.Code, judgment.Scope),
                () => Assert.Equal(conclusion, judgment.Conclusion));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Docs")]
    [InlineData("Code")]
    [InlineData("Test")]
    public async Task GeneralWorker_JudgePreflightAsync_ParsesScopeValues(string scopeName)
    {
        // Arrange
        var scope = Enum.Parse<PreflightScope>(scopeName);
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(JudgmentJson(scope, PreflightConclusion.Proceed));
            var worker = BuildWorker(root, endpoint);

            // Act
            var judgment = await JudgePreflightAsync(worker, MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(scope, judgment.Scope),
                () => Assert.Equal(PreflightConclusion.Proceed, judgment.Conclusion));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorker_JudgePreflightAsync_MalformedFirstReply_RetriesAndReturnsCorrectedJudgment()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "not json at all",
                JudgmentJson(PreflightScope.Test, PreflightConclusion.InsufficientSpecificity));
            var worker = BuildWorker(root, endpoint);

            // Act
            var judgment = await JudgePreflightAsync(worker, MakeBrief(), TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(PreflightScope.Test, judgment.Scope),
                () => Assert.Equal(PreflightConclusion.InsufficientSpecificity, judgment.Conclusion),
                () => Assert.Equal(2, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<PreflightJudgment> JudgePreflightAsync(
        GeneralWorker worker, WorkerBrief brief, CancellationToken cancellationToken)
    {
        var method = typeof(GeneralWorker).GetMethod(
            "JudgePreflightAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(GeneralWorker), "JudgePreflightAsync");

        var task = (Task<PreflightJudgment?>)method.Invoke(worker, [brief, cancellationToken])!;
        return (await task.ConfigureAwait(false))!;
    }

    private static GeneralWorker BuildWorker(string root, QueuedEndpoint endpoint) =>
        new(
            root,
            Effort.Medium,
            "planner charter",
            "document charter",
            "developer charter",
            "verifier charter",
            endpointFor: _ => endpoint);

    private static WorkerBrief MakeBrief() =>
        new(
            "parent-123",
            "Add focused validation for the helper and update matching tests.",
            Effort.Medium,
            "medium code change",
            [],
            [],
            "general worker is appropriate",
            ["toolkit/general-worker.md"],
            ["do not hide failures"],
            ["src/Helper.cs", "test/HelperTests.cs"]);

    private static string JudgmentJson(PreflightScope scope, PreflightConclusion conclusion) =>
        $$"""{"scope":"{{scope}}","conclusion":"{{conclusion}}"}""";

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-gw-preflight-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
