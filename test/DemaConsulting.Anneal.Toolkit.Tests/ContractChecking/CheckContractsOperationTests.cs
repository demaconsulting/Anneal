using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for the operation that composes the contract check.
/// </summary>
/// <remarks>
///     Disposable. The operation decides nothing about contracts, so what is worth pinning here is only what
///     it adds: the verdict it reports, the lines it renders, and its refusal to read an invocation it does
///     not understand rather than checking the wrong thing under a default.
/// </remarks>
public class CheckContractsOperationTests
{
    private const string Contract = """
                                    ## Contract

                                    ### Provides

                                    - **INGEST-01** - Accepts records.
                                      *Verified by:* `AcceptedRecordIsDurable`
                                    """;

    private const string Tests = """
                                 public class IngestContractTests
                                 {
                                     [Fact]
                                     public void AcceptedRecordIsDurable()
                                     {
                                     }
                                 }
                                 """;

    /// <summary>
    ///     Validates that a repository whose contract holds succeeds, and that the run says how much was
    ///     checked rather than only that it passed.
    /// </summary>
    [Fact]
    public async Task CheckContractsOperation_ExecuteAsync_ContractHolds_SucceedsAndSaysWhatWasChecked()
    {
        // Arrange
        using var repository = Standard("Passed");
        var output = new StringWriter();
        IOperation operation = new CheckContractsOperation(repository.Root);

        // Act
        var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
            () => Assert.Contains("1 clauses, 1 test links checked.", output.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that a contract which does not hold fails and says which clause, since a gate that only
    ///     reported a verdict would leave the reader to run the check again by hand.
    /// </summary>
    [Fact]
    public async Task CheckContractsOperation_ExecuteAsync_ContractDoesNotHold_FailsAndSaysWhy()
    {
        // Arrange: the clause's test ran and failed
        using var repository = Standard("Failed");
        var output = new StringWriter();
        IOperation operation = new CheckContractsOperation(repository.Root);

        // Act
        var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
            () => Assert.Contains(
                "error: ingest.md: clause INGEST-01 names test 'AcceptedRecordIsDurable' whose most recent " +
                "result is 'Failed'",
                output.ToString(),
                StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that an option nobody recognizes is refused rather than ignored, because ignoring it
    ///     would check the repository under a default the caller did not ask for and report success.
    /// </summary>
    [Fact]
    public async Task CheckContractsOperation_ExecuteAsync_UnrecognizedOption_IsRefused()
    {
        // Arrange
        using var repository = Standard("Passed");
        var output = new StringWriter();
        IOperation operation = new CheckContractsOperation(repository.Root);

        // Act
        var result = await operation.ExecuteAsync(
            ["-NoSuchOption", "test"], output, TestContext.Current.CancellationToken);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(OperationOutcome.UsageError, result.Outcome),
            () => Assert.Equal(string.Empty, output.ToString()));
    }

    /// <summary>
    ///     Validates that a caller who withdraws before the run starts gets no verdict at all, rather than a
    ///     pass inferred from a check that never happened.
    /// </summary>
    [Fact]
    public async Task CheckContractsOperation_ExecuteAsync_AlreadyCancelled_ReachesNoVerdict()
    {
        // Arrange
        using var repository = Standard("Passed");
        IOperation operation = new CheckContractsOperation(repository.Root);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation.ExecuteAsync([], TextWriter.Null, cancellation.Token));
    }

    /// <summary>
    ///     Validates that the operation needs no model, which is what lets an enforcement gate be trusted to
    ///     answer the same way twice.
    /// </summary>
    [Fact]
    public void CheckContractsOperation_RequiredRole_IsNone()
    {
        // Arrange / Act
        IOperation operation = new CheckContractsOperation(".");

        // Assert
        Assert.Multiple(
            () => Assert.Null(operation.RequiredRole),
            () => Assert.Equal(OperationCategory.Enforcement, operation.Category));
    }

    private static TemporaryRepository Standard(string outcome)
    {
        var repository = new TemporaryRepository();
        repository.WriteDocument("ingest.md", Contract);
        repository.Write("test/Ingest.Tests/Contract/IngestContractTests.cs", Tests);
        repository.WriteTrx("artifacts/tests/results.trx", [("AcceptedRecordIsDurable", outcome)]);
        return repository;
    }
}
