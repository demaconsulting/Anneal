using System.Text.Json;
using System.Text.Json.Serialization;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Reads a repository's recorded invocations and reports, for each action found, its pass rate across five
///     cumulative time windows.
/// </summary>
/// <remarks>
///     `TOOLKIT-08` already makes every invocation append a structured <see cref="InvocationRecord" />; nothing
///     read it until this operation. It records nothing new and computes nothing that is not already implied by
///     what is on disk, which is why it is deterministic and consults no model.
///     <para>
///         It declares <see cref="OperationCategory.Advisory" /> because it answers a question nobody put — it
///         is read at the start of a stage to ground a conversation in data, not in place of a decision the way
///         a research operation's answer is — and nothing downstream is obliged to act on it.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, though a single instance reads the file
///         system and therefore sees whatever is on disk at the moment it runs.
///     </para>
/// </remarks>
public sealed class StatsOperation : IOperation
{
    /// <remarks>
    ///     Matches how <see cref="RecordStore" /> writes each line, so a record this operation could not parse
    ///     would be one the store itself could not have produced.
    /// </remarks>
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    ///     The cumulative windows reported, narrowest first, each one described by how far back it reaches from
    ///     the moment the operation runs. Null marks the all-time window, which reaches back to everything.
    /// </summary>
    private static readonly (string Label, TimeSpan? Reach)[] Windows =
    [
        ("today", null),
        ("last 3 days", TimeSpan.FromDays(3)),
        ("last 7 days", TimeSpan.FromDays(7)),
        ("last 30 days", TimeSpan.FromDays(30)),
        ("all-time", null)
    ];

    private readonly string _repositoryRoot;

    /// <summary>
    ///     Creates an operation that reads the current working directory's records, which is the repository root
    ///     when the tool is invoked as a repository's own tool.
    /// </summary>
    public StatsOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation that reads an explicit repository's records.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository whose invocation records are read. Must not be null or blank; it need not hold any
    ///     records, in which case the report says so.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public StatsOperation(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    /// <inheritdoc />
    public string Name => "stats";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Advisory;

    /// <inheritdoc />
    public string Summary => "Report each action's pass rate across five cumulative time windows";

    /// <inheritdoc />
    /// <remarks>
    ///     None. This reads the repository's own recorded invocations and aggregates counts, so no capability
    ///     tier could change what it finds.
    /// </remarks>
    public ModelRole? RequiredRole => null;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal stats - reports, for each action recorded in this repository's invocation " +
        "records, its pass rate across today, the last 3 days, the last 7 days, the last 30 days, and " +
        "all-time.";

    /// <inheritdoc />
    /// <remarks>
    ///     Reports <see cref="OperationOutcome.UsageError" /> when given any argument at all, since this action
    ///     takes none. Otherwise it always reports <see cref="OperationOutcome.Succeeded" />: an empty corpus is
    ///     a valid and successful answer for an advisory operation, not a failure to find something.
    ///     <para>
    ///         It carries no finding. Its whole answer is the per-action, per-window lines it renders, so
    ///         nothing structured is left over for a caller to consume.
    ///     </para>
    /// </remarks>
    public Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        // Withdrawn before it began is still withdrawn: nothing is read and no outcome is invented.
        cancellationToken.ThrowIfCancellationRequested();

        // No usage line is written here: the dispatcher renders Usage - the single declared source - on the
        // usage-error path, so the text a caller sees after a misuse cannot drift from what help prints.
        if (arguments.Count != 0)
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));

        var now = DateTimeOffset.UtcNow;
        var records = ReadRecords(cancellationToken);

        if (records.Count == 0)
        {
            output.WriteLine("stats: no invocations have been recorded yet.");
            return Task.FromResult(new OperationResult(OperationOutcome.Succeeded));
        }

        output.WriteLine("stats: pass rate by action (Succeeded / (Succeeded + Failed + Refused + Escalated))");

        var byAction = records
            .GroupBy(record => record.Action, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var actionRecords in byAction)
        {
            cancellationToken.ThrowIfCancellationRequested();

            output.WriteLine($"  {actionRecords.Key}");

            foreach (var (label, reach) in Windows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var since = reach is { } span ? now - span : (DateTimeOffset?)null;
                var inWindow = actionRecords.Where(record => InWindow(record, label, now, since));

                var (succeeded, denominator) = Tally(inWindow);

                output.WriteLine(
                    denominator == 0
                        ? $"    {label,-12} no data"
                        : $"    {label,-12} {succeeded * 100 / denominator}% ({succeeded}/{denominator})");
            }
        }

        return Task.FromResult(new OperationResult(OperationOutcome.Succeeded));
    }

    /// <returns>
    ///     True when <paramref name="record" /> falls within the window named by <paramref name="label" />: the
    ///     current UTC calendar date for "today", or on or after <paramref name="since" /> for every other
    ///     window.
    /// </returns>
    private static bool InWindow(InvocationRecord record, string label, DateTimeOffset now, DateTimeOffset? since)
    {
        if (label == "today")
            return record.At.UtcDateTime.Date == now.UtcDateTime.Date;

        // "all-time" carries no reach at all, and matches every record.
        return since is null || record.At >= since;
    }

    /// <returns>
    ///     The count of <see cref="OperationOutcome.Succeeded" /> records, and the denominator of succeeded,
    ///     failed, refused and escalated records together. <see cref="OperationOutcome.UsageError" /> records
    ///     are excluded from both: a caller's typo is not evidence about the process.
    /// </returns>
    private static (int Succeeded, int Denominator) Tally(IEnumerable<InvocationRecord> records)
    {
        var succeeded = 0;
        var denominator = 0;

        foreach (var record in records)
        {
            if (!Enum.TryParse<OperationOutcome>(record.Outcome, out var outcome))
                continue;

            switch (outcome)
            {
                case OperationOutcome.Succeeded:
                    succeeded++;
                    denominator++;
                    break;

                case OperationOutcome.Failed:
                case OperationOutcome.Refused:
                case OperationOutcome.Escalated:
                    denominator++;
                    break;

                case OperationOutcome.UsageError:
                default:
                    // A caller's typo, or an outcome this build does not know: neither is evidence about the
                    // process, so neither counts toward either side of the rate.
                    break;
            }
        }

        return (succeeded, denominator);
    }

    private IReadOnlyList<InvocationRecord> ReadRecords(CancellationToken cancellationToken)
    {
        var path = RecordStore.InvocationsPathFor(_repositoryRoot);
        if (!File.Exists(path))
            return [];

        var records = new List<InvocationRecord>();
        foreach (var line in File.ReadLines(path))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // A line can be truncated or otherwise malformed when a process was killed mid-append, since
            // RecordStore.Write is not crash-atomic. That line is evidence lost, not evidence this operation
            // may refuse to answer on account of: the rest of the file is still a valid corpus, and one
            // corrupt line must not crash the whole report.
            InvocationRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<InvocationRecord>(line, ReadOptions);
            }
            catch (JsonException)
            {
                continue;
            }
            catch (FormatException)
            {
                continue;
            }

            if (record is not null)
                records.Add(record);
        }

        return records;
    }
}
