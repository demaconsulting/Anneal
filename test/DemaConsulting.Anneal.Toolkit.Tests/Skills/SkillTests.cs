using DemaConsulting.Anneal.Toolkit.Skills;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Skills;

/// <summary>
///     Interior tests for the shared <see cref="Skill" /> shape's own invariants.
/// </summary>
public class SkillTests
{
    [Theory]
    [InlineData("sub/dir")]
    [InlineData("sub\\dir")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData(".")]
    public void Constructor_RejectsAnIdThatIsNotASinglePathSegment(string id)
    {
        // Act / Assert: an id that would resolve to more than one path segment, or to a directory
        // traversal, is rejected up front so no skill file can ever be written where a same-tier
        // directory-only loader would fail to find it again.
        var exception = Assert.Throws<ArgumentException>(() => new Skill(id, ["tag"], "summary", "body"));
        Assert.Contains("single path segment", exception.Message, StringComparison.Ordinal);
    }
}
