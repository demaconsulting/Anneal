using System.Text.Json;
using System.Text.Json.Serialization;
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

            // For a whole-tree diff, mark untracked files as intent-to-add first so 'git diff HEAD'
            // sees brand-new files as additions rather than silently omitting them. A named-file diff has the
            // same blind spot for a brand-new file, but no production call site passes a non-empty list today
            // (both RouteOperation and MaintainOperation call the whole-tree overload); this overload is kept
            // for callers that genuinely want a named subset.
            if (files.Count == 0)
                await git.RunAsync(["add", "-N", "."], cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    ///     Writes a JSON triage context file alongside the patch file written by <see cref="WriteAsync(string, CancellationToken)" />,
    ///     recording the human-readable narrative that would otherwise only appear on the live console.
    /// </summary>
    /// <remarks>
    ///     A developer or agent that later encounters a dirty working tree without having watched the original console
    ///     output can open this file to learn why the run stopped and what work remains unverified. The patch file
    ///     records <em>what</em> changed; this file records <em>why the run stopped</em> and what to do next.
    ///     The timestamp is shared with the patch file so the two can be matched by name.
    ///     Failures are silenced — snapshot failure must never mask the escalation or failure being reported.
    /// </remarks>
    /// <param name="patchPath">
    ///     The repository-relative path returned by <see cref="WriteAsync(string, CancellationToken)" />
    ///     (e.g. <c>.anneal/logs/snapshots/interrupted-20260812T120000Z.patch</c>). The JSON companion is written
    ///     next to it with the same timestamp stem and a <c>.json</c> extension. Must not be null.
    /// </param>
    /// <param name="repositoryRoot">Absolute path to the repository root.</param>
    /// <param name="context">The triage narrative to persist.</param>
    /// <param name="cancellationToken">Token observed for cooperative cancellation.</param>
    /// <returns>
    ///     The repository-relative path of the written JSON file (e.g.
    ///     <c>.anneal/logs/snapshots/interrupted-20260812T120000Z.json</c>), or <c>null</c> when any I/O failure
    ///     occurred. Null is a silent no-op.
    /// </returns>
    internal static async Task<string?> WriteTriageContextAsync(
        string patchPath,
        string repositoryRoot,
        InterruptedTriageContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // Derive the JSON file path from the patch path by replacing the .patch extension.
            var jsonRelative = Path.ChangeExtension(patchPath.Replace('/', Path.DirectorySeparatorChar), ".json");
            var jsonAbsolute = Path.Combine(repositoryRoot, jsonRelative);

            var json = JsonSerializer.Serialize(context, InterruptedTriageContextJsonContext.Default.InterruptedTriageContext);
            await File.WriteAllTextAsync(jsonAbsolute, json, cancellationToken).ConfigureAwait(false);

            return patchPath.Replace(".patch", ".json");
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
///     The triage narrative persisted alongside an interrupted-diff patch file so a later human or agent that only
///     sees a dirty working tree can learn why the run stopped and what remains unverified.
/// </summary>
/// <param name="Outcome">
///     The operation outcome that ended this run — "Escalated" or "Failed". Never null.
/// </param>
/// <param name="RecommendedNextStep">
///     What the run's own oracle or the operation recommends a person do next. Never null; may be empty when no
///     recommendation was produced.
/// </param>
/// <param name="WhatWasTried">
///     Each step the run attempted, oldest first. Never null; may be empty.
/// </param>
/// <param name="FilesChanged">
///     Files already written to disk before the run stopped. Never null; may be empty.
/// </param>
/// <param name="Summary">
///     A brief account of what had been done before the run stopped. Never null; may be empty.
/// </param>
/// <param name="EscalationOrFailureReason">
///     Why the run escalated or failed, when a reason is available. Null when no specific reason was produced
///     (e.g. a budget exhaustion with no accompanying message).
/// </param>
internal sealed record InterruptedTriageContext(
    string Outcome,
    string RecommendedNextStep,
    IReadOnlyList<string> WhatWasTried,
    IReadOnlyList<string> FilesChanged,
    string Summary,
    string? EscalationOrFailureReason);

[JsonSerializable(typeof(InterruptedTriageContext))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class InterruptedTriageContextJsonContext : JsonSerializerContext;

