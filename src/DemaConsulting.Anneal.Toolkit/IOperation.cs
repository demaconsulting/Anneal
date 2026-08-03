namespace DemaConsulting.Anneal.Toolkit;

/// <summary>
///     One action of the tool: the unit a caller names on the command line as <c>dotnet anneal &lt;action&gt;</c>.
/// </summary>
/// <remarks>
///     The interface is public so that a consumer can compose the tool with its own operations, and so that
///     the gating rule can be exercised through the same surface a real operation uses rather than through an
///     internal hook. An implementation is expected to be stateless and safe to invoke once per process.
/// </remarks>
public interface IOperation
{
    /// <summary>
    ///     The action name a caller types. Matched case-insensitively, and listed verbatim when a caller names
    ///     an action that does not exist.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     The single category this operation declares. It alone decides whether a failing outcome gates a
    ///     build; see <see cref="OperationCategory" />.
    /// </summary>
    OperationCategory Category { get; }

    /// <summary>
    ///     One line describing what the action does, shown in the action list. It is the only description a
    ///     caller who has not read the source will see, so it states the operation's purpose rather than its
    ///     arguments.
    /// </summary>
    string Summary { get; }

    /// <summary>
    ///     Runs the operation and reports what it concluded.
    /// </summary>
    /// <param name="arguments">
    ///     The arguments following the action name, in the order given, never null and possibly empty. An
    ///     implementation validates them itself and reports <see cref="OperationOutcome.UsageError" /> rather
    ///     than throwing when they are unusable, stating the form it expects as it does so.
    /// </param>
    /// <param name="output">
    ///     Where the operation writes its findings. Must not be null. Everything a caller is meant to read
    ///     goes here, so that a test and a terminal see identical output.
    /// </param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> when the operation answered its question and the answer
    ///     is positive; <see cref="OperationOutcome.Refused" /> when the question could not be answered on the
    ///     available evidence; <see cref="OperationOutcome.UsageError" /> when the arguments could not be used
    ///     and nothing was attempted; <see cref="OperationOutcome.Failed" /> when the operation ran and the
    ///     answer is no. The caller maps this to an exit code using <see cref="Category" />, except for a usage
    ///     error, which is the caller's own mistake and is category-independent; the operation never decides
    ///     that itself.
    /// </returns>
    OperationOutcome Execute(IReadOnlyList<string> arguments, TextWriter output);
}
