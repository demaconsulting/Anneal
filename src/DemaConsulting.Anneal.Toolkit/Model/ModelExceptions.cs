namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     Thrown when no model can be reached, so an operation that needs a judgement cannot obtain one.
/// </summary>
/// <remarks>
///     This is the type that makes "offline is a failure, not a degraded mode" enforceable rather than merely
///     stated. Every provider translates its own transport, authentication and configuration failures into this
///     one exception, so an operation can report the cause by name without knowing which provider it was talking
///     to — and, more importantly, cannot accidentally handle the failure by substituting a weaker deterministic
///     answer, because there is no deterministic answer on this path to fall back to.
/// </remarks>
public sealed class ModelUnavailableException : Exception
{
    /// <summary>
    ///     Creates the exception with a message naming the cause a caller should report.
    /// </summary>
    /// <param name="message">
    ///     Human-readable description naming why no model could be reached. It is written verbatim into the
    ///     operation's output, so it states the cause rather than the symptom.
    /// </param>
    public ModelUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Creates the exception with a message naming the cause and the underlying provider failure.
    /// </summary>
    /// <param name="message">Human-readable description naming why no model could be reached.</param>
    /// <param name="innerException">The provider failure this was translated from.</param>
    public ModelUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
///     Thrown when a model's reply could not be decoded into the requested type within the retry budget.
/// </summary>
/// <remarks>
///     A described schema is a prompt-level hint rather than a transport guarantee, so a reply that does not
///     decode is an expected outcome and not a defect. The probe path therefore re-prompts with the model's own
///     mistake first, and raises this only once the budget is spent — at which point the operation fails and no
///     partially populated result is handed to a caller.
/// </remarks>
public sealed class ModelParseException : Exception
{
    /// <summary>
    ///     Creates the exception describing the exhausted retry budget and the last parse error.
    /// </summary>
    /// <param name="message">Human-readable description of what could not be decoded, and after how many attempts.</param>
    /// <param name="innerException">The last decode error encountered.</param>
    public ModelParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
