namespace DemaConsulting.Anneal.Toolkit.Recording;

/// <summary>
///     The record of one tool invocation a model made: which tool, with what arguments, and what came back —
///     including when the tool refused.
/// </summary>
/// <remarks>
///     A model transcript records the prompt sent and the reply received. When the provider runs the tool loop
///     itself, everything the model actually <em>did</em> happens between those two, so a transcript of the
///     conversation alone would record a writing worker's question and its final summary while being blind to
///     every file it touched — omitting the only part of that worker's behavior worth auditing.
///     <para>
///         A refused call is recorded on the same footing as a completed one, and that is the point rather than
///         thoroughness. "This write was attempted and refused" is a fact; a worker's own account of having
///         respected a protected file is a claim. Only the first can be checked, and only the first can be the
///         basis on which an operation escalates.
///     </para>
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="At">When the invocation completed, was refused, or faulted.</param>
/// <param name="Tool">The tool name as the model called it. Never null.</param>
/// <param name="Arguments">
///     The arguments the model supplied, rendered as JSON, verbatim and untruncated. Never null: a call with no
///     arguments records an empty object rather than nothing.
/// </param>
/// <param name="Result">
///     How the call ended — one of the classifications defined on <c>ToolReply</c>. Recorded as a name so its
///     meaning survives the set of classifications growing.
/// </param>
/// <param name="Outcome">
///     What the tool returned to the model, or the fault message when it threw. Never null; this is the text the
///     model went on to reason from, which is what makes it evidence rather than a summary.
/// </param>
public sealed record ToolCallTranscript(
    DateTimeOffset At,
    string Tool,
    string Arguments,
    string Result,
    string Outcome);
