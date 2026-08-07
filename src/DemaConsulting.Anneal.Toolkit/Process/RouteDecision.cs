using System.ComponentModel;
using DemaConsulting.Anneal.Toolkit.Primitives;

namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>How broad a bounded research pass a <see cref="RouteDecision.NeedResearch" /> is asking for.</summary>
internal enum RouteResearchScope
{
    /// <summary>A narrow, targeted look-around answering one specific question.</summary>
    [Description("a narrow, targeted look-around")]
    Narrow,

    /// <summary>A broader look-around across more of the repository.</summary>
    [Description("a broader look-around across more of the repository")]
    Broad
}

/// <summary>
///     What a bounded route-oracle pass concluded: a worker to run, a research question to answer first, or that
///     no route exists for this work.
/// </summary>
/// <remarks>
///     A closed union of three cases, all reached by the route oracle successfully answering its own question —
///     see <see cref="Router" /> for how each case is composed, and <c>docs/architecture/process.md</c> § Decisions
///     ("the router asks one narrow typed question per pass … against two independent counters").
/// </remarks>
internal abstract record RouteDecision
{
    private RouteDecision()
    {
    }

    /// <summary>A worker was selected to run this work.</summary>
    /// <param name="WorkerKey">The catalog key of the selected worker.</param>
    /// <param name="Why">Why this worker is the right one, in a sentence a person can check.</param>
    internal sealed record SelectWorker(string WorkerKey, string Why) : RouteDecision;

    /// <summary>The router lacks the facts to route honestly and needs a bounded look-around first.</summary>
    /// <param name="Question">The research question to answer.</param>
    /// <param name="Scope">How broad the look-around should be.</param>
    /// <param name="Why">Why routing cannot proceed without this research.</param>
    internal sealed record NeedResearch(string Question, RouteResearchScope Scope, string Why) : RouteDecision;

    /// <summary>No route exists for this work, whatever research or worker catalog is available.</summary>
    /// <param name="Why">Why no route exists, in a sentence a person can check.</param>
    /// <param name="HumanOnlyNextStep">
    ///     The specific step only a person can take, when one is known — for example "this is a Migration
    ///     proposal" or "needs interactive architecture-design" — or null when none is known.
    /// </param>
    internal sealed record NoRoute(string Why, string? HumanOnlyNextStep) : RouteDecision;
}

/// <summary>The closed vocabulary a <see cref="RouteDecisionEnvelope" /> decodes its kind as.</summary>
internal enum RouteDecisionKind
{
    /// <summary>A worker was selected.</summary>
    [Description("a worker was selected to run this work")]
    SelectWorker,

    /// <summary>Research is needed before routing can proceed.</summary>
    [Description("research is needed before routing can proceed")]
    NeedResearch,

    /// <summary>No route exists for this work.</summary>
    [Description("no route exists for this work")]
    NoRoute
}

/// <summary>
///     What a route-oracle probe decoded, before it is mapped onto <see cref="RouteDecision" />.
/// </summary>
/// <remarks>
///     A flat envelope rather than a discriminated union, matching the same house pattern
///     <see cref="Primitives.PlanningDecisionEnvelope" /> and <see cref="Primitives.DevelopmentEnvelope" /> already
///     use: <see cref="Model.SchemaDescriber" /> describes a type shallowly, so a union is decoded as one kind
///     field plus the union's payload fields, each left empty when the decoded kind does not use it, and then
///     mapped onto the real type afterward.
///     <para>
///         Implements <see cref="IOracleDecision" /> directly, unlike the other primitives' envelopes, because
///         <see cref="Router" /> composes its route question through <see cref="Oracle{TDecision}" /> rather than
///         building its own <see cref="Model.ModelSession" /> — the Router is a bounded loop over a single narrow
///         typed question, exactly what <see cref="Oracle{TDecision}" /> already is.
///     </para>
/// </remarks>
internal sealed record RouteDecisionEnvelope : IOracleDecision
{
    /// <summary>Which case of <see cref="RouteDecision" /> this reply reaches.</summary>
    public required RouteDecisionKind Kind { get; init; }

    /// <summary>Why, whatever <see cref="Kind" /> is reached. Never empty.</summary>
    public required string Why { get; init; }

    /// <summary>The selected worker's catalog key, when <see cref="Kind" /> is <see cref="RouteDecisionKind.SelectWorker" />; empty otherwise.</summary>
    public required string WorkerKey { get; init; }

    /// <summary>The research question, when <see cref="Kind" /> is <see cref="RouteDecisionKind.NeedResearch" />; empty otherwise.</summary>
    public required string Question { get; init; }

    /// <summary>How broad the research should be, when <see cref="Kind" /> is <see cref="RouteDecisionKind.NeedResearch" />; ignored otherwise.</summary>
    public required RouteResearchScope ResearchScope { get; init; }

    /// <summary>
    ///     The specific step only a person can take, whatever <see cref="Kind" /> is reached, or the empty string
    ///     when none is named. Carried independently of <see cref="Kind" /> so a caller can learn a human-only next
    ///     step even from a pass that otherwise selected a worker or asked for research, and so budget exhaustion
    ///     can read the most recently named one back out of the ledger.
    /// </summary>
    public required string HumanOnlyNextStep { get; init; }

    /// <inheritdoc />
    public required bool HasSufficientEvidence { get; init; }
}
