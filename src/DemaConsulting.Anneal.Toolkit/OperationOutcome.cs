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
    Refused
}
