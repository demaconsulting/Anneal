using DemaConsulting.Anneal.Toolkit.Process.Routing;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for <see cref="RepositoryFacts.Gather" />'s vision-fact extraction: frontmatter
///     stripping, heading stripping, and paragraph-level splitting of the remaining body.
/// </summary>
public class RepositoryFactsTests : IDisposable
{
    private readonly string _root;

    public RepositoryFactsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteVision(string content)
    {
        var dir = Path.Combine(_root, ".anneal", "governance");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "vision.md"), content);
    }

    [Fact]
    public void ReadVisionFacts_MissingFile_ReturnsEmpty()
    {
        // Arrange: no vision.md on disk

        // Act
        var facts = RepositoryFacts.Gather(_root, "fix something", null);

        // Assert
        Assert.Empty(facts.VisionFacts);
    }

    [Fact]
    public void ReadVisionFacts_ProseParagraphsNoBullets_ReturnsParagraphFacts()
    {
        // Arrange: prose vision.md with no bullets — the production shape
        WriteVision("""
            # Vision

            First paragraph that describes something important
            spanning two lines.

            Second paragraph with more context.
            """);

        // Act
        var facts = RepositoryFacts.Gather(_root, "fix something", null);

        // Assert: two paragraphs returned, not empty
        Assert.Equal(2, facts.VisionFacts.Count);
        Assert.Contains("First paragraph", facts.VisionFacts[0]);
        Assert.Contains("Second paragraph", facts.VisionFacts[1]);
    }

    [Fact]
    public void ReadVisionFacts_WithYamlFrontmatter_FrontmatterStripped()
    {
        // Arrange: vision.md with YAML frontmatter block
        WriteVision("""
            ---
            reference: docs/user-guide/
            ---

            # Vision

            Body paragraph here.
            """);

        // Act
        var facts = RepositoryFacts.Gather(_root, "fix something", null);

        // Assert: only the body paragraph returned, no frontmatter fields
        Assert.Single(facts.VisionFacts);
        Assert.Equal("Body paragraph here.", facts.VisionFacts[0]);
    }

    [Fact]
    public void ReadVisionFacts_HeadingStripped_HeadingNotInFacts()
    {
        // Arrange
        WriteVision("""
            # Vision

            Real content here.
            """);

        // Act
        var facts = RepositoryFacts.Gather(_root, "fix something", null);

        // Assert: the '# Vision' heading itself is not present as a fact
        Assert.All(facts.VisionFacts, f => Assert.DoesNotContain("# Vision", f));
        Assert.Single(facts.VisionFacts);
        Assert.Equal("Real content here.", facts.VisionFacts[0]);
    }

    [Fact]
    public void ReadVisionFacts_MultipleParagraphsWithFrontmatterAndHeading_AllParagraphsDistinct()
    {
        // Arrange: full production-style file with frontmatter + heading + multiple paragraphs
        WriteVision("""
            ---
            reference: docs/user-guide/
            ---

            # Vision

            Alpha paragraph content.

            Beta paragraph content that is longer
            and wraps across lines.

            Gamma paragraph.
            """);

        // Act
        var facts = RepositoryFacts.Gather(_root, "fix something", null);

        // Assert: three distinct paragraphs, each non-empty
        Assert.Equal(3, facts.VisionFacts.Count);
        Assert.Contains("Alpha", facts.VisionFacts[0]);
        Assert.Contains("Beta", facts.VisionFacts[1]);
        Assert.Contains("Gamma", facts.VisionFacts[2]);
    }

    [Fact]
    public void ReadVisionFacts_BulletFile_BulletsReturnedAsParagraphFacts()
    {
        // Arrange: a bullet-list vision.md — bullets are not prose paragraphs but are still returned
        // as paragraph-level facts (each bullet group becomes one joined fact)
        WriteVision("""
            # Vision

            - First bullet
            - Second bullet
            """);

        // Act
        var facts = RepositoryFacts.Gather(_root, "fix something", null);

        // Assert: one paragraph fact containing both bullets joined
        Assert.Single(facts.VisionFacts);
        Assert.Contains("First bullet", facts.VisionFacts[0]);
        Assert.Contains("Second bullet", facts.VisionFacts[0]);
    }
}
