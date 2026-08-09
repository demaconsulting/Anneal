using System.Diagnostics;
using System.Reflection;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Recording;

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
    ///     Exit code when the operation escalated: it ran, could not finish, and finishing needs a decision only
    ///     the user can make.
    /// </summary>
    /// <remarks>
    ///     Its own code for the same reason refusal has one, one level up. A caller that read an escalation as a
    ///     failure would look for a defect in the repository, and one that read it as success would ship without
    ///     the decision ever being made. It is never <see cref="ExitGatedFailure" />, whatever the operation's
    ///     category: needing the user is not a verdict a build may be failed on.
    /// </remarks>
    public const int ExitEscalated = 4;

    /// <summary>
    ///     The operations this tool ships. Each name in this list is a promise: an agent that invokes an
    ///     action by name depends on it, which is why the set is enumerated in the Toolkit contract rather
    ///     than left open.
    /// </summary>
    public static IReadOnlyList<IOperation> DefaultOperations { get; } =
    [
        new VerifyEvidenceOperation(),
        new ProbeRuleOwnerOperation(),
        new CheckContractsOperation(),
        new LintFixOperation(),
        new StatsOperation(),
        new RouteOperation(),
        new MaintainOperation(),
        new StageContractOperation(),
        new VerifyChangeOperation()
    ];

    /// <summary>
    ///     The Anneal version this tool was built from.
    /// </summary>
    /// <remarks>
    ///     Read from the built assembly rather than written in the source, so it cannot state one version while
    ///     the payload beside it is another. It is what makes an installed payload identifiable by version
    ///     instead of inferred from its contents: <c>dotnet anneal version</c> reports it, and every invocation
    ///     record carries it, so a repository that has run the tool once can say which version produced what is
    ///     in it.
    /// </remarks>
    public static string Version { get; } =
        typeof(AnnealTool).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(AnnealTool).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>
    ///     Runs the action named by the first argument against the operations this tool ships.
    /// </summary>
    /// <param name="arguments">
    ///     The command line, action first. Must not be null. An empty list is a usage error, not a default
    ///     action: guessing what an unattended caller meant is how a tool runs the wrong check.
    /// </param>
    /// <param name="output">Where the action list, and everything the operation reports, is written. Must not be null.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged into the action it dispatches to.</param>
    /// <returns>
    ///     <see cref="ExitSuccess" />, <see cref="ExitGatedFailure" />, <see cref="ExitUsageError" />,
    ///     <see cref="ExitRefused" /> or <see cref="ExitEscalated" />, mapped as the four-argument overload
    ///     documents.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="arguments" /> or <paramref name="output" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public static Task<int> RunAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken) =>
        RunAsync(arguments, output, DefaultOperations, cancellationToken);

    /// <summary>
    ///     Runs the action named by the first argument against a caller-supplied set of operations.
    /// </summary>
    /// <remarks>
    ///     <c>help</c> and <c>help &lt;action&gt;</c> are handled here, before dispatch, and are the only
    ///     invocations that reach the action list on a success exit; every other path to it is a usage error.
    ///     <para>
    ///         The caller's cancellation signal is passed to the action unchanged and none is substituted for
    ///         it, so a withdrawn invocation stops where it is rather than running to completion. A cancelled
    ///         invocation therefore produces no exit code at all: it reached no outcome, and inventing one would
    ///         let a caller read its own withdrawal as an answer.
    ///     </para>
    /// </remarks>
    /// <param name="arguments">
    ///     The command line, action first. Must not be null. An empty list is a usage error.
    /// </param>
    /// <param name="output">Where the action list, and everything the operation reports, is written. Must not be null.</param>
    /// <param name="operations">
    ///     The operations to dispatch against. Must not be null; names are matched case-insensitively, and an
    ///     empty set means every action is unknown.
    /// </param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged into the action it dispatches to.</param>
    /// <returns>
    ///     <see cref="ExitSuccess" /> when the operation succeeded, or when it ran, failed, and its category does
    ///     not gate; <see cref="ExitRefused" /> when the operation refused; <see cref="ExitEscalated" /> when it
    ///     escalated; <see cref="ExitGatedFailure" /> when
    ///     a failing operation declares <see cref="OperationCategory.Enforcement" />; <see cref="ExitUsageError" />
    ///     when no action was named, the named action does not exist, or the action could not use the arguments
    ///     given — the last of those whatever category the action declares.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        IReadOnlyList<IOperation> operations,
        CancellationToken cancellationToken) =>
        await RunAsync(arguments, output, operations, Directory.GetCurrentDirectory(), cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    ///     Runs the action named by the first argument, recording the invocation into a named repository.
    /// </summary>
    /// <remarks>
    ///     The root is where this invocation's record is appended, which is the only reason the dispatcher needs
    ///     one. It is a destination and not a switch: there is no value of it, and no other argument, that
    ///     leaves an invocation unrecorded.
    /// </remarks>
    /// <param name="arguments">The command line, action first. Must not be null.</param>
    /// <param name="output">Where everything a person reads is written. Must not be null.</param>
    /// <param name="operations">The operations to dispatch against. Must not be null.</param>
    /// <param name="repositoryRoot">
    ///     The repository this invocation belongs to, and under which its record is appended. Must not be null
    ///     or blank.
    /// </param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged into the action it dispatches to.</param>
    /// <returns>The exit code, mapped as the four-argument overload documents.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is empty or blank.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        IReadOnlyList<IOperation> operations,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var at = DateTimeOffset.UtcNow;
        var started = Stopwatch.GetTimestamp();

        // The scope is what lets the record below state what the invocation spent on models without every
        // operation signature carrying an accumulator to serve a fact only the dispatcher reports.
        using var scope = InvocationScope.Begin();

        var dispatched = await DispatchAsync(arguments, output, operations, cancellationToken)
            .ConfigureAwait(false);

        // An invocation withdrawn mid-flight never reaches here, and that is deliberate: it reached no outcome,
        // and a record whose outcome field had to be invented would be the first untrue row in a file whose
        // whole value is that it can be aggregated at face value.
        new RecordStore(repositoryRoot).Append(new InvocationRecord(
            at,
            Version,
            arguments.Count == 0 ? string.Empty : arguments[0],
            [.. arguments.Skip(1)],
            dispatched.Outcome.ToString(),
            dispatched.Category?.ToString(),
            dispatched.ExitCode,
            scope.Interactions,
            scope.Usage,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds));

        return dispatched.ExitCode;
    }

    /// <remarks>
    ///     Everything the dispatcher decides, separated from the recording of it so that no path can reach an
    ///     exit code without an outcome the record can state.
    /// </remarks>
    private static async Task<Dispatched> DispatchAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        IReadOnlyList<IOperation> operations,
        CancellationToken cancellationToken)
    {
        // No action at all is the same failure as an unrecognized one: in both cases the caller does not yet
        // know what this tool offers, and the repair is the same list. Bare "anneal" is deliberately not an
        // alias for "help" - it stays the usage error TOOLKIT-10 fixes, so a script that omits its action can
        // still detect the omission by exit code rather than reading a zero and a help screen.
        if (arguments.Count == 0)
        {
            output.WriteLine("anneal: no action named.");
            WriteAvailableActions(output, operations);
            return new Dispatched(ExitUsageError, OperationOutcome.UsageError, null);
        }

        // "help" is a dispatcher verb, handled before dispatch rather than shipped as an operation: it lists
        // the whole operation set, which no operation is given, and it must exit 0 and never gate, which is
        // guaranteed by keeping it outside the outcome-and-category machinery entirely.
        if (string.Equals(arguments[0], "help", StringComparison.OrdinalIgnoreCase))
            return RunHelp([.. arguments.Skip(1)], output, operations);

        // "version" is a dispatcher verb for the same reasons, and for one more: what it reports is a property
        // of the payload rather than of anything the tool does, so there is no repository state it could read
        // and no outcome it could reach other than success.
        if (string.Equals(arguments[0], "version", StringComparison.OrdinalIgnoreCase))
            return RunVersion([.. arguments.Skip(1)], output, operations);

        var action = arguments[0];
        var operation = operations.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, action, StringComparison.OrdinalIgnoreCase));

        if (operation is null)
        {
            output.WriteLine($"anneal: unknown action '{action}'.");
            WriteAvailableActions(output, operations);
            return new Dispatched(ExitUsageError, OperationOutcome.UsageError, null);
        }

        // The caller's token, unchanged. Substituting one here - or blocking on the result - would leave the
        // invocation uninterruptible for as long as a model takes to answer, which is the whole of TOOLKIT-I5.
        var result = await operation
            .ExecuteAsync([.. arguments.Skip(1)], output, cancellationToken)
            .ConfigureAwait(false);

        // The finding travels back to whoever holds the operation; a process exit code is one bit of it, so
        // this path reads the outcome only. Nothing here parses what was rendered.
        var outcome = result.Outcome;
        if (outcome == OperationOutcome.Succeeded)
            return new Dispatched(ExitSuccess, outcome, operation.Category);

        // A misuse is not an outcome. The operation never ran, so the gating rule has nothing to weigh, and the
        // caller gets the same code an unknown action already produces - whatever category was named. Reading
        // the category first is exactly the fail-open path this short-circuit removes.
        if (outcome == OperationOutcome.UsageError)
        {
            output.WriteLine($"anneal: '{operation.Name}' was given arguments it cannot use, so no check ran.");

            // Render the operation's single declared usage, the same text "help <action>" prints, so the two
            // cannot state the invocation differently. The operation itself writes no usage line.
            output.WriteLine(operation.Usage);
            return new Dispatched(ExitUsageError, outcome, operation.Category);
        }

        // Refusal short-circuits the gating rule entirely. It is not a verdict, so no category may turn it into
        // one, and it gets its own code so a caller reading only the exit status cannot mistake it for an answer.
        if (outcome == OperationOutcome.Refused)
        {
            output.WriteLine($"anneal: '{operation.Name}' refused - the question was not answerable.");
            return new Dispatched(ExitRefused, outcome, operation.Category);
        }

        // Escalation short-circuits the gating rule for the same reason refusal does, and renders distinctly
        // from both: a caller shown "failed" would go looking for a defect, when what is actually needed is a
        // decision only they can make.
        if (outcome == OperationOutcome.Escalated)
        {
            output.WriteLine(
                $"anneal: '{operation.Name}' escalated - it cannot finish without a decision you must make.");
            return new Dispatched(ExitEscalated, outcome, operation.Category);
        }

        // The category decides, not the operation and not the exit code it would have liked. A non-gating
        // failure still says so, because a caller who cannot see the outcome in the exit code must be able
        // to read it.
        if (operation.Category != OperationCategory.Enforcement)
        {
            output.WriteLine(
                $"anneal: '{operation.Name}' failed, and does not gate ({Describe(operation.Category)}).");
            return new Dispatched(ExitSuccess, outcome, operation.Category);
        }

        return new Dispatched(ExitGatedFailure, outcome, operation.Category);
    }

    /// <summary>
    ///     Reports the Anneal version the tool was built from.
    /// </summary>
    /// <remarks>
    ///     One line and nothing else, so a script reads it without parsing. A trailing argument is a misuse
    ///     rather than a request to describe some other payload's version: there is only one payload here, and
    ///     the only version it can honestly report is its own.
    /// </remarks>
    private static Dispatched RunVersion(
        IReadOnlyList<string> extra, TextWriter output, IReadOnlyList<IOperation> operations)
    {
        if (extra.Count > 0)
        {
            output.WriteLine("anneal: 'version' takes no arguments.");
            WriteAvailableActions(output, operations);
            return new Dispatched(ExitUsageError, OperationOutcome.UsageError, null);
        }

        output.WriteLine(Version);
        return new Dispatched(ExitSuccess, OperationOutcome.Succeeded, null);
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
    private static Dispatched RunHelp(
        IReadOnlyList<string> topics, TextWriter output, IReadOnlyList<IOperation> operations)
    {
        // Bare "help": the whole surface, on a success exit rather than only when a caller errs.
        if (topics.Count == 0)
        {
            WriteAvailableActions(output, operations);
            return new Dispatched(ExitSuccess, OperationOutcome.Succeeded, null);
        }

        // "help" describes one action at a time; more than one topic is a misuse, not a request to describe.
        if (topics.Count > 1)
        {
            output.WriteLine("anneal: 'help' describes one action at a time; name a single action, or none.");
            WriteAvailableActions(output, operations);
            return new Dispatched(ExitUsageError, OperationOutcome.UsageError, null);
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
            return new Dispatched(ExitUsageError, OperationOutcome.UsageError, null);
        }

        output.WriteLine(operation.Usage);
        return new Dispatched(ExitSuccess, OperationOutcome.Succeeded, null);
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
        output.WriteLine("Run 'help <action>' for detail on one.");
    }

    private static string Describe(OperationCategory category) =>
        category.ToString().ToLowerInvariant();

    /// <remarks>
    ///     What the dispatcher decided, in the terms the invocation record states it in. The outcome and the
    ///     category travel beside the exit code rather than being inferred back out of it, because the mapping
    ///     is deliberately lossy in both directions - a non-gating failure and a success share an exit code,
    ///     and a usage error can arrive with or without an operation to attribute it to.
    /// </remarks>
    private sealed record Dispatched(int ExitCode, OperationOutcome Outcome, OperationCategory? Category);
}
