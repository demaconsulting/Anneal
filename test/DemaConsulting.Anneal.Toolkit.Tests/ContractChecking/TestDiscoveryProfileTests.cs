using DemaConsulting.Anneal.Toolkit.Testing;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for building the discovery profiles a check runs against.
/// </summary>
/// <remarks>
///     Disposable. Every rejection here exists because the alternative is silent: a misspelled field taking
///     its default, or a parameter quietly losing to a profile record, would leave the check examining the
///     wrong thing while reporting success.
/// </remarks>
public class TestDiscoveryProfileTests
{
    /// <summary>
    ///     Validates that a repository saying nothing gets exactly one unlabelled profile carrying the
    ///     defaults, so its output reads as though profiles did not exist.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_NoRecords_YieldsOneUnlabelledDefaultProfile()
    {
        // Arrange
        var errors = new List<string>();

        // Act
        var profiles = TestDiscoveryProfile.Parse([], EmptyFields, errors);

        // Assert
        var profile = Assert.Single(profiles);
        Assert.Multiple(
            () => Assert.Empty(errors),
            () => Assert.Equal(string.Empty, profile.Label),
            () => Assert.Equal(["test", "tests"], profile.Roots),
            () => Assert.Equal(["*.cs"], profile.FilePatterns),
            () => Assert.Equal("Contract", profile.ContractFolder),
            () => Assert.Equal(TestResultFormat.Trx, profile.Format));
    }

    /// <summary>
    ///     Validates that a field a record omits takes the default rather than nothing.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_OmittedField_TakesItsDefault()
    {
        // Arrange: a record stating only what makes it different
        var errors = new List<string>();

        // Act
        var profiles = TestDiscoveryProfile.Parse(["TestRoots=suite"], EmptyFields, errors);

        // Assert
        var profile = Assert.Single(profiles);
        Assert.Multiple(
            () => Assert.Empty(errors),
            () => Assert.Equal(["suite"], profile.Roots),
            () => Assert.Equal(["*.cs"], profile.FilePatterns));
    }

    /// <summary>
    ///     Validates that several profiles are labelled, so a finding names the profile it belongs to.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_SeveralRecords_AreLabelled()
    {
        // Arrange
        var errors = new List<string>();

        // Act
        var profiles = TestDiscoveryProfile.Parse(
            ["TestRoots=test", "TestRoots=.;TestResultFormat=text"], EmptyFields, errors);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(errors),
            () => Assert.Equal(["profile 1: ", "profile 2: "], profiles.Select(profile => profile.Label)),
            () => Assert.Equal(TestResultFormat.Text, profiles[1].Format));
    }

    /// <summary>
    ///     Validates that records arriving newline-joined in one argument are read as separate records, which
    ///     is how a caller passes more than one through a script parameter.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_NewlineJoinedRecords_AreReadSeparately()
    {
        // Arrange
        var errors = new List<string>();

        // Act
        var profiles = TestDiscoveryProfile.Parse(["TestRoots=test\nTestRoots=."], EmptyFields, errors);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(errors),
            () => Assert.Equal(2, profiles.Count));
    }

    /// <summary>
    ///     Validates that a misspelled field is rejected rather than ignored.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_UnknownField_IsRejected()
    {
        // Arrange
        var errors = new List<string>();

        // Act
        var profiles = TestDiscoveryProfile.Parse(["TestRoot=test"], EmptyFields, errors);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(profiles),
            () => Assert.Contains(
                "profile 1: unknown field 'TestRoot' - expected one of: TestRoots, TestFilePatterns, " +
                "ContractTestFolder, TestAttributes, TestDeclarationPattern, TestResults, TestResultFormat",
                errors));
    }

    /// <summary>
    ///     Validates that a field set twice in one record is rejected rather than resolved.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_FieldSetTwice_IsRejected()
    {
        // Arrange
        var errors = new List<string>();

        // Act
        TestDiscoveryProfile.Parse(["TestRoots=test;TestRoots=."], EmptyFields, errors);

        // Assert
        Assert.Contains("profile 1: field 'TestRoots' is set more than once", errors);
    }

    /// <summary>
    ///     Validates that an entry which is not a field assignment is rejected.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_EntryThatIsNotKeyValue_IsRejected()
    {
        // Arrange
        var errors = new List<string>();

        // Act
        TestDiscoveryProfile.Parse(["TestRoots=test;loose text"], EmptyFields, errors);

        // Assert
        Assert.Contains("profile 1: 'loose text' is not a Key=Value field", errors);
    }

    /// <summary>
    ///     Validates that a record with nothing recognizable in it is rejected rather than becoming a profile
    ///     of pure defaults.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_RecordWithNoRecognizedFields_IsRejected()
    {
        // Arrange
        var errors = new List<string>();

        // Act
        TestDiscoveryProfile.Parse(["nonsense"], EmptyFields, errors);

        // Assert
        Assert.Contains("profile 1: 'nonsense' is not a Key=Value field", errors);
    }

    /// <summary>
    ///     Validates that a result format the check cannot read is rejected.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_UnknownResultFormat_IsRejected()
    {
        // Arrange
        var errors = new List<string>();

        // Act
        var profiles = TestDiscoveryProfile.Parse(["TestResultFormat=xml"], EmptyFields, errors);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(profiles),
            () => Assert.Contains("profile 1: TestResultFormat 'xml' is not one of: trx, text", errors));
    }

    /// <summary>
    ///     Validates that records and individually supplied fields together are rejected rather than merged,
    ///     since which one won would be invisible at the call site.
    /// </summary>
    [Fact]
    public void TestDiscoveryProfile_Parse_RecordsAndFieldsTogether_AreRejected()
    {
        // Arrange
        var errors = new List<string>();
        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TestRoots"] = "test" };

        // Act
        var profiles = TestDiscoveryProfile.Parse(["TestRoots=."], supplied, errors);

        // Assert
        Assert.Multiple(
            () => Assert.Empty(profiles),
            () => Assert.Contains(
                "-TestProfiles cannot be combined with -TestRoots - move those values into a profile record",
                errors));
    }

    private static Dictionary<string, string> EmptyFields => new(StringComparer.OrdinalIgnoreCase);
}
