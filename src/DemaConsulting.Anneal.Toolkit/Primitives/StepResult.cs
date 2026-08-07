namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     What one primitive step produced: the outcome it reached, the typed finding beside it, and anything worth
///     noting that is not the finding itself.
/// </summary>
/// <remarks>
///     The primitive-level counterpart to <see cref="OperationResult" />, and deliberately built the same way:
///     outcome and finding are peers, never folded into one another, so a composing caller reads a verdict and a
///     value without parsing either out of the other. It stays internal because nothing outside the Toolkit
///     assembly calls a primitive directly — a primitive is vocabulary a compiled process composes, not a surface
///     a consumer invokes on its own, per <c>docs/architecture/toolkit.md</c> § Composition.
///     <para>
///         <see cref="Outcome" /> reuses <see cref="OperationOutcome" /> unchanged rather than growing a
///         primitive-specific vocabulary: a typed finding such as a <c>Reroute</c> case is a primitive
///         successfully answering its own question, not a new kind of invocation result, which is the same
///         reasoning <c>docs/architecture/toolkit.md</c> § Decisions gives for the compiled catalog as a whole.
///     </para>
/// </remarks>
/// <typeparam name="TFinding">The typed value this step computes.</typeparam>
/// <param name="Outcome">What the step concluded.</param>
/// <param name="Finding">
///     What the step found, or null when it has nothing structured to report — which on a failing or refused
///     path is an honest answer, not an omission.
/// </param>
/// <param name="Notes">Anything worth a reader's attention beside the finding. Never null; empty when there is none.</param>
internal sealed record StepResult<TFinding>(
    OperationOutcome Outcome,
    TFinding? Finding,
    IReadOnlyList<ProcessNote> Notes);

/// <summary>
///     One observation a primitive attaches to its own <see cref="StepResult{TFinding}" />: something worth a
///     reader's attention that is not itself the finding.
/// </summary>
/// <remarks>
///     Deliberately thin — a primitive's typed finding is where a real answer lives, and
///     <see cref="ProcessNote" /> exists only for the handful of things a caller should see even when there is no
///     finding to attach them to: why a probe was refused, why a budget was exhausted, what a script reported
///     when it faulted. A primitive that has something structured to say defines its own finding type instead of
///     growing this one a field at a time.
/// </remarks>
/// <param name="Text">What is worth noting, in a sentence a person reads directly. Never null.</param>
internal sealed record ProcessNote(string Text);
