using System.ComponentModel;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     What a <see cref="DocumentAuthor" /> pass produced: the files it changed, and a summary of the change.
/// </summary>
/// <param name="FilesChanged">
///     The repository-relative paths the pass changed. Corroborated against the real working-tree diff by
///     <see cref="DocumentAuthor" /> before this record is returned: any file the model self-reported but that
///     shows no real diff entry has already been dropped. When git is unavailable the list is the model's
///     self-report unchanged.
/// </param>
/// <param name="Summary">What changed and why, in the depth a reviewer needs to judge it without re-reading every file.</param>
internal sealed record DocumentChangeSet(IReadOnlyList<string> FilesChanged, string Summary);

/// <summary>
///     What a bounded documentation-authoring pass concluded: a change set was authored, or the work belongs to a
///     different worker because a better owner was named.
/// </summary>
/// <remarks>
///     A closed union of two cases, and both map to <see cref="OperationOutcome.Succeeded" /> in
///     <see cref="DocumentAuthor" />'s own outcome mapping: reaching either <see cref="Authored" /> or
///     <see cref="Reroute" /> is this primitive successfully answering its own question, never a failure to
///     answer, per <c>.anneal/architecture/toolkit.md</c> § Decisions ("a typed 'Reroute' answer is a primitive
///     successfully answering its own question, not a new invocation outcome").
///     <para>
///         <see cref="OperationOutcome.Refused" /> is reserved for a rarer, genuinely distinct case this union
///         does not (yet) carry a finding for: the pass cannot determine correct document ownership honestly
///         enough to answer at all — not even "reroute this". Nothing in this pass gives a probe reply a way to
///         state that distinctly from <see cref="Reroute" />, so that path is currently unreachable through
///         <see cref="DocumentAuthor" />, the same honest limitation <see cref="Research" />'s
///         <see cref="OperationOutcome.Escalated" /> path documents for its own currently-unreachable case. A
///         later pass that needs to distinguish "I don't know who owns this" from "this belongs to worker X"
///         should widen this union with a third case rather than repurpose <see cref="Reroute" /> to mean both.
///     </para>
/// </remarks>
internal abstract record DocumentAuthoringResult
{
    private DocumentAuthoringResult()
    {
    }

    /// <summary>A change set was authored.</summary>
    /// <param name="Changes">What was authored.</param>
    internal sealed record Authored(DocumentChangeSet Changes) : DocumentAuthoringResult;

    /// <summary>A better owner was named for this change; it does not belong to the pass that answered.</summary>
    /// <param name="Why">Why this worker is the wrong one, in a sentence a person can check.</param>
    internal sealed record Reroute(string Why) : DocumentAuthoringResult;
}

/// <summary>What a documentation-authoring probe decoded, before it is mapped onto <see cref="DocumentAuthoringResult" />.</summary>
internal sealed record DocumentAuthoringEnvelope
{
    /// <summary>Which case of <see cref="DocumentAuthoringResult" /> this reply reaches.</summary>
    public required DocumentAuthoringOutcomeKind Kind { get; init; }

    /// <summary>
    ///     Why this change belongs to a different worker entirely, when <see cref="Kind" /> is
    ///     <see cref="DocumentAuthoringOutcomeKind.Reroute" />; the empty string otherwise. Never a hedge about
    ///     whether the authoring itself finished — that judgment belongs to
    ///     <see cref="DocumentAuthoringOutcomeKind.Authored" />.
    /// </summary>
    public required string Why { get; init; }

    /// <summary>The repository-relative files changed, when authored; empty otherwise.</summary>
    public required IReadOnlyList<string> FilesChanged { get; init; }

    /// <summary>What was authored and why, when authored; the empty string otherwise.</summary>
    public required string Summary { get; init; }
}

/// <summary>The closed vocabulary a <see cref="DocumentAuthoringEnvelope" /> decodes its kind as.</summary>
internal enum DocumentAuthoringOutcomeKind
{
    /// <summary>A change set was authored.</summary>
    [Description("a documentation change set was authored, per the tool results already shown in this conversation")]
    Authored,

    /// <summary>A better owner was named for this change, or this pass is not the correct one to make it.</summary>
    [Description(
        "this change belongs entirely to a different worker — a scope/ownership judgment, never a way to " +
        "hedge uncertainty about whether the authoring itself finished")]
    Reroute
}
