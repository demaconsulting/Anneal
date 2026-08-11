using System.Diagnostics;
using System.Text.RegularExpressions;
using DemaConsulting.Anneal.Toolkit.Operations;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     What a <see cref="DiffCheck" /> concluded: whether the diff could be read, the files it touched, and the
///     patch text itself.
/// </summary>
/// <param name="Available">Whether the diff was read successfully. False means neither field below can be trusted.</param>
/// <param name="ChangedFiles">
///     The repository-relative paths the diff touched, forward-slash separated, parsed from each patch's
///     <c>diff --git a/... b/...</c> header. Empty when the diff was empty or could not be read.
/// </param>
/// <param name="Patch">
///     The diff's own text, trimmed to what a reader — human or model — needs to judge it, since a verifier is
///     granted no tools of its own to go read the working tree a second time.
/// </param>
internal sealed record DiffFinding(bool Available, IReadOnlyList<string> ChangedFiles, string Patch);

/// <summary>
///     Runs <c>git diff</c> against the repository and reports both the changed-file list and the patch text
///     itself.
/// </summary>
/// <remarks>
///     A caller judging scope honesty needs to know what actually changed, not what a model self-reports having
///     changed — every existing "changed file" signal in this Toolkit (<c>DevelopmentEnvelope.FilesChanged</c>,
///     <c>RepositoryFacts.ChangedFileHints</c>) is a caller- or model-supplied hint, never a fact read from the
///     repository itself. This primitive closes that gap the same way <see cref="DeterministicCheck" /> closes it
///     for a build or a lint pass: one small, deterministic, no-model-call step whose answer is a fact about the
///     repository.
///     <para>
///         <c>git</c> is run directly rather than through <see cref="Operations.PowerShellScripts" />: that seam
///         invokes one of the repository's own <c>.ps1</c> files by convention, and a diff is not a repository
///         script — it is a fact about the working tree <c>git</c> itself holds.
///     </para>
///     <para>Thread safety: instances are immutable and safe to share; the process started is not.</para>
/// </remarks>
internal sealed partial class DiffCheck
{
    /// <remarks>The trim bound applied to a patch before it is folded into a verifier's evidence.</remarks>
    private const int MaxPatchLength = 8000;

    private readonly RunGitCommand _runGit;
    private readonly TimeSpan _timeout;

    /// <summary>
    ///     Binds a diff check to a repository and, optionally, a substituted <c>git</c> runner.
    /// </summary>
    /// <param name="repositoryRoot">The repository the diff is read from. Must not be null or blank.</param>
    /// <param name="timeout">
    ///     The most time the diff may take before it is reported unavailable rather than left running. Must be
    ///     greater than <see cref="TimeSpan.Zero" />; defaults to one minute.
    /// </param>
    /// <param name="runGit">
    ///     Runs one <c>git</c> invocation, or null to run it through the real <c>git</c> executable. Injected so
    ///     this primitive's whole behavior is exercisable without a real repository.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout" /> is not greater than zero.</exception>
    public DiffCheck(string repositoryRoot, TimeSpan? timeout = null, RunGitCommand? runGit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _timeout = timeout ?? TimeSpan.FromMinutes(1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeout, TimeSpan.Zero);

        _runGit = runGit ?? new GitProcess(Path.GetFullPath(repositoryRoot)).RunAsync;
    }

    /// <summary>
    ///     Reads the changed-file list against a base reference, or the uncommitted working tree when none is
    ///     given.
    /// </summary>
    /// <param name="baseRef">
    ///     A branch or commit to diff against, compared as <c>{baseRef}...HEAD</c>, or null/blank to diff
    ///     uncommitted work against <c>HEAD</c> instead.
    /// </param>
    /// <param name="cancellationToken">The caller's signal; cancelling it stops the run without reporting a verdict.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> with <see cref="DiffFinding.Available" /> true when the diff
    ///     was read, including an empty one; <see cref="OperationOutcome.Failed" /> with
    ///     <see cref="DiffFinding.Available" /> false when <c>git</c> exited non-zero or the check timed out.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<DiffFinding>> RunAsync(string? baseRef, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string> arguments = string.IsNullOrWhiteSpace(baseRef)
            ? ["diff", "HEAD"]
            : ["diff", $"{baseRef}...HEAD"];

