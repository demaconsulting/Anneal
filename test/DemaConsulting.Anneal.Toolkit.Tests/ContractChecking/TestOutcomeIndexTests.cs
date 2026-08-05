using DemaConsulting.Anneal.Toolkit.Testing;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for pooling the outcomes recorded across result files.
/// </summary>
/// <remarks>
///     Disposable. Every rule here exists to stop an old result vouching for a test that fails today, which
///     was the normal local state rather than an edge case while results accumulated in an ignored directory.
/// </remarks>
public class TestOutcomeIndexTests
{
    private static readonly DateTime Older = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    ///     Validates that an older passing run cannot vouch for a test that has since failed.
    /// </summary>
    [Fact]
    public void TestOutcomeIndex_Read_NewerFailure_WinsOverAnOlderPass()
    {
        // Arrange: two runs of the same test, a day apart
        using var repository = new TemporaryRepository();
        repository.WriteTrx("artifacts/tests/old.trx", [("AcceptedRecordIsDurable", "Passed")], Older);
        repository.WriteTrx("artifacts/tests/new.trx", [("AcceptedRecordIsDurable", "Failed")], Newer);
        var warnings = new List<string>();

        // Act
        var index = TestOutcomeIndex.Read(
            repository.Root, "artifacts/tests/*.trx", TestResultFormat.Trx, warnings);

        // Assert
        var outcome = Assert.Single(index.Matching("AcceptedRecordIsDurable"));
        Assert.Multiple(
            () => Assert.Equal("Failed", outcome.Outcome),
            () => Assert.False(outcome.IsPass),
            () => Assert.Equal(Newer, index.NewestResultUtc));
    }

    /// <summary>
    ///     Validates that within one run a failing case is not overwritten by a passing sibling of the same
    ///     age.
    /// </summary>
    [Fact]
    public void TestOutcomeIndex_Read_FailingCaseAndPassingSibling_KeepsTheFailure()
    {
        // Arrange: one data-driven test, one case of which failed
        using var repository = new TemporaryRepository();
        repository.WriteTrx(
            "artifacts/tests/results.trx",
            [("PreservesPerConnectionOrder(size: 1)", "Failed"), ("PreservesPerConnectionOrder(size: 2)", "Passed")],
            Newer);
        var warnings = new List<string>();

        // Act
        var index = TestOutcomeIndex.Read(
            repository.Root, "artifacts/tests/*.trx", TestResultFormat.Trx, warnings);

        // Assert
        var outcome = Assert.Single(index.Matching("PreservesPerConnectionOrder"));
        Assert.Equal("Failed", outcome.Outcome);
    }

    /// <summary>
    ///     Validates that a clause naming a bare test name matches a recorded name qualified by its class,
    ///     but not one that merely starts with it.
    /// </summary>
    [Fact]
    public void TestOutcomeIndex_Matching_QualifiedName_MatchesOnTheFinalSegmentOnly()
    {
        // Arrange
        using var repository = new TemporaryRepository();
        repository.WriteTrx(
            "artifacts/tests/results.trx",
            [("Ingest.Tests.Contract.AcceptedRecordIsDurable", "Passed")],
            Newer);
        var warnings = new List<string>();

        // Act
        var index = TestOutcomeIndex.Read(
            repository.Root, "artifacts/tests/*.trx", TestResultFormat.Trx, warnings);

        // Assert
        Assert.Multiple(
            () => Assert.Single(index.Matching("AcceptedRecordIsDurable")),
            () => Assert.Empty(index.Matching("Accepted")),
            () => Assert.Empty(index.Matching("RecordIsDurable")));
    }

    /// <summary>
    ///     Validates that a run which was never recorded is distinguishable from one that recorded nothing.
    /// </summary>
    [Fact]
    public void TestOutcomeIndex_Read_NoResultFiles_ReportsNoneFound()
    {
        // Arrange: a repository whose tests have not been run
        using var repository = new TemporaryRepository();
        var warnings = new List<string>();

        // Act
        var index = TestOutcomeIndex.Read(
            repository.Root, "artifacts/tests/*.trx", TestResultFormat.Trx, warnings);

        // Assert
        Assert.Multiple(
            () => Assert.False(index.FoundResultFiles),
            () => Assert.Equal(DateTime.MinValue, index.NewestResultUtc),
            () => Assert.Empty(warnings));
    }

    /// <summary>
    ///     Validates that a result file outside the configured location is ignored, so a stray file elsewhere
    ///     in the tree cannot satisfy the check.
    /// </summary>
    [Fact]
    public void TestOutcomeIndex_Read_ResultOutsideTheConfiguredLocation_IsIgnored()
    {
        // Arrange
        using var repository = new TemporaryRepository();
        repository.WriteTrx("elsewhere/results.trx", [("AcceptedRecordIsDurable", "Passed")], Newer);
        var warnings = new List<string>();

        // Act
        var index = TestOutcomeIndex.Read(
            repository.Root, "artifacts/tests/*.trx", TestResultFormat.Trx, warnings);

        // Assert
        Assert.False(index.FoundResultFiles);
    }

    /// <summary>
    ///     Validates that merging one profile's outcomes into another applies the same newest-wins rule,
    ///     since a clause names a test rather than a framework.
    /// </summary>
    [Fact]
    public void TestOutcomeIndex_Merge_OlderPassFromAnotherProfile_DoesNotOverwriteAFailure()
    {
        // Arrange: the same name recorded by two frameworks, one of them stale
        using var repository = new TemporaryRepository();
        repository.WriteTrx("artifacts/tests/results.trx", [("SharedName", "Failed")], Newer);
        repository.WriteTextResults("results/tests.txt", [("SharedName", "Passed")], Older);
        var warnings = new List<string>();

        var pooled = TestOutcomeIndex.Read(
            repository.Root, "artifacts/tests/*.trx", TestResultFormat.Trx, warnings);
        var other = TestOutcomeIndex.Read(
            repository.Root, "results/*.txt", TestResultFormat.Text, warnings);

        // Act
        pooled.Merge(other);

        // Assert
        var outcome = Assert.Single(pooled.Matching("SharedName"));
        Assert.Equal("Failed", outcome.Outcome);
    }
}
