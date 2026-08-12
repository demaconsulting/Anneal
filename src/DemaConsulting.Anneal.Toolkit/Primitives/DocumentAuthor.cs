using System.ComponentModel;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Tools;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     Authors documentation against a declared scope, and reports either the change it made or why the work
///     belongs to a different owner.
/// </summary>
/// <remarks>
///     Composed the same way <see cref="Operations.LintFixOperation" /> composes a writing worker: a
///     <see cref="ModelSession.RunAsync" /> pass with read-and-edit tools granted, followed by a schema-last
///     <see cref="ModelSession.ProbeAsync{T}" /> extraction of what happened. There is no repair loop inside this
///     primitive — a caller that wants one composes it from <see cref="RepairLoop{TState}" />, sending a
///     verifier's finding back through another call to this same author rather than restarting from the top.
///     <para>
///         A protected-write refusal escalates regardless of what the probe decoded, on the same reasoning
///         <see cref="Operations.LintFixOperation" /> already uses: a refusal is a recorded fact about the run, not
///         a claim the model gets to characterize.
///     </para>
///     <para>
///         When the authored change's file count exceeds <c>targetFileCountBudget</c>, an extra cheap
///         Light-role oracle probe judges whether the touched file list is proportionate to and traceable from
///         the original instruction, or looks like scope drift. Failure only follows when that oracle judges
///         the file list disproportionate; a proportionate over-budget change succeeds exactly as an in-budget
///         change does. Changes within the budget are never probed and pay no extra model call.
///     </para>
///     <para>
///         Every <c>scopeDriftCheckInterval</c> successful edit-tool calls, a cheap Light-role scope-drift probe
///         is run mid-authoring to verify the pass is still working within the original instruction's scope.
///         When the oracle detects clear drift the run is aborted and reported as <see cref="OperationOutcome.Failed" />;
///         when the oracle lacks sufficient evidence to judge, execution continues. This is a post-turn check —
///         it fires after each <see cref="ModelSession.RunAsync" /> turn that crossed the K-boundary — so it can
///         catch a pass that has started heading in the wrong direction before the probe extraction turn.
///     </para>
///     <para>
///         After the authoring pass, the self-reported file list is corroborated against a real
///         <c>git diff HEAD</c> snapshot: any file the model claims to have changed that has no real diff entry
///         is dropped before the proportionality oracle sees the list. When git is unavailable the self-reported
///         list is used unchanged — the corroboration is a strengthening check, not a hard dependency.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
internal sealed class DocumentAuthor
{
    /// <remarks>
    ///     The charter keeps the oracle focused on proportionality alone: the question is not whether the
    ///     change is correct, only whether the file list is traceable from the instruction.
    /// </remarks>
    private const string ProportionalityOracleCharter =
        """
        You are a proportionality judge. You are given an authoring instruction and the list of files that
        a documentation pass reports having changed. Your only job is to decide whether the file list is
        proportionate to and traceable from that instruction, or whether it looks like scope drift.
        Proportionate means: every file in the list can be explained as a direct, necessary consequence of
        the instruction — a system contract, a cross-reference in a related or parent document, the governing
        standard that the instruction explicitly concerns. Scope drift means files were touched that have no
        visible connection to the instruction. Answer with HasSufficientEvidence = true and Proportionate =
        true when the list is proportionate; HasSufficientEvidence = true and Proportionate = false when it
        looks like drift, surfacing your reasoning in the Why field so the failure note is useful.
        """;

    private readonly string _repositoryRoot;
    private readonly string _charter;
    private readonly ModelRole _role;
    private readonly int _targetFileCountBudget;
    private readonly int _scopeDriftCheckInterval;
    private readonly int _maxOutputTokens;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunGitCommand? _runGit;

