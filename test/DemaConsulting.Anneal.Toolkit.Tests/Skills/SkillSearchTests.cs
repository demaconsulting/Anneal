using DemaConsulting.Anneal.Toolkit.Skills;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Skills;

/// <summary>
///     Interior tests for the shared lexical skill ranking.
/// </summary>
public class SkillSearchTests
{
    [Fact]
    public void Rank_PrefersTheSkillWhoseIdTagsAndSummaryMatchMoreOfTheQuery()
    {
        // Arrange: two skills that both match, but one matches more strongly.
        var strong = new Skill(
            "todo-placeholder-verifier",
            ["contracts", "placeholders"],
            "Use TODO. only for a planned verifier placeholder.",
            "Strong match.");
        var weak = new Skill(
            "todo-test-names",
            ["tests"],
            "A real test name may contain Todo without becoming a planned obligation.",
            "Weak match.");

        // Act: rank them against a query about TODO placeholders.
        var ranked = SkillSearch.Rank("TODO placeholder verifier", [weak, strong]);

        // Assert: both match and the stronger lexical match comes first.
        Assert.Multiple(
            () => Assert.Equal(2, ranked.Count),
            () => Assert.Equal("todo-placeholder-verifier", ranked[0].Id),
            () => Assert.Equal("todo-test-names", ranked[1].Id));
    }
}
