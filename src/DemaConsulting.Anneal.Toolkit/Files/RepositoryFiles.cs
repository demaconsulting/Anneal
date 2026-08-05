using System.Text.RegularExpressions;

namespace DemaConsulting.Anneal.Toolkit.Files;

/// <summary>
///     Finds the source files in a repository that a check should read, applying the two exclusion rules that
///     must never drift apart between one caller and the next.
/// </summary>
/// <remarks>
///     Both rules are fail-closed, and both are the reason this lives in one place rather than being written
///     out at each call site.
///     <list type="bullet">
///         <item>
///             Build output and vendored code are pruned during the walk rather than filtered after it. A
///             repository carrying <c>node_modules</c> costs seconds per pass otherwise, and a compiled copy
///             of a deleted test under <c>obj/</c> would keep the promise it proved alive.
///         </item>
///         <item>
///             Hidden entries are skipped. Reading them is the fail-open direction: a stale copy of a deleted
///             test under a hidden directory would satisfy a clause whose real test is gone.
///         </item>
///     </list>
///     <para>Thread safety: stateless and safe for concurrent calls; each call reads the disk as it is then.</para>
/// </remarks>
public static class RepositoryFiles
{
    /// <summary>
    ///     Directory names whose contents are build output or vendored code rather than repository sources.
    /// </summary>
    private static readonly string[] ExcludedDirectories = ["bin", "obj", "node_modules", ".venv", ".git"];

    /// <summary>
    ///     Directory names excluded from a whole-repository glob search. Narrower than
    ///     <see cref="ExcludedDirectories" /> on purpose: a glob names where its files live, so pruning
    ///     <c>bin</c> and <c>obj</c> would hide results a build legitimately wrote there, while vendored
    ///     dependency trees hold results belonging to somebody else's project entirely.
    /// </summary>
    private static readonly string[] ExcludedFromGlob = ["node_modules", ".venv"];

    /// <summary>
    ///     Finds every file under the named roots whose name matches one of the patterns.
    /// </summary>
    /// <remarks>
    ///     A root may itself be a wildcard such as <c>test/*</c>, which is expanded to the directories it
    ///     names; a root that resolves to nothing is skipped rather than reported, because "this repository
    ///     has no such directory" is a normal state for a configuration shared across repositories. A file
    ///     reached through more than one root is returned once.
    /// </remarks>
    /// <param name="repositoryRoot">
    ///     Directory that relative roots are resolved against. Must not be null or blank.
    /// </param>
    /// <param name="roots">Roots to search, relative to the repository root. Must not be null.</param>
    /// <param name="patterns">
    ///     File-name patterns, where <c>*</c> and <c>?</c> are wildcards and matching is case-insensitive.
    ///     Must not be null. A file matching any one of them is returned.
    /// </param>
    /// <returns>The matching files, in discovery order.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="roots" /> or <paramref name="patterns" /> is null.
    /// </exception>
    public static IReadOnlyList<FileInfo> UnderRoots(
        string repositoryRoot, IReadOnlyList<string> roots, IReadOnlyList<string> patterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(patterns);

        var matchers = patterns.Select(WildcardMatcher).ToList();
        var found = new List<FileInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();

        foreach (var root in roots)
        {
            foreach (var directory in ExpandRoot(repositoryRoot, root))
                pending.Enqueue(directory);

            while (pending.Count > 0)
            {
                var directory = new DirectoryInfo(pending.Dequeue());

                foreach (var child in directory.EnumerateDirectories())
                {
                    if (ExcludedDirectories.Contains(child.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (IsHidden(child)) continue;
                    pending.Enqueue(child.FullName);
                }

                foreach (var file in directory.EnumerateFiles())
                {
                    if (!matchers.Any(matcher => matcher.IsMatch(file.Name))) continue;
                    if (IsHidden(file)) continue;
                    if (seen.Add(file.FullName)) found.Add(file);
                }
            }
        }

        return found;
    }

    /// <summary>
    ///     Finds every file anywhere in the repository whose repository-relative path the glob names.
    /// </summary>
    /// <remarks>
    ///     Searched from the repository root rather than from the glob's leading directories, so that a glob
    ///     naming a directory which does not exist yields nothing instead of throwing — an absent results
    ///     directory is the ordinary state of a tree whose tests have not been run.
    /// </remarks>
    /// <param name="repositoryRoot">The repository root. Must not be null or blank.</param>
    /// <param name="glob">The glob to match repository-relative paths against. Must not be null.</param>
    /// <returns>The matching files, oldest written first, so a caller reading them in order sees the newest last.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="glob" /> is null.</exception>
    public static IReadOnlyList<FileInfo> MatchingGlob(string repositoryRoot, GlobPattern glob)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(glob);

        var root = new DirectoryInfo(repositoryRoot);
        if (!root.Exists) return [];

        var found = new List<FileInfo>();
        var pending = new Queue<DirectoryInfo>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();

            foreach (var child in directory.EnumerateDirectories())
            {
                if (ExcludedFromGlob.Contains(child.Name, StringComparer.OrdinalIgnoreCase)) continue;
                if (IsHidden(child)) continue;
                pending.Enqueue(child);
            }

            foreach (var file in directory.EnumerateFiles())
            {
                if (IsHidden(file)) continue;
                if (glob.Matches(Path.GetRelativePath(root.FullName, file.FullName))) found.Add(file);
            }
        }

        return [.. found.OrderBy(file => file.LastWriteTimeUtc)];
    }

    /// <remarks>
    ///     Windows decides by attribute; the Unix-like platforms treat a dot-prefixed name as hidden, and so
    ///     does the enumeration this replaces. Both are applied so that one rule holds on every platform.
    /// </remarks>
    private static bool IsHidden(FileSystemInfo entry) =>
        entry.Attributes.HasFlag(FileAttributes.Hidden) ||
        (!OperatingSystem.IsWindows() && entry.Name.StartsWith('.'));

    /// <remarks>
    ///     Expanded a segment at a time rather than by handing the whole path to the platform, because only
    ///     the wildcard segments should be resolved against the disk: a literal segment naming a directory
    ///     that does not exist must yield nothing rather than an error.
    /// </remarks>
    private static IEnumerable<string> ExpandRoot(string repositoryRoot, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return [];

        var segments = root.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> current = [Path.IsPathRooted(root) ? Path.GetPathRoot(root)! : repositoryRoot];

        foreach (var segment in segments)
            current = segment.Contains('*') || segment.Contains('?')
                ? current.SelectMany(directory => SafeMatchingDirectories(directory, segment))
                : current.Select(directory => Path.Combine(directory, segment)).ToList();

        return current.Where(Directory.Exists).Select(Path.GetFullPath).ToList();
    }

    private static IEnumerable<string> SafeMatchingDirectories(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return [];

        var matcher = WildcardMatcher(pattern);
        return new DirectoryInfo(directory)
            .EnumerateDirectories()
            .Where(child => matcher.IsMatch(child.Name))
            .Select(child => child.FullName)
            .ToList();
    }

    /// <remarks>
    ///     Built from an escaped pattern so that a name carrying regex punctuation — a dot, most obviously —
    ///     is matched literally. Only <c>*</c> and <c>?</c> survive escaping as wildcards.
    /// </remarks>
    private static Regex WildcardMatcher(string pattern)
    {
        var expanded = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
        return new Regex($"^{expanded}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
