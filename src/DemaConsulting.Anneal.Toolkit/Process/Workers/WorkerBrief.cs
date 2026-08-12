using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     What a <see cref="Router" /> hands a selected worker: a deterministic projection of the
///     <see cref="RoutingLedger" />, never a fresh oracle summary of it.
/// </summary>
/// <remarks>
///     <c>.anneal/architecture/process.md</c> § Decisions explicitly rejects a default "probe the oracle for a
///     summary" step between routing and running a worker — a worker is handed exactly what the ledger already
///     holds, projected by ordinary code, so building a brief costs nothing beyond the research and reroutes the
///     run already paid for.
/// </remarks>
/// <param name="ParentInvocationId">
///     The identifier minted for this routing run, so the worker's own <see cref="Recording.ProcessStepRecord" />
///     entries — should it choose to write any in a later pass — correlate to the same parent as the Router's own.
/// </param>
/// <param name="OriginalWorkItem">The work item as the caller originally stated it.</param>
/// <param name="Effort">
///     The Effort classification the caller is asking this worker to execute at. For routed work this is the route
///     oracle's classified Effort; for direct callers it is the fixed Effort that front door declared.
/// </param>
/// <param name="ClassificationHypothesis">What the router currently believes this work classifies as, or null.</param>
/// <param name="RelevantResearchFindings">Every research finding the routing run has gathered so far.</param>
/// <param name="PriorReroutes">Every reroute the routing run has recorded so far, oldest first.</param>
/// <param name="ScopeHint">Why this worker was selected, carried from the route decision's own rationale.</param>
/// <param name="ConstraintRefs">
///     The architecture documents the router judged relevant to this work item, so a worker knows what to read
///     before it starts, not what it must obey — obeying them is the worker's own standards-loading job.
/// </param>
/// <param name="TenetFacts">
///     The bullet-level tenets from <c>.anneal/governance/tenets.md</c>, gathered deterministically alongside
///     vision and constraint facts. Empty when tenets.md is absent or contains no bullets. Workers use this to
///     check produced plans and diffs against the repository's fundamental non-negotiable constraints without
///     re-reading the file on every pass.
/// </param>
/// <param name="ChangedFileHints">
///     The changed-file hints gathered for the work item, reused for automatic skill lookup so the worker reads
///     the same coarse file-scope signal the router already gathered.
/// </param>
internal sealed record WorkerBrief(
    string ParentInvocationId,
    string OriginalWorkItem,
    Effort Effort,
    string? ClassificationHypothesis,
    IReadOnlyList<ResearchFinding> RelevantResearchFindings,
    IReadOnlyList<WorkerReroute> PriorReroutes,
    string ScopeHint,
    IReadOnlyList<string> ConstraintRefs,
    IReadOnlyList<string> TenetFacts,
    IReadOnlyList<string> ChangedFileHints)
{
    /// <summary>
    ///     Projects a <see cref="WorkerBrief" /> from a <see cref="RoutingLedger" /> deterministically — no oracle
    ///     call, no model call, a pure read of state the run already accumulated.
    /// </summary>
    /// <param name="ledger">The ledger to project from. Must not be null.</param>
    /// <param name="parentInvocationId">The identifier minted for this routing run. Must not be null or blank.</param>
    /// <param name="scopeHint">Why the worker being briefed was selected. Must not be null.</param>
    /// <param name="effort">The Effort the route oracle classified for this worker run.</param>
    /// <returns>The projected brief.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ledger" /> or <paramref name="scopeHint" /> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="parentInvocationId" /> is null, empty or blank.</exception>
    public static WorkerBrief FromLedger(RoutingLedger ledger, string parentInvocationId, string scopeHint, Effort effort)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentInvocationId);
        ArgumentNullException.ThrowIfNull(scopeHint);

        return new WorkerBrief(
            parentInvocationId,
            ledger.OriginalWorkItem,
            effort,
            ledger.ClassificationHypothesis,
            [.. ledger.ResearchHistory],
            [.. ledger.WorkerReroutes],
            scopeHint,
            [.. ledger.Facts.RelevantArchitectureNodes],
            [.. ledger.Facts.TenetFacts],
            [.. ledger.Facts.ChangedFileHints]);
    }

    /// <summary>
    ///     Renders <see cref="RelevantResearchFindings" /> as one line per finding, for embedding in an
    ///     instruction a worker's model-backed pass reads.
    /// </summary>
    /// <returns>One <c>"- {answer}"</c> line per finding, or <c>"none"</c> when there are none.</returns>
    public string RenderResearch() =>
        RelevantResearchFindings.Count == 0
            ? "none"
            : string.Join("\n", RelevantResearchFindings.Select(finding => $"- {finding.Answer}"));

    /// <summary>
    ///     Renders <see cref="PriorReroutes" /> as one line per reroute, for embedding in an instruction a
    ///     worker's model-backed pass reads.
    /// </summary>
    /// <returns>One <c>"- from '{worker}': {why}"</c> line per reroute, or <c>"none"</c> when there are none.</returns>
    public string RenderReroutes() =>
        PriorReroutes.Count == 0
            ? "none"
            : string.Join("\n", PriorReroutes.Select(reroute => $"- from '{reroute.WorkerKey}': {reroute.Why}"));

    /// <summary>
    ///     Renders <see cref="TenetFacts" /> as a tenet section appended to an existing verifier question, or
    ///     returns an empty string when <see cref="TenetFacts" /> is empty so the caller's question is unchanged.
    /// </summary>
    /// <returns>
    ///     A newline-leading tenet-check block when <see cref="TenetFacts" /> is non-empty, or an empty string
    ///     when it is empty — allowing simple string concatenation without a branch at the call site.
    /// </returns>
    public string RenderTenetSection()
    {
        if (TenetFacts.Count == 0)
            return string.Empty;

        var bullets = string.Join("\n", TenetFacts.Select(t => $"- {t}"));
        return $"""


               Also judge the diff against these repository tenets:
               {bullets}
               For each tenet, identify the nearest candidate in the diff you considered — a new statement, a removed
               declaration, a new dependency, new logging of a named sensitive field, or similar concrete evidence —
               and state whether it violates the tenet. Require positive evidence visible in the diff before reporting
               a violation; name the exact tenet text and the exact diff element causing the contradiction when a
               violation is found. Report any violation as a concern owned by Tenet.
               """;
    }
}
