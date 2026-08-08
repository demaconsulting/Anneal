using System.ComponentModel;
using DemaConsulting.Anneal.Toolkit.Primitives;

namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>
///     What a mandatory cumulative-check pass concluded over a whole proposed phase set: the union crosses no
///     boundary no single phase crosses alone, or it does and the decomposition must escalate instead of running.
/// </summary>
/// <remarks>
///     A narrow, single-question decision — the smallest possible oracle addition alongside the existing route
///     question, matching <see cref="RouteDecision" />'s own shape rather than growing a second general-purpose
///     router. See <see cref="Router" /> for where this is composed, and <c>docs/architecture/toolkit/route.md</c>
///     § Decisions for why this recurses through the same <see cref="Router" /> rather than a standalone type.
/// </remarks>
internal abstract record CumulativeCheckDecision
{
    private CumulativeCheckDecision()
    {
    }

    /// <summary>The whole phase set's union crosses no boundary no single phase crosses alone.</summary>
    /// <param name="Why">Why the union is clear, in a sentence a person can check.</param>
    internal sealed record Clear(string Why) : CumulativeCheckDecision;

    /// <summary>
    ///     The phase set's union crosses a boundary no single phase crosses alone — a higher-scope change hiding
    ///     in the decomposition — and must escalate rather than route.
    /// </summary>
    /// <param name="Why">Why the union crosses a boundary, in a sentence a person can check.</param>
    /// <param name="HumanOnlyNextStep">
    ///     The specific step only a person can take, when one is known, or null when none is known.
    /// </param>
    internal sealed record Escalate(string Why, string? HumanOnlyNextStep) : CumulativeCheckDecision;
}

/// <summary>The closed vocabulary a <see cref="CumulativeCheckEnvelope" /> decodes its kind as.</summary>
internal enum CumulativeCheckKind
{
    /// <summary>The phase set's union crosses no boundary no single phase crosses alone.</summary>
    [Description("the phase set's union crosses no boundary no single phase crosses alone")]
    Clear,

    /// <summary>The phase set's union crosses a boundary no single phase crosses alone.</summary>
    [Description("the phase set's union crosses a boundary no single phase crosses alone")]
    Escalate
}

/// <summary>What a cumulative-check probe decoded, before it is mapped onto <see cref="CumulativeCheckDecision" />.</summary>
/// <remarks>
///     A flat envelope, matching <see cref="RouteDecisionEnvelope" />'s own house pattern: one kind field plus the
///     union's payload fields, each left empty when the decoded kind does not use it.
/// </remarks>
internal sealed record CumulativeCheckEnvelope : IOracleDecision
{
    /// <summary>Which case of <see cref="CumulativeCheckDecision" /> this reply reaches.</summary>
    public required CumulativeCheckKind Kind { get; init; }

    /// <summary>Why, whatever <see cref="Kind" /> is reached. Never empty.</summary>
    public required string Why { get; init; }

    /// <summary>
    ///     The specific step only a person can take, when <see cref="Kind" /> is
    ///     <see cref="CumulativeCheckKind.Escalate" /> and one is known, or the empty string otherwise.
    /// </summary>
    public required string HumanOnlyNextStep { get; init; }

    /// <inheritdoc />
    public required bool HasSufficientEvidence { get; init; }
}
