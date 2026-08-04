using DemaConsulting.Anneal.Toolkit.Operations;

namespace DemaConsulting.Anneal.Toolkit;

/// <summary>
///     The tool's entry surface: it resolves the action named first on the command line, runs it, and turns
///     the outcome into a process exit code.
/// </summary>
/// <remarks>
///     Dispatch is separated from every operation so that the two rules a consumer depends on — an unknown
///     action is discoverable rather than silent, and only an enforcement operation may fail a build — are
///     implemented once instead of once per action. The operation set is injectable for the same reason:
///     the gating rule is a property of the dispatcher, so it has to be provable against categories no
///     shipped operation currently uses.
///     <para>
///         Thread safety: stateless and safe to call concurrently, provided the supplied writers are.
///     </para>
/// </remarks>
public static class AnnealTool
{
    /// <summary>
    ///     Exit code when the operation succeeded, or failed without gating.
    /// </summary>
    public const int ExitSuccess = 0;

    /// <summary>
    ///     Exit code when an enforcement operation failed. This is the only code that stops a build.
    /// </summary>
    public const int ExitGatedFailure = 1;

    /// <summary>
    ///     Exit code when the tool could not act on the invocation at all: no action was named, the named action
    ///     does not exist, or the action could not use the arguments given. Distinct from
    ///     <see cref="ExitGatedFailure" /> and <see cref="ExitRefused" /> so a caller can tell "I typed it wrong"
    ///     from "the check found a problem" and from "the question was not answerable".
    /// </summary>
    /// <remarks>
    ///     It is reached whatever category the named action declares, because no outcome was reached for a
    ///     category to weigh. A non-gating category must never turn the caller's own mistake into a zero exit.
    /// </remarks>
    public const int ExitUsageError = 2;

    /// <summary>
    ///     Exit code when the operation refused: the question could not be answered on the available evidence.
    /// </summary>
    /// <remarks>
    ///     Its own code because refusal is neither success nor failure, and a caller that cannot tell them apart
    ///     will read a refusal as an answer. It is never <see cref="ExitGatedFailure" />, whatever the
    ///     operation's category: refusing to guess is not a verdict a build may be failed on.
    /// </remarks>
    public const int ExitRefused = 3;

    /// <summary>
    ///     The operations this tool ships. Each name in this list is a promise: an agent that invokes an
    ///     action by name depends on it, which is why the set is enumerated in the Toolkit contract rather
    ///     than left open.
    /// </summary>
    public static IReadOnlyList<IOperation> DefaultOperations { get; } =
        [new VerifyEvidenceOperation(), new ProbeRuleOwnerOperation()];

    /// <summary>
    ///     Runs the action named by the first argument against the operations this tool ships.
    /// </summary>
    /// <param name="arguments">
    ///     The command line, action first. Must not be null. An empty list is a usage error, not a default
    ///     action: guessing what an unattended caller meant is how a tool runs the wrong check.
    /// </param>
    /// <param name="output">Where the action list, and everything the operation reports, is written. Must not be null.</param>
    /// <returns>
    ///     <see cref="ExitSuccess" />, <see cref="ExitGatedFailure" />, <see cref="ExitUsageError" /> or
    ///     <see cref="ExitRefused" />, mapped as the three-argument overload documents.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="arguments" /> or <paramref name="output" /> is null.</exception>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output) =>
        Run(arguments, output, DefaultOperations);

    /// <summary>
    ///     Runs the action named by the first argument against a caller-supplied set of operations.
    /// </summary>
    /// <remarks>
    ///     <c>help</c> and <c>help &lt;action&gt;</c> are handled here, before dispatch, and are the only
    ///     invocations that reach the action list on a success exit; every other path to it is a usage error.
    /// </remarks>
    /// <param name="arguments">
    ///     The command line, action first. Must not be null. An empty list is a usage error.
    /// </param>
    /// <param name="output">Where the action list, and everything the operation reports, is written. Must not be null.</param>
    /// <param name="operations">
    ///     The operations to dispatch against. Must not be null; names are matched case-insensitively, and an
    ///     empty set means every action is unknown.
    /// </param>
    /// <returns>
    ///     <see cref="ExitSuccess" /> when the operation succeeded, or when it ran, failed, and its category does
    ///     not gate; <see cref="ExitRefused" /> when the operation refused; <see cref="ExitGatedFailure" /> when
    ///     a failing operation declares <see cref="OperationCategory.Enforcement" />; <see cref="ExitUsageError" />
    ///     when no action was named, the named action does not exist, or the action could not use the arguments
    ///     given — the last of those whatever category the action declares.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, IReadOnlyList<IOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(operations);

        // No action at all is the same failure as an unrecognized one: in both cases the caller does not yet
        // know what this tool offers, and the repair is the same list. Bare "anneal" is deliberately not an
        // alias for "help" - it stays the usage error TOOLKIT-10 fixes, so a script that omits its action can
        // still detect the omission by exit code rather than reading a zero and a help screen.
        if (arguments.Count == 0)
        {
            output.WriteLine("anneal: no action named.");
            WriteAvailableActions(output, operations);
            return ExitUsageError;
        }

