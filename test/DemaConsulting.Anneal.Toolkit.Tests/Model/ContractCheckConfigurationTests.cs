using DemaConsulting.Anneal.Toolkit.Model;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Model;

/// <summary>
///     Interior tests for <see cref="ContractCheckConfiguration" />: a repository with no configuration file
///     gets the plain <c>-Strict</c> default, and one that names its own arguments gets those instead.
/// </summary>
public class ContractCheckConfigurationTests
{
    [Fact]
    public void Load_NoConfigurationFile_ReturnsDefault()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            // Act
            var configuration = ContractCheckConfiguration.Load(root);

            // Assert
            Assert.Equal(["-Strict"], configuration.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ConfigurationNamesArguments_ReturnsThem()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            WriteConfig(
                root,
                """
                {"contractCheck":{"arguments":["-TestProfiles","TestRoots=test","-Strict"]}}
                """);

            // Act
            var configuration = ContractCheckConfiguration.Load(root);

            // Assert
            Assert.Equal(["-TestProfiles", "TestRoots=test", "-Strict"], configuration.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ConfigurationOmitsContractCheckSection_ReturnsDefault()
    {
        // Arrange: a repository configuring models but not the contract check gets the plain default,
        // matching ModelConfiguration's own "omitted section falls back" behavior.
        var root = CreateTemporaryDirectory();
        try
        {
            WriteConfig(root, """{"models":{"light":["some-model"]}}""");

            // Act
            var configuration = ContractCheckConfiguration.Load(root);

            // Assert
            Assert.Equal(["-Strict"], configuration.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ConfigurationFileUnreadable_ThrowsInvalidOperationException()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            WriteConfig(root, "not valid json");

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => ContractCheckConfiguration.Load(root));
            Assert.Contains(ContractCheckConfiguration.RelativePath, exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteConfig(string root, string content)
    {
        var path = Path.Combine(root, ContractCheckConfiguration.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-contract-check-config-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
