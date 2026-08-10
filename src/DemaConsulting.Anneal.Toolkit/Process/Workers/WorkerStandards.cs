using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Routing;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     Reads a worker's own small, fixed list of standards documents from <c>.github/standards/</c> verbatim, for
///     injection into a <see cref="Developer" />/<see cref="DocumentAuthor" />/<see cref="Planner" /> prompt before
///     its first (and any repair) call.
/// </summary>
/// <remarks>
///     Mirrors <c>AGENTS.md</c>'s own "Standards Application" table, statically, per worker — the same table the
///     prose agents <c>apply</c> and <c>architecture-update</c> already read before touching anything. This is
///     deliberately the minimal mechanism <c>MIGRATION.md</c>'s S12 entry describes: no oracle call, no dynamic
///     selection from the work item's own text, a fixed list per worker decided once at compile time.
///     <para>
///         A standard that does not exist at the expected path is skipped, not a thrown exception — a repository
///         that has not installed Anneal's standards (or has renamed one) still gets a worker that runs, just
///         without that one piece of optional guidance. A compiled worker should not hard-fail an entire run
///         because advisory content is absent, the same "best effort over an optional read" posture
///         <see cref="Process.Routing.RepositoryFacts" /> already takes for <c>vision.md</c> and <c>MIGRATION.md</c>.
///     </para>
/// </remarks>
internal static class WorkerStandards
{
    /// <summary>
    ///     Renders the named standards documents, verbatim, wrapped for injection into a prompt.
    /// </summary>
    /// <param name="repositoryRoot">The repository read. Must not be null or blank.</param>
    /// <param name="fileNames">
    ///     The file names under <c>.github/standards/</c> to load, in the order they should render. A name whose
    ///     file does not exist is skipped rather than failing the whole render.
    /// </param>
    /// <returns>
    ///     The requested standards, each wrapped in a <c>&lt;standard&gt;</c> tag naming its file, joined with blank
    ///     lines; or <c>"none available"</c> when every named file was missing.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public static string Render(string repositoryRoot, params string[] fileNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        List<string> sections = [];
        foreach (var fileName in fileNames)
        {
            var path = Path.Combine(repositoryRoot, ".github", "standards", fileName);
            if (!File.Exists(path))
                continue;

            sections.Add(
                $"""
                 <standard name="{fileName}">
                 {File.ReadAllText(path)}
                 </standard>
                 """);
        }

        return sections.Count == 0 ? "none available" : string.Join("\n\n", sections);
    }
}
