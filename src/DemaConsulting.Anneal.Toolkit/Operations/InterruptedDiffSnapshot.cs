using DemaConsulting.Anneal.Toolkit.Primitives;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Writes a recoverable patch file capturing the uncommitted diff of a set of files before a human triages an
///     interrupted run.
/// </summary>
/// <remarks>
///     A pre-triage snapshot exists because <c>git checkout</c> and <c>rm</c> are one-way doors for uncommitted
///     changes: once a human decides to revert the partial work, there is nothing to fall back to if that decision
///     turns out to be wrong. Writing the diff first costs nothing and preserves every option. This helper is shared
///     between <see cref="RouteOperation" /> and <see cref="MaintainOperation" /> so neither duplicates the logic.
/// </remarks>
internal static class InterruptedDiffSnapshot
{
    /// <summary>
    ///     Runs <c>git diff HEAD</c> over the whole working tree and writes the result as
    ///     <c>.anneal/logs/snapshots/interrupted-&lt;timestamp&gt;.patch</c>.
    /// </summary>
    /// <remarks>
    ///     This overload is the correct one to call at every Failed or Escalated exit point: it reads the diff
    ///     as a fact from git rather than trusting the worker's own self-reported file list, which may
    ///     under-report what was actually touched. If git reports no diff, the method returns null and writes
    ///     nothing — the same no-op behavior as the file-scoped overload when a diff is empty.
    /// </remarks>
    /// <param name="repositoryRoot">Absolute path to the repository root.</param>
    /// <param name="cancellationToken">Token observed for cooperative cancellation.</param>
    /// <returns>
    ///     The repository-relative path of the written patch file, or <c>null</c> when the diff subprocess
    ///     produced no output, returned a non-zero exit code, or any I/O failure occurred. Null is a silent
    ///     no-op: snapshot failure must never mask the real escalation or failure being reported.
    /// </returns>
    internal static Task<string?> WriteAsync(string repositoryRoot, CancellationToken cancellationToken) =>
        WriteAsync(repositoryRoot, [], cancellationToken);

    /// <summary>
    ///     Runs <c>git diff HEAD -- &lt;files&gt;</c> and writes the result as
    ///     <c>.anneal/logs/snapshots/interrupted-&lt;timestamp&gt;.patch</c>.
    /// </summary>
    /// <remarks>
    ///     Pass an empty <paramref name="files"/> list (or call the no-file-list overload) to diff the whole
    ///     working tree rather than a worker-reported subset — prefer the whole-tree overload at Failed/Escalated
    ///     exit points so a worker that under-reports its own file list cannot silently suppress the snapshot.
    /// </remarks>
    /// <param name="repositoryRoot">Absolute path to the repository root.</param>
    /// <param name="files">
    ///     The files whose uncommitted diff should be captured. Pass an empty list to diff the entire working tree.
    ///     Must not be null.
    /// </param>
    /// <param name="cancellationToken">Token observed for cooperative cancellation.</param>
    /// <returns>
    ///     The repository-relative path of the written patch file (e.g.
    ///     <c>.anneal/logs/snapshots/interrupted-20260811T140000Z.patch</c>), or <c>null</c> when the diff subprocess
    ///     produced no output, returned a non-zero exit code, or any I/O failure occurred. Null is a silent
    ///     no-op: snapshot failure must never mask the real escalation or failure being reported.
    /// </returns>
    internal static async Task<string?> WriteAsync(
        string repositoryRoot,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken)
    {
        try
        {
            var git = new GitProcess(repositoryRoot);

            // An empty files list produces 'git diff HEAD' with no path filter — a whole-tree diff.
            IReadOnlyList<string> arguments = files.Count > 0
                ? ["diff", "HEAD", "--", .. files]
                : ["diff", "HEAD"];
            var run = await git.RunAsync(arguments, cancellationToken).ConfigureAwait(false);

            if (run.ExitCode != 0 || string.IsNullOrWhiteSpace(run.Output))
                return null;

            var snapshotsDir = Path.Combine(repositoryRoot, ".anneal", "logs", "snapshots");
            Directory.CreateDirectory(snapshotsDir);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var patchFileName = $"interrupted-{timestamp}.patch";
            var patchPath = Path.Combine(snapshotsDir, patchFileName);

            await File.WriteAllTextAsync(patchPath, run.Output, cancellationToken).ConfigureAwait(false);

            // Repository-relative forward-slash path for display consistency with the rest of the tool's output.
            return $".anneal/logs/snapshots/{patchFileName}";
        }
        catch
        {
            return null;
        }
    }
}
