using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     The full result of running a compiled worker: its outcome, the typed finding when the worker reached a
///     typed answer, any files it wrote before stopping when it did not, and the notes it accumulated.
/// </summary>
/// <remarks>
///     Replaces <c>StepResult&lt;WorkerRunResult&gt;</c> as the return type of each worker's
///     <c>RunAsync</c> method so that interrupted-change data can be carried alongside the outcome without
///     changing <see cref="WorkerRunResult" />'s own closed-union invariant. The <see cref="Interrupted" />
///     field is populated only when a worker wrote files before stopping on an
///     <see cref="OperationOutcome.Escalated" /> or <see cref="OperationOutcome.Failed" /> outcome and those
///     files were recoverable from the last state the worker reached; it is null when a worker stopped for a
///     reason that left no real file changes (e.g., no model was reachable at all).
/// </remarks>
/// <param name="Outcome">The outcome of the run.</param>
/// <param name="Finding">
///     The typed worker answer, populated only when the worker reached a typed answer
///     (<see cref="WorkerRunResult.Completed" /> or <see cref="WorkerRunResult.Reroute" />). Null otherwise.
/// </param>
/// <param name="Interrupted">
///     Files the worker already wrote before the run was interrupted, or null when nothing was written. Null on
///     a successful completion — use <see cref="Finding" /> instead on the <see cref="OperationOutcome.Succeeded" /> path.
/// </param>
/// <param name="Notes">Notes accumulated during the run. Never null; may be empty.</param>
internal sealed record WorkerExecutionResult(
    OperationOutcome Outcome,
    WorkerRunResult? Finding,
    ChangeSetBeforeStopping? Interrupted,
    IReadOnlyList<ProcessNote> Notes);
