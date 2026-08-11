using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     Produces a single-shot <see cref="PlanningDecision" /> for a worker that asked whether the work in front of
///     it needs a plan.
/// </summary>
/// <remarks>
///     <b>Single-shot only.</b> This type asks exactly one probe question and returns exactly one decision; it
///     never loops, never re-asks on a <see cref="PlanningDecision.Reroute" /> or
///     <see cref="PlanningDecision.DirectExecutionIsBetter" /> answer, and holds no state between calls. That is
///     deliberate and is the whole of what a planner is allowed to cost — see
///     <c>.anneal/architecture/process.md</c> § Decisions, "Bounded repairs, no planning phase" and "The compiled
///     catalog is a Router choosing a bounded worker": a universal plan-build-review loop was rejected once
///     already, and a planner that re-plans on its own answer reintroduces exactly that multiplier under a
///     different name.
///     <para>
///         <c>scopeDriftCheckInterval</c> is accepted for API consistency with <see cref="Developer" /> and
///         <see cref="DocumentAuthor" /> but has no effect here: a planner issues no edit-tool calls and
///         therefore never crosses the K-boundary that triggers the check.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but each call opens its own conversation.
///     </para>
/// </remarks>
internal sealed class Planner
{
    private readonly string _repositoryRoot;
    private readonly string _charter;
    private readonly bool _enabled;
    private readonly int _maxPlanSteps;
    private readonly ModelRole _role;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;

    /// <summary>
    ///     Binds a planner to a repository and the charter its single question carries.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository whose configuration names the models behind the capability roles. Must not be null or
    ///     blank.
    /// </param>
    /// <param name="charter">
    ///     The system message the question carries: what "the work" is, and that a plan, a preference for direct
    ///     execution, or a reroute are all correct answers. Must not be null.
    /// </param>
    /// <param name="enabled">
    ///     Whether this planner may be asked at all. A worker that never wants a plan sets this false rather than
    ///     never constructing a <see cref="Planner" />, so the "enabled" control knob is visible at the call site
    ///     that composes a worker together, not buried in whether an object exists.
    /// </param>
    /// <param name="maxPlanSteps">
    ///     The most steps a <see cref="PlanningDecision.Plan" /> may contain before it is treated as having failed
    ///     to stay narrow. Must be greater than zero; defaults to 8.
    /// </param>
    /// <param name="scopeDriftCheckInterval">
    ///     Accepted for API consistency with <see cref="Developer" /> and <see cref="DocumentAuthor" />. A
    ///     planner issues no edit-tool calls, so this value has no effect regardless of what is passed. Must be
    ///     zero or greater; defaults to 5.
    /// </param>
    /// <param name="role">The capability tier the single question is served at. Defaults to <see cref="ModelRole.Medium" />.</param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this primitive's whole behavior is exercisable without a network call.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="charter" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxPlanSteps" /> is not greater than zero, or
    ///     <paramref name="scopeDriftCheckInterval" /> is negative.
    /// </exception>
    public Planner(
        string repositoryRoot,
        string charter,
        bool enabled = true,
        int maxPlanSteps = 8,
        int scopeDriftCheckInterval = 5,
        ModelRole role = ModelRole.Medium,
        Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(charter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPlanSteps);
        ArgumentOutOfRangeException.ThrowIfNegative(scopeDriftCheckInterval);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _charter = charter;
        _enabled = enabled;
        _maxPlanSteps = maxPlanSteps;
        _role = role;
        _endpointFor = endpointFor;
    }

    /// <summary>
    ///     Asks, once, whether the described work needs a plan.
    /// </summary>
    /// <param name="question">The work to judge, stated as a worker would state it. Must not be null or blank.</param>
    /// <param name="contextArtifacts">
    ///     The authoritative material the model may rely on. Must not be null; may be empty.
    /// </param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Refused" /> with no finding when this planner is disabled, because nothing
    ///     was asked; <see cref="OperationOutcome.Succeeded" /> with the decoded decision when a plan, a
    ///     preference for direct execution, or a reroute was reached; <see cref="OperationOutcome.Refused" /> with
    ///     no finding when a decoded plan exceeded <c>maxPlanSteps</c>, because the answer given was not one this
    ///     planner may hand back; <see cref="OperationOutcome.Failed" /> with no finding when no model could be
    ///     reached or no reply decoded.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="question" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contextArtifacts" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<PlanningDecision>> PlanAsync(
        string question, IReadOnlyList<string> contextArtifacts, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(contextArtifacts);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_enabled)
            return new StepResult<PlanningDecision>(
                OperationOutcome.Refused, null, [new ProcessNote("this planner is disabled")]);

        var roles = new ModelRoles(_repositoryRoot, _endpointFor);
        var session = new ModelSession(roles, _charter, tools: null);

        try
        {
            var composed = contextArtifacts.Count == 0
                ? question
                : $"""
                   {question}

                   <context>
                   {string.Join("\n---\n", contextArtifacts)}
                   </context>
                   """;

            var envelope = await session
                .ProbeAsync<PlanningDecisionEnvelope>(composed, _role, cancellationToken)
                .ConfigureAwait(false);

            if (envelope.Kind == PlanningDecisionKind.Plan && envelope.PlanSteps.Count > _maxPlanSteps)
                return new StepResult<PlanningDecision>(
                    OperationOutcome.Refused,
                    null,
                    [new ProcessNote($"the plan had {envelope.PlanSteps.Count} steps, over the {_maxPlanSteps}-step budget")]);

            PlanningDecision decision = envelope.Kind switch
            {
                PlanningDecisionKind.Plan =>
                    new PlanningDecision.Plan(new ImplementationPlan(envelope.PlanSummary, envelope.PlanSteps)),
                PlanningDecisionKind.DirectExecutionIsBetter =>
                    new PlanningDecision.DirectExecutionIsBetter(envelope.Why),
                PlanningDecisionKind.Reroute =>
                    new PlanningDecision.Reroute(envelope.Why),
                _ => throw new ArgumentOutOfRangeException(nameof(envelope), envelope.Kind, "Unknown planning decision kind.")
            };

            return new StepResult<PlanningDecision>(OperationOutcome.Succeeded, decision, []);
        }
        catch (ModelUnavailableException exception)
        {
            return new StepResult<PlanningDecision>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
        catch (ModelParseException exception)
        {
            return new StepResult<PlanningDecision>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
    }
}
