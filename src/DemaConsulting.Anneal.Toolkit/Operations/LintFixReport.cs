namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     What a <c>lint-fix</c> run concluded, as data beside its outcome: how much of the budget it spent, what
///     lint still reported, and what — if anything — it needed the user for.
/// </summary>
/// <remarks>
///     Carried so a composing caller reads what happened rather than re-parsing the lines rendered for a person.
///     The escalation fields are the ones that earn the type: "the repository is not clean" is already in the
///     outcome, while "it is not clean because a repair needs a protected file changed, and here is which one"
///     is the thing a caller has to act on and cannot recover from an exit code.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="Iterations">How many worker iterations were spent. Zero when the repository was already clean.</param>
/// <param name="RemainingOutput">
///     What <c>lint.ps1</c> reported on the last run, or the empty string when it reported nothing because it
///     passed. Never null.
/// </param>
/// <param name="RefusedWrites">
///     The refused tool calls, each rendered as the tool name and the arguments the model supplied, in the
///     order it attempted them. Never null; empty on every path except escalation.
/// </param>
/// <param name="ContractFailures">
///     The contract-check errors found in the lint output, which are not lint issues and stop the run. Never
///     null; empty unless the run stopped for them.
/// </param>
public sealed record LintFixReport(
    int Iterations,
    string RemainingOutput,
    IReadOnlyList<string> RefusedWrites,
    IReadOnlyList<string> ContractFailures);
