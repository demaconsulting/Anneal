using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Tools;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     Performs a bounded look-around over the repository and reports a structured <see cref="ResearchFinding" />.
/// </summary>
/// <remarks>
///     Composed from the same two-pass shape <see cref="Operations.ProbeRuleOwnerOperation" /> already
///     established: a free-form <see cref="ModelSession.RunAsync" /> pass with read-only tools in scope, so the
///     model reasons over real files rather than a schema it is trying to fill, followed by a schema-last
///     <see cref="ModelSession.ProbeAsync{T}" /> extraction. What is new here is the bound — up to
///     <c>maxTurns</c> rounds of look-around, re-probed after each one, so a caller pays for research iterations
///     only until the finding says it is enough.
///     <para>
///         Read-only by construction: it is granted <see cref="ToolGroups.Read" /> and nothing else, so it cannot
///         itself hit a write boundary. <see cref="OperationOutcome.Escalated" /> is still reachable, defensively,
///         if a future grant ever lets a research pass attempt a protected write — the same signal
///         <see cref="Operations.LintFixOperation" /> already reads off <see cref="ModelSession.RefusedProtectedWrites" />.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but each call opens its own conversation.
///     </para>
/// </remarks>
internal sealed class Research
{
    private readonly string _repositoryRoot;
    private readonly string _charter;
    private readonly ModelRole _role;
    private readonly int _maxTurns;
    private readonly int _evidenceBudget;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;

    /// <summary>
    ///     Binds a research pass to a repository and the charter its look-around carries.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository read, outside which every tool call is refused, and whose configuration names the
    ///     models behind the capability roles. Must not be null or blank.
    /// </param>
    /// <param name="charter">
    ///     The system message every turn carries: what the model is looking for, and that refusing to conclude is
    ///     a correct answer. Must not be null.
    /// </param>
    /// <param name="role">The capability tier this pass runs at. Defaults to <see cref="ModelRole.Medium" />.</param>
    /// <param name="maxTurns">
    ///     The most look-around iterations spent before the finding is reported as-is, whatever it says. Must be
    ///     greater than zero; defaults to 3.
    /// </param>
    /// <param name="evidenceBudget">
    ///     The most evidence references a finding may cite before the excess is dropped, so a caller downstream is
    ///     never handed more than it asked to be shown. Must be greater than zero; defaults to 5.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this primitive's whole behavior is exercisable without a network call.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="charter" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxTurns" /> or <paramref name="evidenceBudget" /> is not greater than zero.
    /// </exception>
    public Research(
        string repositoryRoot,
        string charter,
        ModelRole role = ModelRole.Medium,
        int maxTurns = 3,
        int evidenceBudget = 5,
        Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(charter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTurns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(evidenceBudget);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _charter = charter;
        _role = role;
        _maxTurns = maxTurns;
        _evidenceBudget = evidenceBudget;
        _endpointFor = endpointFor;
    }

    /// <summary>
    ///     Investigates a question and reports what was found.
    /// </summary>
    /// <param name="question">The research question. Must not be null or blank.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> with the finding when it reports itself sufficient;
    ///     <see cref="OperationOutcome.Refused" /> with the finding when the budget was spent and it still cannot
    ///     answer honestly; <see cref="OperationOutcome.Escalated" /> when the pass hit a protected boundary only a
    ///     person can resolve; <see cref="OperationOutcome.Failed" /> with no finding when no model could be
    ///     reached or no reply decoded.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="question" /> is null, empty or blank.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<ResearchFinding>> InvestigateAsync(
        string question, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        cancellationToken.ThrowIfCancellationRequested();

        var roles = new ModelRoles(_repositoryRoot, _endpointFor);
        var session = new ModelSession(
            roles, _charter, new ToolGroups(_repositoryRoot).SelectTools([ToolGroups.Read]));

        try
        {
            ResearchFinding finding = new()
            {
                Question = question,
                Answer = string.Empty,
                EvidenceRefs = [],
                Implications = string.Empty,
                SufficientForNextDecision = false
            };

            for (var turn = 0; turn < _maxTurns; turn++)
            {
                await session.RunAsync(
                        turn == 0
                            ? $"""
                               Research question: {question}

                               Investigate using your read-only tools and report what you find, naming the files
                               you consulted.
                               """
                            : "That was not yet enough to answer honestly. Continue investigating.",
                        _role,
                        cancellationToken)
                    .ConfigureAwait(false);

                finding = await session
                    .ProbeAsync<ResearchFinding>(
                        "Report your research finding for the question above.", role: null, cancellationToken)
                    .ConfigureAwait(false);

                if (finding.SufficientForNextDecision)
                    break;
            }

            finding = Trim(finding, _evidenceBudget);

            if (session.RefusedProtectedWrites.Count > 0)
                return new StepResult<ResearchFinding>(
                    OperationOutcome.Escalated,
                    finding,
                    [new ProcessNote("research reached a protected boundary only a person can resolve")]);

            return finding.SufficientForNextDecision
                ? new StepResult<ResearchFinding>(OperationOutcome.Succeeded, finding, [])
                : new StepResult<ResearchFinding>(
                    OperationOutcome.Refused,
                    finding,
                    [new ProcessNote($"could not answer honestly within {_maxTurns} research iteration(s)")]);
        }
        catch (ModelUnavailableException exception)
        {
            return new StepResult<ResearchFinding>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
        catch (ModelParseException exception)
        {
            return new StepResult<ResearchFinding>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
    }

    private static ResearchFinding Trim(ResearchFinding finding, int evidenceBudget) =>
        finding.EvidenceRefs.Count <= evidenceBudget
            ? finding
            : finding with { EvidenceRefs = [.. finding.EvidenceRefs.Take(evidenceBudget)] };
}
