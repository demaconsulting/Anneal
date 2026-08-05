using DemaConsulting.Anneal.Toolkit.Architecture;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for how a verifier string is read.
/// </summary>
/// <remarks>
///     Disposable. The obligation form matters most: exempting a real test whose name merely carries the word
///     would silently drop the only enforced check in the process.
/// </remarks>
public class ContractVerifierTests
{
    /// <summary>
    ///     Validates that a quoted case name is taken whole, spaces and punctuation included, rather than
    ///     split at the colon that separates it from its file.
    /// </summary>
    [Fact]
    public void ContractVerifier_TestName_QuotedCase_IsTakenWhole()
    {
        // Arrange
        var verifier = new ContractVerifier("""suite.ps1: "clean repository passes" """.Trim());

        // Act / Assert
        Assert.Equal("clean repository passes", verifier.TestName);
    }

    /// <summary>
    ///     Validates that a namespace-qualified identifier resolves to its final segment, which is what a
    ///     declaration carries.
    /// </summary>
    [Fact]
    public void ContractVerifier_TestName_QualifiedIdentifier_TakesTheFinalSegment()
    {
        // Arrange
        var verifier = new ContractVerifier("Ingest.Tests.Contract.AcceptedRecordIsDurable");

        // Act / Assert
        Assert.Equal("AcceptedRecordIsDurable", verifier.TestName);
    }

    /// <summary>
    ///     Validates that the placeholder form, with either separator, is a planned obligation.
    /// </summary>
    [Theory]
    [InlineData("TODO.AcceptedRecordIsDurable")]
    [InlineData("TODO_AcceptedRecordIsDurable")]
    public void ContractVerifier_IsPlannedObligation_PlaceholderForm_IsAnObligation(string text)
    {
        // Act / Assert
        Assert.True(new ContractVerifier(text).IsPlannedObligation);
    }

    /// <summary>
    ///     Validates that a genuine test whose name merely carries the word is checked normally.
    /// </summary>
    [Theory]
    [InlineData("TodoItemsAreReturned")]
    [InlineData("TODOItemsAreReturned")]
    [InlineData("List_TODO_Items")]
    [InlineData("""suite.ps1: "TODO obligation is an error" """)]
    [InlineData("TODO-suite.ps1: \"a case\"")]
    public void ContractVerifier_IsPlannedObligation_NameCarryingTheWord_IsNotAnObligation(string text)
    {
        // Act / Assert
        Assert.False(new ContractVerifier(text.Trim()).IsPlannedObligation);
    }
}
