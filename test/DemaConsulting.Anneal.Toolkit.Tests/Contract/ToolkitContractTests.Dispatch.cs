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
///     Boundary tests for the command surface itself - dispatch, usage, and help - rather than any one shipped action.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{
    /// <summary>
    ///     TOOLKIT-01 — an unrecognized action exits with the caller-error code of TOOLKIT-10 and lists the
    ///     actions that exist, so a caller discovers the surface without reading the source. The set it lists
    ///     is every action the tool ships, and each is a reachable action rather than only a name in the list.
    /// </summary>
    [Fact]
    public async Task UnknownActionListsAvailableActions()
    {
        // Arrange: a caller who has named an action this tool does not have
        var output = new StringWriter();

        // Act: the action is named first, as "dotnet anneal <action>"
        var exitCode = await AnnealTool.RunAsync(["no-such-action"], output, TestContext.Current.CancellationToken);
        var written = output.ToString();

        // Act: check-contracts, one of the listed actions, dispatched against a repository whose contract holds
        using var repository = BuildContractRepository("AcceptedRecordIsDurable", "Passed");
        var reachableOutput = new StringWriter();
        var reachableExit = await AnnealTool.RunAsync(
            ["check-contracts"],
            reachableOutput,
            [new CheckContractsOperation(repository.Root)],
            repository.Root,
            TestContext.Current.CancellationToken);

        // Assert: the caller-error code, and the shipped set is exactly the eight actions, each discoverable
        // from the output and each actually reachable rather than merely advertised
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitUsageError, exitCode),
            () => Assert.Contains("unknown action 'no-such-action'", written, StringComparison.Ordinal),
            () => Assert.NotEmpty(AnnealTool.DefaultOperations),
            () => Assert.Equal(
                new[]
                {
                    "check-contracts", "file-skill", "lint-fix", "maintain", "probe-rule-owner", "route",
                    "search-skills", "stage-contract", "stats", "verify-change", "verify-evidence"
                },
                AnnealTool.DefaultOperations.Select(operation => operation.Name).OrderBy(name => name).ToArray()),
            () => Assert.All(
                AnnealTool.DefaultOperations,
                operation => Assert.Contains(operation.Name, written, StringComparison.Ordinal)),
            // check-contracts was reached and reported a verdict, not turned away as an unknown action
            () => Assert.Equal(AnnealTool.ExitSuccess, reachableExit),
            () => Assert.DoesNotContain("unknown action", reachableOutput.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    ///     TOOLKIT-10 — an invocation whose arguments the named action cannot use exits with the caller-error
    ///     code whatever category that action declares, while the outcomes of actions that actually ran keep
    ///     the mapping TOOLKIT-02 and TOOLKIT-06 describe.
    /// </summary>
    [Fact]
    public async Task UsageErrorExitsAsCallerErrorWhateverTheCategory()
    {
        // Arrange and act: the same usage error under a category that gates and one that does not
        var researchMisuse = await RunStub(OperationCategory.Research, OperationOutcome.UsageError);
        var enforcementMisuse = await RunStub(OperationCategory.Enforcement, OperationOutcome.UsageError);

        // Act: the same two operations, having actually run and reported an answer
        var researchFailure = await RunStub(OperationCategory.Research, OperationOutcome.Failed);
        var enforcementFailure = await RunStub(OperationCategory.Enforcement, OperationOutcome.Failed);
        var researchRefusal = await RunStub(OperationCategory.Research, OperationOutcome.Refused);
        var enforcementRefusal = await RunStub(OperationCategory.Enforcement, OperationOutcome.Refused);

        // Act: a caller who scripted an option the action does not take, as the reported defect did
        var misuseOutput = new StringWriter();
        await AnnealTool.RunAsync(
            ["stub", "--rule", "some rule"],
            misuseOutput,
            [new StubOperation(OperationCategory.Research, OperationOutcome.UsageError)],
            TestContext.Current.CancellationToken);
        var written = misuseOutput.ToString();

        // Assert: the caller's own mistake never reads as a check that ran, in either direction, and the
        // outcomes of operations that did run are exactly where TOOLKIT-02 and TOOLKIT-06 left them
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitUsageError, researchMisuse),
            () => Assert.NotEqual(AnnealTool.ExitSuccess, researchMisuse),
            () => Assert.Equal(AnnealTool.ExitUsageError, enforcementMisuse),
            () => Assert.NotEqual(AnnealTool.ExitGatedFailure, enforcementMisuse),
            () => Assert.Equal(researchMisuse, enforcementMisuse),
            () => Assert.Contains("'stub'", written, StringComparison.Ordinal),
            () => Assert.Contains("dotnet anneal stub", written, StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitSuccess, researchFailure),
            () => Assert.Equal(AnnealTool.ExitGatedFailure, enforcementFailure),
            () => Assert.Equal(AnnealTool.ExitRefused, researchRefusal),
            () => Assert.Equal(AnnealTool.ExitRefused, enforcementRefusal));
    }

    /// <summary>
    ///     TOOLKIT-12 — <c>dotnet anneal help</c>, given no further argument, lists every shipped action with
    ///     its one-line summary and exits with the success code, so the surface is discoverable without
    ///     provoking an error.
    /// </summary>
    [Fact]
    public async Task HelpListsEveryActionAndSucceeds()
    {
        // Arrange: a caller who wants to learn the surface deliberately, not by making a mistake
        var output = new StringWriter();

        // Act: "dotnet anneal help", with no action to describe
        var exitCode = await AnnealTool.RunAsync(["help"], output, TestContext.Current.CancellationToken);
        var written = output.ToString();

        // Assert: the success code, and every shipped action with its summary is present in the listing
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
            () => Assert.NotEmpty(AnnealTool.DefaultOperations),
            () => Assert.All(
                AnnealTool.DefaultOperations,
                operation => Assert.Multiple(
                    () => Assert.Contains(operation.Name, written, StringComparison.Ordinal),
                    () => Assert.Contains(operation.Summary, written, StringComparison.Ordinal))));
    }

    /// <summary>
    ///     TOOLKIT-13 — <c>dotnet anneal help &lt;action&gt;</c> prints the named action's detailed usage and
    ///     exits with the success code, while an action that does not ship is the usage error TOOLKIT-10
    ///     defines, reported with the same list of existing actions an unknown action already produces.
    /// </summary>
    [Fact]
    public async Task HelpForActionPrintsItsUsageAndRejectsUnknown()
    {
        // Arrange: a shipped action to describe, and a name that ships nowhere
        var known = AnnealTool.DefaultOperations[0];

        // Act: "help <known>" describes it
        var knownOutput = new StringWriter();
        var knownExit = await AnnealTool.RunAsync(
            ["help", known.Name], knownOutput, TestContext.Current.CancellationToken);
        var knownWritten = knownOutput.ToString();

        // Act: "help <unknown>" is a usage error listing what does exist
        var unknownOutput = new StringWriter();
        var unknownExit = await AnnealTool.RunAsync(
            ["help", "no-such-action"], unknownOutput, TestContext.Current.CancellationToken);
        var unknownWritten = unknownOutput.ToString();

        // Assert: the known action's detailed usage is printed and succeeds; the unknown one is the
        // caller-error code with every real action still discoverable, so help fabricates no guidance
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, knownExit),
            () => Assert.Contains(known.Usage, knownWritten, StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitUsageError, unknownExit),
            () => Assert.Contains("no-such-action", unknownWritten, StringComparison.Ordinal),
            () => Assert.All(
                AnnealTool.DefaultOperations,
                operation => Assert.Contains(operation.Name, unknownWritten, StringComparison.Ordinal)));
    }

    /// <summary>
    ///     TOOLKIT-I4 — the detailed usage an action presents through <c>help &lt;action&gt;</c> and the usage
    ///     it presents when invoked with arguments it cannot use are one and the same text, drawn from a single
    ///     declared source, so the two renderings cannot state the invocation differently or drift apart.
    /// </summary>
    [Fact]
    public async Task HelpAndUsageErrorShareOneUsageSource()
    {
        // Arrange: a stub whose usage is a distinctive literal declared exactly once. If the two renderings
        // ever drew from separate strings, only one of them could contain this literal, and this test fails.
        const string distinctiveUsage = "usage: dotnet anneal stub <sigil-7f3a9c> - one positional argument";
        IReadOnlyList<IOperation> operations =
            [new StubOperation(OperationCategory.Research, OperationOutcome.UsageError, distinctiveUsage)];

        // Act: the discovery rendering, "help <action>"
        var helpOutput = new StringWriter();
        var helpExit = await AnnealTool.RunAsync(
            ["help", "stub"], helpOutput, operations, TestContext.Current.CancellationToken);
        var helpWritten = helpOutput.ToString();

        // Act: the usage-error rendering, the action given arguments it cannot use
        var misuseOutput = new StringWriter();
        var misuseExit = await AnnealTool.RunAsync(
            ["stub", "--flag", "value"], misuseOutput, operations, TestContext.Current.CancellationToken);
        var misuseWritten = misuseOutput.ToString();

        // Assert: both renderings carry the one declared literal verbatim, and each takes the exit its path owns
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, helpExit),
            () => Assert.Contains(distinctiveUsage, helpWritten, StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitUsageError, misuseExit),
            () => Assert.Contains(distinctiveUsage, misuseWritten, StringComparison.Ordinal));
    }

    /// <summary>
    ///     TOOLKIT-09 — the tool reports the Anneal version it was built from, so an installed payload can be
    ///     identified by version rather than inferred from its contents.
    /// </summary>
    /// <remarks>
    ///     The reported version is compared against the version stamped into the built assembly, not against a
    ///     literal. A test that asserted a particular number would have to be edited at every release and would
    ///     agree with a tool that reported a version it was not built from — which is the failure the clause
    ///     names, since a payload whose self-report and whose contents disagree is worse than one that reports
    ///     nothing.
    ///     <para>
    ///         The report is taken from the installed payload as a caller takes it, by running the built tool in
    ///         a process of its own, because "an installed payload can be identified" is a claim about the thing
    ///         on disk rather than about a property an in-process test can read.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ToolReportsPayloadVersion()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: the version stamped into the payload beside these tests, read from the file itself
            var built = FileVersionInfo
                .GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "DemaConsulting.Anneal.Toolkit.dll"))
                .ProductVersion;

            // Act: ask the installed payload, as a caller does, in a process of its own
            using var process = StartTool(root, "version");
            var reported = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            // Act: and through the command surface, so the record written below is from a known invocation
            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["version"], output, AnnealTool.DefaultOperations, root, TestContext.Current.CancellationToken);

            var records = ReadRecords(RecordStore.InvocationsPathFor(root));

            Assert.Multiple(
                // Reporting a version is something the tool does, not something it fails at.
                () => Assert.Equal(0, process.ExitCode),
                () => Assert.Equal(0, exitCode),

                // One line, so a caller reads it without parsing.
                () => Assert.Single(reported.Split('\n', StringSplitOptions.RemoveEmptyEntries)),
                () => Assert.Equal(AnnealTool.Version, reported.Trim()),
                () => Assert.Equal(AnnealTool.Version, output.ToString().Trim()),

                // It is a version, and it is the one the payload was built from.
                () => Assert.Matches(@"^\d+\.\d+\.\d+", AnnealTool.Version),
                () => Assert.Equal(built, AnnealTool.Version),

                // And every record the payload writes carries it, so a run can be attributed to a version later.
                () => Assert.All(records, record => Assert.Equal(AnnealTool.Version, Text(record, "toolVersion"))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
