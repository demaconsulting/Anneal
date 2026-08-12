using DemaConsulting.Anneal.Toolkit.Architecture;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Architecture;

/// <summary>
///     Interior tests for <see cref="ArchitectureCoverage" />. All three entry points are stateless, so
///     these tests are pure in-memory exercises with no file I/O.
/// </summary>
public class ArchitectureCoverageTests
{
    // -------------------------------------------------------------------------
    // ReadCoversGlobs
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadCoversGlobs_NullMarkdown_ReturnsEmpty()
    {
        // Arrange / Act
        var result = ArchitectureCoverage.ReadCoversGlobs(null);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReadCoversGlobs_NoFrontMatter_ReturnsEmpty()
    {
        // Arrange
        var markdown = "# Title\n\nsome prose\n";

        // Act
        var result = ArchitectureCoverage.ReadCoversGlobs(markdown);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReadCoversGlobs_FrontMatterWithoutCoversKey_ReturnsEmpty()
    {
        // Arrange
        var markdown = "---\nname: Foo\n---\n\n# Title\n";

        // Act
        var result = ArchitectureCoverage.ReadCoversGlobs(markdown);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReadCoversGlobs_SingleGlob_ReturnsIt()
    {
        // Arrange
        var markdown = "---\ncovers:\n  - src/**\n---\n\n# Title\n";

        // Act
        var result = ArchitectureCoverage.ReadCoversGlobs(markdown);

        // Assert
        Assert.Equal(["src/**"], result);
    }

    [Fact]
    public void ReadCoversGlobs_MultipleGlobs_ReturnsAllInOrder()
    {
        // Arrange — mirrors the real toolkit.md front-matter shape
        var markdown = "---\ncovers:\n  - src/DemaConsulting.Anneal.Toolkit/**\n  - test/DemaConsulting.Anneal.Toolkit.Tests/**\n---\n";

        // Act
        var result = ArchitectureCoverage.ReadCoversGlobs(markdown);

        // Assert
        Assert.Equal(
            ["src/DemaConsulting.Anneal.Toolkit/**", "test/DemaConsulting.Anneal.Toolkit.Tests/**"],
            result);
    }

    [Fact]
    public void ReadCoversGlobs_OtherKeyAfterCovers_OnlyReturnsCoversBullets()
    {
        // Arrange
        var markdown = "---\ncovers:\n  - src/**\nname: Foo\n---\n";

        // Act
        var result = ArchitectureCoverage.ReadCoversGlobs(markdown);

        // Assert — "name: Foo" closes the covers block; only src/** is a glob
        Assert.Equal(["src/**"], result);
    }

    // -------------------------------------------------------------------------
    // CoversAnyFile / MatchingFiles
    // -------------------------------------------------------------------------

    [Fact]
    public void CoversAnyFile_EmptyGlobs_ReturnsFalse()
    {
        // Arrange / Act / Assert
        Assert.False(ArchitectureCoverage.CoversAnyFile([], ["src/Foo.cs"]));
    }

    [Fact]
    public void CoversAnyFile_EmptyFiles_ReturnsFalse()
    {
        // Arrange / Act / Assert
        Assert.False(ArchitectureCoverage.CoversAnyFile(["src/**"], []));
    }

    [Fact]
    public void CoversAnyFile_MatchingFile_ReturnsTrue()
    {
        // Arrange
        var globs = new[] { "src/DemaConsulting.Anneal.Toolkit/**" };
        var files = new[] { "src/DemaConsulting.Anneal.Toolkit/Program.cs" };

        // Act / Assert
        Assert.True(ArchitectureCoverage.CoversAnyFile(globs, files));
    }

    [Fact]
    public void CoversAnyFile_NoMatchingFile_ReturnsFalse()
    {
        // Arrange
        var globs = new[] { "src/DemaConsulting.Anneal.Toolkit/**" };
        var files = new[] { ".github/agents/route.md" };

        // Act / Assert
        Assert.False(ArchitectureCoverage.CoversAnyFile(globs, files));
    }

    [Fact]
    public void CoversAnyFile_OneOfSeveralFilesMatches_ReturnsTrue()
    {
        // Arrange
        var globs = new[] { "src/**" };
        var files = new[] { ".github/agents/route.md", "src/DemaConsulting.Anneal.Toolkit/Program.cs" };

        // Act / Assert
        Assert.True(ArchitectureCoverage.CoversAnyFile(globs, files));
    }

    [Fact]
    public void MatchingFiles_ReturnsOnlyMatchedPaths()
    {
        // Arrange
        var globs = new[] { "src/**" };
        var files = new[] { "src/Foo.cs", ".github/bar.md", "src/Baz.cs" };

        // Act
        var result = ArchitectureCoverage.MatchingFiles(globs, files);

        // Assert
        Assert.Equal(["src/Foo.cs", "src/Baz.cs"], result);
    }

    // -------------------------------------------------------------------------
    // GlobMatches (internal — tested directly for the glob mini-language)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("src/**", "src/Foo.cs", true)]
    [InlineData("src/**", "src/deep/nested/Bar.cs", true)]
    [InlineData("src/**", "test/Foo.cs", false)]
    [InlineData("**/*.cs", "src/deep/Foo.cs", true)]
    [InlineData("**/*.cs", "src/deep/Foo.md", false)]
    [InlineData("install.ps1", "install.ps1", true)]
    [InlineData("install.ps1", "other.ps1", false)]
    [InlineData("src/DemaConsulting.Anneal.Toolkit/**", "src/DemaConsulting.Anneal.Toolkit/Program.cs", true)]
    [InlineData("src/DemaConsulting.Anneal.Toolkit/**", "src/DemaConsulting.Anneal.Toolkit.Tests/Foo.cs", false)]
    [InlineData(".github/agents/**", ".github/agents/route.md", true)]
    [InlineData(".github/agents/**", ".github/standards/coding-principles.md", false)]
    [InlineData("src/*/Foo.cs", "src/Bar/Foo.cs", true)]
    [InlineData("src/*/Foo.cs", "src/Bar/Baz/Foo.cs", false)]  // * does not cross directory boundaries
    public void GlobMatches_Patterns(string glob, string path, bool expected)
    {
        // Arrange / Act
        var result = ArchitectureCoverage.GlobMatches(glob, path);

        // Assert
        Assert.Equal(expected, result);
    }

    // -------------------------------------------------------------------------
    // PatchTouchesContractSection
    // -------------------------------------------------------------------------

    [Fact]
    public void PatchTouchesContractSection_NullPatch_ReturnsFalse()
    {
        // Arrange / Act / Assert
        Assert.False(ArchitectureCoverage.PatchTouchesContractSection(null, ".anneal/architecture/toolkit.md"));
    }

    [Fact]
    public void PatchTouchesContractSection_EmptyDocumentPath_ReturnsFalse()
    {
        // Arrange
        var patch = BuildPatch(".anneal/architecture/toolkit.md", "## Contract\n", "+**TOOLKIT-01** something\n");

        // Act / Assert
        Assert.False(ArchitectureCoverage.PatchTouchesContractSection(patch, ""));
    }

    [Fact]
    public void PatchTouchesContractSection_PatchNotTouchingTargetFile_ReturnsFalse()
    {
        // Arrange — the patch is for a different file
        var patch = BuildPatch("src/Other.cs", "## Contract\n", "+added line\n");

        // Act / Assert
        Assert.False(ArchitectureCoverage.PatchTouchesContractSection(patch, ".anneal/architecture/toolkit.md"));
    }

    [Fact]
    public void PatchTouchesContractSection_AddedLineInContractSection_ReturnsTrue()
    {
        // Arrange
        var patch = BuildPatch(
            ".anneal/architecture/toolkit.md",
            "## Contract\n",
            "+**TOOLKIT-01** new clause\n");

        // Act / Assert
        Assert.True(ArchitectureCoverage.PatchTouchesContractSection(patch, ".anneal/architecture/toolkit.md"));
    }

    [Fact]
    public void PatchTouchesContractSection_RemovedLineInContractSection_ReturnsTrue()
    {
        // Arrange
        var patch = BuildPatch(
            ".anneal/architecture/toolkit.md",
            "## Contract\n",
            "-**TOOLKIT-01** old clause\n");

        // Act / Assert
        Assert.True(ArchitectureCoverage.PatchTouchesContractSection(patch, ".anneal/architecture/toolkit.md"));
    }

    [Fact]
    public void PatchTouchesContractSection_ChangeInProseNotContract_ReturnsFalse()
    {
        // Arrange — addition is before any ## Contract heading
        var patch = BuildPatch(
            ".anneal/architecture/toolkit.md",
            "# Toolkit\n",
            "+some prose\n");

        // Act / Assert
        Assert.False(ArchitectureCoverage.PatchTouchesContractSection(patch, ".anneal/architecture/toolkit.md"));
    }

    [Fact]
    public void PatchTouchesContractSection_ChangeAfterContractClosedByOtherHeading_ReturnsFalse()
    {
        // Arrange — change is after a ## Appendix heading that closes the contract block
        var contextAndChange =
            " ## Contract\n" +
            " some context line\n" +
            " ## Appendix\n" +
            "+appended prose\n";

        var patch =
            $"diff --git a/.anneal/architecture/toolkit.md b/.anneal/architecture/toolkit.md\n" +
            $"@@ -1,3 +1,4 @@\n" +
            contextAndChange;

        // Act / Assert
        Assert.False(ArchitectureCoverage.PatchTouchesContractSection(patch, ".anneal/architecture/toolkit.md"));
    }

    [Fact]
    public void PatchTouchesContractSection_MultipleHunks_PicksContractHunk()
    {
        // Arrange — first hunk is in prose, second is in ## Contract
        var patch =
            "diff --git a/.anneal/architecture/toolkit.md b/.anneal/architecture/toolkit.md\n" +
            "@@ -1,2 +1,3 @@\n" +
            " # Toolkit\n" +
            "+some prose addition\n" +
            "@@ -10,3 +11,4 @@\n" +
            " ## Contract\n" +
            "+**TOOLKIT-99** new clause\n";

        // Act / Assert
        Assert.True(ArchitectureCoverage.PatchTouchesContractSection(patch, ".anneal/architecture/toolkit.md"));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Builds a minimal unified diff for a single file. <paramref name="contextLine" /> is a context
    ///     line (no leading +/-) and <paramref name="changedLine" /> already carries its + or - prefix.
    /// </summary>
    private static string BuildPatch(string filePath, string contextLine, string changedLine) =>
        $"diff --git a/{filePath} b/{filePath}\n" +
        $"@@ -1,1 +1,2 @@\n" +
        $" {contextLine}" +
        changedLine;
}
