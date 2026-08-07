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
    /// <summary>Verifying a change against the tier it was classified under.</summary>
    [Description("verifying a change against its declared tier")]
    TierCheck,

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

/// <summary>The closed vocabulary of what a <see cref="Verifier" /> concludes about the work it judged.</summary>
internal enum VerificationVerdict
{
    /// <summary>The work passed; no repair is needed.</summary>
    [Description("the work passed; no repair is needed")]
    Passed,

    /// <summary>A documentation repair is needed.</summary>
    [Description("a documentation repair is needed")]
    DocumentationRepairRequired,

    /// <summary>A code repair is needed.</summary>
    [Description("a code repair is needed")]
    CodeRepairRequired,

    /// <summary>Both a documentation repair and a code repair are needed.</summary>
    [Description("both a documentation repair and a code repair are needed")]
    BothRepairsRequired,

    /// <summary>The classification underneath this work was wrong and needs rerouting.</summary>
    [Description("the classification underneath this work was wrong and needs rerouting")]
    RerouteRequired
}

/// <summary>
///     What a verification pass concluded: the verdict, the fixes required to clear it, and anything worth noting
///     that does not block.
/// </summary>
/// <remarks>
///     <see cref="RequiredFixes" /> is exactly what a <see cref="RepairLoop{TState}" /> sends back to the
///     primitive that owns the repair — a documentation finding to <see cref="DocumentAuthor" />, a code finding
///     to <see cref="Developer" /> — so it is carried as data a caller composes on rather than prose a caller
///     re-parses.
/// </remarks>
internal sealed record VerificationFinding
{
    /// <summary>What was concluded.</summary>
    public required VerificationVerdict Verdict { get; init; }

    /// <summary>The fixes required to clear the verdict. Empty when <see cref="Verdict" /> is <see cref="VerificationVerdict.Passed" />.</summary>
    public required IReadOnlyList<string> RequiredFixes { get; init; }

    /// <summary>Advisory notes nobody is obliged to act on. Never null; empty when there are none.</summary>
    public required IReadOnlyList<string> AdvisoryNotes { get; init; }

    /// <summary>
    ///     Whether the evidence supplied was enough to reach this verdict honestly. False overrides
    ///     <see cref="Verdict" /> to <see cref="OperationOutcome.Refused" /> in <see cref="Verifier" />'s own
    ///     outcome mapping, the same way <see cref="ResearchFinding.SufficientForNextDecision" /> does for research.
    /// </summary>
    public required bool EvidenceSufficient { get; init; }
}