        // "help" is a dispatcher verb, handled before dispatch rather than shipped as an operation: it lists
        // the whole operation set, which no operation is given, and it must exit 0 and never gate, which is
        // guaranteed by keeping it outside the outcome-and-category machinery entirely.
        if (string.Equals(arguments[0], "help", StringComparison.OrdinalIgnoreCase))
            return RunHelp([.. arguments.Skip(1)], output, operations);

        var action = arguments[0];
        var operation = operations.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, action, StringComparison.OrdinalIgnoreCase));

        if (operation is null)
        {
            output.WriteLine($"anneal: unknown action '{action}'.");
            WriteAvailableActions(output, operations);
            return ExitUsageError;
        }

        var outcome = operation.Execute([.. arguments.Skip(1)], output);
        if (outcome == OperationOutcome.Succeeded)
            return ExitSuccess;

        // A misuse is not an outcome. The operation never ran, so the gating rule has nothing to weigh, and the
        // caller gets the same code an unknown action already produces - whatever category was named. Reading
        // the category first is exactly the fail-open path this short-circuit removes.
        if (outcome == OperationOutcome.UsageError)
        {
            output.WriteLine($"anneal: '{operation.Name}' was given arguments it cannot use, so no check ran.");

            // Render the operation's single declared usage, the same text "help <action>" prints, so the two
            // cannot state the invocation differently. The operation itself writes no usage line.
            output.WriteLine(operation.Usage);
            return ExitUsageError;
        }

        // Refusal short-circuits the gating rule entirely. It is not a verdict, so no category may turn it into
        // one, and it gets its own code so a caller reading only the exit status cannot mistake it for an answer.
        if (outcome == OperationOutcome.Refused)
        {
            output.WriteLine($"anneal: '{operation.Name}' refused - the question was not answerable.");
            return ExitRefused;
        }

        // The category decides, not the operation and not the exit code it would have liked. A non-gating
        // failure still says so, because a caller who cannot see the outcome in the exit code must be able
        // to read it.
        if (operation.Category != OperationCategory.Enforcement)
        {
            output.WriteLine(
                $"anneal: '{operation.Name}' failed, and does not gate ({Describe(operation.Category)}).");
            return ExitSuccess;
        }

        return ExitGatedFailure;
    }

    /// <summary>
    ///     Serves the discovery path: <c>help</c> lists every shipped action, and <c>help &lt;action&gt;</c>
    ///     prints one action's detailed usage. Both exit <see cref="ExitSuccess" />; the discovery path never
    ///     gates and never fails.
    /// </summary>
    /// <remarks>
    ///     <c>help</c> reuses the very listing an unknown action produces, now reached on a success path
    ///     instead of only an error one, so the surface a caller learns deliberately and the surface a mistake
    ///     reveals are one text. A topic the action list does not contain — including <c>help</c> itself,
    ///     which is a dispatcher verb and not a shipped action, so <c>help help</c> names nothing — is the
    ///     usage error TOOLKIT-10 defines, repaired with that same list, so <c>help</c> never fabricates
    ///     guidance for a surface that does not exist.
    /// </remarks>
    private static int RunHelp(IReadOnlyList<string> topics, TextWriter output, IReadOnlyList<IOperation> operations)
    {
        // Bare "help": the whole surface, on a success exit rather than only when a caller errs.
        if (topics.Count == 0)
        {
            WriteAvailableActions(output, operations);
            return ExitSuccess;
        }

        // "help" describes one action at a time; more than one topic is a misuse, not a request to describe.
        if (topics.Count > 1)
        {
            output.WriteLine("anneal: 'help' describes one action at a time; name a single action, or none.");
            WriteAvailableActions(output, operations);
            return ExitUsageError;
        }

        var requested = topics[0];
        var operation = operations.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, requested, StringComparison.OrdinalIgnoreCase));

        // An unknown topic - which "help" itself is, being no operation - is the usage error TOOLKIT-10 fixes,
        // reported with the same action list an unknown action already produces.
        if (operation is null)
        {
            output.WriteLine($"anneal: unknown action '{requested}'.");
            WriteAvailableActions(output, operations);
            return ExitUsageError;
        }

        output.WriteLine(operation.Usage);
        return ExitSuccess;
    }

    private static void WriteAvailableActions(TextWriter output, IReadOnlyList<IOperation> operations)
    {
        if (operations.Count == 0)
        {
            output.WriteLine("No actions are available.");
            return;
        }

        output.WriteLine("Available actions:");
        foreach (var operation in operations.OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
            output.WriteLine($"  {operation.Name} - {operation.Summary} [{Describe(operation.Category)}]");
    }

    private static string Describe(OperationCategory category) =>
        category.ToString().ToLowerInvariant();
}
