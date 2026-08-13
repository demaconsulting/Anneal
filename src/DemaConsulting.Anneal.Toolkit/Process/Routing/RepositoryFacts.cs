namespace DemaConsulting.Anneal.Toolkit.Process.Routing;

/// <summary>
///     The repository facts a <see cref="Router" /> gathers deterministically before asking a route oracle
///     anything, so the oracle's question is grounded in what is actually true of the checkout rather than left
///     for the model to go rediscover on every pass.
/// </summary>
/// <remarks>
///     Every fact here is computed by reading files and matching text, never by a model call — the same
///     "judgement stays in the model; code owns control flow" split <c>.anneal/architecture/toolkit.md</c> §
///     Decisions draws for the Toolkit as a whole, applied to how a <see cref="RoutingLedger" /> is assembled.
/// </remarks>
/// <param name="VisionFacts">
///     The paragraph-level content of <c>.anneal/governance/vision.md</c> with YAML frontmatter and the top-level
///     heading stripped, or empty when the file is absent or its body is blank.
/// </param>
/// <param name="TenetFacts">
///     The bullet-level content of <c>.anneal/governance/tenets.md</c> with YAML frontmatter and the top-level
///     heading stripped: one entry per bullet line (lines starting with <c>- </c> or <c>* </c>), with the
///     bullet marker itself removed. Empty when the file is absent, its body is blank, or it contains no bullets.
/// </param>
/// <param name="MigrationPresent">Whether <c>.anneal/work/active-plan.md</c> exists in the repository.</param>
/// <param name="MigrationCurrentStage">
///     The heading text of <c>.anneal/work/active-plan.md</c>'s <c>## Current stage</c> entry, or null when the
///     file is absent or the heading is not found.
/// </param>
/// <param name="RelevantArchitectureNodes">
///     The <c>.anneal/architecture/*.md</c> file names whose own name is mentioned, case-insensitively, in the work
///     item's text. Empty when none match, which is an honest answer rather than a guess.
/// </param>
/// <param name="ChangedFileHints">
///     The changed-file hints a caller supplied, or empty when none were given. These are evidence for the route
///     oracle to judge request intent and scope itself; RepositoryFacts does not pre-classify intent from keywords.
/// </param>
internal sealed record RepositoryFacts(
    IReadOnlyList<string> VisionFacts,
    IReadOnlyList<string> TenetFacts,
    bool MigrationPresent,
    string? MigrationCurrentStage,
    IReadOnlyList<string> RelevantArchitectureNodes,
    IReadOnlyList<string> ChangedFileHints)
{
    /// <summary>
    ///     Gathers repository facts for a work item, reading only the files this method names and matching only on
    ///     their own text — no model call, no directory walk beyond <c>.anneal/architecture/</c>.
    /// </summary>
    /// <param name="repositoryRoot">The repository read. Must not be null or blank.</param>
    /// <param name="workItem">The work item text the facts are gathered against. Must not be null or blank.</param>
    /// <param name="changedFileHints">The changed-file hints a caller supplied, or null when none were given.</param>
    /// <returns>The gathered facts.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="repositoryRoot" /> or <paramref name="workItem" /> is null, empty or blank.
    /// </exception>
    public static RepositoryFacts Gather(
        string repositoryRoot, string workItem, IReadOnlyList<string>? changedFileHints)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem);

        var root = Path.GetFullPath(repositoryRoot);

        return new RepositoryFacts(
            VisionFacts: ReadVisionFacts(root),
            TenetFacts: ReadTenetFacts(root),
            MigrationPresent: File.Exists(Path.Combine(root, ".anneal", "work", "active-plan.md")),
            MigrationCurrentStage: ReadMigrationCurrentStage(root),
            RelevantArchitectureNodes: ReadRelevantArchitectureNodes(root, workItem),
            ChangedFileHints: changedFileHints ?? []);
    }

    /// <remarks>
    ///     Bullet extraction was the original approach, but vision.md is prose paragraphs — there are no bullets
    ///     to extract, so the old code silently returned an empty list every time. The correct extraction strips
    ///     a leading YAML frontmatter block (--- ... ---) and the top-level '# ' heading, then splits the
    ///     remaining body on blank-line boundaries and returns each non-empty paragraph as one fact string.
    ///     This lets the whole document body reach the route oracle without any structural assumption about
    ///     whether the author chose bullets, numbered lists, or prose.
    /// </remarks>
    private static IReadOnlyList<string> ReadVisionFacts(string root)
    {
        var path = Path.Combine(root, ".anneal", "governance", "vision.md");
        if (!File.Exists(path))
            return [];

        var lines = File.ReadAllLines(path);
        var bodyStart = 0;

        // Strip YAML frontmatter block (--- ... ---) if present at the top of the file.
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var fmEnd = -1;
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "---")
                    continue;
                fmEnd = i;
                break;
            }

            if (fmEnd >= 0)
                bodyStart = fmEnd + 1;
        }

        // Skip blank lines and then the top-level '# ' heading line.
        while (bodyStart < lines.Length && string.IsNullOrWhiteSpace(lines[bodyStart]))
            bodyStart++;

        if (bodyStart < lines.Length && lines[bodyStart].StartsWith("# ", StringComparison.Ordinal))
            bodyStart++;

        // Collect remaining non-empty paragraphs, splitting on blank lines.
        List<string> facts = [];
        List<string> paragraph = [];

        for (var i = bodyStart; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                if (paragraph.Count > 0)
                {
                    facts.Add(string.Join(" ", paragraph));
                    paragraph.Clear();
                }
            }
            else
            {
                paragraph.Add(lines[i].Trim());
            }
        }

        if (paragraph.Count > 0)
            facts.Add(string.Join(" ", paragraph));

        return facts;
    }

    /// <remarks>
    ///     Bullet extraction is appropriate for tenets.md because its structure is a list of discrete,
    ///     independently-evaluable assertions — unlike vision.md's prose paragraphs, which describe connected
    ///     intent and lose meaning when split at bullet boundaries. Each bullet becomes exactly one tenet entry,
    ///     with the leading <c>- </c> or <c>* </c> marker stripped so callers receive plain text.
    /// </remarks>
    private static IReadOnlyList<string> ReadTenetFacts(string root)
    {
        var path = Path.Combine(root, ".anneal", "governance", "tenets.md");
        if (!File.Exists(path))
            return [];

        var lines = File.ReadAllLines(path);
        var bodyStart = 0;

        // Strip YAML frontmatter block (--- ... ---) if present at the top of the file.
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var fmEnd = -1;
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "---")
                    continue;
                fmEnd = i;
                break;
            }

            if (fmEnd >= 0)
                bodyStart = fmEnd + 1;
        }

        // Skip blank lines and then the top-level '# ' heading line.
        while (bodyStart < lines.Length && string.IsNullOrWhiteSpace(lines[bodyStart]))
            bodyStart++;

        if (bodyStart < lines.Length && lines[bodyStart].StartsWith("# ", StringComparison.Ordinal))
            bodyStart++;

        // Collect bullet lines, stripping the leading '- ' or '* ' marker.
        List<string> tenets = [];
        for (var i = bodyStart; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("- ", StringComparison.Ordinal))
                tenets.Add(line[2..].Trim());
            else if (line.StartsWith("* ", StringComparison.Ordinal))
                tenets.Add(line[2..].Trim());
        }

        return tenets;
    }

    private static string? ReadMigrationCurrentStage(string root)
    {
        var path = Path.Combine(root, ".anneal", "work", "active-plan.md");
        if (!File.Exists(path))
            return null;

        var section = ReadSection(File.ReadAllLines(path), "## Current stage");
        var heading = section.FirstOrDefault(line => line.StartsWith("### ", StringComparison.Ordinal));
        return heading?[4..].Trim();
    }

    /// <remarks>
    ///     Returns every line between <paramref name="heading" /> (exclusive) and the next line starting with
    ///     <c>## </c> (exclusive), or end of file. A heading not found yields an empty section rather than a
    ///     thrown exception, because "the file no longer says this" is a fact this method reports, not an error.
    /// </remarks>
    private static IReadOnlyList<string> ReadSection(IReadOnlyList<string> lines, string heading)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith(heading, StringComparison.OrdinalIgnoreCase))
                continue;
            start = i + 1;
            break;
        }

        if (start < 0)
            return [];

        List<string> section = [];
        for (var i = start; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal))
                break;
            section.Add(lines[i]);
        }

        return section;
    }

    private static IReadOnlyList<string> ReadRelevantArchitectureNodes(string root, string workItem)
    {
        var directory = Path.Combine(root, ".anneal", "architecture");
        if (!Directory.Exists(directory))
            return [];

        List<string> matches = [];
        foreach (var file in Directory.GetFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (workItem.Contains(name, StringComparison.OrdinalIgnoreCase))
                matches.Add(Path.GetFileName(file));
        }

        return matches;
    }
}