    /// <summary>
    ///     Binds a documentation author to a repository and the charter its authoring pass carries.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository authored into, outside which every tool call is refused, and whose configuration names
    ///     the models behind the capability roles. Must not be null or blank.
    /// </param>
    /// <param name="charter">
    ///     The system message the pass carries: what it may author, what it must leave alone, and that naming the
    ///     wrong worker is a correct answer. Must not be null.
    /// </param>
    /// <param name="role">The capability tier the pass runs at. Defaults to <see cref="ModelRole.Heavy" />.</param>
    /// <param name="targetFileCountBudget">
    ///     The most files an authored change may touch before a Light-role proportionality oracle is consulted to
    ///     decide whether the excess is justified by the instruction or looks like scope drift. Must be greater
    ///     than zero; defaults to 3.
    /// </param>
    /// <param name="scopeDriftCheckInterval">
    ///     After every this-many successful edit-tool calls, a cheap Light-role scope-drift probe runs to confirm
    ///     the pass is still working within the original instruction's scope. Zero disables the periodic check.
    ///     Defaults to 5.
    /// </param>
    /// <param name="maxOutputTokens">
    ///     The ceiling on generated output for every turn. Defaults to <see cref="ModelSession.DefaultMaxOutputTokens" />.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this primitive's whole behavior is exercisable without a network call.
    /// </param>
    /// <param name="runGit">
    ///     Runs one <c>git</c> invocation for the post-authoring corroboration diff, or null to use the real
    ///     <c>git</c> executable. Injected so the corroboration check is exercisable without a real repository.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="charter" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="targetFileCountBudget" /> is not greater than zero, or
    ///     <paramref name="scopeDriftCheckInterval" /> is negative.
    /// </exception>
    public DocumentAuthor(
        string repositoryRoot,
        string charter,
        ModelRole role = ModelRole.Heavy,
        int targetFileCountBudget = 3,
        int scopeDriftCheckInterval = 5,
        int maxOutputTokens = ModelSession.DefaultMaxOutputTokens,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunGitCommand? runGit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(charter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetFileCountBudget);
        ArgumentOutOfRangeException.ThrowIfNegative(scopeDriftCheckInterval);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _charter = charter;
        _role = role;
        _targetFileCountBudget = targetFileCountBudget;
        _scopeDriftCheckInterval = scopeDriftCheckInterval;
        _maxOutputTokens = maxOutputTokens;
        _endpointFor = endpointFor;
        _runGit = runGit;
    }

    /// <summary>
    ///     Authors documentation against an instruction, and reports what happened.
    /// </summary>
    /// <param name="instruction">What to author, stated as a caller would state it. Must not be null or blank.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Escalated" /> with the decoded result when a repair needed a protected
    ///     path; <see cref="OperationOutcome.Succeeded" /> with the decoded result when a change was authored or
    ///     the pass named a better owner — both are this primitive successfully answering its own question, per
    ///     <c>.anneal/architecture/toolkit.md</c> § Decisions; <see cref="OperationOutcome.Failed" /> with no finding
    ///     when no model could be reached, when the periodic scope-drift check detected clear scope drift, or when
    ///     the authored change's file count exceeded the budget and the Light-role proportionality oracle judged it
    ///     disproportionate — in that case the oracle's stated reasoning is the failure note;
    ///     <see cref="OperationOutcome.Refused" /> is reserved for the rarer case where ownership cannot be
    ///     determined honestly enough to answer at all — see the remarks on <see cref="DocumentAuthoringResult" />
    ///     for why that path is currently unreachable.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="instruction" /> is null, empty or blank.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<DocumentAuthoringResult>> AuthorAsync(
        string instruction, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        cancellationToken.ThrowIfCancellationRequested();

        var roles = new ModelRoles(_repositoryRoot, _endpointFor);
        var session = new ModelSession(
            roles,
            _charter,
            new ToolGroups(_repositoryRoot).SelectTools([ToolGroups.Read, ToolGroups.Edit]),
            _maxOutputTokens);

