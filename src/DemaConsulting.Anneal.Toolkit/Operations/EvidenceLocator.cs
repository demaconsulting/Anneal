using System.Text.RegularExpressions;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     One citation taken from an agent report: a claim that <see cref="Quote" /> appears at
///     <see cref="Line" /> of <see cref="Path" />.
/// </summary>
/// <remarks>
///     Internal because the operation's boundary is its command line and its output, not this shape. A report
///     names locators in prose, so parsing is deliberately tolerant about what surrounds one and strict about
///     the locator itself: a citation the parser half-understands would produce a verdict about the wrong
///     line, which is worse than not recognizing it at all.
/// </remarks>
internal sealed record EvidenceLocator(string Path, int Line, string Quote)
{
    /// <remarks>
    ///     The recognized form is a path and a 1-based line number, optionally in backticks, followed by a
    ///     double-quoted quotation on the same line — the shape agent reports already use, as in
    ///     <c>`lint.ps1:42` - "exit ($lintError ? 1 : 0)"</c>. The separator between the two is optional and
    ///     may be a dash, an em dash or a colon, because report authors are inconsistent about it and the
    ///     choice carries no meaning.
    /// </remarks>
    private static readonly Regex LocatorPattern = new(
        "`?(?<path>[^\\s`\"]+):(?<line>[0-9]+)`?[^\\S\r\n]*(?:[-\u2013\u2014:][^\\S\r\n]*)?\"(?<quote>[^\"\r\n]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Extracts every locator cited in a report, in the order they appear.
    /// </summary>
    internal static IReadOnlyList<EvidenceLocator> ParseAll(string report)
    {
        var locators = new List<EvidenceLocator>();

        foreach (Match match in LocatorPattern.Matches(report))
        {
            // A line number too large to be one is not a locator this operation understands, and silently
            // clamping it would invent a citation the report never made.
            if (!int.TryParse(match.Groups["line"].Value, out var line) || line <= 0)
                continue;

            locators.Add(new EvidenceLocator(match.Groups["path"].Value, line, match.Groups["quote"].Value));
        }

        return locators;
    }

    /// <summary>
    ///     Renders the locator the way the report wrote it, for output a reader can match back to the report.
    /// </summary>
    public override string ToString() => $"{Path}:{Line} \"{Quote}\"";
}
