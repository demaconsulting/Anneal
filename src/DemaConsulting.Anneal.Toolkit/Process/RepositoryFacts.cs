using System.ComponentModel;

namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>
///     A coarse, deterministic hint about what kind of process a work item's own text is likely to need, decided
///     by keyword alone rather than by any model call.
/// </summary>
/// <remarks>
///     Deliberately imprecise. This is a fact worth handing to a route oracle as context, not a substitute for the
///     oracle's own judgement — a keyword match is cheap and honest about being cheap, which is why it is never
///     treated as the routing decision itself.
/// </remarks>
internal enum RequestImplication
{
    /// <summary>No scope could be inferred from the work item's own text.</summary>
    [Description("no scope could be inferred from the work item text")]
    Unknown,

    /// <summary>The work item's text implies writing or changing code or documentation.</summary>
    [Description("the work item implies writing or code/documentation changes")]
    Writing,

    /// <summary>The work item's text implies judging existing work rather than producing new work.</summary>
    [Description("the work item implies verification only, with no authoring")]
    VerificationOnly,

    /// <summary>The work item's text implies only looking around, with no authoring or verification.</summary>
    [Description("the work item implies research only, with no authoring or verification")]
    ResearchOnly
}

/// <summary>
///     The repository facts a <see cref="Router" /> gathers deterministically before asking a route oracle
///     anything, so the oracle's question is grounded in what is actually true of the checkout rather than left
///     for the model to go rediscover on every pass.
/// </summary>
/// <remarks>
///     Every fact here is computed by reading files and matching text, never by a model call — the same
///     "judgement stays in the model; code owns control flow" split <c>docs/architecture/toolkit.md</c> § Decisions
///     draws for the Toolkit as a whole, applied to how a <see cref="RoutingLedger" /> is assembled.
/// </remarks>
/// <param name="ReadmeDirectionFacts">
///     The bullet-level lines under <c>README.md</c>'s own <c>## Direction</c> heading, or empty when the file or
///     heading is absent.
/// </param>
/// <param name="MigrationPresent">Whether <c>MIGRATION.md</c> exists in the repository.</param>
/// <param name="MigrationCurrentStage">
///     The heading text of <c>MIGRATION.md</c>'s <c>## Current stage</c> entry, or null when the file is absent or
///     the heading is not found.
/// </param>
/// <param name="RelevantArchitectureNodes">
///     The <c>docs/architecture/*.md</c> file names whose own name is mentioned, case-insensitively, in the work
///     item's text. Empty when none match, which is an honest answer rather than a guess.
/// </param>
/// <param name="ChangedFileHints">The changed-file hints a caller supplied, or empty when none were given.</param>
/// <param name="RequestsTemplateSync">Whether the work item's text names template synchronization explicitly.</param>
/// <param name="Implication">The coarse scope keyword matching implies for this work item.</param>
internal sealed record RepositoryFacts(
    IReadOnlyList<string> ReadmeDirectionFacts,
    bool MigrationPresent,
    string? MigrationCurrentStage,
    IReadOnlyList<string> RelevantArchitectureNodes,
    IReadOnlyList<string> ChangedFileHints,
    bool RequestsTemplateSync,
    RequestImplication Implication)
{
    /// <summary>
    ///     Gathers repository facts for a work item, reading only the files this method names and matching only on
    ///     their own text — no model call, no directory walk beyond <c>docs/architecture/</c>.
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
            ReadmeDirectionFacts: ReadDirectionFacts(root),
            MigrationPresent: File.Exists(Path.Combine(root, "MIGRATION.md")),
            MigrationCurrentStage: ReadMigrationCurrentStage(root),
            RelevantArchitectureNodes: ReadRelevantArchitectureNodes(root, workItem),
            ChangedFileHints: changedFileHints ?? [],
            RequestsTemplateSync: ContainsAll(workItem, "template", "sync"),
            Implication: InferImplication(workItem));
    }

    private static IReadOnlyList<string> ReadDirectionFacts(string root)
    {
        var path = Path.Combine(root, "README.md");
        if (!File.Exists(path))
            return [];

        var section = ReadSection(File.ReadAllLines(path), "## Direction");
        return [.. section
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal) ||
                           line.StartsWith("* ", StringComparison.Ordinal))
            .Select(line => line[2..].Trim())];
    }

    private static string? ReadMigrationCurrentStage(string root)
    {
        var path = Path.Combine(root, "MIGRATION.md");
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
        var directory = Path.Combine(root, "docs", "architecture");
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

    private static bool ContainsAll(string text, params string[] words) =>
        words.All(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <remarks>
    ///     Checked in a fixed order — verification, then research, then writing — because a request naming more
    ///     than one keyword group is read as the more cautious of the two rather than the more active one: judging
    ///     existing work is a narrower claim than changing it, so a request that mentions both is treated as the
    ///     narrower reading until an oracle says otherwise.
    /// </remarks>
    private static RequestImplication InferImplication(string workItem)
    {
        var verificationWords = new[] { "verify", "check", "audit", "review" };
        var researchWords = new[] { "research", "investigate", "look into", "explore" };
        var writingWords = new[] { "fix", "implement", "add", "change", "write", "author", "build", "repair" };

        if (verificationWords.Any(word => workItem.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return RequestImplication.VerificationOnly;

        if (researchWords.Any(word => workItem.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return RequestImplication.ResearchOnly;

        if (writingWords.Any(word => workItem.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return RequestImplication.Writing;

        return RequestImplication.Unknown;
    }
}
