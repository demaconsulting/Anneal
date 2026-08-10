using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Providers;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the stats action, TOOLKIT-21.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{

    /// <summary>
    ///     TOOLKIT-21 — stats reports, for each action found in a repository's invocation records, its pass rate
    ///     across five cumulative time windows, with the raw counts behind every percentage, excluding
    ///     UsageError from both sides.
    /// </summary>
    [Fact]
    public async Task StatsReportsPerActionPassRatesAcrossWindows()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;

            WriteInvocationRecords(
                root,
                // "verify-evidence": one success today, one failure 10 days ago (last 30 days and all-time
                // only), and a usage error today that must not enter either side of the rate.
                Record("verify-evidence", nameof(OperationOutcome.Succeeded), now),
                Record("verify-evidence", nameof(OperationOutcome.Failed), now - TimeSpan.FromDays(10)),
                Record("verify-evidence", nameof(OperationOutcome.UsageError), now),
                // "probe-rule-owner": nothing at all today, so "today" has no data for it, but one refusal
                // inside the last 3 days keeps every wider window non-empty.
                Record("probe-rule-owner", nameof(OperationOutcome.Refused), now - TimeSpan.FromDays(2)));

            var output = new StringWriter();
            var operation = new StatsOperation(root);
            var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),

                // verify-evidence: today is 1/1 (the usage error excluded entirely); the 10-day-old failure only
                // enters once the window reaches back that far.
                () => Assert.Contains("verify-evidence", written, StringComparison.Ordinal),
                () => Assert.Contains("today", written, StringComparison.Ordinal),
                () => Assert.Contains("100% (1/1)", written, StringComparison.Ordinal),
                () => Assert.Contains("50% (1/2)", written, StringComparison.Ordinal),

                // probe-rule-owner: today has nothing recorded for it at all - a zero denominator - so it must
                // say so rather than print a rate.
                () => Assert.Contains("probe-rule-owner", written, StringComparison.Ordinal),
                () => Assert.Contains("no data", written, StringComparison.Ordinal),

                // Cumulative: the 2-day-old refusal enters "last 3 days" onward but not "today".
                () => Assert.Contains("0% (0/1)", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     `stats` takes no arguments, so given any at all it is a usage error - the arguments were never used
    ///     rather than answered against.
    /// </summary>
    [Fact]
    public async Task StatsRejectsAnyArgument()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var operation = new StatsOperation(root);
            var result = await operation.ExecuteAsync(
                ["unexpected"], TextWriter.Null, TestContext.Current.CancellationToken);

            Assert.Equal(OperationOutcome.UsageError, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     A repository with no invocation record file at all has recorded nothing, which is a successful and
    ///     honest answer for an advisory operation, not a failure to find something.
    /// </summary>
    [Fact]
    public async Task StatsReportsNothingRecordedWhenCorpusIsMissing()
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
}
