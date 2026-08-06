using System.Diagnostics.CodeAnalysis;

namespace DemaConsulting.Anneal.Toolkit.Model.Tools;

/// <summary>
///     The single containment primitive for every tool a model can drive: it resolves a model-supplied,
///     notionally relative path against the repository root and decides whether the result stays inside it.
/// </summary>
/// <remarks>
///     Every filesystem-touching tool funnels its path through here, so "a model can only touch files under one
///     repository root" lives in one auditable place rather than being re-derived — and previously mis-derived,
///     by a boundary-less prefix test that admitted a sibling directory sharing the root's name — at each call
///     site.
///     <para>
///         <b>What it guarantees.</b> When <see cref="TryResolve" /> answers true, the path returned is a
///         normalized absolute path whose textual form is the root itself or a descendant of it across a real
///         directory-separator boundary, so a sibling directory whose name merely starts with the root's is
///         rejected. Rooted, drive-qualified, cross-drive, UNC and device inputs are rejected, as is anything
///         climbing above the root through <c>..</c>. An alternate-data-stream suffix such as
///         <c>fix.ps1::$DATA</c> — which the runtime resolves to the file's own contents, defeating any check
///         phrased over the resolved path — is rejected, as is any other path carrying a colon. A path the
///         runtime cannot parse at all — an embedded NUL,
///         an illegal character, an over-long name — is rejected too. It fails closed and never throws, because
///         a containment check that throws is one some caller will eventually wrap in a swallow.
///     </para>
///     <para>
///         <b>What it deliberately does not guarantee.</b> The check is purely <em>lexical</em>. It does not
///         resolve symbolic links, directory junctions or mount points. A link that already exists under the
///         root and
///         points outside it is textually contained and <em>will</em> be accepted here despite physically
///         escaping. Hard containment would require resolving the real target of every segment, which this type
///         intentionally does not do; the residual exposure is bounded because nothing the Toolkit grants a model
///         can create such a link — there is no shell tool and no link-creating tool in any group.
///     </para>
///     <para>Thread safety: stateless and safe to call concurrently.</para>
/// </remarks>
public static class RepositoryPath
{
    /// <summary>
    ///     Resolves a model-supplied path under a repository root, answering whether it stays inside.
    /// </summary>
    /// <remarks>
    ///     A rooted input is refused outright rather than normalized. It is never a legitimate value for a
    ///     "repository-relative path" argument, and refusing it removes the ambient nondeterminism of the
    ///     drive-relative form <c>C:foo</c>, which the runtime would otherwise resolve against the process's
    ///     per-drive current directory instead of against the root.
    /// </remarks>
    /// <param name="repositoryRoot">
    ///     The root every path is confined to. Normalized internally, so a caller may pass a path that is not
    ///     yet absolute. Null or empty answers false.
    /// </param>
    /// <param name="relativePath">
    ///     The model-supplied path, treated as adversarial. Empty or <c>"."</c> resolves to the root itself.
    ///     Null answers false.
    /// </param>
    /// <param name="fullPath">
    ///     The contained absolute path when the answer is true — possibly the root itself — and null otherwise.
    /// </param>
    /// <returns>True when the path is contained; false when it escapes, is rooted, or cannot be parsed.</returns>
    public static bool TryResolve(
        string? repositoryRoot, string? relativePath, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrEmpty(repositoryRoot) || relativePath is null)
            return false;

        if (Path.IsPathRooted(relativePath))
            return false;

        // An alternate-data-stream suffix is refused here rather than downstream, because the runtime resolves
        // it away silently: 'fix.ps1::$DATA' names the default data stream of 'fix.ps1', so File.Exists answers
        // true and a write through it lands in the file itself. A deny-list matching text would miss it, and so
        // would every other check phrased over the resolved relative path. A colon has no legitimate place in a
        // repository-relative path on any platform this runs on - a drive qualifier is already refused as
        // rooted - so refusing it outright closes the alias for every tool at once rather than for one check.
        if (relativePath.Contains(':', StringComparison.Ordinal))
            return false;

        string root, candidate, relative;
        try
        {
            root = Path.GetFullPath(repositoryRoot);
            candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            relative = Path.GetRelativePath(root, candidate);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException
                or System.Security.SecurityException)
        {
            // Fail closed. A path the runtime cannot even parse is not one this primitive may guess about.
            return false;
        }

        // A relative form beginning with ".." climbed above the root, and a rooted one means the candidate
        // landed on a different volume: GetRelativePath returns the absolute target when there is no shared
        // root left to express.
        if (relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            return false;

        fullPath = candidate;
        return true;
    }

    /// <summary>
    ///     As <see cref="TryResolve" />, but also refuses a path that resolves to the repository root itself.
    /// </summary>
    /// <remarks>
    ///     For a caller that must name a file, where "the root" is never a valid answer and would otherwise
    ///     surface as an opaque I/O error deep inside a write rather than as a refusal the model can act on.
    /// </remarks>
    /// <param name="repositoryRoot">The root every path is confined to. Normalized internally.</param>
    /// <param name="relativePath">The model-supplied path, treated as adversarial.</param>
    /// <param name="fullPath">The contained absolute file path when the answer is true, and null otherwise.</param>
    /// <returns>True when the path is contained and is not the root itself; false otherwise.</returns>
    public static bool TryResolveFile(
        string? repositoryRoot, string? relativePath, [NotNullWhen(true)] out string? fullPath)
    {
        if (!TryResolve(repositoryRoot, relativePath, out fullPath))
            return false;

        if (!string.Equals(fullPath, Path.GetFullPath(repositoryRoot!), PathComparison))
            return true;

        fullPath = null;
        return false;
    }

    /// <summary>
    ///     Renders a contained absolute path as the repository-relative, forward-slashed form a model and a
    ///     transcript both read.
    /// </summary>
    /// <param name="repositoryRoot">The root the path is relative to. Must not be null or blank.</param>
    /// <param name="fullPath">The absolute path to render. Must not be null or blank.</param>
    /// <returns>The repository-relative path, using <c>/</c> whatever the host separator is.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is null, empty or blank.</exception>
    public static string Relative(string repositoryRoot, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        return Path.GetRelativePath(Path.GetFullPath(repositoryRoot), fullPath).Replace('\\', '/');
    }

    /// <remarks>
    ///     Matched to the host filesystem's own semantics: comparing case-sensitively on Windows would let
    ///     <c>.CSPELL.YAML</c> past a check that <c>.cspell.yaml</c> fails.
    /// </remarks>
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
