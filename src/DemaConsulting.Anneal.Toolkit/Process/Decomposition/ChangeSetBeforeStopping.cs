using DemaConsulting.Anneal.Toolkit.Process.Workers;

namespace DemaConsulting.Anneal.Toolkit.Process.Decomposition;

/// <summary>
///     Files a worker already wrote to disk before an <see cref="OperationOutcome.Escalated" /> or
///     <see cref="OperationOutcome.Failed" /> outcome stopped it short of completion.
/// </summary>
/// <remarks>
///     Preserved so a caller of <c>dotnet anneal route</c> can see the working-tree edits a worker made before
///     it was interrupted, without conflating them with the completion-path fields on <see cref="WorkerRunResult" />.
///     <see cref="WorkerRunResult" />'s own doc comment reserves its <see cref="WorkerRunResult.Completed" /> /
///     <see cref="WorkerRunResult.Reroute" /> distinction for a worker that "did run and reached a typed answer";
///     an interrupted run never reached one, so this is a separate record rather than an extension of that union.
///     Overloading <c>ProcessNote</c> for file-path data was also rejected: a note carries a sentence for
///     human reading, not a list of file paths a program should iterate.
/// </remarks>
/// <param name="FilesChanged">
///     The files the worker wrote before stopping, in the order it reported them. Never null; may be empty when
///     nothing was written.
/// </param>
/// <param name="Summary">A brief account of what was changed before stopping. Never null; may be empty.</param>
internal sealed record ChangeSetBeforeStopping(IReadOnlyList<string> FilesChanged, string Summary);
