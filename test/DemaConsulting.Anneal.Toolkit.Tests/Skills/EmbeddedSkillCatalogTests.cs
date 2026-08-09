using DemaConsulting.Anneal.Toolkit.Skills;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Skills;

/// <summary>
///     Interior tests for the built-in embedded skill catalog.
/// </summary>
public class EmbeddedSkillCatalogTests
{
    [Fact]
    public void Load_ReadsTheBundledSkillCatalogFromEmbeddedMarkdownResources()
    {
        // Arrange: the production assembly and its embedded skill resources.
        var loader = new SkillCatalogLoader(typeof(SkillCatalogLoader).Assembly);

        // Act: load the bundled catalog.
        var skills = loader.Load();
        var placeholderSkill = Assert.Single(skills, skill => skill.Id == "check-contracts-placeholder-form");

        // Assert: the expected worked example is present and well-formed.
        Assert.Multiple(
            () => Assert.Contains("check-contracts", placeholderSkill.Tags),
            () => Assert.Contains("TODO.", placeholderSkill.Summary, StringComparison.Ordinal),
            () => Assert.Contains("unfulfilled obligation", placeholderSkill.Body, StringComparison.Ordinal));
    }
}
