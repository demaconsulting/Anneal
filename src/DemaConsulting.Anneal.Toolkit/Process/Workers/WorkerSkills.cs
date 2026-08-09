using DemaConsulting.Anneal.Toolkit.Skills;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     Reads and renders the automatically injected skill context for a worker prompt.
/// </summary>
/// <remarks>
///     Skills are not standards, so they are assembled through their own helper rather than folded into
///     <see cref="WorkerStandards" />. The same lexical ranking drives both explicit <c>search-skills</c> queries
///     and this automatic prompt injection: one query string assembled from the worker's own work-item text and
///     changed-file hints, one ranked result list, and each match rendered with its summary and body before the
///     model ever asks.
/// </remarks>
internal static class WorkerSkills
{
    /// <summary>
    ///     Renders the ranked skill matches relevant to this worker turn.
    /// </summary>
    /// <param name="repositoryRoot">The repository whose local skill tier is read. Must not be null, empty, or blank.</param>
    /// <param name="workItem">The work-item text the worker is acting on. Must not be null.</param>
    /// <param name="changedFileHints">The changed-file hints gathered for the work item. Must not be null.</param>
    /// <returns>The rendered matched skills, or <c>"none"</c> when no skill matched.</returns>
    public static string Render(string repositoryRoot, string workItem, IReadOnlyList<string> changedFileHints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(changedFileHints);

        var query = string.Join(
            "\n",
            new[] { workItem }.Concat(changedFileHints.Where(hint => !string.IsNullOrWhiteSpace(hint))));

        var matches = SkillSearch.Rank(
            query,
            [.. new RepositorySkillCatalog(repositoryRoot).Load(), .. EmbeddedSkillCatalog.All]);

        if (matches.Count == 0)
            return "none";

        return string.Join(
            "\n\n",
            matches.Select(skill =>
                $"""
                 <skill id="{skill.Id}">
                 summary: {skill.Summary}

                 {skill.Body}
                 </skill>
                 """));
    }
}
