using System.Text.RegularExpressions;

namespace DemaConsulting.Anneal.Toolkit.Architecture;

/// <summary>
///     Stateless utility for deciding whether a set of changed files falls inside a document's declared
///     <c>covers:</c> scope, and for detecting whether a unified diff patch alters a document's own
///     <c>## Contract</c> section.
/// </summary>
/// <remarks>
///     Two questions arise repeatedly when checking whether architecture documents kept pace with a change:
///     <list type="bullet">
///         <item>Did this change touch any file this document is responsible for?</item>
///         <item>Did this change also update the document's own <c>## Contract</c> section?</item>
///     </list>
///     Neither question needs hashing, a ledger, or a model call; both are answerable from the YAML front
///     matter, the changed-file list already produced by <see cref="Primitives.DiffCheck" />, and the diff
///     text itself. This type wraps those two checks so they can be consumed independently by whichever
///     worker wires them in later.
///     <para>Thread safety: all methods are pure and safe for concurrent use.</para>
/// </remarks>
public static partial class ArchitectureCoverage
{
    /// <summary>
    ///     Extracts the <c>covers:</c> glob list from a document's YAML front-matter block.
    /// </summary>
    /// <param name="markdown">
    ///     The full Markdown source of one architecture document. The front-matter block must begin at the very
    ///     first character — a leading blank line causes the block to be read as a thematic break and no globs
    ///     are returned. Null is treated as an empty document.
    /// </param>
    /// <returns>
    ///     The glob patterns in declaration order, normalized to forward-slash path separators; empty when the
    ///     document carries no front-matter or no <c>covers:</c> key. Never null.
    /// </returns>
    public static IReadOnlyList<string> ReadCoversGlobs(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return [];

        // Front-matter is delimited by lines that are exactly "---". The block must start on
        // line 1; anything after the closing delimiter is Markdown prose and is ignored here.
        var lines = markdown.Split('\n');
        if (lines.Length < 2 || lines[0].TrimEnd() != "---")
            return [];

        var end = Array.FindIndex(lines, 1, l => l.TrimEnd() == "---");
        if (end < 0)
            return [];

        var globs = new List<string>();
        var inCovers = false;

        for (var i = 1; i < end; i++)
        {
            var line = lines[i];

            // A top-level key resets which block we are in.
            if (line.Length > 0 && line[0] != ' ' && line[0] != '\t')
            {
                inCovers = line.TrimEnd() == "covers:";
                continue;
            }

            if (!inCovers) continue;

            // A YAML sequence item under covers: begins with optional whitespace then "- ".
            var match = CoversBullet().Match(line);
            if (match.Success)
                globs.Add(match.Groups["glob"].Value.Trim().Replace('\\', '/'));
        }

        return globs;
    }

    /// <summary>
    ///     Determines whether any path in <paramref name="changedFiles" /> matches at least one of
    ///     <paramref name="coversGlobs" />.
    /// </summary>
    /// <param name="coversGlobs">
    ///     The glob patterns from a document's <c>covers:</c> front-matter, as returned by
    ///     <see cref="ReadCoversGlobs" />. Null is treated as an empty list.
    /// </param>
    /// <param name="changedFiles">
    ///     Repository-relative paths of files touched by a change, typically from
    ///     <see cref="Primitives.DiffFinding.ChangedFiles" />. Null is treated as an empty list.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when at least one file matches at least one glob;
    ///     <see langword="false" /> when no match is found or either input is empty.
    /// </returns>
    public static bool CoversAnyFile(
        IReadOnlyList<string>? coversGlobs,
        IReadOnlyList<string>? changedFiles)
    {
        if (coversGlobs is null or { Count: 0 }) return false;
        if (changedFiles is null or { Count: 0 }) return false;

        return changedFiles.Any(file => coversGlobs.Any(glob => GlobMatches(glob, file)));
    }

    /// <summary>
    ///     Returns the subset of <paramref name="changedFiles" /> matched by at least one glob in
    ///     <paramref name="coversGlobs" />.
    /// </summary>
    /// <param name="coversGlobs">
    ///     The glob patterns from a document's <c>covers:</c> front-matter. Null is treated as empty.
    /// </param>
    /// <param name="changedFiles">
    ///     Repository-relative paths of files touched by a change. Null is treated as empty.
    /// </param>
    /// <returns>The matched paths, in the order they appeared in <paramref name="changedFiles" />. Never null.</returns>
    public static IReadOnlyList<string> MatchingFiles(
        IReadOnlyList<string>? coversGlobs,
        IReadOnlyList<string>? changedFiles)
    {
        if (coversGlobs is null or { Count: 0 }) return [];
        if (changedFiles is null or { Count: 0 }) return [];

        return changedFiles
            .Where(file => coversGlobs.Any(glob => GlobMatches(glob, file)))
            .ToList();
    }

