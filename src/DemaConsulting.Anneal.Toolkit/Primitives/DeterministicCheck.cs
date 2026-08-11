using DemaConsulting.Anneal.Toolkit.Operations;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     What a <see cref="DeterministicCheck" /> concluded: whether it passed, what it left as its exit code, and
///     the evidence a reader or a <see cref="Verifier" /> can check the verdict against.
/// </summary>
/// <param name="Name">The check's own name, as a caller and a report both refer to it.</param>
/// <param name="Passed">Whether the check passed. The whole of the verdict; nothing here is a judgement call.</param>
/// <param name="ExitCode">What the script left. Zero means it passed.</param>
/// <param name="Summary">What the script reported, trimmed to what a reader needs to act on it.</param>
/// <param name="EvidenceRefs">
///     What was run — the script name, and any selector applied — so the verdict can be reproduced rather than
///     taken on trust.
/// </param>
internal sealed record CheckFinding(
    string Name, bool Passed, int ExitCode, string Summary, IReadOnlyList<string> EvidenceRefs);

/// <summary>
///     Runs one deterministic build/test/check step and reports what it found. The one primitive in this library
///     that makes no model call.
/// </summary>
/// <remarks>
///     A thin wrapper over <see cref="RunRepositoryScript" /> — the same seam <see cref="Operations.LintFixOperation" />
///     already runs <c>fix.ps1</c> and <c>lint.ps1</c> through — rather than a new way of running a script, because
///     a deterministic check's whole reason for existing is that its answer is a fact about the repository, and
///     inventing a second execution path would be a second place that fact could be gotten wrong.
///     <para>
///         A timeout is enforced here rather than left to the script, because a check with no answer within its
///         bound is exactly the case a bounded process must not hang on. A caller's own cancellation and the
///         timeout are both honored; only the timeout is reported as a failed check, since the caller's own
///         cancellation is withdrawal, not a verdict.
///     </para>
///     <para>Thread safety: instances are immutable and safe to share; the scripts run are not.</para>
/// </remarks>
internal sealed class DeterministicCheck
{
    private readonly RunRepositoryScript _runScript;
    private readonly TimeSpan _timeout;
    private readonly string _repositoryRoot;

