using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Recording;

/// <summary>
///     One message as it was sent to a model, kept in the form the model saw it.
/// </summary>
/// <param name="Role">The speaker — <c>system</c>, <c>user</c> or <c>assistant</c>. Never null.</param>
/// <param name="Text">What that speaker said, verbatim and untruncated. Never null.</param>
public sealed record TranscriptMessage(string Role, string Text);

/// <summary>
///     The record of one model interaction: what was sent, what came back, which model answered and what it
///     cost.
/// </summary>
/// <remarks>
///     This is the only evidence the Toolkit produces that cannot be reconstructed by re-running something. A
///     build, a deterministic check and a report can all be regenerated; a model asked the same question
///     tomorrow may answer differently, so the exchange behind a judgement exists only in the moment it was
///     made. It is therefore recorded for every interaction — the ones that answered as much as the ones that
///     failed — rather than only where something already looked wrong, because the case a later audit exists
///     to catch is the confidently wrong answer, which is silent by construction.
/// </remarks>
/// <param name="At">When the interaction was completed or failed.</param>
/// <param name="Activity">The kind of call: the name of a <see cref="ModelActivity" />.</param>
/// <param name="Role">The capability role asked for: the name of a <see cref="ModelRole" />.</param>
/// <param name="Model">
///     The concrete model the role resolved to — the candidate that actually answered, not the one the
///     configuration lists first — so which model served a judgement stays auditable. Never null.
/// </param>
/// <param name="Prompt">Every message sent, in order. Never null and never empty.</param>
/// <param name="Reply">What the model replied, or null when the interaction produced no reply.</param>
/// <param name="Usage">What the interaction consumed, or null when the provider reported nothing.</param>
/// <param name="Result">
///     <see cref="Replied" /> when the model answered, <see cref="Failed" /> when it did not. Recorded as a
///     name so its meaning survives the set of results growing.
/// </param>
/// <param name="Failure">Why the interaction produced no reply, or null when it produced one.</param>
public sealed record ModelTranscript(
    DateTimeOffset At,
    string Activity,
    string Role,
    string Model,
    IReadOnlyList<TranscriptMessage> Prompt,
    string? Reply,
    ModelUsage? Usage,
    string Result,
    string? Failure)
{
    /// <summary>
    ///     The <see cref="Result" /> of an interaction the model answered.
    /// </summary>
    public const string Replied = "Replied";

    /// <summary>
    ///     The <see cref="Result" /> of an interaction that produced no reply, whatever the reason.
    /// </summary>
    public const string Failed = "Failed";
}
