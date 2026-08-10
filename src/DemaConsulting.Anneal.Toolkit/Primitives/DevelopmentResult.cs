using System.ComponentModel;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>What a <see cref="Developer" /> pass produced when it completed: the files it changed, and why.</summary>
/// <param name="FilesChanged">
///     The repository-relative paths the pass reports having changed. Self-reported, the same way
///     <see cref="DocumentChangeSet.FilesChanged" /> is; checking it against the working tree is a
///     <see cref="Verifier" />'s job.
/// </param>
/// <param name="Summary">What changed and why, in the depth a reviewer needs to judge it.</param>
internal sealed record ChangeSetSummary(IReadOnlyList<string> FilesChanged, string Summary);

/// <summary>
///     What a bounded code/test-authoring pass concluded: the work was completed, or a better owner was named for
///     it.
/// </summary>
/// <remarks>
///     A closed union of two cases, and both map to <see cref="OperationOutcome.Succeeded" /> in
///     <see cref="Developer" />'s own outcome mapping, the same way <see cref="DocumentAuthoringResult" /> maps
///     its own two cases: reaching either <see cref="Completed" /> or <see cref="Reroute" /> is this primitive
///     successfully answering its own question, per <c>.anneal/architecture/toolkit.md</c> § Decisions ("a typed
///     'Reroute' answer is a primitive successfully answering its own question, not a new invocation outcome").
///     <para>
///         <see cref="OperationOutcome.Refused" /> is reserved for a rarer, genuinely distinct case this union
///         does not (yet) carry a finding for: the pass cannot proceed honestly enough to answer at all — not
///         even "reroute this to worker X". Nothing in this pass gives a probe reply a way to state that
///         distinctly from <see cref="Reroute" />, so that path is currently unreachable through
///         <see cref="Developer" />, the same honest limitation <see cref="DocumentAuthoringResult" /> documents
///         for its own <see cref="OperationOutcome.Refused" /> case. A later pass that needs to distinguish
///         "I cannot proceed at all" from "this belongs to worker X" should widen this union with a third case
///         rather than repurpose <see cref="Reroute" /> to mean both.
///     </para>
/// </remarks>
internal abstract record DevelopmentResult
{
    private DevelopmentResult()
    {
    }

    /// <summary>The work was completed.</summary>
    /// <param name="Summary">What was changed.</param>
    internal sealed record Completed(ChangeSetSummary Summary) : DevelopmentResult;

    /// <summary>A better owner was named for this change; it does not belong to the pass that answered.</summary>
    /// <param name="Why">Why this worker is the wrong one, in a sentence a person can check.</param>
    /// <param name="SuggestedWorker">The worker this change likely belongs to instead, or null when none is known.</param>
    internal sealed record Reroute(string Why, string? SuggestedWorker) : DevelopmentResult;
}

/// <summary>What a code/test-authoring probe decoded, before it is mapped onto <see cref="DevelopmentResult" />.</summary>
internal sealed record DevelopmentEnvelope
{
    /// <summary>Which case this reply reaches.</summary>
    public required DevelopmentOutcomeKind Kind { get; init; }

    /// <summary>
    ///     Why this change belongs to a different worker entirely, when <see cref="Kind" /> is
    ///     <see cref="DevelopmentOutcomeKind.Reroute" />; the empty string otherwise. Never a hedge about whether
    ///     the edit itself finished — that judgment belongs to <see cref="DevelopmentOutcomeKind.Completed" />.
    /// </summary>
    public required string Why { get; init; }

    /// <summary>The worker this change likely belongs to, when rerouting; the empty string when none is known.</summary>
    public required string SuggestedWorker { get; init; }

    /// <summary>The repository-relative files changed, when completed; empty otherwise.</summary>
    public required IReadOnlyList<string> FilesChanged { get; init; }

    /// <summary>What was changed and why, when completed; the empty string otherwise.</summary>
    public required string Summary { get; init; }
}

/// <summary>The closed vocabulary a <see cref="DevelopmentEnvelope" /> decodes its kind as.</summary>
internal enum DevelopmentOutcomeKind
{
    /// <summary>The work was completed.</summary>
    [Description("the code or test change was completed, per the tool results already shown in this conversation")]
    Completed,

    /// <summary>A better owner was named for this change; it does not belong to the developer that answered.</summary>
    [Description(
        "this change belongs entirely to a different worker — a scope/ownership judgment, never a way to " +
        "hedge uncertainty about whether the edit itself finished")]
    Reroute
}
