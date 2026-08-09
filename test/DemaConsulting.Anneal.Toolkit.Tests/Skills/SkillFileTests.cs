using DemaConsulting.Anneal.Toolkit.Skills;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Skills;

/// <summary>
///     Interior tests for the shared skill file format.
/// </summary>
public class SkillFileTests
{
    [Fact]
    public void WriteAndRead_RoundTripPreservesTheSharedSkillShape()
    {
        // Arrange: one valid skill in the shared shape.
        var skill = new Skill(
            "protected-path-tripwire-normalization",
            ["paths", "maintenance"],
            "Normalize repository paths before comparing them to declared scope.",
            "Use Path.GetFullPath and Path.GetRelativePath before comparing path scope.");

        // Act: render it and parse it back.
        var rendered = SkillFile.Write(skill);
        var parsed = SkillFile.Read(rendered, "inline");

        // Assert: the round-trip preserved every field.
        Assert.Multiple(
            () => Assert.Equal(skill.Id, parsed.Id),
            () => Assert.Equal(skill.Tags, parsed.Tags),
            () => Assert.Equal(skill.Summary, parsed.Summary),
            () => Assert.Equal(skill.Body, parsed.Body));
    }

    [Fact]
    public void Read_RejectsAMalformedFrontMatterBlock()
    {
        // Arrange: a skill file whose tags key is missing.
        const string malformed = """
                                 ---
                                 id: malformed
                                 summary: Missing tags.
                                 ---

                                 Body.
                                 """;

        // Act / Assert: parsing fails closed.
        var exception = Assert.Throws<SkillFormatException>(() => SkillFile.Read(malformed, "malformed.md"));
        Assert.Contains("required front matter key 'tags' is missing", exception.Message, StringComparison.Ordinal);
    }
}
