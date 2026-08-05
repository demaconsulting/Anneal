using DemaConsulting.Anneal.Toolkit.Testing;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for reading a result file.
/// </summary>
/// <remarks>
///     Disposable. Two behaviors matter beyond the happy path: a file that cannot be read yields a warning
///     and no results, so the tests it would have vouched for are reported as never having run; and a result
///     file which records outcomes without the definitions a schema-aware reader wants is still read, because
///     discarding it would stop a build over a file that plainly says what happened.
/// </remarks>
public class TestResultReaderTests
{
    /// <summary>
    ///     Validates that a TRX naming its tests through test definitions is read as qualified names and
    ///     outcomes.
    /// </summary>
    [Fact]
    public void TestResultReader_Read_Trx_ReadsQualifiedNamesAndOutcomes()
    {
        // Arrange: the shape dotnet test writes, with a class and a method
        const string trx = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                             <Results>
                               <UnitTestResult testId="11111111-1111-1111-1111-111111111111"
                                 testName="AcceptedRecordIsDurable" outcome="Passed" />
                             </Results>
                             <TestDefinitions>
                               <UnitTest name="AcceptedRecordIsDurable" id="11111111-1111-1111-1111-111111111111">
                                 <TestMethod className="Ingest.Tests.Contract.IngestContractTests"
                                   name="AcceptedRecordIsDurable" />
                               </UnitTest>
                             </TestDefinitions>
                           </TestRun>
                           """;
        var warnings = new List<string>();

        // Act
        var results = TestResultReader.Read(trx, "results.trx", TestResultFormat.Trx, warnings);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(warnings),
            () => Assert.Equal(
                [("Ingest.Tests.Contract.IngestContractTests.AcceptedRecordIsDurable", "Passed")], results));
    }

    /// <summary>
    ///     Validates that a result file recording outcomes without test definitions is still read.
    /// </summary>
    [Fact]
    public void TestResultReader_Read_TrxWithoutTestDefinitions_IsStillRead()
    {
        // Arrange: outcomes recorded against names, with no cross-reference to a definition
        const string trx = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                             <Results>
                               <UnitTestResult testName="AcceptedRecordIsDurable" outcome="Passed" />
                               <UnitTestResult testName="PreservesPerConnectionOrder" outcome="Failed" />
                             </Results>
                           </TestRun>
                           """;
        var warnings = new List<string>();

        // Act
        var results = TestResultReader.Read(trx, "results.trx", TestResultFormat.Trx, warnings);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(warnings),
            () => Assert.Equal(
                [("AcceptedRecordIsDurable", "Passed"), ("PreservesPerConnectionOrder", "Failed")], results));
    }

    /// <summary>
    ///     Validates that a data-driven case is recorded against the method the clause names, so its cases
    ///     merge and one failing case is not hidden by its passing siblings.
    /// </summary>
    [Fact]
    public void TestResultReader_Read_DataDrivenCase_RecordsTheMethodName()
    {
        // Arrange
        const string trx = """
                           <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                             <Results>
                               <UnitTestResult testName="PreservesPerConnectionOrder(size: 1)" outcome="Passed" />
                               <UnitTestResult testName="PreservesPerConnectionOrder(size: 2)" outcome="Failed" />
                             </Results>
                           </TestRun>
                           """;
        var warnings = new List<string>();

        // Act
        var results = TestResultReader.Read(trx, "results.trx", TestResultFormat.Trx, warnings);

        // Assert
        Assert.Equal(
            [("PreservesPerConnectionOrder", "Passed"), ("PreservesPerConnectionOrder", "Failed")], results);
    }

    /// <summary>
    ///     Validates that a JUnit result file is read without being configured differently, which is a
    ///     capability the format detection supplies.
    /// </summary>
    [Fact]
    public void TestResultReader_Read_JUnit_IsRead()
    {
        // Arrange: what most non-Microsoft runners emit
        const string junit = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <testsuites>
                               <testsuite name="Ingest" tests="1">
                                 <testcase classname="IngestContractTests" name="AcceptedRecordIsDurable" />
                               </testsuite>
                             </testsuites>
                             """;
        var warnings = new List<string>();

        // Act
        var results = TestResultReader.Read(junit, "results.xml", TestResultFormat.Trx, warnings);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(warnings),
            () => Assert.Equal([("IngestContractTests.AcceptedRecordIsDurable", "Passed")], results));
    }

    /// <summary>
    ///     Validates that a file which is not results at all warns and vouches for nothing.
    /// </summary>
    [Fact]
    public void TestResultReader_Read_UnreadableFile_WarnsAndReadsNothing()
    {
        // Arrange
        var warnings = new List<string>();

        // Act
        var results = TestResultReader.Read("not xml at all", "results.trx", TestResultFormat.Trx, warnings);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(results),
            () => Assert.Equal(["Could not parse test results: results.trx"], warnings));
    }

    /// <summary>
    ///     Validates that a text tally is read as an outcome token followed by the name, taken whole so a
    ///     named case may hold spaces and punctuation.
    /// </summary>
    [Fact]
    public void TestResultReader_Read_Text_ReadsOutcomeThenWholeName()
    {
        // Arrange: comments and blank lines are not results
        const string text = """
                            # outcome name

                            Passed clean repository passes
                            Failed stale results are rejected
                            """;
        var warnings = new List<string>();

        // Act
        var results = TestResultReader.Read(text, "tests.txt", TestResultFormat.Text, warnings);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(warnings),
            () => Assert.Equal(
                [("clean repository passes", "Passed"), ("stale results are rejected", "Failed")], results));
    }

    /// <summary>
    ///     Validates that a line which is not a result warns and is skipped, rather than being read as a
    ///     nameless outcome.
    /// </summary>
    [Fact]
    public void TestResultReader_Read_TextLineThatIsNotAResult_Warns()
    {
        // Arrange
        const string text = """
                            Passed clean repository passes
                            Orphan
                            """;
        var warnings = new List<string>();

        // Act
        var results = TestResultReader.Read(text, "tests.txt", TestResultFormat.Text, warnings);

        // Assert
        Assert.Multiple(
            () => Assert.Equal([("clean repository passes", "Passed")], results),
            () => Assert.Equal(["Could not parse result line in tests.txt: Orphan"], warnings));
    }
}
