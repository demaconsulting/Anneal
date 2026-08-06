using System.Text.Json;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Recording;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Operations;

/// <summary>
///     Interior tests for <see cref="StatsOperation" />: the window arithmetic and rate calculation the
///     contract clause TOOLKIT-21 rests on, exercised in finer grain than the one boundary test needs.
/// </summary>
public class StatsOperationTests
{
    [Fact]
    public async Task RejectsAnyArgumentEvenWhenBenign()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var operation = new StatsOperation(root);
            var result = await operation.ExecuteAsync(
                ["extra"], TextWriter.Null, TestContext.Current.CancellationToken);

            Assert.Equal(OperationOutcome.UsageError, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsNothingRecordedWhenTheFileDoesNotExist()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var operation = new StatsOperation(root);
            var output = new StringWriter();
            var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains("no invocations", output.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsNothingRecordedWhenTheFileIsEmpty()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = RecordStore.InvocationsPathFor(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);

            var operation = new StatsOperation(root);
            var output = new StringWriter();
            var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains("no invocations", output.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UsageErrorRecordsAreExcludedFromTheRateEntirely()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;

            // Every record for "lint-fix" is a usage error, so the denominator is zero and the window must
            // say so, never 0% or 100% - a usage-error-only history is not evidence about the process at all.
            WriteRecords(
                root,
                Record("lint-fix", nameof(OperationOutcome.UsageError), now),
                Record("lint-fix", nameof(OperationOutcome.UsageError), now));

            var operation = new StatsOperation(root);
            var output = new StringWriter();
            var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains("lint-fix", written, StringComparison.Ordinal),
                () => Assert.Contains("no data", written, StringComparison.Ordinal),
                () => Assert.DoesNotContain("%", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AZeroDenominatorWindowRendersNoDataRatherThanAPercentage()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Only a record from 10 days ago, so "today" and "last 3 days" have nothing at all for this action.
            WriteRecords(
                root,
                Record("check-contracts", nameof(OperationOutcome.Succeeded), DateTimeOffset.UtcNow - TimeSpan.FromDays(10)));

            var operation = new StatsOperation(root);
            var output = new StringWriter();
            await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Contains("no data", written, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ARecordTenDaysOldCountsInWiderWindowsButNotNarrowerOnes()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            WriteRecords(
                root,
                Record("check-contracts", nameof(OperationOutcome.Succeeded), DateTimeOffset.UtcNow - TimeSpan.FromDays(10)));

            var operation = new StatsOperation(root);
            var output = new StringWriter();
            await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);
            var lines = output.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .SkipWhile(line => !line.Contains("check-contracts", StringComparison.Ordinal))
                .Skip(1)
                .Take(5)
                .ToArray();

            Assert.Multiple(
                () => Assert.Contains("no data", lines[0]), // today
                () => Assert.Contains("no data", lines[1]), // last 3 days
                () => Assert.Contains("no data", lines[2]), // last 7 days
                () => Assert.Contains("100% (1/1)", lines[3]), // last 30 days
                () => Assert.Contains("100% (1/1)", lines[4])); // all-time
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ACorruptLineIsSkippedRatherThanCrashingTheReport()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var path = RecordStore.InvocationsPathFor(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // One well-formed record, and one line truncated as if the process were killed mid-append -
            // RecordStore.Write is not crash-atomic, so this is a plausible line on disk rather than a
            // contrived one.
            File.WriteAllLines(
                path,
                [
                    JsonSerializer.Serialize(Record("verify-evidence", nameof(OperationOutcome.Succeeded), now)),
                    """{"at":"2026-01-01T00:00:00Z","toolVersion":"test","action":"""
                ]);

            var operation = new StatsOperation(root);
            var output = new StringWriter();
            var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains("verify-evidence", written, StringComparison.Ordinal),
                () => Assert.Contains("100% (1/1)", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteRecords(string root, params InvocationRecord[] records)
    {
        var path = RecordStore.InvocationsPathFor(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, records.Select(record => JsonSerializer.Serialize(record)));
    }

    private static InvocationRecord Record(string action, string outcome, DateTimeOffset at) =>
        new(at, "test", action, [], outcome, null, 0, 0, null, 0);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-stats-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
