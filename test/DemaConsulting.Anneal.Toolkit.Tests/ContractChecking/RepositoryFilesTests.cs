using DemaConsulting.Anneal.Toolkit.Files;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for finding the files a check should read.
/// </summary>
/// <remarks>
///     Disposable. What is worth protecting here is the two exclusion rules, both fail-closed: build output
///     and vendored code are pruned so a compiled copy of a deleted test cannot keep its clause alive, and
///     hidden entries are skipped for the same reason.
/// </remarks>
public class RepositoryFilesTests
{
    /// <summary>
    ///     Validates that build output and vendored dependencies are not searched for test declarations.
    /// </summary>
    [Fact]
    public void RepositoryFiles_UnderRoots_ExcludedDirectory_IsNotSearched()
    {
        // Arrange: one real test source, and copies of it where a build would leave them
        using var repository = new TemporaryRepository();
        repository.Write("test/Suite/RealTests.cs", "// real");
        repository.Write("test/Suite/bin/Debug/StaleTests.cs", "// stale");
        repository.Write("test/Suite/obj/StaleTests.cs", "// stale");
        repository.Write("test/Suite/node_modules/pkg/VendoredTests.cs", "// vendored");

        // Act
        var found = RepositoryFiles.UnderRoots(repository.Root, ["test"], ["*.cs"]);

        // Assert: only the source a person wrote
        Assert.Equal(["RealTests.cs"], found.Select(file => file.Name));
    }

    /// <summary>
    ///     Validates that a hidden directory supplies no test declarations.
    /// </summary>
    [Fact]
    public void RepositoryFiles_UnderRoots_HiddenDirectory_IsNotSearched()
    {
        // Arrange: a test source that survives only under a hidden directory
        using var repository = new TemporaryRepository();
        repository.Write("test/Suite/RealTests.cs", "// real");
        repository.CreateHiddenDirectory("test/.attic");
        repository.Write("test/.attic/DeletedTests.cs", "// deleted");

        // Act
        var found = RepositoryFiles.UnderRoots(repository.Root, ["test"], ["*.cs"]);

        // Assert
        Assert.Equal(["RealTests.cs"], found.Select(file => file.Name));
    }

    /// <summary>
    ///     Validates that a wildcard root is expanded to the directories it names.
    /// </summary>
    [Fact]
    public void RepositoryFiles_UnderRoots_WildcardRoot_IsExpanded()
    {
        // Arrange: two sibling suites and one outside the wildcard's reach
        using var repository = new TemporaryRepository();
        repository.Write("test/First.Tests/FirstTests.cs", "// first");
        repository.Write("test/Second.Tests/SecondTests.cs", "// second");
        repository.Write("other/ThirdTests.cs", "// third");

        // Act
        var found = RepositoryFiles.UnderRoots(repository.Root, ["test/*"], ["*.cs"]);

        // Assert
        Assert.Equal(
            ["FirstTests.cs", "SecondTests.cs"],
            found.Select(file => file.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Validates that a root the repository does not have is skipped rather than reported, since one
    ///     configuration is shared across repositories with different layouts.
    /// </summary>
    [Fact]
    public void RepositoryFiles_UnderRoots_MissingRoot_IsSkipped()
    {
        // Arrange: only one of the two conventional roots exists
        using var repository = new TemporaryRepository();
        repository.Write("test/Suite/RealTests.cs", "// real");

        // Act
        var found = RepositoryFiles.UnderRoots(repository.Root, ["test", "tests"], ["*.cs"]);

        // Assert
        Assert.Equal(["RealTests.cs"], found.Select(file => file.Name));
    }

    /// <summary>
    ///     Validates that a file reachable through two roots is returned once, so its declarations are not
    ///     counted twice.
    /// </summary>
    [Fact]
    public void RepositoryFiles_UnderRoots_FileUnderTwoRoots_IsReturnedOnce()
    {
        // Arrange: overlapping roots naming the same file
        using var repository = new TemporaryRepository();
        repository.Write("test/Suite/RealTests.cs", "// real");

        // Act
        var found = RepositoryFiles.UnderRoots(repository.Root, ["test", "test/Suite"], ["*.cs"]);

        // Assert
        Assert.Single(found);
    }

    /// <summary>
    ///     Validates that a whole-repository glob search does not descend into vendored dependency trees,
    ///     whose results belong to somebody else's project.
    /// </summary>
    [Fact]
    public void RepositoryFiles_MatchingGlob_VendoredDirectory_IsNotSearched()
    {
        // Arrange: a result file of our own and one inside a vendored package
        using var repository = new TemporaryRepository();
        repository.Write("artifacts/tests/ours.trx", "<TestRun />");
        repository.Write("node_modules/pkg/artifacts/tests/theirs.trx", "<TestRun />");

        // Act
        var found = RepositoryFiles.MatchingGlob(repository.Root, GlobPattern.Parse("**/artifacts/tests/*.trx"));

        // Assert
        Assert.Equal(["ours.trx"], found.Select(file => file.Name));
    }

    /// <summary>
    ///     Validates that matched files arrive oldest first, so a caller reading them in order sees the
    ///     newest last and the newest result wins.
    /// </summary>
    [Fact]
    public void RepositoryFiles_MatchingGlob_SeveralFiles_ArriveOldestFirst()
    {
        // Arrange: two result files written a day apart
        using var repository = new TemporaryRepository();
        var older = repository.Write("artifacts/tests/older.trx", "<TestRun />");
        var newer = repository.Write("artifacts/tests/newer.trx", "<TestRun />");
        File.SetLastWriteTimeUtc(older, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var found = RepositoryFiles.MatchingGlob(repository.Root, GlobPattern.Parse("artifacts/tests/*.trx"));

        // Assert
        Assert.Equal(["older.trx", "newer.trx"], found.Select(file => file.Name));
    }
}
