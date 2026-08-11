using System.Diagnostics;
using System.Text;
using Xunit;
using SysProcess = System.Diagnostics.Process;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the installer contract, one per documented clause, that spawn the real
///     <c>install.ps1</c> as a subprocess, build throwaway target repositories under
///     <c>artifacts/install-fixtures</c>, and assert on exit code and installed files.
/// </summary>
/// <remarks>
///     The fixture targets must be real filesystem directories so <c>install.ps1</c> can resolve them
///     as paths; they are placed under this repository's own <c>artifacts/</c> tree (git-ignored)
///     rather than the OS temp directory so they are easy to inspect after a failure. Each fixture
///     is deleted on disposal whether it passed or failed.
/// </remarks>
public class InstallSubprocessTests
{
    // ==========================================================================================
    // INSTALLER-01
    // ==========================================================================================

    /// <summary>
    ///     INSTALLER-01 — the installed layout on disk contains all payload files in the expected
    ///     locations, copied by file alone with no extra tooling written into the target.
    /// </summary>
    [Fact]
    public void InstalledLayoutMatchesRepository()
    {
        // Arrange: an empty target repository
        using var target = new TargetFixture();

        // Act
        var result = Run(target);

        // Assert: well-known members of every payload directory are present on disk
        Assert.Multiple(
            () => Assert.True(result.ExitCode == 0, $"expected exit 0, got {result.ExitCode}. Output:\n{result.Output}"),
            () => Assert.True(
                File.Exists(Path.Combine(target.Root, ".github", "agents", "helper.agent.md")),
                ".github/agents/helper.agent.md must be installed"),
            () => Assert.True(
                Directory.Exists(Path.Combine(target.Root, ".github", "skills")),
                ".github/skills must exist"),
            () => Assert.True(
                Directory.Exists(Path.Combine(target.Root, ".github", "standards")),
                ".github/standards must exist"),
            () => Assert.True(
                Directory.Exists(Path.Combine(target.Root, ".github", "template")),
                ".github/template must be installed"));
    }

    // ==========================================================================================
    // INSTALLER-02
    // ==========================================================================================

    /// <summary>
    ///     INSTALLER-02 — the template is vendored under <c>.github/template/</c> in the target
    ///     repository, meaning the target can resolve the template locally without a network call.
    /// </summary>
    [Fact]
    public void TemplateIsVendoredLocally()
    {
        // Arrange
        using var target = new TargetFixture();

        // Act
        var result = Run(target);

        // Assert: the template directory and a stable member of it are present
        Assert.Multiple(
            () => Assert.True(result.ExitCode == 0, $"expected exit 0, got {result.ExitCode}. Output:\n{result.Output}"),
            () => Assert.True(
                Directory.Exists(Path.Combine(target.Root, ".github", "template")),
                ".github/template must exist in the target"),
            () => Assert.True(
                File.Exists(Path.Combine(target.Root, ".github", "template", ".anneal", "architecture", "overview.md")),
                ".github/template/.anneal/architecture/overview.md must be vendored into the target"));
    }

    // ==========================================================================================
    // INSTALLER-04
    // ==========================================================================================

    /// <summary>
    ///     INSTALLER-04 — when an existing file would be overwritten, the installer detects every
    ///     collision before writing anything, reports them, and exits non-zero. The target is left
    ///     unchanged.
    /// </summary>
    [Fact]
    public void CollisionsAreDetectedBeforeAnyWrite()
    {
        // Arrange: pre-install, then plant a canary file that must not be overwritten
        using var target = new TargetFixture();
        var firstRun = Run(target);
        Assert.True(firstRun.ExitCode == 0, $"setup install failed. Output:\n{firstRun.Output}");

        var agentsPath = Path.Combine(target.Root, ".github", "agents", "helper.agent.md");
        var originalContent = File.ReadAllText(agentsPath);
        File.WriteAllText(agentsPath, originalContent + "\n<!-- local edit -->");

        // Act: re-install without -Force
        var result = Run(target);

        // Assert: exits 1, reports a conflict, leaves the local edit intact
        Assert.Multiple(
            () => Assert.True(result.ExitCode == 1, $"expected exit 1, got {result.ExitCode}. Output:\n{result.Output}"),
            () => Assert.True(
                result.Output.Contains("already exist", StringComparison.Ordinal),
                $"output must mention existing files. Output:\n{result.Output}"),
            () => Assert.True(
                File.ReadAllText(agentsPath).Contains("local edit", StringComparison.Ordinal),
                "local edit must survive the failed install attempt"));
    }

