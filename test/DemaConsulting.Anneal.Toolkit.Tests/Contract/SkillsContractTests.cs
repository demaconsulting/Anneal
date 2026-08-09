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
}
