namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What a <c>verify-change</c> run concluded, as data beside its outcome: what the diff actually touched, and
///     the two deterministic checks and the verifier's judgement run against it.
/// </summary>
/// <remarks>
///     A new record rather than reusing <see cref="MaintainReport" /> or <see cref="StageContractReport" />: this
///     operation constructs no <c>Process.Router</c>, <c>Primitives.DocumentAuthor</c>, or
///     <c>Primitives.Developer</c> — it edits nothing — so the fields either existing report carries for authored
///     changes would sit permanently empty here. It is additive alongside both, not a fourth incompatible outcome
///     shape: every field still reports through the same <see cref="OperationOutcome" /> vocabulary underneath.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="DiffAvailable">
///     Whether the diff itself was read successfully. False means <see cref="ChangedFiles" /> is empty for lack
///     of evidence, not because nothing changed, and the pre-existing-obligation exception below was not applied.
/// </param>
/// <param name="ChangedFiles">
///     The repository-relative paths the diff touched. Never null; empty when the diff was empty or unavailable.
/// </param>
/// <param name="BuildPassed">Whether <c>build.ps1</c> exited zero.</param>
/// <param name="ContractConformancePassed">
///     Whether the repository's strict contract check passed once unfulfilled-obligation failures in systems the
///     diff did not touch were set aside as pre-existing, per <c>scope-check.agent.md</c>'s own exception.
/// </param>
/// <param name="Concerns">
///     Every concern the verifier's judgement pass found, rendered as <c>"{owner}: {fix}"</c>. Empty when the
///     verdict was <c>Passed</c> or when a deterministic check failed before a model was consulted at all.
/// </param>
/// <param name="AdvisoryNotes">
///     Notes nobody is obliged to act on: the verifier's own advisory notes, plus the pre-existing unfulfilled
///     obligations this run set aside. Never null; empty when there are none.
/// </param>
public sealed record VerifyChangeReport(
    bool DiffAvailable,
    IReadOnlyList<string> ChangedFiles,
    bool BuildPassed,
    bool ContractConformancePassed,
    IReadOnlyList<string> Concerns,
    IReadOnlyList<string> AdvisoryNotes);