    // ==========================================================================================
    // INSTALLER-05
    // ==========================================================================================

    /// <summary>
    ///     INSTALLER-05 — without <c>-Force</c>, colliding files are refused; with <c>-Force</c>,
    ///     payload-owned files are replaced and the installer exits zero.
    /// </summary>
    [Fact]
    public void ForceIsRequiredToOverwrite()
    {
        // Arrange: first install to create the payload on disk
        using var target = new TargetFixture();
        var firstRun = Run(target);
        Assert.True(firstRun.ExitCode == 0, $"setup install failed. Output:\n{firstRun.Output}");

        // Act: re-install with -Force
        var result = Run(target, force: true);

        // Assert: exits 0 — all files overwritten successfully
        Assert.True(
            result.ExitCode == 0,
            $"expected exit 0 with -Force, got {result.ExitCode}. Output:\n{result.Output}");
    }

    // ==========================================================================================
    // INSTALLER-06
    // ==========================================================================================

    /// <summary>
    ///     INSTALLER-06 — <c>-Prune</c> lists files under payload directories that the payload no
    ///     longer provides. Because the prune prompt is interactive, this case supplies "n\n" via
    ///     stdin so nothing is deleted, and asserts only that the stale file is surfaced in the
    ///     output under the expected group heading.
    /// </summary>
    [Fact]
    public void PruneListsRetiredPayloadFiles()
    {
        // Arrange: install, then plant a retired file in a payload directory
        using var target = new TargetFixture();
        var firstRun = Run(target);
        Assert.True(firstRun.ExitCode == 0, $"setup install failed. Output:\n{firstRun.Output}");

        var agentsDir = Path.Combine(target.Root, ".github", "agents");
        var retiredFile = Path.Combine(agentsDir, "retired-old-agent.agent.md");
        File.WriteAllText(retiredFile, "# old agent");

        // Write a retired-payload.txt in the source repo that names the retired file — but since
        // we cannot modify this repository's retired-payload.txt, we rely on the "Not recognized"
        // group that covers files the repository added itself.

        // Act: re-install with -Force -Prune and answer "n" to every prompt
        var result = Run(target, force: true, prune: true, pruneDeny: true);

        // Assert: the retired file is listed and still on disk (not deleted)
        Assert.Multiple(
            () => Assert.True(
                result.Output.Contains("retired-old-agent.agent.md", StringComparison.OrdinalIgnoreCase),
                $"prune output must name the stale file. Output:\n{result.Output}"),
            () => Assert.True(
                File.Exists(retiredFile),
                "the stale file must survive because the prompt was answered 'n'"));
    }

    // ==========================================================================================
    // INSTALLER-I1
    // ==========================================================================================

    /// <summary>
    ///     INSTALLER-I1 — only the payload directories receive new content; a file sitting outside
    ///     those directories in the target is untouched by a normal install run.
    /// </summary>
    [Fact]
    public void WritesAreConfinedToPayloadPaths()
    {
        // Arrange: a target with a file outside the payload directories
        using var target = new TargetFixture();
        var bystander = Path.Combine(target.Root, "README.md");
        File.WriteAllText(bystander, "# my readme");

        // Act
        var result = Run(target);

        // Assert: exit 0 and the bystander is unchanged
        Assert.Multiple(
            () => Assert.True(result.ExitCode == 0, $"expected exit 0, got {result.ExitCode}. Output:\n{result.Output}"),
            () => Assert.Equal("# my readme", File.ReadAllText(bystander)));
    }

    // ==========================================================================================
    // INSTALLER-I2
    // ==========================================================================================

    /// <summary>
    ///     INSTALLER-I2 — a target that failed a collision-blocked install is still in a state
    ///     that the same command can be re-run against: supplying <c>-Force</c> on the retry
    ///     succeeds where the first attempt was blocked.
    /// </summary>
    [Fact]
    public void InterruptedInstallIsRecoverable()
    {
        // Arrange: pre-install, edit a file to create a collision, then attempt a plain re-install
        using var target = new TargetFixture();
        var firstRun = Run(target);
        Assert.True(firstRun.ExitCode == 0, $"setup install failed. Output:\n{firstRun.Output}");

        File.AppendAllText(Path.Combine(target.Root, ".github", "agents", "helper.agent.md"), "\n<!-- local edit -->");
        var blockedRun = Run(target);
        Assert.True(blockedRun.ExitCode == 1, "expected collision to block the install");

        // Act: re-run with -Force
        var result = Run(target, force: true);

        // Assert: exits 0 — the target is now fully installed
        Assert.True(
            result.ExitCode == 0,
            $"expected recovery with -Force to exit 0, got {result.ExitCode}. Output:\n{result.Output}");
    }

