namespace DemaConsulting.Anneal.Toolkit.Skills;

/// <summary>
///     A curated, atomic lesson shipped either as a repository-local file or as an embedded Toolkit resource.
/// </summary>
/// <remarks>
///     A skill is one search unit across both tiers defined by <c>docs/architecture/toolkit/skills.md</c>: the
///     caller never reasons about a repository-local file and an embedded card as different result shapes. The
///     type therefore captures only the shared contract - identifier, tags, summary, and markdown body - and
///     nothing about how the skill was stored.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <exception cref="ArgumentException">
///     Thrown when the id, summary, or body is null, empty, or blank, or when the tag list is empty or contains
///     a null, empty, or blank entry.
/// </exception>
public sealed record Skill
{
    /// <summary>
    ///     Creates a skill in the shared shape both skill tiers use.
    /// </summary>
    public Skill(string id, IReadOnlyList<string> tags, string summary, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        if (tags.Count == 0)
            throw new ArgumentException("A skill must carry at least one tag.", nameof(tags));

        var cleaned = tags
            .Select(tag => string.IsNullOrWhiteSpace(tag)
                ? throw new ArgumentException("Skill tags must not contain blank entries.", nameof(tags))
                : tag.Trim())
            .ToArray();

        Id = id.Trim();
        Tags = cleaned;
        Summary = summary.Trim();
        Body = body;
    }

    /// <summary>The skill identifier.</summary>
    public string Id { get; }

    /// <summary>The skill's classification tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>The one-line summary shown in search results.</summary>
    public string Summary { get; }

    /// <summary>The markdown body returned when the skill is surfaced.</summary>
    public string Body { get; }
}
