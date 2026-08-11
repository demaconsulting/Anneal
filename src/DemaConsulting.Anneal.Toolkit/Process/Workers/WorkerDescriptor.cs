using DemaConsulting.Anneal.Toolkit.Process.Routing;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>Names one worker in the catalog a <see cref="Router" /> selects from: its key and its one-line role.</summary>
/// <param name="Key">
///     The catalog key a <see cref="RouteDecision.SelectWorker" /> names to select this worker. Never null or blank.
/// </param>
/// <param name="Description">The worker's one-line role, shown to the route oracle as part of its question.</param>
internal sealed record WorkerDescriptor(string Key, string Description);

/// <summary>Runs one worker against a deterministically-projected brief.</summary>
/// <param name="brief">What the worker needs to know, projected from the <see cref="RoutingLedger" />.</param>
/// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
/// <returns>
///     The full result of the run, including any files written before the worker stopped short of completion.
/// </returns>
internal delegate Task<WorkerExecutionResult> WorkerRunner(WorkerBrief brief, CancellationToken cancellationToken);

/// <summary>One entry in the catalog a <see cref="Router" /> selects from: a worker's descriptor paired with how to run it.</summary>
/// <param name="Descriptor">The worker's catalog identity.</param>
/// <param name="Runner">How to run the worker once selected.</param>
internal sealed record WorkerCatalogEntry(WorkerDescriptor Descriptor, WorkerRunner Runner);
