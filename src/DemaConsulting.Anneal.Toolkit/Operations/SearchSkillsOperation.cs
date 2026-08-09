using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Skills;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Performs lexical search across repository-local skills and the embedded Toolkit-wide catalog.
/// </summary>
/// <remarks>
///     This is the read-only half of the Skills surface: it loads both tiers into one shared shape, applies the
///     one lexical ranking the contract defines, and returns the ranked matches with their full bodies. It
///     consults no model and keeps no separate notion of "relevance" for different callers.
///     <para>
///         It declares <see cref="OperationCategory.Research" /> because it answers a question the caller puts by
///         naming a query. That is the Runtime's research category exactly: a non-gating answer to a requested
///         question, not an unsolicited advisory report.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, though each run reads the repository's skill
///         directory and therefore sees whatever is on disk at the moment it runs.
///     </para>
/// </remarks>
public sealed class SearchSkillsOperation : IOperation
{
    private readonly string _repositoryRoot;

    /// <summary>
    ///     Creates an operation over the current working directory.
    /// </summary>
    public SearchSkillsOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root.
    /// </summary>
    /// <param name="repositoryRoot">The repository whose local skill tier is read. Must not be null, empty, or blank.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty, or blank.</exception>
    public SearchSkillsOperation(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    /// <inheritdoc />
    public string Name => "search-skills";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Research;

    /// <inheritdoc />
    public string Summary => "Search repository-local and built-in skills by lexical match";

    /// <inheritdoc />
    public ModelRole? RequiredRole => null;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal search-skills <query> - performs lexical search over repository-local skills in " +
        ".anneal/skills/ and the built-in Toolkit skill catalog, matching against each skill's id, tags, and " +
        "summary, and returns the ranked matches with their full bodies.";

    /// <inheritdoc />
    /// <remarks>
    ///     Expects exactly one positional query argument. A missing query is a usage error; an empty query is a
    ///     successful search with zero matches.
    /// </remarks>
    public Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Count != 1)
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));

        var query = arguments[0];
        if (string.IsNullOrWhiteSpace(query))
        {
            output.WriteLine("search-skills: 0 match(es).");
            return Task.FromResult(new OperationResult(
                OperationOutcome.Succeeded,
                new SearchSkillsReport([])));
        }

        var localSkills = new RepositorySkillCatalog(_repositoryRoot).Load();
        var ranked = SkillSearch.Rank(query, [.. localSkills, .. EmbeddedSkillCatalog.All]);

        output.WriteLine($"search-skills: {ranked.Count} match(es).");
        foreach (var skill in ranked)
        {
            output.WriteLine($"- {skill.Id}");
            output.WriteLine($"  tags: {string.Join(", ", skill.Tags)}");
            output.WriteLine($"  summary: {skill.Summary}");
            output.WriteLine("  body:");

            foreach (var line in skill.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                output.WriteLine($"    {line}");
        }

        return Task.FromResult(new OperationResult(
            OperationOutcome.Succeeded,
            new SearchSkillsReport(ranked)));
    }
}
