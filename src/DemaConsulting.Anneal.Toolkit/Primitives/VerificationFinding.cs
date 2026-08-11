using System.ComponentModel;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     What kind of judgement a <see cref="Verifier" /> was asked to make, so its charter and its evidence
///     handling can be tailored to what "correct" means for that judgement without growing a new type per intent.
/// </summary>
/// <remarks>
///     A closed vocabulary rather than a free-form string: a verifier composed into a worker declares which of
///     these it is answering, and the set stays small and named for the same reason <see cref="OperationCategory" />
///     does — an intent nobody declared is an intent nothing downstream can route on.
/// </remarks>
internal enum VerificationIntent
{
    /// <summary>Verifying a repository's layout against the shipped template.</summary>
    [Description("verifying repository layout against the template")]
    TemplateAudit,

    /// <summary>Verifying that a change conforms to the contract clauses it touches.</summary>
    [Description("verifying contract conformance")]
    ContractConformance,

    /// <summary>Any other narrow verification judgement not named above.</summary>
    [Description("a verification judgement not covered by a more specific intent")]
    Other
}

/// <summary>
///     The closed vocabulary of who owns clearing a <see cref="VerificationConcern" />.
/// </summary>
/// <remarks>
///     A third owner here is exactly why <see cref="VerificationVerdict" /> no longer encodes owner combinations
///     itself: adding <see cref="Tenet" /> to a two-owner enum would have doubled it again, to eight verdict
///     values for 2^3 combinations, the same growth <see cref="VerificationConcern" />'s remarks describe.
/// </remarks>
internal enum VerificationOwner
{
    /// <summary>A fix is owned by <see cref="DocumentAuthor" /> updating documentation.</summary>
    [Description("a documentation fix is owned by DocumentAuthor")]
    Documentation,

    /// <summary>A fix is owned by <see cref="Developer" /> changing code.</summary>
    [Description("a code fix is owned by Developer")]
    Code,

    /// <summary>
    ///     A fix is owned by checking the work against <c>.anneal/work/constraints.md</c>,
    ///     <c>.anneal/governance/tenets.md</c>, and the affected system contracts — a "Tenet Check" — distinct
    ///     from a documentation fix or a code fix.
    /// </summary>
    [Description("a tenet-check fix is owned by checking the work against .anneal/work/constraints.md, .anneal/governance/tenets.md, and the affected system contracts")]
    Tenet
}

/// <summary>The closed vocabulary of what a <see cref="Verifier" /> concludes about the work it judged.</summary>
internal enum VerificationVerdict
{
    /// <summary>The work passed; no repair is needed.</summary>
    [Description("the work passed; no repair is needed")]
    Passed,

    /// <summary>One or more owners have a concern that needs fixing; see <see cref="VerificationFinding.Concerns" />.</summary>
    [Description("one or more owners have a concern that needs fixing")]
    RepairRequired,

    /// <summary>The classification underneath this work was wrong and needs rerouting.</summary>
    [Description("the classification underneath this work was wrong and needs rerouting")]
    RerouteRequired,

    /// <summary>The router can name a specific step only a human can take.</summary>
    [Description("the router can name a specific step only a human can take")]
    Escalated,

    /// <summary>The evidence supplied was insufficient to reach an honest verdict.</summary>
    [Description("the evidence supplied was insufficient to reach an honest verdict")]
    Refused
}

/// <summary>
///     A single fix a <see cref="Verifier" /> found, and which owner is responsible for clearing it.
/// </summary>
/// <remarks>
///     Replaces the old enum-encoded verdict combinations (a value for documentation alone, code alone, and both
///     together) with an explicit typed list: a third owner — or a fourth — is one more list entry, not one more
///     enum value for every combination it can appear in.
/// </remarks>
internal sealed record VerificationConcern
{
    /// <summary>Who owns clearing this concern.</summary>
    public required VerificationOwner Owner { get; init; }

    /// <summary>The specific fix required for <see cref="Owner" /> to clear. Must not be null, empty or blank.</summary>
    public required string FixText { get; init; }
}

/// <summary>
///     What a verification pass concluded: the verdict, the concerns to clear it, and anything worth noting that
///     does not block.
/// </summary>
/// <remarks>
///     A caller reads <see cref="Concerns" /> and dispatches each to its owner — <see cref="VerificationOwner.Documentation" />
///     to <see cref="DocumentAuthor" />, <see cref="VerificationOwner.Code" /> to <see cref="Developer" />, and
///     <see cref="VerificationOwner.Tenet" /> to a tenet-check repair — rather than the old
///     <c>RequiredFixes</c> string list needing a separate interpretation per <see cref="Verdict" /> value.
/// </remarks>
internal sealed record VerificationFinding
{
    /// <summary>What was concluded.</summary>
    public required VerificationVerdict Verdict { get; init; }

    /// <summary>The concerns to clear the verdict. Empty when <see cref="Verdict" /> is <see cref="VerificationVerdict.Passed" />.</summary>
    public required IReadOnlyList<VerificationConcern> Concerns { get; init; }

    /// <summary>Advisory notes nobody is obliged to act on. Never null; empty when there are none.</summary>
    public required IReadOnlyList<string> AdvisoryNotes { get; init; }

    /// <summary>
    ///     Whether the evidence supplied was enough to reach this verdict honestly. False overrides
    ///     <see cref="Verdict" /> to <see cref="OperationOutcome.Refused" /> in <see cref="Verifier" />'s own
    ///     outcome mapping, the same way <see cref="ResearchFinding.SufficientForNextDecision" /> does for research.
    /// </summary>
    public required bool EvidenceSufficient { get; init; }
}
