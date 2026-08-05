using System.Text.RegularExpressions;

namespace DemaConsulting.Anneal.Toolkit.Files;

/// <summary>
///     A path glob matched against a repository-relative path, as a caller writes one to point at a set of
///     files — <c>artifacts/tests/*.trx</c>, or <c>artifacts/**/*.trx</c>.
/// </summary>
/// <remarks>
///     The whole path is matched rather than only the file name. Matching the leaf alone is the mistake this
///     type exists to prevent: a stray results file anywhere in the tree would then satisfy a check that was
///     told to look in one directory, and stale results are how a failing test is silently reported as
///     passing.
///     <para>
///         <c>*</c> matches within one path segment, <c>**/</c> matches any run of whole segments, and a
///         trailing <c>**</c> matches the rest of the path. Nothing else is special: <c>?</c> and character
///         classes are literal, because no caller has needed them and a glob dialect nobody uses is a
///         dialect nobody has tested. Matching is case-insensitive, so a glob behaves the same on a
///         case-insensitive file system as on a case-sensitive one rather than passing locally and failing
///         in CI.
///     </para>
///     <para>Thread safety: instances are immutable and safe to share.</para>
/// </remarks>
public sealed partial class GlobPattern
{
    private readonly Regex _matcher;

    private GlobPattern(string text, Regex matcher)
    {
        Text = text;
        _matcher = matcher;
    }

    /// <summary>
    ///     The glob exactly as the caller wrote it, for messages that must name what was searched for.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Compiles a glob into a matcher.
    /// </summary>
    /// <param name="glob">
    ///     The glob, written with either separator. Must not be null. A leading <c>./</c> is dropped so that
    ///     <c>./artifacts/x</c> and <c>artifacts/x</c> are the same pattern.
    /// </param>
    /// <returns>A matcher for repository-relative paths.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="glob" /> is null.</exception>
    public static GlobPattern Parse(string glob)
    {
        ArgumentNullException.ThrowIfNull(glob);

        var normalized = glob.Replace('\\', '/').TrimStart('.', '/');

        // Escaping first, then expanding the wildcards longest-form-first, is what keeps "**" from being read
        // as two single-segment wildcards: after escaping, every '*' is the two characters \* and the three
        // forms are distinguishable.
        var pattern = Regex.Escape(normalized);
        pattern = DoubleStarSegment().Replace(pattern, "(?:[^/]+/)*");
        pattern = DoubleStar().Replace(pattern, ".*");
        pattern = SingleStar().Replace(pattern, "[^/]*");

        return new GlobPattern(
            glob,
            new Regex($"^{pattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    /// <summary>
    ///     Reports whether a repository-relative path is one this glob names.
    /// </summary>
    /// <param name="relativePath">
    ///     The path relative to the repository root, written with either separator. Must not be null.
    /// </param>
    /// <returns>True when the glob matches the whole path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="relativePath" /> is null.</exception>
    public bool Matches(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        return _matcher.IsMatch(relativePath.Replace('\\', '/'));
    }

    [GeneratedRegex(@"(\\\*){2}/", RegexOptions.CultureInvariant)]
    private static partial Regex DoubleStarSegment();

    [GeneratedRegex(@"(\\\*){2}", RegexOptions.CultureInvariant)]
    private static partial Regex DoubleStar();

    [GeneratedRegex(@"\\\*", RegexOptions.CultureInvariant)]
    private static partial Regex SingleStar();
}
