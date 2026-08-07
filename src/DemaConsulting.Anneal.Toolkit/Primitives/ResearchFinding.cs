namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     What a bounded research pass concluded: the question it was given, the answer it reached, the evidence
///     behind it, what that evidence implies, and whether it is enough to make the next decision on.
/// </summary>
/// <remarks>
///     Every property is required, matching <see cref="Operations.RuleOwnerAnswer" />'s own reasoning: a reply
///     missing one fails the decode and takes the visible retry path rather than arriving half-filled.
///     <see cref="SufficientForNextDecision" /> is the field a caller composes on — a router or worker reads it to
///     decide whether to spend another research iteration, ask an <see cref="Oracle{TDecision}" />, or proceed —
///     so it is carried as data rather than left for a caller to infer from prose.
/// </remarks>
internal sealed record ResearchFinding
{
    /// <summary>The question this research pass was given.</summary>
    public required string Question { get; init; }

    /// <summary>The answer reached, in the depth a caller needs to act on it.</summary>
    public required string Answer { get; init; }

    /// <summary>
    ///     The files or facts consulted, so a reader — or a later <see cref="Verifier" /> pass — can check the
    ///     answer rather than take it.
    /// </summary>
    public required IReadOnlyList<string> EvidenceRefs { get; init; }

    /// <summary>What the answer implies for the decision it will feed, in one or two sentences.</summary>
    public required string Implications { get; init; }

    /// <summary>
    ///     Whether this finding is enough for the next decision to be made honestly, or whether more research, a
    ///     narrower question, or a person is needed instead.
    /// </summary>
    public required bool SufficientForNextDecision { get; init; }
}
