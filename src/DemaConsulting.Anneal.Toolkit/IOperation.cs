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
    ///     The action's detailed usage: how it is invoked and every argument it takes, phrased as a caller
    ///     needs to read it to invoke the action correctly. This is the single declared source of that text —
    ///     <c>dotnet anneal help &lt;action&gt;</c> prints it, and the dispatcher renders it again when the
    ///     action is given arguments it cannot use — so the two renderings cannot state the invocation
    ///     differently or drift apart as the action changes.
    /// </summary>
    /// <remarks>
    ///     It is required rather than given a default that falls back to <see cref="Summary" />, and the
    ///     distinction is deliberate: a default would let an operation ship its one-line purpose in place of
    ///     real usage with nothing to notice the substitution, which is the exact gap a single source exists
    ///     to close. The compiler therefore forces every implementer to author usage. <see cref="Summary" />
    ///     answers "what does this action do"; this answers "how do I invoke it"; the two are not
    ///     interchangeable, and an operation whose usage merely repeats its summary has failed to describe how
    ///     it is called.
    /// </remarks>
    string Usage { get; }

    /// <summary>
    ///     Runs the operation and reports what it concluded.
    /// </summary>
    /// <param name="arguments">
    ///     The arguments following the action name, in the order given, never null and possibly empty. An
    ///     implementation validates them itself and reports <see cref="OperationOutcome.UsageError" /> rather
    ///     than throwing when they are unusable; it does not write its own usage text on that path, because the
    ///     dispatcher renders <see cref="Usage" /> — the single declared source — so the usage a caller is
    ///     shown after a misuse is the same text <c>help &lt;action&gt;</c> prints.
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
