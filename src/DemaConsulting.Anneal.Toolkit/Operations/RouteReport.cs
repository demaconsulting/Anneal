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
///     <para>
///         When a selected worker stopped short — Escalated or Failed — but had already written files to disk,
///         <see cref="FilesChangedBeforeStopping" /> and <see cref="SummaryBeforeStopping" /> are non-empty so
///         a caller can see what is already on disk. These are additive alongside the failure-report fields;
///         they are always empty on a completed run.
///     </para>
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
/// <param name="FilesChangedBeforeStopping">
///     Files the selected worker wrote to disk before an Escalated or Failed outcome stopped it short of
///     completion, in the order the worker reported them. Never null; empty on a completed run or when no files
///     were written before the worker stopped.
/// </param>
/// <param name="SummaryBeforeStopping">
///     A brief account of what the worker changed before stopping. Never null; empty on a completed run or when
///     no files were written before the worker stopped.
/// </param>
public sealed record RouteReport(
    IReadOnlyList<string> FilesChanged,
    string Summary,
    IReadOnlyList<string> WhatWasTried,
    string WhatWasLearned,
    IReadOnlyList<string> RejectedWorkers,
    string RecommendedNextStep,
    IReadOnlyList<string> FilesChangedBeforeStopping,
    string SummaryBeforeStopping);
