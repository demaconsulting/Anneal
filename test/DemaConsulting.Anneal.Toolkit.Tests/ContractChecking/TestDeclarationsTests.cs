using DemaConsulting.Anneal.Toolkit.Testing;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for extracting declared test names from a source file.
/// </summary>
/// <remarks>
///     Disposable. The narrowness is the point: matching bare identifiers anywhere in a test source would let
///     a private helper, or a doc comment naming the test it proves, keep a clause's promise alive after the
///     test itself was gone.
/// </remarks>
public class TestDeclarationsTests
{
    /// <summary>
    ///     Validates that a run of attribute lines still reaches the method it marks.
    /// </summary>
    [Fact]
    public void TestDeclarations_FromAttributes_RunOfAttributeLines_ReachesTheMethod()
    {
        // Arrange: the shape a data-driven xUnit test takes
        const string source = """
                              public class IngestTests
                              {
                                  [Theory]
                                  [InlineData(1)]
                                  [InlineData(2)]
                                  public void PreservesPerConnectionOrder(int size)
                                  {
                                  }
                              }
                              """;

        // Act
        var names = TestDeclarations.FromAttributes(source, ["Fact", "Theory"]);

        // Assert
        Assert.Equal(["PreservesPerConnectionOrder"], names);
    }

    /// <summary>
    ///     Validates that a test surviving only in a comment does not satisfy a clause.
    /// </summary>
    [Fact]
    public void TestDeclarations_FromAttributes_NameOnlyInAComment_IsNotDeclared()
    {
        // Arrange: the doc comment names the test the clause points at, but the method is gone
        const string source = """
                              public class IngestTests
                              {
                                  /// <summary>
                                  ///     Replaces AcceptedRecordIsDurable().
                                  /// </summary>
                                  // [Fact] public void AcceptedRecordIsDurable() { }
                                  [Fact]
                                  public void AcceptsRecords()
                                  {
                                  }
                              }
                              """;

        // Act
        var names = TestDeclarations.FromAttributes(source, ["Fact", "Theory"]);

        // Assert
        Assert.Equal(["AcceptsRecords"], names);
    }

    /// <summary>
    ///     Validates that a method carrying no test attribute is not a declared test.
    /// </summary>
    [Fact]
    public void TestDeclarations_FromAttributes_MethodWithoutATestAttribute_IsNotDeclared()
    {
        // Arrange: a helper beside a test
        const string source = """
                              public class IngestTests
                              {
                                  private static void AcceptedRecordIsDurable()
                                  {
                                  }

                                  [Fact]
                                  public void AcceptsRecords()
                                  {
                                  }
                              }
                              """;

        // Act
        var names = TestDeclarations.FromAttributes(source, ["Fact", "Theory"]);

        // Assert
        Assert.Equal(["AcceptsRecords"], names);
    }

    /// <summary>
    ///     Validates that an attribute name is matched as a whole word, so a similarly named attribute does
    ///     not mark a method as a test.
    /// </summary>
    [Fact]
    public void TestDeclarations_FromAttributes_SimilarlyNamedAttribute_DoesNotDeclare()
    {
        // Arrange
        const string source = """
                              public class IngestTests
                              {
                                  [FactoryMethod]
                                  public void BuildsAnIngestQueue()
                                  {
                                  }
                              }
                              """;

        // Act
        var names = TestDeclarations.FromAttributes(source, ["Fact", "Theory"]);

        // Assert
        Assert.Empty(names);
    }

    /// <summary>
    ///     Validates that a caller-supplied shape declares the named cases of a suite whose tests are not
    ///     attribute-marked methods.
    /// </summary>
    [Fact]
    public void TestDeclarations_FromPattern_NamedCases_AreDeclared()
    {
        // Arrange: the shape a PowerShell suite of named cases takes
        const string source = """
                              Test-Case -Name "clean repository passes" -Repo $repo
                              Test-Case -Name "stale results are rejected" -Repo $repo
                              # Test-Case -Name "a commented-out case" -Repo $repo
                              """;

        // Act
        var names = TestDeclarations.FromPattern(source, @"^\s*Test-Case\s+-Name\s+""(?<name>[^""]+)""");

        // Assert
        Assert.Equal(["clean repository passes", "stale results are rejected"], names);
    }

    /// <summary>
    ///     Validates that a blank declaration pattern is refused rather than silently matching everything.
    /// </summary>
    [Fact]
    public void TestDeclarations_FromPattern_BlankPattern_ThrowsArgumentException()
    {
        // Act / Assert
        Assert.Throws<ArgumentException>(() => TestDeclarations.FromPattern("anything", "  "));
    }
}
