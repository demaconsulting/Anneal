using System.Text;
using DemaConsulting.TestResults;
using DemaConsulting.TestResults.IO;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     A throwaway repository on disk, built one file at a time, for tests that must exercise a check which
///     reads the file system.
/// </summary>
/// <remarks>
///     Real files rather than an abstracted file system, because most of what these checks get wrong is
///     exactly what an abstraction would paper over: hidden attributes, wildcard roots, and the write times
///     that decide whether a result is stale. A seam here would prove the seam rather than the behavior.
///     <para>Thread safety: not safe for concurrent use; each test owns one instance.</para>
/// </remarks>
internal sealed class TemporaryRepository : IDisposable
{
    /// <summary>
    ///     How far into the future a result file is stamped when a test is not exercising staleness. Results
    ///     must post-date the test sources or every fixture would report itself stale.
    /// </summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);

    public TemporaryRepository()
    {
        Root = Path.Combine(Path.GetTempPath(), $"anneal-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(Root, "docs", "architecture"));
    }

    /// <summary>
    ///     The repository's root directory.
    /// </summary>
    public string Root { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
        catch (IOException)
        {
            // A fixture left behind in the temporary directory is litter, not a test failure.
        }
    }

    /// <summary>
    ///     Writes a file, creating the directories above it.
    /// </summary>
    /// <returns>The file's full path.</returns>
    public string Write(string relativePath, string contents)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents, Encoding.UTF8);
        return full;
    }

    /// <summary>
    ///     Writes a system document into the architecture tree.
    /// </summary>
    public void WriteDocument(string name, string contents) => Write($"docs/architecture/{name}", contents);

    /// <summary>
    ///     Writes a well-formed TRX recording the named outcomes.
    /// </summary>
    /// <remarks>
    ///     Names are written whole into the result's own name rather than split into a class and a method,
    ///     because a clause names a test and the check resolves the final segment itself.
    /// </remarks>
    public void WriteTrx(string relativePath, IEnumerable<(string Name, string Outcome)> outcomes, DateTime? written = null)
    {
        var run = new TestResults.TestResults { Name = "Fixture" };

        foreach (var (name, outcome) in outcomes)
            run.Results.Add(new TestResult
            {
                Name = name,
                Outcome = Enum.Parse<TestOutcome>(outcome, true)
            });

        Stamp(Write(relativePath, TrxSerializer.Serialize(run)), written);
    }

    /// <summary>
    ///     Writes a text tally: one result per line, an outcome token then the test name.
    /// </summary>
    public void WriteTextResults(
        string relativePath, IEnumerable<(string Name, string Outcome)> outcomes, DateTime? written = null)
    {
        var lines = outcomes.Select(entry => $"{entry.Outcome} {entry.Name}");
        Stamp(Write(relativePath, string.Join("\n", ["# outcome name", .. lines])), written);
    }

    /// <summary>
    ///     Creates a directory the platform treats as hidden.
    /// </summary>
    /// <remarks>
    ///     Windows decides by attribute and the Unix-like platforms by a dot-prefixed name, so the attribute
    ///     is set here and the caller supplies a dot-prefixed leaf; one fixture then asserts the same thing
    ///     everywhere.
    /// </remarks>
    public string CreateHiddenDirectory(string relativePath)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(full);

        if (OperatingSystem.IsWindows())
            File.SetAttributes(full, File.GetAttributes(full) | FileAttributes.Hidden);

        return full;
    }

    private static void Stamp(string path, DateTime? written) =>
        File.SetLastWriteTimeUtc(path, written ?? DateTime.UtcNow.Add(Fresh));
}
