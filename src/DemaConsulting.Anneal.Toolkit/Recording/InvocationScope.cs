using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Recording;

/// <summary>
///     Accumulates what the model interactions of one invocation consumed, so the invocation's own record can
///     state it.
/// </summary>
/// <remarks>
///     Ambient rather than threaded through every signature. The alternative — carrying a usage accumulator
///     down through the operation interface to the model seam — would put a bookkeeping parameter in the
///     public contract of every operation, including the ones that consult no model, to serve a fact only the
///     dispatcher reports. It is internal precisely because that choice is interior and reversible.
///     <para>
///         Thread safety: safe to call concurrently; each invocation gets its own scope, and concurrent
///         invocations on the same thread pool see their own through the asynchronous flow.
///     </para>
/// </remarks>
internal sealed class InvocationScope : IDisposable
{
    private static readonly AsyncLocal<InvocationScope?> Ambient = new();

    private readonly InvocationScope? _enclosing;
    private readonly Lock _gate = new();

    private InvocationScope(InvocationScope? enclosing) => _enclosing = enclosing;

    /// <summary>
    ///     The scope the current asynchronous flow belongs to, or null when nothing is being recorded.
    /// </summary>
    internal static InvocationScope? Current => Ambient.Value;

    /// <summary>
    ///     How many model interactions have been observed in this scope.
    /// </summary>
    internal int Interactions { get; private set; }

    /// <summary>
    ///     What those interactions consumed in total, or null when none reported usage.
    /// </summary>
    internal ModelUsage? Usage { get; private set; }

    /// <summary>
    ///     Opens a scope for one invocation.
    /// </summary>
    /// <returns>The scope, which restores its predecessor when disposed.</returns>
    internal static InvocationScope Begin()
    {
        var scope = new InvocationScope(Ambient.Value);
        Ambient.Value = scope;
        return scope;
    }

    /// <summary>
    ///     Notes one model interaction and what it consumed.
    /// </summary>
    /// <param name="usage">What the interaction consumed, or null when the provider reported nothing.</param>
    internal void Observe(ModelUsage? usage)
    {
        lock (_gate)
        {
            Interactions++;
            if (usage is not null)
                Usage = (Usage ?? ModelUsage.None).Add(usage);
        }
    }

    /// <inheritdoc />
    public void Dispose() => Ambient.Value = _enclosing;
}