        try
        {
            await session.RunAsync(instruction, _role, cancellationToken).ConfigureAwait(false);

            // lastCheckAt[0] tracks the SuccessfulEditCallCount at which the scope check last ran.
            // A single-element array is used so async helpers can mutate the value without ref parameters,
            // which C# does not allow in async methods.
            var lastCheckAt = new[] { 0 };

            // After the run, check whether the K-boundary was crossed and the pass looks like it drifted.
            var driftResult = await CheckScopeDriftAsync(session, instruction, lastCheckAt, cancellationToken)
                .ConfigureAwait(false);
            if (driftResult is not null)
                return driftResult;

            var envelope = await session
                .ProbeAsync<DocumentAuthoringEnvelope>(
                    """
                    Report what you authored, per the tool results already shown above in this conversation —
                    those results are the evidence of what happened, not your narrative impression of how the
                    attempt felt. An earlier tool call that failed and was then corrected later in this same
                    conversation is not, by itself, evidence the work is unfinished; self-recovery is the normal,
                    successful path. Reroute is reserved only for "this change belongs to a different worker" — a
                    scope/ownership judgment — never for hedging uncertainty about whether the authoring itself
                    finished.
                    """,
                    role: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (session.RefusedProtectedWrites.Count > 0)
                return new StepResult<DocumentAuthoringResult>(
                    OperationOutcome.Escalated,
                    Map(envelope),
                    [new ProcessNote("the correct change needs a protected file, which needs your approval")]);

            // Corroborate the self-reported file list against the real working tree diff. A model
            // may hallucinate files it never actually wrote; feeding a fabricated list to the
            // proportionality oracle causes false failures. Drop any file the diff shows no entry
            // for; fall back to the full self-report when git is unavailable so the corroboration
            // is a strengthening check rather than a new hard dependency.
            var corroboratedFiles = envelope.Kind == DocumentAuthoringOutcomeKind.Authored
                ? await CorroborateFilesAsync(envelope.FilesChanged, cancellationToken).ConfigureAwait(false)
                : envelope.FilesChanged;

            var effectiveEnvelope = corroboratedFiles != envelope.FilesChanged
                ? envelope with { FilesChanged = corroboratedFiles }
                : envelope;

            if (effectiveEnvelope.Kind == DocumentAuthoringOutcomeKind.Authored &&
                effectiveEnvelope.FilesChanged.Count > _targetFileCountBudget)
            {
                // Over budget: ask a cheap Light-role oracle whether the file list is proportionate to the
                // instruction before deciding to fail. A correctly-scoped change that legitimately needs more
                // files (e.g. a contract doc, cross-references in related docs, the governing standard) should
                // not be rejected by a fixed ceiling; the ceiling's only job is to gate whether the extra model
                // call is worth making.
                var oracle = new Oracle<FileScopeJudgement>(
                    _repositoryRoot,
                    ProportionalityOracleCharter,
                    ModelRole.Light,
                    endpointFor: _endpointFor);

                var fileList = string.Join("\n", effectiveEnvelope.FilesChanged.Select(f => $"- {f}"));
                var question =
                    $"""
                     Instruction given to the documentation pass:
                     {instruction}

                     Files the pass reports having changed ({effectiveEnvelope.FilesChanged.Count}, over the {_targetFileCountBudget}-file budget):
                     {fileList}

                     Is this file list proportionate to and traceable from the instruction, or does it look like scope drift?
                     """;

                var judgement = await oracle.AskAsync(question, [], cancellationToken).ConfigureAwait(false);

                if (judgement.Outcome != OperationOutcome.Succeeded || judgement.Finding?.Proportionate != true)
                {
                    var reason = judgement.Finding?.Why;
                    var note = string.IsNullOrWhiteSpace(reason)
                        ? $"touched {effectiveEnvelope.FilesChanged.Count} files, over the {_targetFileCountBudget}-file budget, and the proportionality oracle judged the file list disproportionate"
                        : reason;
                    return new StepResult<DocumentAuthoringResult>(OperationOutcome.Failed, null, [new ProcessNote(note)]);
                }
            }

            // Authored or Reroute: both are this primitive successfully answering its own question.
            return new StepResult<DocumentAuthoringResult>(OperationOutcome.Succeeded, Map(effectiveEnvelope), []);
        }
        catch (ModelUnavailableException exception)
        {
            return new StepResult<DocumentAuthoringResult>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
        catch (ModelParseException exception)
        {
            return new StepResult<DocumentAuthoringResult>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
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
    ///     runs a second time. Only a second negative verdict causes failure.
    /// </remarks>
    private async Task<StepResult<DocumentAuthoringResult>?> CheckScopeDriftAsync(
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
            ? "scope drift detected mid-authoring"
            : reasonAfterRepair;
        return new StepResult<DocumentAuthoringResult>(OperationOutcome.Failed, null, [new ProcessNote(note)]);
    }

    private async Task<(IReadOnlyList<string> ChangedFiles, string Patch)> ReadDiffAsync(
        CancellationToken cancellationToken)
    {
        var finding = await new DiffCheck(_repositoryRoot, runGit: _runGit)
            .TryReadAsync(null, cancellationToken).ConfigureAwait(false);
        return finding is null ? ([], string.Empty) : (finding.ChangedFiles, finding.Patch);
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

        var realFiles = finding.ChangedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var corroborated = selfReported.Where(f => realFiles.Contains(f)).ToList();

        // Return the original reference when nothing was dropped to avoid an unnecessary allocation
        // and to make the no-change path detectable by reference equality in the caller.
        return corroborated.Count == selfReported.Count ? selfReported : corroborated;
    }

    private static DocumentAuthoringResult Map(DocumentAuthoringEnvelope envelope) => envelope.Kind switch
    {
        DocumentAuthoringOutcomeKind.Authored =>
            new DocumentAuthoringResult.Authored(new DocumentChangeSet(envelope.FilesChanged, envelope.Summary)),
        DocumentAuthoringOutcomeKind.Reroute =>
            new DocumentAuthoringResult.Reroute(envelope.Why),
        _ => throw new ArgumentOutOfRangeException(nameof(envelope), envelope.Kind, "Unknown authoring outcome kind.")
    };

    /// <summary>
    ///     The typed decision the proportionality oracle answers with when a change exceeds the file-count budget.
    /// </summary>
    private sealed record FileScopeJudgement : IOracleDecision
    {
        [Description("true when the file list is proportionate to and traceable from the instruction")]
        public required bool Proportionate { get; init; }

        [Description("the oracle's reasoning when the file list looks like scope drift; empty when proportionate")]
        public required string Why { get; init; }

        [Description("true when the instruction and file list provided enough information to judge proportionality honestly")]
        public required bool HasSufficientEvidence { get; init; }
    }
}
