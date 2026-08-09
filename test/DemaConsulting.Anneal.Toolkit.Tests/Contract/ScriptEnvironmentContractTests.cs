using DemaConsulting.Anneal.Toolkit.Operations;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary test for TOOLKIT-24: every script the Toolkit runs has <c>ANNEAL_TOOLKIT</c> set in its
///     environment, so the script itself can detect it is a child of this process rather than a person's
///     direct invocation.
/// </summary>
/// <remarks>
///     This is the one promise <see cref="PowerShellScripts" /> makes about how it runs a script, rather than
///     about what a caller concludes from a script's result, so it is verified directly against the real
///     class instead of through <see cref="Toolkit.AnnealTool" /> - there is no CLI action whose whole
///     contract is "a script it happens to run sees a particular environment variable".
/// </remarks>
public class ScriptEnvironmentContractTests
{
    /// <summary>
    ///     TOOLKIT-24 (script environment promise) — a real script, run through <see cref="PowerShellScripts" />,
    ///     observes <c>ANNEAL_TOOLKIT=1</c> in its own environment. Verified by
    ///     <c>ScriptsRunUnderTheToolkitSeeTheAnnealToolkitVariable</c>.
    /// </summary>
    [Fact]
    public async Task ScriptsRunUnderTheToolkitSeeTheAnnealToolkitVariable()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: a script whose only job is to report what it saw in its own environment
            File.WriteAllText(
                Path.Combine(root, "report-env.ps1"),
                "Write-Output \"seen=$env:ANNEAL_TOOLKIT\"; exit 0");

            var scripts = new PowerShellScripts(root);

            // Act
            var run = await scripts.RunAsync("report-env.ps1", TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(0, run.ExitCode),
                () => Assert.Contains("seen=1", run.Output, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "anneal-tk24-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(path);
        return path;
    }
}
