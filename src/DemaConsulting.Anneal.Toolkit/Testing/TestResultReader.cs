using System.Text.RegularExpressions;
using System.Xml;
using DemaConsulting.TestResults.IO;

namespace DemaConsulting.Anneal.Toolkit.Testing;

/// <summary>
///     Reads the results of a test run out of a result file.
/// </summary>
/// <remarks>
///     A file that cannot be read yields a warning and no results rather than an exception. That is the
///     fail-closed direction: the tests it would have vouched for are then reported as never having run,
///     which stops the check, whereas treating an unreadable file as an empty run and carrying on would let a
///     clause pass unverified.
///     <para>Thread safety: stateless and safe for concurrent calls.</para>
/// </remarks>
public static partial class TestResultReader
{
    /// <summary>
    ///     Reads one result file.
    /// </summary>
    /// <param name="contents">The file's whole text. Must not be null.</param>
    /// <param name="fileName">The file's name, used to say which file a warning is about. Must not be null.</param>
    /// <param name="format">The form the file takes.</param>
    /// <param name="warnings">Collects a message for each part that could not be read. Must not be null.</param>
    /// <returns>The results read, as a test name and the outcome recorded for it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static IReadOnlyList<(string Name, string Outcome)> Read(
        string contents, string fileName, TestResultFormat format, IList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(warnings);

        return format == TestResultFormat.Text
            ? ReadText(contents, fileName, warnings)
            : ReadStructured(contents, fileName, warnings);
    }

    /// <remarks>
    ///     The format is detected from the file rather than asserted, so a repository whose runner emits
    ///     JUnit is read without being configured differently from one emitting TRX. A file that cannot be
    ///     deserialized yields a warning and no results — the tests it would have vouched for are then
    ///     reported as never having run, which is the fail-closed direction.
    /// </remarks>
    private static IReadOnlyList<(string Name, string Outcome)> ReadStructured(
        string contents, string fileName, IList<string> warnings)
    {
        DemaConsulting.TestResults.TestResults run;

        try
        {
            run = Serializer.Deserialize(contents);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException or XmlException)
        {
            warnings.Add($"Could not parse test results: {fileName}");
            return [];
        }

        var results = new List<(string Name, string Outcome)>();

        foreach (var result in run.Results)
        {
            var qualified = result.ClassName.Length > 0 ? $"{result.ClassName}.{result.Name}" : result.Name;
            AddResult(results, qualified, result.Outcome.ToString());
        }

        return results;
    }

    /// <remarks>
    ///     A data-driven case is recorded as <c>Name(size: 1)</c>; the clause names the method, so the
    ///     arguments are dropped and the cases merge — which is what lets one failing case fail the clause
    ///     its passing siblings would otherwise keep alive.
    /// </remarks>
    private static void AddResult(List<(string Name, string Outcome)> results, string testName, string outcome)
    {
        var name = testName.Split('(')[0].Trim();
        if (name.Length == 0) return;

        results.Add((name, outcome));
    }

    private static IReadOnlyList<(string Name, string Outcome)> ReadText(
        string contents, string fileName, IList<string> warnings)
    {
        var results = new List<(string Name, string Outcome)>();

        foreach (var line in contents.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (IgnoredLine().IsMatch(line)) continue;

            if (ResultLine().Match(line) is not { Success: true } result)
            {
                warnings.Add($"Could not parse result line in {fileName}: {line}");
                continue;
            }

            results.Add((result.Groups["name"].Value, result.Groups["outcome"].Value));
        }

        return results;
    }

    [GeneratedRegex(@"^\s*(#|$)", RegexOptions.CultureInvariant)]
    private static partial Regex IgnoredLine();

    /// <remarks>
    ///     The rest of the line after the outcome is the name, taken whole: a named case is not an identifier
    ///     and may hold spaces and punctuation.
    /// </remarks>
    [GeneratedRegex(@"^\s*(?<outcome>\S+)\s+(?<name>\S.*?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ResultLine();
}
