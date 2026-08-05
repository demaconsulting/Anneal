using System.Text.RegularExpressions;

namespace DemaConsulting.Anneal.Toolkit.Testing;

/// <summary>
///     Extracts the names of the tests a source file declares.
/// </summary>
/// <remarks>
///     Extraction is deliberately narrow. Matching bare identifiers anywhere in a test source is far too
///     generous: a private helper, or a string literal, or a doc comment naming the test it proves, would each
///     keep a clause's promise alive after the test itself was gone.
///     <para>Thread safety: stateless and safe for concurrent calls.</para>
/// </remarks>
public static partial class TestDeclarations
{
    /// <summary>
    ///     Extracts declarations written as a method preceded by a run of attribute lines, at least one of
    ///     which names a test attribute.
    /// </summary>
    /// <remarks>
    ///     Comments are stripped first, because doc comments routinely mention the test name of the clause
    ///     they prove and leaving them in would defeat the existence check. Attribute lines accumulate, so a
    ///     <c>[Theory]</c> followed by several <c>[InlineData]</c> lines still reaches its method.
    /// </remarks>
    /// <param name="text">The source file's whole text. Must not be null.</param>
    /// <param name="attributes">
    ///     Attribute names marking a method as a test. Must not be null. Matched as whole words, so
    ///     <c>Fact</c> does not match <c>FactoryAttribute</c>.
    /// </param>
    /// <returns>The declared test names, in the order declared, including any declared more than once.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static IReadOnlyList<string> FromAttributes(string text, IReadOnlyList<string> attributes)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(attributes);

        var attributePattern = new Regex(
            $"(?<![\\w])({string.Join("|", attributes.Select(Regex.Escape))})(?![\\w])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var stripped = LineComment().Replace(BlockComment().Replace(text, " "), " ");

        var names = new List<string>();
        var pending = false;

        foreach (var line in stripped.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (AttributeLine().IsMatch(line))
            {
                if (attributePattern.IsMatch(line)) pending = true;

                // An attribute line carrying nothing after the closing bracket cannot also be the
                // declaration, so the run continues on the next line.
                if (!AttributeThenCode().IsMatch(line)) continue;
            }

            if (!pending) continue;
            if (line.Trim().Length == 0) continue;

            if (MethodName().Match(line) is { Success: true } declaration)
            {
                names.Add(declaration.Groups[1].Value);
                pending = false;
                continue;
            }

            // A non-blank, non-attribute line that is not a declaration ends the attribute run.
            pending = false;
        }

        return names;
    }

    /// <summary>
    ///     Extracts declarations matching a caller-supplied shape, for a suite whose tests are named cases
    ///     rather than attribute-marked methods.
    /// </summary>
    /// <remarks>
    ///     Matched line by line so that a pattern can anchor itself against commented-out declarations, which
    ///     no generic comment stripper could do across every language this is meant to reach.
    /// </remarks>
    /// <param name="text">The source file's whole text. Must not be null.</param>
    /// <param name="pattern">
    ///     A regular expression with a named group <c>name</c> holding the declared test name. Must not be
    ///     null or blank.
    /// </param>
    /// <returns>The declared test names, in the order declared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="pattern" /> is null or blank.</exception>
    /// <exception cref="RegexParseException">Thrown when <paramref name="pattern" /> is not a valid expression.</exception>
    public static IReadOnlyList<string> FromPattern(string text, string pattern)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var matcher = new Regex(pattern, RegexOptions.CultureInvariant);

        return
        [
            .. text
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .SelectMany(line => matcher.Matches(line))
                .Select(match => match.Groups["name"].Value.Trim())
                .Where(name => name.Length > 0)
        ];
    }

    [GeneratedRegex(@"/\*[\s\S]*?\*/", RegexOptions.CultureInvariant)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"^\s*\[", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeLine();

    [GeneratedRegex(@"\]\s*\S", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeThenCode();

    [GeneratedRegex(@"\b([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex MethodName();
}
