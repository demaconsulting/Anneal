using DemaConsulting.Anneal.Toolkit.Files;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for how a result glob is matched against a repository-relative path.
/// </summary>
/// <remarks>
///     Disposable. The behavior worth protecting is that the whole path is honored: matching only the leaf
///     would let a stray result file anywhere in the tree satisfy a check that was told to look in one
///     directory, which is how stale results silently mark a failing clause as passing.
/// </remarks>
public class GlobPatternTests
{
    /// <summary>
    ///     Validates that a file whose name matches but whose directory does not is not matched.
    /// </summary>
    [Fact]
    public void GlobPattern_Matches_RightNameWrongDirectory_DoesNotMatch()
    {
        // Arrange: a glob naming one results directory
        var glob = GlobPattern.Parse("artifacts/tests/*.trx");

        // Act / Assert: the leaf alone is not enough
        Assert.Multiple(
            () => Assert.True(glob.Matches("artifacts/tests/results.trx")),
            () => Assert.False(glob.Matches("other/results.trx")),
            () => Assert.False(glob.Matches("artifacts/tests/nested/results.trx")));
    }

    /// <summary>
    ///     Validates that a double star followed by a separator spans any number of whole directories,
    ///     including none.
    /// </summary>
    [Fact]
    public void GlobPattern_Matches_DoubleStarSegment_SpansAnyNumberOfDirectories()
    {
        // Arrange: the default results glob
        var glob = GlobPattern.Parse("artifacts/**/*.trx");

        // Act / Assert
        Assert.Multiple(
            () => Assert.True(glob.Matches("artifacts/results.trx")),
            () => Assert.True(glob.Matches("artifacts/tests/results.trx")),
            () => Assert.True(glob.Matches("artifacts/tests/net10.0/results.trx")),
            () => Assert.False(glob.Matches("build/artifacts/tests/results.trx")));
    }

    /// <summary>
    ///     Validates that a path written with backslashes reads as the same path, so a glob behaves the same
    ///     on either platform.
    /// </summary>
    [Fact]
    public void GlobPattern_Matches_BackslashPath_ReadsTheSameAsForwardSlash()
    {
        // Arrange
        var glob = GlobPattern.Parse("artifacts\\tests\\*.trx");

        // Act / Assert
        Assert.Multiple(
            () => Assert.True(glob.Matches("artifacts\\tests\\results.trx")),
            () => Assert.True(glob.Matches("artifacts/tests/results.trx")));
    }

    /// <summary>
    ///     Validates that a leading current-directory marker is ignored, so the two ways of writing the same
    ///     glob are one pattern.
    /// </summary>
    [Fact]
    public void GlobPattern_Parse_LeadingCurrentDirectory_IsIgnored()
    {
        // Arrange
        var glob = GlobPattern.Parse("./artifacts/tests/*.trx");

        // Act / Assert: the text is preserved for messages even though matching ignores it
        Assert.Multiple(
            () => Assert.True(glob.Matches("artifacts/tests/results.trx")),
            () => Assert.Equal("./artifacts/tests/*.trx", glob.Text));
    }

    /// <summary>
    ///     Validates that matching ignores case, so a glob does not pass locally and fail in CI.
    /// </summary>
    [Fact]
    public void GlobPattern_Matches_DifferentCase_StillMatches()
    {
        // Arrange
        var glob = GlobPattern.Parse("artifacts/tests/*.trx");

        // Act / Assert
        Assert.True(glob.Matches("Artifacts/Tests/Results.TRX"));
    }

    /// <summary>
    ///     Validates that punctuation in a glob is matched literally rather than as a pattern.
    /// </summary>
    [Fact]
    public void GlobPattern_Matches_PunctuationInThePattern_IsLiteral()
    {
        // Arrange: the dot must not behave as a regular-expression wildcard
        var glob = GlobPattern.Parse("artifacts/tests.trx");

        // Act / Assert
        Assert.Multiple(
            () => Assert.True(glob.Matches("artifacts/tests.trx")),
            () => Assert.False(glob.Matches("artifacts/tests-trx")));
    }

    /// <summary>
    ///     Validates that a null glob is refused rather than treated as matching nothing.
    /// </summary>
    [Fact]
    public void GlobPattern_Parse_Null_ThrowsArgumentNullException()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => GlobPattern.Parse(null!));
    }
}
