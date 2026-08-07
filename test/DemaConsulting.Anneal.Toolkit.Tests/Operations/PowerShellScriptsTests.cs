using System.Diagnostics;
using DemaConsulting.Anneal.Toolkit.Operations;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Operations;

/// <summary>
///     Interior tests for the PowerShell script runner.
/// </summary>
/// <remarks>
///     No contract clause promises how a script is run — the clause that matters, <c>TOOLKIT-19</c>, is about
///     what <c>lint-fix</c> concludes, and it runs with the script seam substituted. What is here is the real
///     host behavior, and in particular that cancelling a run stops the script rather than merely stopping the
///     wait for it: <c>fix.ps1</c> edits files, so one that survived its caller would keep changing the
///     repository after the caller withdrew.
/// </remarks>
public class PowerShellScriptsTests
{
    [Fact]
    public async Task CancellingARunStopsTheScript()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: a script that announces its own process and then keeps running for far longer than the
            // test will wait, so anything still alive at the end is a leak rather than a slow exit
            var pidFile = Path.Combine(root, "pid.txt");
            File.WriteAllText(
                Path.Combine(root, "slow.ps1"),
                $"""
                 $PID | Set-Content -Path '{pidFile}'
                 Start-Sleep -Seconds 60
                 """);

            var scripts = new PowerShellScripts(root);
            using var cancellation = new CancellationTokenSource();
            var run = scripts.RunAsync("slow.ps1", cancellation.Token);

            // Act: cancel once the script is definitely running
            var pid = await WaitForPid(pidFile);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

            // Assert: the script is gone by the time the caller was told the run stopped
            Assert.True(HasExited(pid));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     Waits for the script to record its process, so cancellation lands on a running script rather than
    ///     racing its startup.
    /// </summary>
    private static async Task<int> WaitForPid(string pidFile)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (File.Exists(pidFile))
            {
                var text = File.ReadAllText(pidFile).Trim();
                if (int.TryParse(text, out var pid))
                    return pid;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException("the script never recorded its process");
    }

    /// <summary>
    ///     States whether the process is gone, treating an unknown identifier as gone.
    /// </summary>
    private static bool HasExited(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    ///     Creates an empty directory that stands in for a repository.
    /// </summary>
    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "anneal-scripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
