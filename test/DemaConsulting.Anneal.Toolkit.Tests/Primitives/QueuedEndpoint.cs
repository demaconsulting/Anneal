using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     A chat endpoint that answers fixed replies in order and reports itself unavailable once its queue is
///     exhausted, so a primitive's whole outcome-mapping behavior is exercisable without a network call.
/// </summary>
/// <remarks>
///     Shared across the primitive tests in this folder rather than redefined per file, the same way
///     <c>OperationInvocationTests.CountingEndpoint</c> is the one fake behind every existing operation test.
/// </remarks>
internal sealed class QueuedEndpoint : IChatEndpoint
{
    private readonly Queue<string> _replies;

    /// <param name="replies">
    ///     The replies handed out in order, one per call. Once exhausted, further calls report the model
    ///     unavailable, standing in for a provider that could not be reached.
    /// </param>
    public QueuedEndpoint(params string[] replies) => _replies = new Queue<string>(replies);

    /// <summary>How many turns this endpoint has been asked to complete.</summary>
    public int Calls { get; private set; }

    public Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
    {
        Calls++;

        return _replies.Count == 0
            ? Task.FromException<ChatTurnResult>(new ModelUnavailableException("no reply was queued for this call"))
            : Task.FromResult(new ChatTurnResult(_replies.Dequeue()));
    }

    public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);
}
