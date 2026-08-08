namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>One worker a <see cref="Router" /> considered and did not select, and why.</summary>
/// <param name="WorkerKey">The rejected worker's catalog key.</param>
/// <param name="Why">Why this worker was rejected, in a sentence a person can check.</param>
internal sealed record RejectedWorker(string WorkerKey, string Why);

/// <summary>
///     What a routing run reports when it cannot complete: what was tried, what was learned, which workers were
///     rejected and why, a recommended next step for a person to take, and any files the selected worker already
///     wrote before stopping short.
/// </summary>
/// <remarks>
///     Built entirely from the <see cref="RoutingLedger" /> a <see cref="Router" /> already accumulated — nothing
///     here is a fresh model call, because a failure report's whole job is to state honestly what the run already
///     knows, not to spend more budget composing a better excuse.
/// </remarks>
/// <param name="WhatWasTried">Each route attempt and research pass made during the run, oldest first.</param>
/// <param name="WhatWasLearned">A summary of what the run's research and reroutes concluded.</param>
/// <param name="RejectedWorkers">Every worker a reroute pointed away from, and why.</param>
/// <param name="RecommendedNextStep">
///     What a person should do next: the most recently named human-only next step, when one was named, or a
///     generic recommendation to review the run manually when none was.
/// </param>
/// <param name="ChangeBeforeStopping">
///     Files the selected worker already wrote to disk before the run was interrupted, or null when the worker
///     wrote nothing or no worker was ever selected. Populated only when a worker reached real authoring state
///     before an Escalated or Failed outcome stopped it.
/// </param>
internal sealed record RouteFailureReport(
    IReadOnlyList<string> WhatWasTried,
    string WhatWasLearned,
    IReadOnlyList<RejectedWorker> RejectedWorkers,
    string RecommendedNextStep,
    ChangeSetBeforeStopping? ChangeBeforeStopping = null);
