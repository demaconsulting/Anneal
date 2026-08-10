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
///     Boundary tests for the check-contracts action, TOOLKIT-17.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{

    /// <summary>
    ///     TOOLKIT-17 — the registered check-contracts action verifies that every contract clause names a
    ///     boundary test that exists and passed: it succeeds when the clause-to-test link holds, and gates the
    ///     build when the link is broken.
    /// </summary>
    /// <remarks>
    ///     Driven through the same command surface a caller has — the action named first, dispatched against a
    ///     throw-away repository — so what is proven is the registered action rather than the check underneath
    ///     it. The verdict is read from the exit code alone, because that is all the enforcement gate that runs
    ///     it in <c>lint.ps1</c> has.
    /// </remarks>
    [Fact]
    public async Task CheckContractsVerifiesTheClauseToTestLink()
    {
        // Arrange: one repository whose clause names an existing passing test, and one whose clause names a
        // test nothing declares - the same clause, the link intact in the first and broken in the second
        using var linked = BuildContractRepository("AcceptedRecordIsDurable", "Passed");
        using var broken = BuildContractRepository("NoSuchBoundaryTest", "Passed");

        // Act: dispatch check-contracts against each, as a real caller does
        var linkedOutput = new StringWriter();
        var linkedExit = await AnnealTool.RunAsync(
            ["check-contracts"],
            linkedOutput,
            [new CheckContractsOperation(linked.Root)],
            linked.Root,
            TestContext.Current.CancellationToken);

        var brokenOutput = new StringWriter();
        var brokenExit = await AnnealTool.RunAsync(
            ["check-contracts"],
            brokenOutput,
            [new CheckContractsOperation(broken.Root)],
            broken.Root,
            TestContext.Current.CancellationToken);

        // Assert: the intact link reports success and says what it checked; the broken link gates the build
        // and names the clause whose test it could not find
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, linkedExit),
            () => Assert.Contains("clauses, 1 test links checked.", linkedOutput.ToString(), StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitGatedFailure, brokenExit),
            () => Assert.Contains("INGEST-01", brokenOutput.ToString(), StringComparison.Ordinal),
            () => Assert.Contains("NoSuchBoundaryTest", brokenOutput.ToString(), StringComparison.Ordinal));
    }
}
