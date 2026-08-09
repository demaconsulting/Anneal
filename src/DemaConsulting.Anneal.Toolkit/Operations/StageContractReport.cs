namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What a <c>stage-contract</c> run concluded, as data beside its outcome: what <c>DocumentAuthor</c>
///     actually changed, and — on escalation or failure — which check stopped it and on what.
/// </summary>
/// <remarks>
///     A new record projecting the internal <c>Primitives.DocumentAuthoringResult</c> directly, for the same
///     reason <see cref="MaintainReport" /> is its own record rather than reusing <see cref="RouteReport" />:
///     <c>stage-contract</c> never constructs a <c>Process.Router</c> or runs <c>Developer</c>/<c>Verifier</c>, so
///     the fields either of those existing reports carries for that machinery would sit permanently empty here.
///     It is additive alongside both, not a fourth incompatible outcome shape — every field still reports
///     through the same <see cref="OperationOutcome" /> vocabulary underneath.
///     <para>
///         At most one of <see cref="OutOfScopeFile" />, <see cref="MalformedCheckOutput" />, and
///         <see cref="RerouteWhy" /> is non-null on any single run: each names a different reason this run did not
///         reach <see cref="OperationOutcome.Succeeded" />, and only one reason applies to a given run.
///     </para>
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="FilesChanged">
///     The files <c>DocumentAuthor</c> actually reports having changed. Never null; empty when nothing was
///     written.
/// </param>
/// <param name="Summary">What was changed, in the author's own words. Never null; empty when nothing was changed.</param>
/// <param name="OutOfScopeFile">
///     The first actually-changed file found outside <c>docs/architecture/</c>, forcing escalation because this
///     action's whole job is to touch the architecture tree and nothing else. Null unless that check is what
///     forced this run's escalation.
/// </param>
/// <param name="MalformedCheckOutput">
///     The output of the non-strict <c>check-contracts</c> run against the staged clause, when it did not pass —
///     meaning the clause itself is malformed, not merely unfulfilled. Null unless that check is what forced
///     this run's failure.
/// </param>
/// <param name="RerouteWhy">
///     Why <c>DocumentAuthor</c> named a better owner for this work, when it did. Null unless that reroute is
///     what forced this run's escalation.
/// </param>
public sealed record StageContractReport(
    IReadOnlyList<string> FilesChanged,
    string Summary,
    string? OutOfScopeFile,
    string? MalformedCheckOutput,
    string? RerouteWhy);
