using System.Diagnostics;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What running one repository script produced: the exit code it left, and everything it wrote.
/// </summary>
/// <remarks>
///     The exit code is the whole of the verdict — this is why <c>lint-fix</c> was chosen as the first compiled
///     process — while the output is the material a worker is shown. They are carried together because a run
///     that reports one without the other cannot be acted on: an exit code with no output says a repair is
///     needed but not which, and output with no exit code has to be parsed to find out whether anything is
///     wrong.
/// </remarks>
/// <param name="ExitCode">What the script left. Zero means it passed.</param>
/// <param name="Output">Everything the script wrote, standard output and error interleaved. Never null.</param>
public sealed record ScriptRun(int ExitCode, string Output);

/// <summary>
///     Runs one of the repository's own scripts on behalf of an operation.
/// </summary>
/// <remarks>
///     A seam, and deliberately not a tool: the scripts are the process's control flow, so the operation runs
///     them and the model never can. It is injectable so that the whole of an operation's state flow — the loop,
///     the budget, the escalation and the failure paths — is exercisable without a PowerShell host, which is
///     what keeps a contract test a test rather than a several-minute rebuild of the repository.
/// </remarks>
/// <param name="script">The repository-root-relative script to run, such as <c>lint.ps1</c>.</param>
/// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
/// <returns>What the script produced.</returns>
public delegate Task<ScriptRun> RunRepositoryScript(string script, CancellationToken cancellationToken);

/// <summary>
///     Runs a repository script through the PowerShell host, which is how the scripts are invoked everywhere
///     else in this process.
/// </summary>
/// <remarks>
///     <c>pwsh</c> rather than <c>powershell</c>, matching every invocation in <c>AGENTS.md</c> and in CI, so a
///     script that works when a person runs it works when an operation does. Standard error is folded into
///     standard output because the linters write findings to both and a reader — human or model — needs them in
///     the order they were produced rather than sorted by stream.
///     <para>
///         Thread safety: stateless and safe to call concurrently, though the scripts themselves are not.
///     </para>
/// </remarks>
public sealed class PowerShellScripts
{
    private readonly string _repositoryRoot;

    /// <summary>
    ///     Binds the runner to the repository whose scripts it runs.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The working directory each script is run from, which is the repository root. Must not be null or
    ///     blank.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public PowerShellScripts(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    /// <summary>
    ///     Runs one script and collects what it produced.
    /// </summary>
    /// <param name="script">The repository-relative script to run. Must not be null or blank.</param>
    /// <param name="cancellationToken">The caller's signal; cancelling it kills the script.</param>
    /// <returns>The exit code and the collected output.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="script" /> is null, empty or blank.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<ScriptRun> RunAsync(string script, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new ProcessStartInfo("pwsh")
            {
                ArgumentList = { "-NoProfile", "-File", Path.Combine(_repositoryRoot, script) },
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();

        // Both streams are read concurrently with the wait. Reading one to completion first deadlocks the
        // moment the other fills its pipe buffer, which a linter reporting hundreds of findings will do.
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        string output, error;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            output = await standardOutput.ConfigureAwait(false);
            error = await standardError.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelling the wait does not stop the script - it only stops waiting for it. Left alone, fix.ps1
            // would carry on editing the repository after its caller withdrew and moved on, which is the
            // opposite of what a cancellation promises. The whole tree goes, because these scripts start
            // linters and the dotnet CLI as children of their own.
            await TerminateAsync(process).ConfigureAwait(false);
            throw;
        }

        return new ScriptRun(
            process.ExitCode,
            error.Length == 0 ? output : output + Environment.NewLine + error);
    }

    /// <summary>
    ///     Stops a script that outlived its caller's interest, and waits for it to actually be gone.
    /// </summary>
    /// <param name="process">The script's process.</param>
    /// <remarks>
    ///     Waits for the kill to take effect rather than only requesting it, so a caller that has been told the
    ///     invocation stopped is not racing a process that is still writing files. A process that exited between
    ///     the check and the kill raises <see cref="InvalidOperationException" />, which is the outcome that was
    ///     wanted anyway.
    /// </remarks>
    private static async Task TerminateAsync(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            // Already gone, or a platform that will not report on the tree. Either way there is nothing left to
            // stop, and throwing here would replace the caller's cancellation with an unrelated failure.
        }
    }
}