    /// <summary>
    ///     Determines whether a unified diff patch contains any line added or removed inside a
    ///     <c>## Contract</c> section of the document at <paramref name="documentPath" />.
    /// </summary>
    /// <param name="patch">
    ///     The full unified diff text, as produced by <c>git diff</c>. Null or blank means no contract
    ///     section was touched.
    /// </param>
    /// <param name="documentPath">
    ///     The repository-relative path of the architecture document whose contract section is being
    ///     probed, using either slash style. Must not be null or blank to return true; an empty string
    ///     always returns false.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> when the patch includes at least one <c>+</c> or <c>-</c> line that
    ///     falls inside the <c>## Contract</c> section of <paramref name="documentPath" />;
    ///     <see langword="false" /> otherwise.
    /// </returns>
    /// <remarks>
    ///     The scan is hunk-level: a <c>diff --git</c> header locates the right file, then each
    ///     <c>@@ ... @@</c> hunk is checked for a <c>## Contract</c> context line or a changed line that
    ///     falls after one. A changed line after any <c>## </c> heading that is not Contract closes the
    ///     contract window for that hunk, matching how a Markdown reader would interpret it.
    /// </remarks>
    public static bool PatchTouchesContractSection(string? patch, string documentPath)
    {
        if (string.IsNullOrWhiteSpace(patch)) return false;
        if (string.IsNullOrEmpty(documentPath)) return false;

        // Normalize the path to forward slashes once so that comparisons below work
        // regardless of which separator the caller used.
        var normalPath = documentPath.Replace('\\', '/');

        var inTargetFile = false;
        var inContractSection = false;

        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            // "diff --git a/... b/..." — switch which file we are scanning.
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                inTargetFile = DiffHeader().IsMatch(line) &&
                               NormalizeDiffPath(DiffHeader().Match(line).Groups["path"].Value) == normalPath;
                inContractSection = false;
                continue;
            }

            if (!inTargetFile) continue;

            // A new hunk resets the contract-section flag; the hunk context will re-establish it.
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                inContractSection = false;
                continue;
            }

            // Context and changed lines alike are inspected for headings so that a hunk whose
            // first context line is already inside Contract is handled correctly.
            var content = line.Length > 0 && (line[0] == '+' || line[0] == '-' || line[0] == ' ')
                ? line[1..]
                : null;

            if (content is null) continue;

            if (content.StartsWith("## ", StringComparison.Ordinal))
            {
                inContractSection = content.TrimEnd()
                    .Equals("## Contract", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            // A changed line inside the contract section is the signal we need.
            if (inContractSection && line.Length > 0 && (line[0] == '+' || line[0] == '-'))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Matches a single repository-relative file path against a glob pattern using the subset of
    ///     glob syntax used in this repository's <c>covers:</c> blocks: literal segments, <c>*</c> for
    ///     any run of non-slash characters, and <c>**</c> for any number of path segments including zero.
    /// </summary>
    /// <param name="glob">The glob pattern, using forward slashes. Must not be null.</param>
    /// <param name="path">The file path to test, using forward slashes. Must not be null.</param>
    /// <returns><see langword="true" /> when the path matches the pattern.</returns>
    /// <remarks>
    ///     The implementation converts the glob to a regex rather than walking both strings in tandem,
    ///     because the <c>**</c> case requires backtracking that a recursive descent handles more clearly
    ///     than an iterative state machine. <see cref="Regex.IsMatch(string)" /> on a compiled pattern is
    ///     fast enough for the call volumes here (one per file per glob per document).
    /// </remarks>
    internal static bool GlobMatches(string glob, string path)
    {
        // Normalize both sides to forward slashes.
        var normGlob = glob.Replace('\\', '/');
        var normPath = path.Replace('\\', '/');

        // "**" is swapped for a private-use character (never present in a real glob) so that
        // Regex.Escape leaves it untouched, then translated to ".*" after escaping; this avoids
        // a marker string that would otherwise need to be a pronounceable, spell-checkable word.
        var pattern = "^" + Regex.Escape(normGlob.Replace("**", "\uE000"))
            .Replace(@"\*", "[^/]*")
            .Replace("\uE000", ".*") + "$";

        return Regex.IsMatch(normPath, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeDiffPath(string diffPath) =>
        diffPath.Replace('\\', '/').TrimStart('/');

    [GeneratedRegex(@"^\s*-\s+(?<glob>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CoversBullet();

    /// <remarks>
    ///     The b/ path is the post-change path and is what we want to match against the document's
    ///     repository-relative path.
    /// </remarks>
    [GeneratedRegex(@"diff --git a/.+ b/(?<path>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DiffHeader();
}
