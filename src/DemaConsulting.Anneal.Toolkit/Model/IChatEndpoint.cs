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
///     The concrete model to drive: the candidate the caller's capability role resolved to, having been found
///     among those the account is offered. Must not be null or blank. It travels with the turn rather than being
///     fixed when an endpoint is built, because the role-to-candidates mapping is data a repository owns and one
///     provider serves every role.
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
///         <see cref="CompleteAsync" /> and <see cref="AvailableModelsAsync" /> — construction must stay
///         network-free so that a test, and a caller validating its arguments, can build one without reaching a
///         model.
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

    /// <summary>
    ///     States which models this endpoint's account is actually offered, so a capability role can resolve to
    ///     the first of its candidates that exists rather than to one that has been retired.
    /// </summary>
    /// <remarks>
    ///     Availability is asked about here and never inferred from a failed call.
    ///     <see cref="ModelUnavailableException" /> deliberately flattens every provider-side error into one
    ///     shape, so a caller that fell back on failure could not tell a retired model from a rate limit, an
    ///     expired credential or a transport fault — and would silently downgrade a heavy-role judgement
    ///     whenever the network hiccuped. This is the seam that makes the question answerable instead.
    ///     <para>
    ///         It is called only when a role is being resolved for a turn that is about to be sent, so an
    ///         invocation that consults no model makes no call here and acquires no network dependency. A
    ///         substituted endpoint answers it offline, which is what keeps role resolution exercisable without
    ///         a provider.
    ///     </para>
    ///     <para>
    ///         An implementation that cannot say — because it has no list, or because the enquiry failed —
    ///         answers with an empty collection rather than an exception. Either way the caller treats the
    ///         enquiry as having stated nothing and proceeds on its first configured candidate: this is an
    ///         optimization over guessing, not a gate.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">Token that cancels the enquiry.</param>
    /// <returns>
    ///     The model identifiers the account may drive, in no particular order, matched against a candidate by
    ///     exact name. Never null; empty means "nothing stated" and never "no model exists".
    /// </returns>
    Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken);
}
