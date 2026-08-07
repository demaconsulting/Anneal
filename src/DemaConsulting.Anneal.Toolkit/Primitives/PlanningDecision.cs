using System.ComponentModel;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     What an <see cref="ImplementationPlan" /> compiles down to: an ordered list of steps a caller executes in
///     turn.
/// </summary>
/// <remarks>
///     Flat by design, matching <see cref="Model.SchemaDescriber" />'s own shallow-by-design shape: a plan is a
///     sequence a worker walks, not a tree it interprets. A step needing its own sub-plan is a sign the work was
///     not narrow enough for a single-shot <see cref="Planner" /> to have planned honestly.
/// </remarks>
/// <param name="Summary">What the plan achieves, in one or two sentences.</param>
/// <param name="Steps">The ordered steps, each one a caller can execute without further planning.</param>
internal sealed record ImplementationPlan(string Summary, IReadOnlyList<string> Steps);

/// <summary>
///     What a single-shot planning pass concluded: a plan to follow, a judgement that planning would cost more
///     than it saves, or a judgement that the work belongs to a different worker entirely.
/// </summary>
/// <remarks>
///     A closed union of exactly three cases, decoded from one probe reply and never re-asked — see
///     <see cref="Planner" /> for why single-shot is load-bearing here. Reusing <see cref="OperationOutcome" />
///     rather than growing a fourth case: whichever of these three a planning pass reaches, the pass itself
///     succeeded at answering its own question, which is <see cref="OperationOutcome.Succeeded" /> carrying this
///     type as its finding, per <c>docs/architecture/toolkit.md</c> § Decisions.
/// </remarks>
internal abstract record PlanningDecision
{
    private PlanningDecision()
    {
    }

    /// <summary>A plan was reached and should be followed.</summary>
    /// <param name="Steps">The plan to follow.</param>
    internal sealed record Plan(ImplementationPlan Steps) : PlanningDecision;

    /// <summary>Planning would cost more than the work it plans, and direct execution is the better route.</summary>
    /// <param name="Why">Why direct execution was judged better, in a sentence a person can check.</param>
    internal sealed record DirectExecutionIsBetter(string Why) : PlanningDecision;

    /// <summary>The work does not belong to the worker that asked for a plan.</summary>
    /// <param name="Why">Why this worker is the wrong one, in a sentence a person can check.</param>
    internal sealed record Reroute(string Why) : PlanningDecision;
}

/// <summary>What a single-shot planning probe decoded, before it is mapped onto <see cref="PlanningDecision" />.</summary>
/// <remarks>
///     A flat envelope rather than a discriminated union, matching the same house pattern
///     <see cref="Operations.RuleOwnerAnswer" /> already uses: <see cref="Model.SchemaDescriber" /> describes a
///     type shallowly, so a union is decoded as one kind field plus the union's payload fields, each left empty
///     when the decoded kind does not use it, and then mapped onto the real type afterward.
/// </remarks>
internal sealed record PlanningDecisionEnvelope
{
    /// <summary>Which case of <see cref="PlanningDecision" /> this reply reaches.</summary>
    public required PlanningDecisionKind Kind { get; init; }

    /// <summary>
    ///     Why, when <see cref="Kind" /> is <see cref="PlanningDecisionKind.DirectExecutionIsBetter" /> or
    ///     <see cref="PlanningDecisionKind.Reroute" />; the empty string otherwise.
    /// </summary>
    public required string Why { get; init; }

    /// <summary>
    ///     What the plan achieves, when <see cref="Kind" /> is <see cref="PlanningDecisionKind.Plan" />; the empty
    ///     string otherwise.
    /// </summary>
    public required string PlanSummary { get; init; }

    /// <summary>
    ///     The ordered plan steps, when <see cref="Kind" /> is <see cref="PlanningDecisionKind.Plan" />; empty
    ///     otherwise.
    /// </summary>
    public required IReadOnlyList<string> PlanSteps { get; init; }
}

/// <summary>The closed vocabulary a <see cref="PlanningDecisionEnvelope" /> decodes its kind as.</summary>
internal enum PlanningDecisionKind
{
    /// <summary>A plan was reached.</summary>
    [Description("a plan was reached and should be followed")]
    Plan,

    /// <summary>Direct execution is the better route than planning.</summary>
    [Description("planning would cost more than the work it plans; direct execution is better")]
    DirectExecutionIsBetter,

    /// <summary>The work belongs to a different worker.</summary>
    [Description("the work does not belong to the worker that asked for a plan")]
    Reroute
}
