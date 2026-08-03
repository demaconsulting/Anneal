namespace DemaConsulting.Anneal.Toolkit;

/// <summary>
///     What an operation concluded, independent of whether that conclusion gates a build.
/// </summary>
/// <remarks>
///     Kept apart from the process exit code on purpose: the exit code is a function of this outcome and the
///     operation's <see cref="OperationCategory" />, so an operation states what it found and never decides
///     whether the build should stop. The set is not closed — <c>TOOLKIT-06</c> adds refusal, which a
///     deterministic operation has no honest way to reach, so it lands with the first model-backed one.
/// </remarks>
public enum OperationOutcome
{
    /// <summary>
    ///     The operation answered the question it was given.
    /// </summary>
    Succeeded,

    /// <summary>
    ///     The operation ran and the answer is negative: the condition it checks does not hold, or the inputs
    ///     it was given are not usable. It is distinct from an unanswerable question, which is refusal.
    /// </summary>
    Failed
}