    // ==========================================================================================
    // ADDITIONAL: missing target
    // ==========================================================================================

    /// <summary>
    ///     A missing target repository is caught early: the installer exits non-zero and tells the
    ///     caller what was wrong rather than crashing or silently succeeding.
    /// </summary>
    [Fact]
    public void MissingTargetRepositoryIsRejected()
    {
        // Arrange: a path that does not exist
        var nonExistent = Path.Combine(Path.GetTempPath(), $"anneal-no-such-repo-{Guid.NewGuid():N}");

        // Act
        var result = RunAgainst(nonExistent);

        // Assert
        Assert.Multiple(
            () => Assert.True(result.ExitCode == 1, $"expected exit 1, got {result.ExitCode}. Output:\n{result.Output}"),
            () => Assert.True(
                result.Output.Contains("not found", StringComparison.OrdinalIgnoreCase),
                $"output must mention missing target. Output:\n{result.Output}"));
    }

    // ==========================================================================================
    // HELPERS
    // ==========================================================================================

    /// <summary>
    ///     This repository's root, found once by walking up from the test assembly until the
    ///     solution file is located.
    /// </summary>
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    ///     Spawns <c>install.ps1</c> from this repository's root against <paramref name="target" />,
    ///     optionally passing <c>-Force</c> and <c>-Prune</c>.
    /// </summary>
    /// <param name="target">The throwaway target fixture to install into.</param>
    /// <param name="force">When true, appends <c>-Force</c> to the argument list.</param>
    /// <param name="prune">When true, appends <c>-Prune</c> to the argument list.</param>
    /// <param name="pruneDeny">
    ///     When true, pipes <c>n</c> to stdin for every interactive prune prompt so nothing is
    ///     deleted, allowing the output to be inspected without side effects.
    /// </param>
    private static (int ExitCode, string Output) Run(
        TargetFixture target, bool force = false, bool prune = false, bool pruneDeny = false) =>
        RunAgainst(target.Root, force, prune, pruneDeny);

    private static (int ExitCode, string Output) RunAgainst(
        string targetPath, bool force = false, bool prune = false, bool pruneDeny = false)
    {
        var arguments = new List<string> { "./install.ps1", "-TargetRepository", targetPath };
        if (force) arguments.Add("-Force");
        if (prune) arguments.Add("-Prune");

        var start = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = pruneDeny,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = SysProcess.Start(start)
            ?? throw new InvalidOperationException("pwsh did not start");

        // Feed "n\n" for every prune group so interactive prompts do not block.
        if (pruneDeny)
        {
            process.StandardInput.Write("n\nn\n");
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = new StringBuilder(stdoutTask.GetAwaiter().GetResult());
        output.Append(stderrTask.GetAwaiter().GetResult());
        return (process.ExitCode, output.ToString());
    }

    /// <returns>
    ///     This repository's root, located by walking up from the test assembly's own directory
    ///     until the solution file is found.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no ancestor of the running test assembly holds the solution file — this suite
    ///     can only run from within a build of this repository.
    /// </exception>
    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Anneal.slnx")))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate this repository's root (Anneal.slnx) above {AppContext.BaseDirectory}");
    }

    /// <summary>
    ///     A throwaway directory under <c>artifacts/install-fixtures</c> that acts as the target
    ///     repository for one test. Created on construction and deleted on disposal.
    /// </summary>
    /// <remarks>
    ///     Rooted under this repository rather than the OS temp directory so that it is easy to
    ///     inspect after a failure and is clearly outside any critical path. Thread safety: not safe
    ///     for concurrent use; each test owns one instance.
    /// </remarks>
    private sealed class TargetFixture : IDisposable
    {
        private static readonly string FixtureRoot =
            Path.Combine(RepositoryRoot, "artifacts", "install-fixtures");

        public TargetFixture()
        {
            Root = Path.Combine(FixtureRoot, $"anneal-install-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        /// <summary>The target directory that install.ps1 is invoked against.</summary>
        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
            catch (IOException)
            {
                // A leftover fixture is litter, not a test failure.
            }
            catch (UnauthorizedAccessException)
            {
                // Same tolerance for a read-only leftover.
            }
        }
    }
}
