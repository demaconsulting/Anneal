namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What an <c>intake</c> run concluded, as data beside its outcome: where the item was filed or proposed, the
///     bullet text involved, and — on escalation — which extra fact stopped it from being appended directly.
/// </summary>
/// <remarks>
///     A dedicated record rather than reusing <see cref="FileSkillReport" /> or <see cref="StageContractReport" />:
///     <c>intake</c> either appends one bullet to a register file or escalates a proposed constraint without
///     writing it, neither of which matches another action's output shape. The action still reports through the
///     shared <see cref="OperationOutcome" /> vocabulary; this record only carries the structured facts beside it.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="TargetFile">
///     The repository-relative file the item was filed into, or would be filed into after repair or admission.
///     Never null; empty only when no destination could honestly be named.
/// </param>
/// <param name="BulletText">
///     The bullet text, without the leading <c>- </c>, that was written or proposed. Never null; empty only when
///     no bullet text could honestly be named.
/// </param>
/// <param name="Why">
///     Why this destination was selected, in a sentence a person can check. Never null; empty only when no
///     decision was obtained.
/// </param>
/// <param name="ConstraintSection">
///     The intended section in <c>.anneal/work/constraints.md</c> when the item was classified as a constraint —
///     <c>Satisfied</c> or <c>Not Yet Satisfied</c> — or null otherwise.
/// </param>
/// <param name="MissingRegisterPath">
///     The register file whose absence forced escalation, when filing could not proceed because the expected
///     register was missing. Null unless that specific escalation reason applied.
/// </param>
public sealed record IntakeReport(
    string TargetFile,
    string BulletText,
    string Why,
    string? ConstraintSection,
    string? MissingRegisterPath);
