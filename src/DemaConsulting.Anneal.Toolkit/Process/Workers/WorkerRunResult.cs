using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Routing;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     What a compiled worker concluded: the work was completed, or the worker learned mid-execution that the
///     classification underneath it was wrong and hands evidence back to the <see cref="Router" />.
/// </summary>
/// <remarks>
///     A closed union of two cases, mirroring how <see cref="DevelopmentResult" /> and
///     <see cref="DocumentAuthoringResult" /> each carry their own two-case union one layer down — reaching either
///     case is the worker successfully answering the question the <see cref="Router" /> handed it, never a failure
///     to answer. A worker's own failure to run at all — a build-repair budget spent, a model unreachable — is
///     reported through the enclosing <see cref="StepResult{TFinding}" />'s <see cref="OperationOutcome" /> instead,
///     with this union's <see cref="Completed" />/<see cref="Reroute" /> distinction reserved for a worker that did
///     run and reached a typed answer.
/// </remarks>
internal abstract record WorkerRunResult
{
    private WorkerRunResult()
    {
    }

    /// <summary>The worker completed the work.</summary>
    /// <param name="Summary">What was changed.</param>
    internal sealed record Completed(ChangeSetSummary Summary) : WorkerRunResult;

    /// <summary>
    ///     The worker learned mid-execution that this work does not belong to it, and hands evidence back to the
    ///     <see cref="Router" /> rather than silently self-promoting its own scope.
    /// </summary>
    /// <param name="Why">Why this worker is the wrong one, in a sentence a person can check.</param>
    /// <param name="EvidenceRefs">What the worker points to in support of the reroute. Never null; may be empty.</param>
    /// <param name="SuggestedWorker">The worker this change likely belongs to instead, or null when none is known.</param>
    internal sealed record Reroute(string Why, IReadOnlyList<string> EvidenceRefs, string? SuggestedWorker)
        : WorkerRunResult;
}
