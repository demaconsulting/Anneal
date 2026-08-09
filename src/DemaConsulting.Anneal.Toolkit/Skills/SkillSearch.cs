namespace DemaConsulting.Anneal.Toolkit.Skills;

/// <summary>
///     Performs deterministic lexical ranking over skills using only id, tags, and summary text.
/// </summary>
/// <remarks>
///     This is the one ranking both explicit <c>search-skills</c> queries and automatic worker-context injection
///     reuse. The contract deliberately defers any vector index; lexical matching is the whole mechanism here.
/// </remarks>
internal static class SkillSearch
{
    /// <summary>
    ///     Ranks the supplied skills against one query.
    /// </summary>
    /// <param name="query">The search query. Must not be null.</param>
    /// <param name="skills">The skills to rank. Must not be null.</param>
    /// <returns>The matching skills, strongest match first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> or <paramref name="skills" /> is null.</exception>
    public static IReadOnlyList<Skill> Rank(string query, IEnumerable<Skill> skills)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(skills);

        if (string.IsNullOrWhiteSpace(query))
            return [];

        var normalizedQuery = query.Trim();
        var tokens = Tokenize(normalizedQuery);

        return skills
            .Select(skill => new { Skill = skill, Score = Score(skill, normalizedQuery, tokens) })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Skill.Id, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Skill)
            .ToArray();
    }

    private static int Score(Skill skill, string query, IReadOnlyList<string> tokens)
    {
        var score = 0;

        if (skill.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 50;

        if (skill.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
            score += 30;

        if (skill.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)))
            score += 20;

        foreach (var token in tokens)
        {
            if (skill.Id.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 10;

            if (skill.Tags.Any(tag => tag.Contains(token, StringComparison.OrdinalIgnoreCase)))
                score += 8;

            if (skill.Summary.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += 6;
        }

        return score;
    }

    private static IReadOnlyList<string> Tokenize(string text)
    {
        List<string> tokens = [];
        var buffer = new List<char>(text.Length);

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Add(char.ToLowerInvariant(character));
                continue;
            }

            FlushBuffer(buffer, tokens);
        }

        FlushBuffer(buffer, tokens);
        return tokens;
    }

    private static void FlushBuffer(List<char> buffer, List<string> tokens)
    {
        if (buffer.Count == 0)
            return;

        tokens.Add(new string([.. buffer]));
        buffer.Clear();
    }
}
