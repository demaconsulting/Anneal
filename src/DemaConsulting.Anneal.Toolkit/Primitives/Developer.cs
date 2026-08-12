using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using DemaConsulting.Anneal.Toolkit.Operations;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     Authors code or tests against a declared scope, optionally driving a deterministic build/test check between
///     attempts, and reports either the completed change or why the work belongs to a different worker.
/// </summary>
/// <remarks>
///     Composed the same way <see cref="DocumentAuthor" /> is, with one addition: when a <see cref="RunRepositoryScript" />
///     is supplied, a failing check re-prompts the same conversation with what the check reported, bounded by
///     <c>maxRepairAttempts</c> — the same shape <see cref="Operations.LintFixOperation" /> already uses for its
///     own iteration budget, here generalized to whatever local script a caller names rather than fixed to
///     <c>lint.ps1</c>. When no check is supplied, a single authoring pass is made and reported as-is; a caller
///     that wants build verification composes one via <see cref="RepairLoop{TState}" /> and a
///     <see cref="DeterministicCheck" /> instead of relying on this shortcut.
///     <para>
///         A protected-write refusal escalates regardless of what the probe decoded, and a build/test-repair
///         budget spent with the check still failing is reported as <see cref="OperationOutcome.Failed" /> —
///         "cannot proceed honestly" is reserved for a pass that never had enough to try, not one that tried and
///         ran out of budget.
///     </para>
///     <para>
///         Every <c>scopeDriftCheckInterval</c> successful edit-tool calls, a cheap Light-role scope-drift probe
///         runs mid-development to verify the pass is still working within the original instruction's scope.
///         When the oracle detects clear drift the run is aborted and reported as <see cref="OperationOutcome.Failed" />;
///         when the oracle lacks sufficient evidence to judge, execution continues. This check fires after each
///         <see cref="ModelSession.RunAsync" /> turn (initial or repair) that crossed the K-boundary.
///     </para>
///     <para>
///         After the development pass, the self-reported file list is corroborated against a real
///         <c>git diff HEAD</c> snapshot: any file the model claims to have changed that has no real diff entry
///         is dropped before the result is returned. When git is unavailable the self-reported list is used
///         unchanged — the corroboration is a strengthening check, not a hard dependency.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
internal sealed class Developer
{
    private readonly string _repositoryRoot;
    private readonly string _charter;
    private readonly IReadOnlyList<string> _toolGrantGroups;
    private readonly ModelRole _role;
    private readonly int _maxRepairAttempts;
    private readonly int _scopeDriftCheckInterval;
    private readonly int _maxOutputTokens;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunRepositoryScript? _buildCheck;
    private readonly RunGitCommand? _runGit;

