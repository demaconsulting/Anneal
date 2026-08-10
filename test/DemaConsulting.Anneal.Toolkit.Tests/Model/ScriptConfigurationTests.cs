using DemaConsulting.Anneal.Toolkit.Model;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Model;

/// <summary>
///     Interior tests for <see cref="ScriptConfiguration" />: default names when the standard scripts exist on
///     disk and no override is configured, null when they do not, explicit overrides trusted as given, and an
///     explicit empty string as a deliberate skip.
/// </summary>
public class ScriptConfigurationTests
{
    [Fact]
    public void Load_NoConfigurationFileAndStandardScriptsExist_ReturnsDefaultNames()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "fix.ps1"), "");
            File.WriteAllText(Path.Combine(root, "build.ps1"), "");
            File.WriteAllText(Path.Combine(root, "lint.ps1"), "");

            // Act
            var configuration = ScriptConfiguration.Load(root);

            // Assert
            Assert.Multiple(
                () => Assert.Equal("fix.ps1", configuration.Fix),
                () => Assert.Equal("build.ps1", configuration.Build),
                () => Assert.Equal("lint.ps1", configuration.Lint));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_NoConfigurationFileAndNoStandardScripts_ReturnsAllNull()
    {
        // Arrange: a repository in a different ecosystem, with none of the standard scripts on disk and no
        // configuration naming replacements.
        var root = CreateTemporaryDirectory();
        try
        {
            // Act
            var configuration = ScriptConfiguration.Load(root);

            // Assert
            Assert.Multiple(
                () => Assert.Null(configuration.Fix),
                () => Assert.Null(configuration.Build),
                () => Assert.Null(configuration.Lint));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ConfigurationNamesOverrides_ReturnsThemTrustedAsGiven()
    {
        // Arrange: the configured names are trusted even though none of the files exist on disk - a caller
        // finds out a misconfigured name is wrong when the script fails to run, not through a silent skip.
        var root = CreateTemporaryDirectory();
        try
        {
            WriteConfig(
                root,
                """
                {"scripts":{"fix":"tools/fix.sh","build":"tools/build.sh","lint":"tools/lint.sh"}}
                """);

            // Act
            var configuration = ScriptConfiguration.Load(root);

            // Assert
            Assert.Multiple(
                () => Assert.Equal("tools/fix.sh", configuration.Fix),
                () => Assert.Equal("tools/build.sh", configuration.Build),
                () => Assert.Equal("tools/lint.sh", configuration.Lint));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ConfigurationNamesEmptyStrings_ReturnsNull()
    {
        // Arrange: an explicit empty string states "this repository has none of these", the same outcome as an
        // absent default that is not on disk.
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "fix.ps1"), "");
            WriteConfig(root, """{"scripts":{"fix":""}}""");

            // Act
            var configuration = ScriptConfiguration.Load(root);

            // Assert: the standard fix.ps1 exists on disk, but the explicit empty string overrides it to null.
            Assert.Null(configuration.Fix);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ConfigurationOmitsScriptsSection_ReturnsDefaults()
    {
        // Arrange: a repository configuring models but not scripts gets the plain default resolution, matching
        // ModelConfiguration's own "omitted section falls back" behavior.
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "build.ps1"), "");
            WriteConfig(root, """{"models":{"light":["some-model"]}}""");

            // Act
            var configuration = ScriptConfiguration.Load(root);

            // Assert
            Assert.Multiple(
                () => Assert.Null(configuration.Fix),
                () => Assert.Equal("build.ps1", configuration.Build));
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
            var exception = Assert.Throws<InvalidOperationException>(() => ScriptConfiguration.Load(root));
            Assert.Contains(ScriptConfiguration.RelativePath, exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteConfig(string root, string content)
    {
        var path = Path.Combine(root, ScriptConfiguration.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-script-config-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
