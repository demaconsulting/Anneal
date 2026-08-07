using DemaConsulting.Anneal.Toolkit.Primitives;

namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>
///     One pass of asking the route oracle a question and reading back the kind of answer it gave.
/// </summary>
/// <param name="At">When the oracle answered.</param>
/// <param name="Question">The question asked, composed from the ledger's state at the time.</param>
/// <param name="DecisionKind">The name of the <see cref="RouteDecision" /> case reached, or the oracle's own outcome name when none was.</param>
/// <param name="Rationale">Why, in the oracle's own words.</param>
/// <param name="HumanOnlyNextStep">
///     The specific step only a person can take, when the oracle named one this pass, whatever kind of decision it
///     otherwise reached; null when none was named.
/// </param>
internal sealed record RouteAttempt(
    DateTimeOffset At, string Question, string DecisionKind, string Rationale, string? HumanOnlyNextStep);

/// <summary>
///     One worker learning mid-execution that the classification underneath it was wrong, and handing evidence
///     back to the <see cref="Router" /> rather than silently self-promoting its own scope.
/// </summary>
/// <param name="WorkerKey">The worker that rerouted.</param>
/// <param name="Why">Why this worker is the wrong one, in a sentence a person can check.</param>
/// <param name="EvidenceRefs">What the worker points to in support of the reroute. Never null; may be empty.</param>
/// <param name="SuggestedWorker">The worker this change likely belongs to instead, or null when none is known.</param>
internal sealed record WorkerReroute(
    string WorkerKey, string Why, IReadOnlyList<string> EvidenceRefs, string? SuggestedWorker);

/// <summary>
///     The structured state a <see cref="Router" /> carries across one routing run: the original request, the
///     facts gathered about the repository, and everything learned since — never a free-form transcript a later
///     pass has to re-read and re-interpret.
/// </summary>
/// <remarks>
///     A class rather than a record, because a routing run accumulates research findings, route attempts and
///     reroutes as it goes; <see cref="ResearchHistory" />, <see cref="RouteAttempts" /> and
///     <see cref="WorkerReroutes" /> are mutable lists for exactly that reason, while <see cref="OriginalWorkItem" />
///     and <see cref="Facts" /> are fixed at construction because they describe the request the run was given,
///     not what the run has since learned.
/// </remarks>
internal sealed class RoutingLedger
{
    /// <summary>The work item as the caller stated it. Never changes across a routing run.</summary>
    public required string OriginalWorkItem { get; init; }

    /// <summary>The repository facts gathered deterministically before this run asked anything.</summary>
    public required RepositoryFacts Facts { get; init; }

    /// <summary>The context artifacts the caller supplied at the start of the run. Never null; may be empty.</summary>
    public required IReadOnlyList<string> InitialContextArtifacts { get; init; }

    /// <summary>Every research finding this run has gathered so far, oldest first.</summary>
    public List<ResearchFinding> ResearchHistory { get; } = [];

    /// <summary>Every route-oracle pass this run has made so far, oldest first.</summary>
    public List<RouteAttempt> RouteAttempts { get; } = [];

    /// <summary>Every worker reroute this run has recorded so far, oldest first.</summary>
    public List<WorkerReroute> WorkerReroutes { get; } = [];

    /// <summary>
    ///     What the router currently believes this work item classifies as, updated as route attempts and reroutes
    ///     narrow it. Null until the first route attempt names one.
    /// </summary>
    public string? ClassificationHypothesis { get; set; }
}
