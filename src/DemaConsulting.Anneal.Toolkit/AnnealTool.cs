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
    ///     Exit code when no action was named, or the named action does not exist. Distinct from
    ///     <see cref="ExitGatedFailure" /> so a caller can tell "I typed it wrong" from "the check found a
    ///     problem".
    /// </summary>
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
    ///     <see cref="ExitSuccess" />, <see cref="ExitGatedFailure" /> or <see cref="ExitUsageError" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="arguments" /> or <paramref name="output" /> is null.</exception>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output) =>
        Run(arguments, output, DefaultOperations);

    /// <summary>
    ///     Runs the action named by the first argument against a caller-supplied set of operations.
    /// </summary>
    /// <param name="arguments">
    ///     The command line, action first. Must not be null. An empty list is a usage error.
    /// </param>
    /// <param name="output">Where the action list, and everything the operation reports, is written. Must not be null.</param>
    /// <param name="operations">
    ///     The operations to dispatch against. Must not be null; names are matched case-insensitively, and an
    ///     empty set means every action is unknown.
    /// </param>
    /// <returns>
    ///     <see cref="ExitSuccess" /> when the operation succeeded, or when it failed and its category does
    ///     not gate; <see cref="ExitRefused" /> when the operation refused; <see cref="ExitGatedFailure" /> when
    ///     a failing operation declares <see cref="OperationCategory.Enforcement" />; <see cref="ExitUsageError" />
    ///     when no action was named or the named action does not exist.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, IReadOnlyList<IOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(operations);

        // No action at all is the same failure as an unrecognized one: in both cases the caller does not yet
        // know what this tool offers, and the repair is the same list.
        if (arguments.Count == 0)
        {
            output.WriteLine("anneal: no action named.");
            WriteAvailableActions(output, operations);
            return ExitUsageError;
        }

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
