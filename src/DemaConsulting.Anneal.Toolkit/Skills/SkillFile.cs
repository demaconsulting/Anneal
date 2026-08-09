using System.Text;

namespace DemaConsulting.Anneal.Toolkit.Skills;

/// <summary>
///     Reads and writes the markdown-plus-front-matter file shape shared by repository-local and embedded skills.
/// </summary>
/// <remarks>
///     The format is deliberately the same across both tiers so one parser validates both: a repository-local
///     skill written today and an embedded Toolkit skill shipped in a release differ only in where the bytes came
///     from. Parsing therefore fails closed - malformed front matter is reported as a format exception rather than
///     guessed at or partially skipped - because a silently dropped skill would be a search surface lying about
///     what knowledge it actually holds.
/// </remarks>
internal static class SkillFile
{
    private const string FrontMatterDelimiter = "---";

    /// <summary>
    ///     Parses one skill file's markdown text into the shared <see cref="Skill" /> shape.
    /// </summary>
    /// <param name="markdown">The full file content. Must not be null.</param>
    /// <param name="sourceName">The source name to mention in any format exception. Must not be null.</param>
    /// <returns>The parsed skill.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="markdown" /> or <paramref name="sourceName" /> is null.</exception>
    /// <exception cref="SkillFormatException">Thrown when the front matter is missing, malformed, or incomplete.</exception>
    public static Skill Read(string markdown, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(sourceName);

        using var reader = new StringReader(markdown);

        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine, FrontMatterDelimiter, StringComparison.Ordinal))
            throw Invalid(sourceName, "the file does not open with YAML front matter");

        string? id = null;
        string? summary = null;
        List<string> tags = [];

        var sawTagsKey = false;
        var inTags = false;

        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
                throw Invalid(sourceName, "the YAML front matter is not closed");

            if (string.Equals(line, FrontMatterDelimiter, StringComparison.Ordinal))
                break;

            if (line.Length == 0)
                continue;

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("  - ", StringComparison.Ordinal))
            {
                if (!inTags)
                    throw Invalid(sourceName, "a tag list item appeared before the 'tags:' key");

                var tag = line[(line.IndexOf("- ", StringComparison.Ordinal) + 2)..].Trim();
                if (string.IsNullOrWhiteSpace(tag))
                    throw Invalid(sourceName, "a tag list item is blank");

                tags.Add(tag);
                continue;
            }

            inTags = false;

            var separator = line.IndexOf(':');
            if (separator < 0)
                throw Invalid(sourceName, $"front matter line '{line}' is not a 'key: value' entry");

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            switch (key)
            {
                case "id":
                    id = RequireValue(sourceName, key, value);
                    break;

                case "summary":
                    summary = RequireValue(sourceName, key, value);
                    break;

                case "tags":
                    sawTagsKey = true;
                    if (!string.IsNullOrEmpty(value))
                    {
                        foreach (var tag in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            tags.Add(tag);
                    }

                    inTags = true;
                    break;

                default:
                    throw Invalid(sourceName, $"front matter key '{key}' is not supported");
            }
        }

        if (string.IsNullOrWhiteSpace(id))
            throw Invalid(sourceName, "required front matter key 'id' is missing or blank");

        if (string.IsNullOrWhiteSpace(summary))
            throw Invalid(sourceName, "required front matter key 'summary' is missing or blank");

        if (!sawTagsKey)
            throw Invalid(sourceName, "required front matter key 'tags' is missing");

        if (tags.Count == 0)
            throw Invalid(sourceName, "the tag list is empty");

        var body = reader.ReadToEnd();
        body = body.StartsWith("\r\n", StringComparison.Ordinal)
            ? body[2..]
            : body.StartsWith('\n') ? body[1..] : body;

        if (string.IsNullOrWhiteSpace(body))
            throw Invalid(sourceName, "the markdown body is missing or blank");

        try
        {
            return new Skill(id, tags, summary, body);
        }
        catch (ArgumentException exception)
        {
            throw Invalid(sourceName, exception.Message, exception);
        }
    }

    /// <summary>
    ///     Renders one skill to the markdown-plus-front-matter file shape both tiers share.
    /// </summary>
    /// <param name="skill">The skill to render. Must not be null.</param>
    /// <returns>The rendered markdown file.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="skill" /> is null.</exception>
    public static string Write(Skill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var builder = new StringBuilder();
        builder.AppendLine(FrontMatterDelimiter);
        builder.Append("id: ").AppendLine(skill.Id);
        builder.AppendLine("tags:");
        foreach (var tag in skill.Tags)
            builder.Append("  - ").AppendLine(tag);
        builder.Append("summary: ").AppendLine(skill.Summary);
        builder.AppendLine(FrontMatterDelimiter);
        builder.AppendLine();
        builder.Append(skill.Body);
        return builder.ToString();
    }

    private static string RequireValue(string sourceName, string key, string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw Invalid(sourceName, $"required front matter key '{key}' is blank")
            : value;

    private static SkillFormatException Invalid(string sourceName, string reason, Exception? innerException = null)
    {
        var message = $"Skill file '{sourceName}' is invalid: {reason}.";
        return innerException is null ? new SkillFormatException(message) : new SkillFormatException(message, innerException);
    }
}

/// <summary>
///     The failure raised when a skill file cannot be parsed into the shared front-matter shape.
/// </summary>
internal sealed class SkillFormatException : Exception
{
    /// <summary>Creates the exception with a message describing the malformed skill file.</summary>
    /// <param name="message">Human-readable description of the failure.</param>
    public SkillFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    /// <param name="message">Human-readable description of the failure.</param>
    /// <param name="innerException">The underlying parse or validation error.</param>
    public SkillFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
