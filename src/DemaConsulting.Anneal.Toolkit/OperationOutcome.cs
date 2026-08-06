namespace DemaConsulting.Anneal.Toolkit;

/// <summary>
///     What an operation concluded, independent of whether that conclusion gates a build.
/// </summary>
/// <remarks>
///     Kept apart from the process exit code on purpose: the exit code is a function of this outcome and the
///     operation's <see cref="OperationCategory" />, so an operation states what it found and never decides
///     whether the build should stop.
/// </remarks>
public enum OperationOutcome
{
    /// <summary>
    ///     The operation answered the question it was given.
    /// </summary>
    Succeeded,

    /// <summary>
    ///     The operation ran and the answer is negative: the condition it checks does not hold. It is distinct
    ///     from an unanswerable question, which is refusal, and from arguments it could not act on at all,
    ///     which is <see cref="UsageError" />.
    /// </summary>
    Failed,

    /// <summary>
    ///     The arguments could not be used, so the operation never ran and has nothing to report.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Failed" /> because the gating rule has nothing to weigh here: no answer was
    ///     attempted, so no category may turn the caller's mistake into a passing check. A research operation
    ///     given the wrong argument form once exited zero on this path, which let an unattended caller read its
    ///     own error as a check that ran and found nothing.
    /// </remarks>
    UsageError,

    /// <summary>
    ///     The question could not be answered on the available evidence, and the operation declines to guess.
    /// </summary>
    /// <remarks>
    ///     Refusal is not failure and not a negative answer: "the rule is stated in three files, so no single
    ///     file owns it" is a true and useful report, while returning one of the three would be a confident
    ///     wrong answer that nothing downstream could detect. A deterministic check has no honest way to reach
    ///     this — its inputs settle its question — which is why it arrived with the first model-backed
    ///     operation rather than with the tool itself.
    /// </remarks>
    Refused,

    /// <summary>
    ///     The operation ran, could not finish, and the reason is that finishing needs a decision only the user
    ///     can make.
    /// </summary>
    /// <remarks>
    ///     Escalation is <see cref="Refused" /> one level up. Refusal says a <em>model call</em> could not be
    ///     answered on the evidence; escalation says an <em>operation</em> cannot proceed without the user, and
    ///     both are distinct from failure for the same reason: a caller who cannot tell them apart acts on the
    ///     wrong one. The forcing case is a repair that requires changing a protected configuration file. An
    ///     operation compiled as success-or-failure would either grind its budget and report failure, which
    ///     blames the code for a configuration decision, or edit around the obstacle, which is worse because it
    ///     looks like success. Neither says the one useful thing: *this needs you*.
    ///     <para>
    ///         It never gates. Like refusal, it is not a verdict on the repository, so no category may turn it
    ///         into one.
    ///     </para>
    /// </remarks>
    Escalated
}
