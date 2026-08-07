namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What a <c>route</c> run concluded, as data beside its outcome: what a selected worker changed, or what the
///     run tried, learned and recommends when no worker completed the work.
/// </summary>
/// <remarks>
///     A projection of the internal <c>Process.RouterOutcome</c> the operation drives, never that type itself: the
///     Process namespace stays internal to the Toolkit assembly, so a composing caller outside it reads this
///     public record instead. Exactly one half is populated on any given result — the completion fields on success,
///     the failure-report fields otherwise — the same "populated half tells you which path was taken" shape
///     <see cref="LintFixReport" /> already uses for its own escalation fields.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="FilesChanged">
///     The files the selected worker changed, in the order it reported them. Never null; empty unless a worker
///     completed the work.
/// </param>
/// <param name="Summary">What was changed, in the worker's own words. Never null; empty unless a worker completed the work.</param>
/// <param name="WhatWasTried">
///     Each route attempt and research pass the run made, oldest first. Never null; empty on a completed run.
/// </param>
/// <param name="WhatWasLearned">
///     A summary of what the run's research and reroutes concluded. Never null; empty on a completed run.
/// </param>
/// <param name="RejectedWorkers">
///     Every worker a reroute pointed away from, each rendered as its catalog key and why. Never null; empty on a
///     completed run.
/// </param>
/// <param name="RecommendedNextStep">
///     What a person should do next, when the run did not complete. Never null; empty on a completed run.
/// </param>
public sealed record RouteReport(
    IReadOnlyList<string> FilesChanged,
    string Summary,
    IReadOnlyList<string> WhatWasTried,
    string WhatWasLearned,
    IReadOnlyList<string> RejectedWorkers,
    string RecommendedNextStep);