    /// <summary>
    ///     Binds a deterministic check to a repository and, optionally, a substituted script runner.
    /// </summary>
    /// <param name="repositoryRoot">The repository the script is run from. Must not be null or blank.</param>
    /// <param name="timeout">
    ///     The most time one check may take before it is reported failed rather than left running. Must be
    ///     greater than <see cref="TimeSpan.Zero" />; defaults to five minutes.
    /// </param>
    /// <param name="runScript">
    ///     Runs one of the repository's scripts, or null to run them through the PowerShell host. Injected so this
    ///     primitive's state flow — the timeout and the outcome mapping — is exercisable without a real script.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="timeout" /> is not greater than zero.</exception>
    public DeterministicCheck(string repositoryRoot, TimeSpan? timeout = null, RunRepositoryScript? runScript = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _timeout = timeout ?? TimeSpan.FromMinutes(5);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeout, TimeSpan.Zero);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _runScript = runScript ?? new PowerShellScripts(_repositoryRoot).RunAsync;
    }

    /// <summary>
    ///     Runs the named check.
    /// </summary>
    /// <param name="name">The check's own name. Must not be null or blank.</param>
    /// <param name="script">
    ///     The repository-relative script to run, or null when the repository configures no such script for this
    ///     check. Must not be empty or blank when given.
    /// </param>
    /// <param name="selector">
    ///     A strictness or scope selector recorded as evidence alongside the script, or null when the check runs
    ///     unqualified. This primitive does not interpret it — a selector is passed through to the caller's own
    ///     <see cref="RunRepositoryScript" /> to act on, exactly as <paramref name="script" /> is.
    /// </param>
    /// <param name="cancellationToken">The caller's signal; cancelling it stops the run without reporting a verdict.</param>
    /// <param name="touchedFiles">
    ///     File names or relative paths the current change actually touches. When non-empty and output would be
    ///     truncated, lines that mention one of these names are promoted ahead of the fixed head+tail selection so
    ///     a warning about a changed file survives truncation even when it sits in the middle of a large report.
    ///     Null and empty are equivalent; all existing callers that pass no hint get unmodified head+tail behavior.
    /// </param>
    /// <returns>
    ///     <see cref="OperationOutcome.UsageError" /> with no finding when <paramref name="name" /> is blank or
    ///     <paramref name="script" /> is empty or blank (but not null); <see cref="OperationOutcome.Succeeded" />
    ///     with a finding marked skipped when <paramref name="script" /> is null; <see cref="OperationOutcome.Succeeded" />
    ///     with the finding when the script exits zero; <see cref="OperationOutcome.Failed" /> with the finding
    ///     when it exits non-zero or the check times out.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<CheckFinding>> RunAsync(
        string name, string? script, string? selector, CancellationToken cancellationToken,
        IReadOnlyList<string>? touchedFiles = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(name))
            return new StepResult<CheckFinding>(
                OperationOutcome.UsageError, null, [new ProcessNote("a check needs both a name and a script")]);

        // A repository that configures no script for this check (see ScriptConfiguration) is not a failure to
        // diagnose - there is nothing to run, so the check passes trivially rather than reporting a script that
        // does not exist.
        if (script is null)
            return new StepResult<CheckFinding>(
                OperationOutcome.Succeeded,
                new CheckFinding(name, true, 0, "skipped - no script configured for this repository", []),
                []);

        if (string.IsNullOrWhiteSpace(script))
            return new StepResult<CheckFinding>(
                OperationOutcome.UsageError, null, [new ProcessNote("a check needs both a name and a script")]);

        IReadOnlyList<string> evidence = selector is null ? [script] : [script, selector];

        using var timeoutSource = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            var run = await _runScript(script, linked.Token).ConfigureAwait(false);
            var passed = run.ExitCode == 0;

            // Write the full output to a log file only when Summarize would actually truncate it,
            // so a human can inspect signal that the trimmed evidence may hide.
            var logPath = await TryWriteFullOutputAsync(name, run.Output).ConfigureAwait(false);

            var summary = logPath is null
                ? Summarize(run.Output, touchedFiles)
                : Summarize(run.Output, touchedFiles) + $"\n\nFull output: {logPath}";

            var finding = new CheckFinding(name, passed, run.ExitCode, summary, evidence);

            return new StepResult<CheckFinding>(
                passed ? OperationOutcome.Succeeded : OperationOutcome.Failed, finding, []);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var finding = new CheckFinding(name, false, -1, $"timed out after {_timeout}", evidence);
            return new StepResult<CheckFinding>(
                OperationOutcome.Failed, finding, [new ProcessNote($"'{script}' did not finish within {_timeout}")]);
        }
    }

    /// <remarks>
    ///     Best-effort: any failure (e.g. the logs directory cannot be created, the disk is full) is
    ///     swallowed rather than propagated, because this is a diagnostic aid and must never change the
    ///     check's reported outcome. Returns null when no file was written.
    ///     <para>
    ///         Only writes when output would actually be truncated by <see cref="Summarize" />, so callers
    ///         who see a log path in the summary know the file always contains more than the trimmed evidence.
    ///     </para>
    /// </remarks>
    private async Task<string?> TryWriteFullOutputAsync(string checkName, string output)
    {
        const int fullBudget = 2000;
        if (output.Length <= fullBudget)
            return null;

        try
        {
            var logsDir = Path.Combine(_repositoryRoot, ".anneal", "logs");
            Directory.CreateDirectory(logsDir);

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var fileName = $"check-output-{checkName}-{timestamp}.txt";
            var filePath = Path.Combine(logsDir, fileName);

            await File.WriteAllTextAsync(filePath, output).ConfigureAwait(false);

            return $".anneal/logs/{fileName}";
        }
        catch
        {
            return null;
        }
    }

    /// <remarks>
    ///     Trimmed rather than truncated blindly, so a short pass/fail script's output is not stretched with
    ///     nothing, while a linter's page of findings is bounded to what a reader — human or model — actually
    ///     needs to act.
    ///     <para>
    ///         When output exceeds the budget, both head and tail are kept rather than just the head: many
    ///         build/lint tools print the actionable summary — error count, final verdict — at the end, after
    ///         pages of per-file detail. Blind head-truncation silently drops exactly that signal. Each half is
    ///         1000 characters, preserving the same 2000-character real-content budget.
    ///     </para>
    ///     <para>
    ///         When <paramref name="touchedFiles" /> is non-empty and output would be truncated, lines that
    ///         mention one of the touched file names are promoted ahead of the head+tail selection so a warning
    ///         about a changed file is not lost to the middle-of-report blind spot. Promoted lines are prepended
    ///         before the head+tail content, with remaining budget split evenly between head and tail. If the
    ///         promoted lines alone exhaust the budget the head+tail halves are omitted entirely.
    ///     </para>
    /// </remarks>
    private static string Summarize(string output, IReadOnlyList<string>? touchedFiles = null)
    {
        const int halfBudget = 1000;
        const int fullBudget = halfBudget * 2;

        if (output.Length <= fullBudget)
            return output;

        // Relevance-aware path: when touched-file hints are available, promote any line that mentions
        // one of those files so it cannot be lost to the middle-of-report blind spot.
        if (touchedFiles is { Count: > 0 })
        {
            var lines = output.Split('\n');
            var promoted = lines
                .Where(l => touchedFiles.Any(f =>
                    l.Contains(Path.GetFileName(f), StringComparison.OrdinalIgnoreCase) ||
                    l.Contains(f, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (promoted.Count > 0)
            {
                var promotedBlock = string.Join('\n', promoted);
                // Promoted lines consume budget first; remaining budget is split evenly for head+tail.
                if (promotedBlock.Length >= fullBudget)
                    return promotedBlock[..fullBudget];

                var remaining = fullBudget - promotedBlock.Length - 1; // -1 for separator newline
                if (remaining <= 0)
                    return promotedBlock;

                var eachHalf = remaining / 2;
                var head = output[..Math.Min(eachHalf, output.Length)];
                var tail = output[^Math.Min(eachHalf, output.Length)..];
                return promotedBlock + "\n" + head + $"\n\u2026[truncated]\u2026\n" + tail;
            }
        }

        var omitted = output.Length - fullBudget;
        return output[..halfBudget] + $"\n\u2026[{omitted} characters omitted]\u2026\n" + output[^halfBudget..];
    }
}
