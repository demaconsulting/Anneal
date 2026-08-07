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
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
internal sealed class DocumentAuthor
{
    private readonly string _repositoryRoot;
    private readonly string _charter;
    private readonly ModelRole _role;
    private readonly int _targetFileCountBudget;
    private readonly int _maxOutputTokens;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;

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
    ///     The most files an authored change may touch before it is treated as having grown past this primitive's
    ///     bound. Must be greater than zero; defaults to 3.
    /// </param>
    /// <param name="maxOutputTokens">
    ///     The ceiling on generated output for every turn. Defaults to <see cref="ModelSession.DefaultMaxOutputTokens" />.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this primitive's whole behavior is exercisable without a network call.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="charter" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="targetFileCountBudget" /> is not greater than zero.</exception>
    public DocumentAuthor(
        string repositoryRoot,
        string charter,
        ModelRole role = ModelRole.Heavy,
        int targetFileCountBudget = 3,
        int maxOutputTokens = ModelSession.DefaultMaxOutputTokens,
        Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(charter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetFileCountBudget);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _charter = charter;
        _role = role;
        _targetFileCountBudget = targetFileCountBudget;
        _maxOutputTokens = maxOutputTokens;
        _endpointFor = endpointFor;
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
    ///     <c>docs/architecture/toolkit.md</c> § Decisions; <see cref="OperationOutcome.Failed" /> with no finding
    ///     when the authored change exceeded the file-count budget or no model could be reached;
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

            var envelope = await session
                .ProbeAsync<DocumentAuthoringEnvelope>(
                    "Report what you authored, or why this change belongs to a different worker.",
                    role: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (session.RefusedProtectedWrites.Count > 0)
                return new StepResult<DocumentAuthoringResult>(
                    OperationOutcome.Escalated,
                    Map(envelope),
                    [new ProcessNote("the correct change needs a protected file, which needs your approval")]);

            if (envelope.Kind == DocumentAuthoringOutcomeKind.Authored &&
                envelope.FilesChanged.Count > _targetFileCountBudget)
                return new StepResult<DocumentAuthoringResult>(
                    OperationOutcome.Failed,
                    null,
                    [new ProcessNote(
                        $"touched {envelope.FilesChanged.Count} files, over the {_targetFileCountBudget}-file budget")]);

            // Authored or Reroute: both are this primitive successfully answering its own question.
            return new StepResult<DocumentAuthoringResult>(OperationOutcome.Succeeded, Map(envelope), []);
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

    private static DocumentAuthoringResult Map(DocumentAuthoringEnvelope envelope) => envelope.Kind switch
    {
        DocumentAuthoringOutcomeKind.Authored =>
            new DocumentAuthoringResult.Authored(new DocumentChangeSet(envelope.FilesChanged, envelope.Summary)),
        DocumentAuthoringOutcomeKind.Reroute =>
            new DocumentAuthoringResult.Reroute(envelope.Why),
        _ => throw new ArgumentOutOfRangeException(nameof(envelope), envelope.Kind, "Unknown authoring outcome kind.")
    };
}
