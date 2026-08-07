namespace DemaConsulting.Anneal.Toolkit.Recording;

/// <summary>
///     The record of one primitive or worker/router step inside a compiled process: what it was, what it
///     concluded, and how much of its budget remained afterward.
/// </summary>
/// <remarks>
///     A second, additive stream beside <see cref="InvocationRecord" /> — <c>TOOLKIT-08</c>'s record answers
///     "did this action succeed," which cannot answer how often a router needed research, how often a worker
///     rerouted, or how often a planner was actually used. Those rates are exactly what keep a compiled catalog
///     honest rather than optimistic, so they are written structurally rather than left to be recalled from
///     memory or scraped from a report after the fact — the same reasoning <c>TOOLKIT-08</c> itself rests on.
///     <para>
///         Correlated to a parent invocation by <see cref="ParentInvocationId" /> rather than by embedding step
///         detail in <see cref="InvocationRecord" /> itself: that would force every non-composing operation's
///         record to carry fields it never populates, the drift <c>TOOLKIT-08</c>'s own design already avoided
///         once. <see cref="InvocationRecord" /> carries no identifier of its own today, so
///         <see cref="ParentInvocationId" /> is an opaque string a caller mints and threads through its own
///         invocation and every step beneath it; minting and threading that identifier is a later pass's work; this
///         one only fixes the shape a step is written in.
///     </para>
///     <para>
///         <see cref="Outcome" /> is a name and never a position, for the same reason
///         <see cref="InvocationRecord.Outcome" /> is: a record written by one version is read against another.
///     </para>
///     <para>
///         The budget fields are deliberately primitive-shaped rather than worker-specific: no worker or router
///         exists yet in this pass, so nothing here names one. A step that spends no counted budget leaves both
///         null, which is "not applicable" and never "zero remaining".
///     </para>
/// </remarks>
/// <param name="At">When the step concluded.</param>
/// <param name="ParentInvocationId">
///     The identifier of the invocation this step belongs to, correlating it back to that invocation's own
///     <see cref="InvocationRecord" /> without either record needing to hold a reference to the other. Never
///     null; empty when the caller has not yet minted one.
/// </param>
/// <param name="Step">
///     The primitive, worker, or router step this record describes, named as the composing code names it —
///     for example <c>Oracle</c>, <c>Research</c>, or a future worker key. Never null or blank.
/// </param>
/// <param name="Outcome">The name of the <see cref="OperationOutcome" /> the step reached, never its numeric position.</param>
/// <param name="ResearchBudgetRemaining">
///     How many research iterations remained after this step, or null when the step does not spend a research
///     budget.
/// </param>
/// <param name="RerouteBudgetRemaining">
///     How many reroutes remained after this step, or null when the step does not spend a reroute budget.
/// </param>
public sealed record ProcessStepRecord(
    DateTimeOffset At,
    string ParentInvocationId,
    string Step,
    string Outcome,
    int? ResearchBudgetRemaining,
    int? RerouteBudgetRemaining);
