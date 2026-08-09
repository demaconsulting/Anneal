using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Skills;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the Skills contract in <c>docs/architecture/toolkit/skills.md</c>.
/// </summary>
/// <remarks>
///     These tests drive the public action surface a caller has - the named action dispatched through
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> - and assert on
///     the exit code, output, and repository state. Nothing here reaches inside an operation's private helpers.
/// </remarks>
public class SkillsContractTests
{
    /// <summary>
    ///     TOOLKIT-38 — <c>file-skill</c> takes an id, a summary, at least one tag, and a body, and writes one
    ///     repository-local skill file under <c>.anneal/skills/</c> in the shared front-matter shape. A missing
    ///     required field is a usage error, a colliding id fails without overwriting the file, and a successful
    ///     write round-trips through the shared reader. Verified by
    ///     <c>FileSkillWritesAWellFormedRepositoryLocalSkill</c>.
    /// </summary>
    [Fact]
    public async Task FileSkillWritesAWellFormedRepositoryLocalSkill()
    {
        // Arrange: an empty repository-local skill catalog and one valid skill request.
        using var repository = new TemporaryRepository();
        var operation = new FileSkillOperation(repository.Root);

        // Act: file a valid skill through the public action surface.
        var successOutput = new StringWriter();
        var successExit = await AnnealTool.RunAsync(
            [
                "file-skill",
                "--id", "check-contracts-placeholder-form",
                "--tags", "contracts,skills",
                "--summary", "Use the TODO. or TODO_ prefix only for planned verifiers.",
                "--body", "Keep placeholder verifiers in the exact TODO. or TODO_ form and replace them once the boundary test exists."
            ],
            successOutput,
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);
        var writtenPath = Path.Combine(repository.Root, ".anneal", "skills", "check-contracts-placeholder-form.md");
        var writtenText = File.ReadAllText(writtenPath);
        var roundTripped = SkillFile.Read(writtenText, writtenPath);

        // Act: file the same skill again, and try one misuse with a missing required body.
        var collisionOutput = new StringWriter();
        var collisionExit = await AnnealTool.RunAsync(
            [
                "file-skill",
                "--id", "check-contracts-placeholder-form",
                "--tags", "contracts,skills",
                "--summary", "Use the TODO. or TODO_ prefix only for planned verifiers.",
                "--body", "A different body."
            ],
            collisionOutput,
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);

        var misuseExit = await AnnealTool.RunAsync(
            [
                "file-skill",
                "--id", "missing-body",
                "--tags", "contracts",
                "--summary", "Missing body",
                "--body", " "
            ],
            new StringWriter(),
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);

        // Assert: the file was written in the shared shape, the collision failed without overwriting, and the
        // missing field was turned away as a usage error.
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, successExit),
            () => Assert.Contains("file-skill: wrote .anneal/skills/check-contracts-placeholder-form.md", successOutput.ToString(), StringComparison.Ordinal),
            () => Assert.Equal("check-contracts-placeholder-form", roundTripped.Id),
            () => Assert.Equal(["contracts", "skills"], roundTripped.Tags),
            () => Assert.Equal("Use the TODO. or TODO_ prefix only for planned verifiers.", roundTripped.Summary),
            () => Assert.Contains("exact TODO. or TODO_ form", roundTripped.Body, StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitSuccess, collisionExit),
            () => Assert.Contains("file-skill: failed", collisionOutput.ToString(), StringComparison.Ordinal),
            () => Assert.Contains("already exists", collisionOutput.ToString(), StringComparison.Ordinal),
            () => Assert.Equal(writtenText, File.ReadAllText(writtenPath)),
            () => Assert.Equal(AnnealTool.ExitUsageError, misuseExit));
    }

    /// <summary>
    ///     TOOLKIT-39 — <c>search-skills</c> performs lexical search across repository-local and embedded skills,
    ///     ranking matches by match strength and returning each match's full body. An empty query succeeds with
    ///     zero matches, and a missing query is a usage error. Verified by
    ///     <c>SearchSkillsRanksLexicalMatchesAcrossBothTiers</c>.
    /// </summary>
    [Fact]
    public async Task SearchSkillsRanksLexicalMatchesAcrossBothTiers()
    {
        // Arrange: one repository-local skill that matches the query weakly beside the embedded built-in skill
        // that matches it more strongly.
        using var repository = new TemporaryRepository();
        Directory.CreateDirectory(Path.Combine(repository.Root, ".anneal", "skills"));
        File.WriteAllText(
            Path.Combine(repository.Root, ".anneal", "skills", "todo-test-names.md"),
            """
            ---
            id: todo-test-names
            tags:
              - tests
              - contracts
            summary: A real test name may contain Todo without becoming a planned obligation.
            ---

            A literal Todo in a real boundary test name is still just that test's name.
            """);

        var operation = new SearchSkillsOperation(repository.Root);

        // Act: search for a TODO placeholder verifier, then ask the zero-result and missing-query cases.
        var output = new StringWriter();
        var exitCode = await AnnealTool.RunAsync(
            ["search-skills", "TODO placeholder verifier"],
            output,
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);
        var report = (await operation.ExecuteAsync(
            ["TODO placeholder verifier"],
            new StringWriter(),
            TestContext.Current.CancellationToken)).FindingAs<SearchSkillsReport>();

        var emptyQueryOutput = new StringWriter();
        var emptyQueryExit = await AnnealTool.RunAsync(
            ["search-skills", ""],
            emptyQueryOutput,
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);

        var missingQueryExit = await AnnealTool.RunAsync(
            ["search-skills"],
            new StringWriter(),
            [operation],
            repository.Root,
            TestContext.Current.CancellationToken);
        var matches = report?.Matches ?? [];

        // Assert: both tiers were searched, the stronger embedded match ranked first, the full body is
        // available, and the empty-query and missing-query cases follow the contract.
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
            () => Assert.NotNull(report),
            () => Assert.Equal(2, matches.Count),
            () => Assert.Equal("check-contracts-placeholder-form", matches[0].Id),
            () => Assert.Equal("todo-test-names", matches[1].Id),
            () => Assert.Contains("unfulfilled obligation", matches[0].Body, StringComparison.Ordinal),
            () => Assert.Contains("check-contracts-placeholder-form", output.ToString(), StringComparison.Ordinal),
            () => Assert.Contains("todo-test-names", output.ToString(), StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitSuccess, emptyQueryExit),
            () => Assert.Contains("0 match(es)", emptyQueryOutput.ToString(), StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitUsageError, missingQueryExit));
    }
}
