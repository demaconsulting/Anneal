using DemaConsulting.Anneal.Toolkit.Files;

namespace DemaConsulting.Anneal.Toolkit.Testing;

/// <summary>
///     An outcome recorded for a test, and when it was recorded.
/// </summary>
/// <param name="Outcome">The outcome as the runner recorded it.</param>
/// <param name="RecordedUtc">When the result file carrying it was last written.</param>
public sealed record RecordedOutcome(string Outcome, DateTime RecordedUtc)
{
    /// <summary>
    ///     Whether this outcome is a pass. Anything else — a failure, a skip, an aborted run — is not, so an
    ///     outcome nobody anticipated reads as a failure rather than as a pass.
    /// </summary>
    public bool IsPass => string.Equals(Outcome, "Passed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
///     The most recent outcome recorded for each test, across a set of result files.
/// </summary>
/// <remarks>
///     The newest result for a name wins, and a result of the same age never overwrites a failure with a
///     pass. Taking the union of passes across every result file let a stale run vouch for a test that fails
///     today —
///     and because an artifacts directory is typically ignored by version control and never cleaned, that was
///     the normal local state rather than an edge case. The same-age rule is what keeps a single failing case
///     of a data-driven test from being hidden by its passing siblings.
///     <para>Thread safety: not safe for concurrent mutation; built by reading and read afterwards.</para>
/// </remarks>
public sealed class TestOutcomeIndex
{
    private readonly Dictionary<string, RecordedOutcome> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Whether any result file was found at all. Distinct from having found no results in them: a run
    ///     that was never recorded and a run that recorded nothing call for different repairs.
    /// </summary>
    public bool FoundResultFiles { get; private set; }

    /// <summary>
    ///     When the newest result file read was last written, or <see cref="DateTime.MinValue" /> when none
    ///     was found.
    /// </summary>
    public DateTime NewestResultUtc { get; private set; } = DateTime.MinValue;

    /// <summary>
    ///     Reads every result file a glob names, newest last.
    /// </summary>
    /// <param name="repositoryRoot">The repository root the glob is relative to. Must not be null or blank.</param>
    /// <param name="resultsGlob">The glob naming the result files. Must not be null.</param>
    /// <param name="format">The form those files take.</param>
    /// <param name="warnings">Collects a message for each file or line that could not be read. Must not be null.</param>
    /// <returns>The outcomes recorded.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resultsGlob" /> or <paramref name="warnings" /> is null.</exception>
    public static TestOutcomeIndex Read(
        string repositoryRoot, string resultsGlob, TestResultFormat format, IList<string> warnings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(resultsGlob);
        ArgumentNullException.ThrowIfNull(warnings);

        var index = new TestOutcomeIndex();
        var files = RepositoryFiles.MatchingGlob(repositoryRoot, GlobPattern.Parse(resultsGlob));

        index.FoundResultFiles = files.Count > 0;

        foreach (var file in files)
        {
            if (file.LastWriteTimeUtc > index.NewestResultUtc) index.NewestResultUtc = file.LastWriteTimeUtc;

            var contents = File.ReadAllText(file.FullName);
            foreach (var (name, outcome) in TestResultReader.Read(contents, file.Name, format, warnings))
                index.Record(name, new RecordedOutcome(outcome, file.LastWriteTimeUtc));
        }

        return index;
    }

    /// <summary>
    ///     Merges another index's outcomes into this one, by the same newest-wins rule used within one index.
    /// </summary>
    /// <param name="other">The index to merge in. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other" /> is null.</exception>
    public void Merge(TestOutcomeIndex other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var (name, outcome) in other._byName) Record(name, outcome);
    }

    /// <summary>
    ///     Finds the outcomes recorded for a test named by a clause.
    /// </summary>
    /// <remarks>
    ///     A clause names a test, not the namespace it lives in, so a recorded name whose final segment is
    ///     the one named also matches. Several may match at once when the same test name occurs in more than
    ///     one class, and all of them are returned: the clause is only kept if every one of them passed,
    ///     because the author cannot have meant one of two identically named tests.
    /// </remarks>
    /// <param name="testName">The test name the clause gives. Must not be null.</param>
    /// <returns>The outcomes recorded under that name, which is empty when the test did not run.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="testName" /> is null.</exception>
    public IReadOnlyList<RecordedOutcome> Matching(string testName)
    {
        ArgumentNullException.ThrowIfNull(testName);

        return
        [
            .. _byName
                .Where(entry =>
                    string.Equals(entry.Key, testName, StringComparison.OrdinalIgnoreCase) ||
                    entry.Key.EndsWith($".{testName}", StringComparison.Ordinal))
                .Select(entry => entry.Value)
        ];
    }

    private void Record(string name, RecordedOutcome outcome)
    {
        if (_byName.TryGetValue(name, out var existing))
        {
            if (existing.RecordedUtc > outcome.RecordedUtc) return;
            if (existing.RecordedUtc == outcome.RecordedUtc && !existing.IsPass) return;
        }

        _byName[name] = outcome;
    }
}
