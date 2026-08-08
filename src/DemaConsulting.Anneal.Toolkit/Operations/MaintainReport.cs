namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What a <c>maintain</c> run concluded, as data beside its outcome: what <c>SmallFixWorker</c> actually
///     changed, and — on escalation — which mechanical check tripped and on what it tripped.
/// </summary>
/// <remarks>
///     A new record projecting the internal <c>Process.WorkerExecutionResult</c> directly, never routing through
///     <c>Process.RouterOutcome</c> the way <see cref="RouteReport" /> does: <c>maintain</c> never constructs a
///     <c>Process.Router</c>, so the phase and reroute-rejection history <see cref="RouteReport" /> exists to carry
///     does not exist here for it to project. Reusing <see cref="RouteReport" /> would leave most of its fields
///     permanently empty on every <c>maintain</c> invocation — its own documented "populated half" shape stretched
///     to fields that would always be the empty half rather than sometimes. This record is additive alongside
///     <see cref="RouteReport" />, not a fourth incompatible outcome shape: both still report through the same
///     <see cref="OperationOutcome" />/<c>Process.WorkerRunResult</c> vocabulary underneath.
///     <para>
///         Exactly one of <see cref="OutOfBoundsFile" />, <see cref="ProtectedPathTripped" />, and
///         <see cref="RerouteWhy" /> is non-null on an escalated run whose escalation this operation itself forced
///         (<c>TOOLKIT-29</c>/<c>TOOLKIT-30</c>/<c>TOOLKIT-31</c>); all three are null on a completed run, and on a
///         run the worker itself escalated or failed for a reason none of these three checks names (for example, a
///         protected-path write the worker's own <c>Developer</c> pass refused).
///     </para>
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="FilesChanged">
///     The files <c>SmallFixWorker</c> actually reports having changed — from its completed change set, or from
///     what it had already written before an escalated or failed run stopped it short. Never null; empty when
///     nothing was written.
/// </param>
/// <param name="Summary">What was changed, in the worker's own words. Never null; empty when nothing was changed.</param>
/// <param name="DeclaredBound">
///     The file-scope bound the caller declared before this run started — the same entries named positionally
///     after the work item. Never null; never empty, since <c>maintain</c> reports a usage error rather than
///     running with no declared bound.
/// </param>
/// <param name="OutOfBoundsFile">
///     The first actually-changed file the mechanical containment check (<c>TOOLKIT-30</c>) found outside
///     <see cref="DeclaredBound" />, forcing escalation. Null unless that check is what forced this run's
///     escalation.
/// </param>
/// <param name="ProtectedPathTripped">
///     The protected path the mechanical tripwire (<c>TOOLKIT-31</c>) found among the actually-changed files,
///     forcing escalation. Null unless that check is what forced this run's escalation.
/// </param>
/// <param name="RerouteWhy">
///     Why the worker named a better owner for this work, when it did. Null unless the worker's own reroute is
///     what forced this run's escalation.
/// </param>
/// <param name="SuggestedWorker">
///     The worker the change likely belongs to instead, when <see cref="RerouteWhy" /> is non-null and one was
///     named. Null otherwise.
/// </param>
public sealed record MaintainReport(
    IReadOnlyList<string> FilesChanged,
    string Summary,
    IReadOnlyList<string> DeclaredBound,
    string? OutOfBoundsFile,
    string? ProtectedPathTripped,
    string? RerouteWhy,
    string? SuggestedWorker);
