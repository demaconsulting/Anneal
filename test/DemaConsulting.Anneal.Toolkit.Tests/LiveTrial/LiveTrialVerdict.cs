using DemaConsulting.Anneal.Toolkit.Primitives;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     The typed pass/fail judgement <see cref="LiveTrialFixture.GradeAsync" />'s grading oracle answers with.
/// </summary>
/// <remarks>
///     Mirrors the shape every <see cref="Router" /> oracle decision already implements
///     (<see cref="IOracleDecision" />): a narrow typed answer that can state honestly when the evidence it was
///     given does not support a verdict, rather than guessing one.
/// </remarks>
public sealed record LiveTrialVerdict : IOracleDecision
{
    /// <inheritdoc />
    public required bool HasSufficientEvidence { get; init; }

    /// <summary>
    ///     Whether the observed outcome satisfied the stated expectation. Meaningful only when
    ///     <see cref="HasSufficientEvidence" /> is true.
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>
    ///     The oracle's own account of why it reached this verdict, in plain text.
    /// </summary>
    public required string Reasoning { get; init; }
}