    /// <summary>
    ///     Binds a developer to a repository and the charter its authoring pass carries.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository authored into, outside which every tool call is refused, and whose configuration names
    ///     the models behind the capability roles. Must not be null or blank.
    /// </param>
    /// <param name="charter">
    ///     The system message the pass carries: what scope it may touch, and that naming the wrong worker is a
    ///     correct answer. Must not be null.
    /// </param>
    /// <param name="toolGrantGroups">
    ///     The <see cref="ToolGroups" /> names this developer is granted. Must not be null; defaults to
    ///     <see cref="ToolGroups.Read" /> and <see cref="ToolGroups.Edit" /> when null is not overridden by an
    ///     empty list — an empty list is itself a grant of nothing, never an absent one.
    /// </param>
    /// <param name="role">The capability tier the pass runs at. Defaults to <see cref="ModelRole.Heavy" />.</param>
    /// <param name="maxRepairAttempts">
    ///     The most times a failing build check re-prompts the conversation before the run is reported failed.
    ///     This is the write/repair budget this primitive enforces; a raw tool-call ceiling is not, for the same
    ///     reason <see cref="Operations.LintFixOperation" /> bounds iterations rather than individual tool calls —
    ///     one authoring turn may make as many read or edit calls as the model needs. Must be zero or greater;
    ///     defaults to 3.
    /// </param>
    /// <param name="scopeDriftCheckInterval">
    ///     After every this-many successful edit-tool calls, a cheap Light-role scope-drift probe runs to confirm
    ///     the pass is still working within the original instruction's scope. Zero disables the periodic check.
    ///     Defaults to 5.
    /// </param>
    /// <param name="maxOutputTokens">
    ///     The context budget: the ceiling on generated output for every turn. Defaults to
    ///     <see cref="ModelSession.DefaultMaxOutputTokens" />.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this primitive's whole behavior is exercisable without a network call.
    /// </param>
    /// <param name="buildCheck">
    ///     Runs a local build or test check after each authoring attempt, or null to skip verification and report
    ///     the first attempt as-is. Injected so the repair loop is exercisable without a real build.
    /// </param>
    /// <param name="runGit">
    ///     Runs one <c>git</c> invocation for the post-development corroboration diff, or null to use the real
    ///     <c>git</c> executable. Injected so the corroboration check is exercisable without a real repository.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="charter" /> or <paramref name="toolGrantGroups" /> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxRepairAttempts" /> is negative, or
    ///     <paramref name="scopeDriftCheckInterval" /> is negative.
    /// </exception>
    public Developer(
        string repositoryRoot,
        string charter,
        IReadOnlyList<string>? toolGrantGroups = null,
        ModelRole role = ModelRole.Heavy,
        int maxRepairAttempts = 3,
        int scopeDriftCheckInterval = 5,
        int maxOutputTokens = ModelSession.DefaultMaxOutputTokens,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? buildCheck = null,
        RunGitCommand? runGit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(charter);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(scopeDriftCheckInterval);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _charter = charter;
        _toolGrantGroups = toolGrantGroups ?? [ToolGroups.Read, ToolGroups.Edit];
        _role = role;
        _maxRepairAttempts = maxRepairAttempts;
        _scopeDriftCheckInterval = scopeDriftCheckInterval;
        _maxOutputTokens = maxOutputTokens;
        _endpointFor = endpointFor;
        _buildCheck = buildCheck;
        _runGit = runGit;
    }

    /// <summary>
    ///     Authors code or tests against an instruction, repairing against the build check when one is configured.
    /// </summary>
    /// <param name="instruction">What to change, stated as a caller would state it. Must not be null or blank.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Escalated" /> with the decoded result when a repair needed a protected
    ///     path or an explicit admission; <see cref="OperationOutcome.Succeeded" /> with the decoded result when
    ///     the change was completed or the pass named a better owner — both are this primitive successfully
    ///     answering its own question, per <c>.anneal/architecture/toolkit.md</c> § Decisions;
    ///     <see cref="OperationOutcome.Failed" /> with no finding when the build-repair budget was spent with the
    ///     check still failing, when the periodic scope-drift check detected clear scope drift, when the check
    ///     itself faulted, or when no model could be reached;
    ///     <see cref="OperationOutcome.Refused" /> is reserved for the rarer case where this pass cannot proceed
    ///     honestly enough to answer at all — see the remarks on <see cref="DevelopmentResult" /> for why that
    ///     path is currently unreachable.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="instruction" /> is null, empty or blank.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<DevelopmentResult>> DevelopAsync(
        string instruction, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        cancellationToken.ThrowIfCancellationRequested();

        var roles = new ModelRoles(_repositoryRoot, _endpointFor);
        var session = new ModelSession(
            roles,
            _charter,
            new ToolGroups(_repositoryRoot).SelectTools(_toolGrantGroups),
            _maxOutputTokens);

