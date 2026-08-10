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
    private readonly int _maxOutputTokens;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunRepositoryScript? _buildCheck;

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
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="charter" /> or <paramref name="toolGrantGroups" /> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxRepairAttempts" /> is negative.</exception>
    public Developer(
        string repositoryRoot,
        string charter,
        IReadOnlyList<string>? toolGrantGroups = null,
        ModelRole role = ModelRole.Heavy,
        int maxRepairAttempts = 3,
        int maxOutputTokens = ModelSession.DefaultMaxOutputTokens,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? buildCheck = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(charter);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRepairAttempts);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _charter = charter;
        _toolGrantGroups = toolGrantGroups ?? [ToolGroups.Read, ToolGroups.Edit];
        _role = role;
        _maxRepairAttempts = maxRepairAttempts;
        _maxOutputTokens = maxOutputTokens;
        _endpointFor = endpointFor;
        _buildCheck = buildCheck;
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
    ///     check still failing, when the check itself faulted, or when no model could be reached;
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

            if (_buildCheck is not null)
            {
                var repaired = await RepairAgainstBuildAsync(session, cancellationToken).ConfigureAwait(false);
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

            // Completed or Reroute: both are this primitive successfully answering its own question.
            return new StepResult<DevelopmentResult>(OperationOutcome.Succeeded, Map(envelope), []);
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
    ///     failing, or null when the check passed and the caller should proceed to report completion.
    /// </returns>
    private async Task<StepResult<DevelopmentResult>?> RepairAgainstBuildAsync(
        ModelSession session, CancellationToken cancellationToken)
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
        }
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
