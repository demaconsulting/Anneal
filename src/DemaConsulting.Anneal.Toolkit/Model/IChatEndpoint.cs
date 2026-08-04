using Microsoft.Extensions.AI;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     One assembled model turn: the ordered conversation to send, the tools the turn may use, and the ceiling
///     on how much the model may generate in reply.
/// </summary>
/// <remarks>
///     The record reuses Microsoft.Extensions.AI's <see cref="ChatMessage" /> and <see cref="AITool" /> as
///     transport types rather than inventing Toolkit equivalents, so a provider that already speaks those shapes
///     needs no translation layer. Nothing here says which provider will answer: that is the point of the seam.
/// </remarks>
/// <param name="Messages">
///     The assembled messages, in order, system message first. Must not be null and must not be empty.
/// </param>
/// <param name="Tools">
///     The tools this turn may use. Must not be null; an empty list means the turn gets no tools at all, which
///     is a stronger statement than "no preference" and is what a probe turn passes.
/// </param>
/// <param name="MaxOutputTokens">
///     The ceiling on generated output for this turn. Deliberately not optional: a reasoning model asked an open
///     question with no ceiling generates until it exhausts the context window, so every turn states a bound
///     rather than trusting the model to stop. Must be greater than zero.
/// </param>
/// <param name="Model">
///     The concrete model to drive, which the caller's capability role resolved to through the repository's own
///     configuration. Must not be null or blank. It travels with the turn rather than being fixed when an
///     endpoint is built, because the role-to-model mapping is data a repository owns and one provider serves
///     every role.
/// </param>
public sealed record ChatTurnRequest(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<AITool> Tools,
    int MaxOutputTokens,
    string Model);

/// <summary>
///     The result of one completed model turn: the assistant text, and what producing it consumed.
/// </summary>
/// <remarks>
///     The text is everything above the seam works from — the probe path runs its own tolerant extraction and
///     visible parse retry over exactly this string. The usage is carried beside it rather than folded into it
///     because nothing above the seam can recompute a token count, and a transcript that omitted what an
///     interaction cost would be a record of the exchange but not of its price.
/// </remarks>
/// <param name="Text">The assistant reply, or the empty string when the provider returned none. Never null.</param>
/// <param name="Usage">
///     What the turn consumed, or null when the provider reported nothing. Null means "not reported" and never
///     "cost nothing".
/// </param>
public sealed record ChatTurnResult(string Text, ModelUsage? Usage = null);

/// <summary>
///     The seam that hides how a single model turn is completed, so that no operation knows which provider
///     answered it.
/// </summary>
/// <remarks>
///     Everything provider-specific — authentication, the tool-invocation loop, how a conversation is rendered
///     onto the wire — lives below this interface, and everything the Toolkit promises about schema ordering,
///     decoding and retry lives above it. That split is what lets the contract tests exercise the whole of the
///     Toolkit's own machinery against a substituted endpoint, with no network call.
///     <para>
///         An implementation is expected to be safe to reuse across calls, and to touch the network only from
///         <see cref="CompleteAsync" /> — construction must stay network-free so that a test, and a caller
///         validating its arguments, can build one without reaching a model.
///     </para>
/// </remarks>
public interface IChatEndpoint
{
    /// <summary>
    ///     Completes one assembled turn and returns the assistant text.
    /// </summary>
    /// <param name="request">The assembled turn. Must not be null.</param>
    /// <param name="cancellationToken">Token that cancels the call.</param>
    /// <returns>The completed turn's assistant text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is null.</exception>
    /// <exception cref="ModelUnavailableException">
    ///     Thrown when no model can be reached, or the provider refused the call. Implementations translate
    ///     their own transport failures into this one exception so that a caller can report the cause without
    ///     knowing the provider.
    /// </exception>
    Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken);
}
