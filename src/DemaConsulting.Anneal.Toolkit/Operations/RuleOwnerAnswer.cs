using System.ComponentModel;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What a probe concluded about where a rule is stated.
/// </summary>
/// <remarks>
///     The vocabulary is closed and includes both ways of failing to find an owner, because they are different
///     problems with different repairs: a rule stated in several places is a duplication to resolve, while a
///     rule stated nowhere is either not a rule or a gap. Collapsing them into one "unknown" would lose exactly
///     the distinction a caller asked for.
/// </remarks>
public enum RuleOwnership
{
    /// <summary>Exactly one file states the rule, and that file is its owner.</summary>
    [Description("exactly one file states the rule")]
    SingleOwner,

    /// <summary>More than one file states the rule, so no single file owns it.</summary>
    [Description("more than one file states the rule, so no single file owns it")]
    StatedInSeveralPlaces,

    /// <summary>No file states the rule.</summary>
    [Description("no file in the repository states the rule")]
    StatedNowhere
}

/// <summary>
///     The typed result of the <c>probe-rule-owner</c> probe: which file owns a rule, and the evidence for it.
/// </summary>
/// <remarks>
///     Every property is required, so a reply missing one fails the decode and takes the visible retry path
///     rather than arriving half-filled. The evidence field is not decoration: a verdict about ownership that
///     names no file cannot be checked by a reader, and a judgement nobody can check is no judgement.
/// </remarks>
public sealed record RuleOwnerAnswer
{
    /// <summary>What was concluded about where the rule is stated.</summary>
    public required RuleOwnership Ownership { get; init; }

    /// <summary>
    ///     The repository-relative path of the owning file when <see cref="Ownership" /> is
    ///     <see cref="RuleOwnership.SingleOwner" />, and the empty string otherwise.
    /// </summary>
    public required string OwningFile { get; init; }

    /// <summary>
    ///     The files consulted and what each was found to say, in one or two sentences, so a reader can check
    ///     the conclusion rather than take it.
    /// </summary>
    public required string Evidence { get; init; }
}