        using var timeoutSource = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            // Mark untracked files as intent-to-add before the whole-tree diff so 'git diff HEAD'
            // sees brand-new files as additions rather than silently omitting them ('??' entries in
            // 'git status' are invisible to 'git diff HEAD' without this step). Skipped for
            // branch-to-branch diffs because those compare committed content and do not have this
            // blind spot. Best-effort: a non-zero exit here (e.g. test stub, no repository) does not
            // abort the diff itself.
            if (string.IsNullOrWhiteSpace(baseRef))
                await _runGit(["add", "-N", "."], linked.Token).ConfigureAwait(false);

            var run = await _runGit(arguments, linked.Token).ConfigureAwait(false);

            if (run.ExitCode != 0)
            {
                var finding = new DiffFinding(false, [], run.Output);
                return new StepResult<DiffFinding>(
                    OperationOutcome.Failed, finding, [new ProcessNote($"git diff exited {run.ExitCode}")]);
            }

            var files = FileHeader()
                .Matches(run.Output)
                .Select(match => match.Groups["path"].Value.Trim().Replace('\\', '/'))
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return new StepResult<DiffFinding>(
                OperationOutcome.Succeeded, new DiffFinding(true, files, Summarize(run.Output)), []);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var finding = new DiffFinding(false, [], $"timed out after {_timeout}");
            return new StepResult<DiffFinding>(
                OperationOutcome.Failed, finding, [new ProcessNote($"git diff did not finish within {_timeout}")]);
        }
    }

    /// <remarks>
    ///     Trimmed rather than truncated blindly, the same reasoning <see cref="DeterministicCheck" />'s own
    ///     <c>Summarize</c> gives: a verifier's evidence budget needs what a reader can act on, not every line a
    ///     large patch produced.
    /// </remarks>
    private static string Summarize(string output) =>
        output.Length <= MaxPatchLength ? output : output[..MaxPatchLength] + "…";

    [GeneratedRegex(@"^diff --git a/(?<path>.+?) b/", RegexOptions.Multiline)]
    private static partial Regex FileHeader();
}

/// <summary>Runs one <c>git</c> invocation and reports what it produced.</summary>
/// <remarks>
///     Public, mirroring <see cref="Operations.RunRepositoryScript" />'s own visibility: a public operation
///     composing <see cref="DiffCheck" /> needs to accept a substituted runner in its own public constructor, the
///     same way <see cref="Operations.MaintainOperation" /> already accepts a substituted
///     <see cref="Operations.RunRepositoryScript" />.
/// </remarks>
/// <param name="arguments">The arguments given to <c>git</c>, in order.</param>
/// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
/// <returns>What <c>git</c> produced.</returns>
public delegate Task<ScriptRun> RunGitCommand(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

/// <summary>
///     Runs <c>git</c> directly against a repository, the default behind <see cref="DiffCheck" />.
/// </summary>
/// <remarks>
///     Mirrors <see cref="Operations.PowerShellScripts" />'s process-hosting shape — concurrent stream reads
///     alongside the wait, and a full process-tree kill on cancellation — but starts <c>git</c> itself rather
///     than <c>pwsh -File</c>, since a diff is not one of the repository's own scripts.
/// </remarks>
internal sealed class GitProcess
{
    private readonly string _repositoryRoot;

    /// <param name="repositoryRoot">The working directory <c>git</c> is run from. Must not be null or blank.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public GitProcess(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = repositoryRoot;
    }

    /// <summary>Runs <c>git</c> with the given arguments and collects what it produced.</summary>
    /// <param name="arguments">The arguments given to <c>git</c>, in order.</param>
    /// <param name="cancellationToken">The caller's signal; cancelling it kills the process.</param>
    /// <returns>The exit code and the collected output.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<ScriptRun> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };

        process.Start();

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
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
            {
                // Already gone, or a platform that will not report on the tree.
            }

            throw;
        }

        return new ScriptRun(
            process.ExitCode,
            error.Length == 0 ? output : output + Environment.NewLine + error);
    }
}