        try
        {
            await session.RunAsync(instruction, _role, cancellationToken).ConfigureAwait(false);

            // lastCheckAt[0] tracks the SuccessfulEditCallCount at which the scope check last ran.
            // A single-element array is used so async helpers can mutate the value without ref parameters,
            // which C# does not allow in async methods.
            var lastCheckAt = new[] { 0 };

            // After the initial authoring run, check whether the K-boundary was crossed and the pass drifted.
            var driftResult = await CheckScopeDriftAsync(session, instruction, lastCheckAt, cancellationToken)
                .ConfigureAwait(false);
            if (driftResult is not null)
                return driftResult;

            if (_buildCheck is not null)
            {
                var repaired = await RepairAgainstBuildAsync(session, instruction, lastCheckAt, cancellationToken)
                    .ConfigureAwait(false);
                if (repaired is not null)
                    return repaired;
            }

            var envelope = await session
                .ProbeAsync<DevelopmentEnvelope>(
                    """
                    Report what you completed, per the tool results already shown above in this conversation —
                    those results are the evidence of what happened, not your narrative impression of how the
                    attempt felt. An earlier tool call that failed and was then corrected later in this same
                    conversation is not, by itself, evidence the work is unfinished; self-recovery is the normal,
                    successful path. Reroute is reserved only for "this change belongs to a different worker" — a
                    scope/ownership judgment — never for hedging uncertainty about whether the edit itself finished.
                    """,
                    role: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (session.RefusedProtectedWrites.Count > 0)
                return new StepResult<DevelopmentResult>(
                    OperationOutcome.Escalated,
                    Map(envelope),
                    [new ProcessNote("the correct change needs a protected file, which needs your approval")]);

            // Corroborate the self-reported file list against the real working tree diff. A model
            // may hallucinate files it never actually wrote; the corrected list is what callers
            // receive as the authoritative record of what changed. Fall back to the self-report
            // when git is unavailable so the corroboration is a strengthening check, not a gate.
            var corroboratedFiles = envelope.Kind == DevelopmentOutcomeKind.Completed
                ? await CorroborateFilesAsync(envelope.FilesChanged, cancellationToken).ConfigureAwait(false)
                : envelope.FilesChanged;

            var effectiveEnvelope = corroboratedFiles != envelope.FilesChanged
                ? envelope with { FilesChanged = corroboratedFiles }
                : envelope;

            // Completed or Reroute: both are this primitive successfully answering its own question.
            return new StepResult<DevelopmentResult>(OperationOutcome.Succeeded, Map(effectiveEnvelope), []);
        }
        catch (ModelUnavailableException exception)
        {
            return new StepResult<DevelopmentResult>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
        catch (ModelParseException exception)
        {
            return new StepResult<DevelopmentResult>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
    }

    /// <returns>
    ///     A terminal <see cref="StepResult{TFinding}" /> when the repair budget was spent with the check still
    ///     failing, when scope drift was detected after a repair turn, or null when the check passed and the
    ///     caller should proceed to report completion.
    /// </returns>
    private async Task<StepResult<DevelopmentResult>?> RepairAgainstBuildAsync(
        ModelSession session, string instruction, int[] lastCheckAt, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var check = await _buildCheck!("build check", cancellationToken).ConfigureAwait(false);
            if (check.ExitCode == 0)
                return null;

            if (attempt == _maxRepairAttempts)
                return new StepResult<DevelopmentResult>(
                    OperationOutcome.Failed,
                    null,
                    [new ProcessNote(
                        $"the build/test check still fails after {_maxRepairAttempts} repair attempt(s): {check.Output}")]);

            await session.RunAsync(
                    $"""
                     The build check failed:

                     <output>
                     {check.Output}
                     </output>

                     Repair the issue by editing the files it names.
                     """,
                    _role,
                    cancellationToken)
                .ConfigureAwait(false);

            // Check for scope drift after each repair turn in case the repair went beyond the original scope.
            var driftResult = await CheckScopeDriftAsync(session, instruction, lastCheckAt, cancellationToken)
                .ConfigureAwait(false);
            if (driftResult is not null)
                return driftResult;
        }
    }

    /// <returns>
    ///     A terminal <see cref="StepResult{TFinding}" /> when the scope-drift oracle still detects clear drift
    ///     after a bounded repair turn, or null when the interval was not reached, the work is still aligned,
    ///     or alignment was restored by the repair.
    /// </returns>
    /// <remarks>
    ///     The interval is a delta: the check fires only once per full interval of new successful edit calls,
    ///     not on every call once the threshold is crossed. <paramref name="lastCheckAt" /><c>[0]</c> is updated
    ///     in place whenever the check runs so the next call can compute the correct delta. A single-element
    ///     array is used instead of a <c>ref</c> parameter because C# does not allow <c>ref</c> in async methods.
    ///     On a negative first verdict the worker is given one bounded repair turn — it is told which files look
    ///     unrelated per the diff and is instructed to revert or justify them — then the grounded scope check
    ///     runs a second time. Only a second negative verdict causes failure, so a single self-correction does
    ///     not abort a legitimate run.
    /// </remarks>
    private async Task<StepResult<DevelopmentResult>?> CheckScopeDriftAsync(
        ModelSession session, string instruction, int[] lastCheckAt, CancellationToken cancellationToken)
    {
        if (_scopeDriftCheckInterval == 0 ||
            session.SuccessfulEditCallCount - lastCheckAt[0] < _scopeDriftCheckInterval)
            return null;

        lastCheckAt[0] = session.SuccessfulEditCallCount;

        var (changedFiles, patch) = await ReadDiffAsync(cancellationToken).ConfigureAwait(false);
        var (aligned, reason) = await session
            .CheckScopeAsync(instruction, changedFiles, patch, cancellationToken)
            .ConfigureAwait(false);
        if (aligned)
            return null;

        // First negative verdict: give the worker one repair turn before declaring failure.
        var fileList = changedFiles.Count > 0
            ? string.Join("\n", changedFiles.Select(f => $"- {f}"))
            : "(unavailable)";
        await session.RunAsync(
                $"""
                 A scope-alignment check flagged the following modified files as potentially unrelated to
                 the original instruction:

                 {fileList}

                 Reason given: {reason}

                 If any of those files were touched by mistake, revert them now. If they are genuinely
                 required by the instruction, leave them in place — they will be re-evaluated. Do not
                 touch any file that was not already modified.
                 """,
                _role,
                cancellationToken)
            .ConfigureAwait(false);

        // Re-read the diff after the repair turn so the second verdict sees the corrected working tree.
        (changedFiles, patch) = await ReadDiffAsync(cancellationToken).ConfigureAwait(false);
        var (alignedAfterRepair, reasonAfterRepair) = await session
            .CheckScopeAsync(instruction, changedFiles, patch, cancellationToken)
            .ConfigureAwait(false);
        if (alignedAfterRepair)
            return null;

        var note = string.IsNullOrWhiteSpace(reasonAfterRepair)
            ? "scope drift detected mid-development"
            : reasonAfterRepair;
        return new StepResult<DevelopmentResult>(OperationOutcome.Failed, null, [new ProcessNote(note)]);
    }

    private async Task<(IReadOnlyList<string> ChangedFiles, string Patch)> ReadDiffAsync(
        CancellationToken cancellationToken)
    {
        var finding = await new DiffCheck(_repositoryRoot, runGit: _runGit)
            .TryReadAsync(null, cancellationToken).ConfigureAwait(false);
        if (finding is null)
            return ([], string.Empty);

        var filtered = DiffCheck.ExcludingAnnealBookkeeping(finding);
        return (filtered.ChangedFiles, filtered.Patch);
    }

    /// <returns>
    ///     The subset of <paramref name="selfReported" /> that appears in the actual working-tree diff, or the
    ///     full <paramref name="selfReported" /> list unchanged when git is unavailable — the corroboration is
    ///     strengthening, not a hard gate.
    /// </returns>
    private async Task<IReadOnlyList<string>> CorroborateFilesAsync(
        IReadOnlyList<string> selfReported,
        CancellationToken cancellationToken)
    {
        if (selfReported.Count == 0)
            return selfReported;

        var finding = await new DiffCheck(_repositoryRoot, runGit: _runGit)
            .TryReadAsync(null, cancellationToken).ConfigureAwait(false);

        if (finding is null)
            return selfReported;

        var realFiles = DiffCheck.ExcludingAnnealBookkeeping(finding).ChangedFiles
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var corroborated = selfReported.Where(f => realFiles.Contains(f)).ToList();

        // Return the original reference when nothing was dropped to avoid an unnecessary allocation
        // and to make the no-change path detectable by reference equality in the caller.
        return corroborated.Count == selfReported.Count ? selfReported : corroborated;
    }

    private static DevelopmentResult Map(DevelopmentEnvelope envelope) => envelope.Kind switch
    {
        DevelopmentOutcomeKind.Completed =>
            new DevelopmentResult.Completed(new ChangeSetSummary(envelope.FilesChanged, envelope.Summary)),
        DevelopmentOutcomeKind.Reroute =>
            new DevelopmentResult.Reroute(
                envelope.Why, string.IsNullOrWhiteSpace(envelope.SuggestedWorker) ? null : envelope.SuggestedWorker),
        _ => throw new ArgumentOutOfRangeException(nameof(envelope), envelope.Kind, "Unknown development outcome kind.")
    };
}
