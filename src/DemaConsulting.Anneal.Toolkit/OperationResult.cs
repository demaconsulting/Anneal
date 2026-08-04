namespace DemaConsulting.Anneal.Toolkit;

/// <summary>
///     What one invocation of an operation produced: the outcome it reached, and — beside it, never in place of
///     it — what it found, as data a caller consumes without parsing the text the operation rendered.
/// </summary>
/// <remarks>
///     The outcome and the finding are peers on purpose. A refusal is a fact about the invocation rather than a
///     value the operation found, so the outcome vocabulary stays where <see cref="OperationOutcome" /> defines
///     it and exit codes keep mapping from the outcome alone; folding one into the other would make a caller
///     read a verdict out of a payload.
///     <para>
///         <see cref="Finding" /> is typed but not type-parameterized, because the dispatcher holds a
///         heterogeneous set of actions and can only hold one shape of operation. Making the operation interface
///         generic in its result would split the public surface in two and force an operation with nothing
///         structured to say to invent a payload; carrying the finding as an optional value lets that operation
///         carry none, which is an answer rather than a failure.
///     </para>
/// </remarks>
/// <param name="Outcome">What the operation concluded about the invocation.</param>
/// <param name="Finding">
///     What the operation found, as the typed value it computed, or null when it has nothing structured to
///     report. Null is deliberate and is never an error: it says this operation's whole answer is its outcome
///     and the text it rendered.
/// </param>
public sealed record OperationResult(OperationOutcome Outcome, object? Finding = null)
{
    /// <summary>
    ///     The finding as <typeparamref name="T" />, or null when there is none or it is something else.
    /// </summary>
    /// <remarks>
    ///     The typed read a composing caller actually wants: it knows which operation it invoked and therefore
    ///     which result type to ask for, and a caller that guesses wrong gets null rather than an exception,
    ///     because "this operation found nothing of that shape" is the same answer in both cases.
    /// </remarks>
    /// <typeparam name="T">The result type the caller expects this operation to compute.</typeparam>
    /// <returns>The finding, or null.</returns>
    public T? FindingAs<T>() where T : class => Finding as T;
}
