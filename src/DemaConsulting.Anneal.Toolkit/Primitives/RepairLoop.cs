namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     Executes one repair attempt over the current state, given the fixes the last verification demanded.
/// </summary>
/// <typeparam name="TState">The state the loop carries between attempts.</typeparam>
/// <param name="state">The state before this attempt.</param>
/// <param name="requiredFixes">
///     The fixes the previous <see cref="RepairLoopVerify{TState}" /> call demanded, or empty on the first
///     attempt. Never null.
/// </param>
/// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
/// <returns>The result of this attempt, whose finding becomes the next state on success.</returns>
internal delegate Task<StepResult<TState>> RepairLoopExecute<TState>(
    TState state, IReadOnlyList<string> requiredFixes, CancellationToken cancellationToken);

/// <summary>Verifies the current state and reports what, if anything, still needs fixing.</summary>
/// <typeparam name="TState">The state the loop carries between attempts.</typeparam>
/// <param name="state">The state to verify.</param>
/// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
/// <returns>The verification result.</returns>
internal delegate Task<StepResult<VerificationFinding>> RepairLoopVerify<TState>(
    TState state, CancellationToken cancellationToken);

/// <summary>
///     Bounds a repair to the single primitive that produced the finding being repaired, rather than restarting a
///     worker from the top.
/// </summary>
/// <remarks>
///     This is the "ownership-directed" repair <c>docs/architecture/process.md</c> § Decisions describes: a
///     documentation finding is sent back through the same <see cref="DocumentAuthor" /> call, a code finding
///     through the same <see cref="Developer" /> call, each spending the one shared budget this loop enforces.
///     What makes that possible without this type knowing anything about <see cref="DocumentAuthor" /> or
///     <see cref="Developer" /> is that <c>execute</c> already <em>is</em> that call, closed over by
///     the caller composing the loop — this type only counts attempts and reads
///     <see cref="VerificationFinding.RequiredFixes" /> back into the next attempt.
///     <para>
///         Execution failing outright — <see cref="OperationOutcome.Failed" />,
///         <see cref="OperationOutcome.UsageError" />, or <see cref="OperationOutcome.Escalated" /> — ends the loop
///         immediately and is returned as-is: there is nothing to verify when the step that was meant to produce
///         the state did not.
///     </para>
///     <para>Thread safety: instances are immutable and safe to share; a run mutates no shared state of its own.</para>
/// </remarks>
/// <typeparam name="TState">The state carried between the execute and verify steps.</typeparam>
internal sealed class RepairLoop<TState>
{
    private readonly int _maxRepairAttempts;

    /// <summary>
    ///     Binds a repair loop to its budget.
    /// </summary>
    /// <param name="maxRepairAttempts">
    ///     The most repair attempts spent after the first execution before the loop reports
    ///     <see cref="OperationOutcome.Failed" />. Must be zero or greater; zero means one execution and one
    ///     verification, with no repair spent at all.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxRepairAttempts" /> is negative.</exception>
    public RepairLoop(int maxRepairAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRepairAttempts);
        _maxRepairAttempts = maxRepairAttempts;
    }

    /// <summary>
    ///     Runs the loop from an initial state until verification passes or the repair budget is spent.
    /// </summary>
    /// <param name="initialState">The state before the first execution.</param>
    /// <param name="execute">The step that produces the next state from the current one. Must not be null.</param>
    /// <param name="verify">The step that judges the produced state. Must not be null.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> with the passing state when verification reaches
    ///     <see cref="VerificationVerdict.Passed" />; the execute step's own result, unchanged, when execution
    ///     itself did not succeed; the verify step's own result, unchanged, when verification is
    ///     <see cref="OperationOutcome.Escalated" /> or <see cref="OperationOutcome.Refused" />, because neither is
    ///     a repair this loop may spend budget chasing; <see cref="OperationOutcome.Failed" /> with the last state
    ///     reached when the repair budget is spent with verification still not passing.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execute" /> or <paramref name="verify" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<TState>> RunAsync(
        TState initialState,
        RepairLoopExecute<TState> execute,
        RepairLoopVerify<TState> verify,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(verify);

        var state = initialState;
        IReadOnlyList<string> fixes = [];

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var executed = await execute(state, fixes, cancellationToken).ConfigureAwait(false);
            if (executed.Outcome != OperationOutcome.Succeeded || executed.Finding is null)
                return executed;

            state = executed.Finding;

            var verified = await verify(state, cancellationToken).ConfigureAwait(false);

            if (verified.Outcome == OperationOutcome.Succeeded &&
                verified.Finding?.Verdict == VerificationVerdict.Passed)
                return new StepResult<TState>(OperationOutcome.Succeeded, state, []);

            if (verified.Outcome is OperationOutcome.Escalated or OperationOutcome.Refused)
                return new StepResult<TState>(verified.Outcome, state, verified.Notes);

            if (attempt == _maxRepairAttempts)
                return new StepResult<TState>(
                    OperationOutcome.Failed,
                    state,
                    [new ProcessNote($"repair budget of {_maxRepairAttempts} attempt(s) exhausted")]);

            fixes = verified.Finding?.RequiredFixes ?? [];
        }
    }
}
