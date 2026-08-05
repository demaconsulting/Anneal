using System.Text.Json;
using System.Text.Json.Serialization;
using DemaConsulting.Anneal.Toolkit.Recording;
using Microsoft.Extensions.AI;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     One conversation with a model, offering exactly two verbs: <see cref="RunAsync" />, whose request and
///     reply both join the conversation, and <see cref="ProbeAsync{T}" />, a one-shot question whose typed
///     answer joins nothing.
/// </summary>
/// <remarks>
///     The two verbs exist to make schema-last ordering the only way to get a typed answer. A run reasons over
///     the repository with tools in scope and no schema in sight, so nothing is decoded and there is nothing to
///     re-prompt against; a probe then presents the schema as the last thing the model reads, with no tools, and
///     decodes. Putting the schema in the opening prompt of the reasoning pass is the known-degraded
///     configuration this whole system was built to avoid, and a caller of this type cannot express it.
///     <para>
///         The probe's question and its schema are assembled here rather than by the caller for the same reason:
///         a caller supplies its question and its authoritative context, and the framework contributes the schema.
///         A caller cannot forget it and cannot place it early.
///     </para>
///     <para>
///         Every turn that crosses this type is transcribed — the messages sent, the reply, the model consulted
///         and what it consumed — including the turns that produced no reply at all. There is no argument, flag
///         or configuration key that suppresses it, because a transcript of a model interaction cannot be
///         recovered by re-running the interaction: an opt-in would guarantee the evidence was missing exactly
///         where something surprising happened.
///     </para>
///     <para>
///         Thread safety: <em>not</em> safe for concurrent use. A session owns a mutable conversation, and two
///         concurrent runs would interleave into it. One session per operation, used from one thread at a time.
///     </para>
/// </remarks>
public sealed class ModelSession
{
    /// <summary>
    ///     The output ceiling applied when a caller states none.
    /// </summary>
    /// <remarks>
    ///     Large enough for a page of reasoning and comfortably larger than any probe result, small enough that a
    ///     model which starts generating without stopping is cut off in seconds rather than minutes.
    /// </remarks>
    public const int DefaultMaxOutputTokens = 4000;

    /// <summary>
    ///     The number of extra attempts a probe makes after its first reply fails to decode.
    /// </summary>
    /// <remarks>
    ///     Two, because the first retry is the one that rescues a near miss — a fenced object, a stray sentence —
    ///     and a model that has now been shown its own parse error twice is not going to be rescued by a third
    ///     showing, only billed for it.
    /// </remarks>
    public const int DefaultMaxParseRetries = 2;

    private const string ThinkCloseTag = "</think>";

    /// <remarks>
    ///     Closed-enum decode: a value outside the described vocabulary fails the parse and takes the visible
    ///     retry path, rather than being silently mapped to whichever member happens to sit at zero. A probe that
    ///     answers the wrong branch confidently is worse than one that fails.
    /// </remarks>
    private static readonly JsonSerializerOptions DecodeOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    private readonly ModelRoles _roles;
    private readonly IReadOnlyList<AITool> _tools;
    private readonly int _maxOutputTokens;
    private readonly int _maxParseRetries;
    private readonly List<ChatMessage> _conversation;

    /// <summary>
    ///     How many decode attempts the most recent probe consumed: 1 when the first reply decoded, more when the
    ///     retry path was entered.
    /// </summary>
    /// <remarks>
    ///     The parse-failure rate under a described schema is a measured property of this design, not an assumed
    ///     one, so the count a probe actually consumed is observable rather than inferred from logs.
    /// </remarks>
    public int LastProbeAttempts { get; private set; }

    /// <summary>
    ///     Opens a conversation over a role resolver.
    /// </summary>
    /// <param name="roles">Resolver from role to serving endpoint. Must not be null.</param>
    /// <param name="charter">
    ///     The system message every turn in this conversation carries: who the model is being asked to be and
    ///     what it may rely on. Must not be null; may be empty, in which case no system message is sent.
    /// </param>
    /// <param name="tools">
    ///     The tools a <see cref="RunAsync" /> turn may use. Null or empty grants none. Every tool granted here
    ///     must be read-only: a probe is a question about a repository, never an edit to one.
    /// </param>
    /// <param name="maxOutputTokens">
    ///     The ceiling on generated output for every turn of this conversation. Must be greater than zero;
    ///     defaults to <see cref="DefaultMaxOutputTokens" />.
    /// </param>
    /// <param name="maxParseRetries">
    ///     Extra attempts a probe makes after a reply fails to decode. Must be zero or greater; defaults to
    ///     <see cref="DefaultMaxParseRetries" />.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="roles" /> or <paramref name="charter" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxOutputTokens" /> is not greater than zero, or
    ///     <paramref name="maxParseRetries" /> is negative.
    /// </exception>
    public ModelSession(
        ModelRoles roles,
        string charter,
        IReadOnlyList<AITool>? tools = null,
        int maxOutputTokens = DefaultMaxOutputTokens,
        int maxParseRetries = DefaultMaxParseRetries)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(charter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(maxParseRetries);

        _roles = roles;
        _tools = tools ?? [];
        _maxOutputTokens = maxOutputTokens;
        _maxParseRetries = maxParseRetries;
        _conversation = charter.Length == 0 ? [] : [new ChatMessage(ChatRole.System, charter)];
    }

    /// <summary>
    ///     Asks a free-form question with the session's tools in scope, and joins both the request and the reply
    ///     to the conversation.
    /// </summary>
    /// <remarks>
    ///     No schema is presented and no JSON is required, so the model answers in prose, reasoning over the
    ///     actual repository through the granted tools. Nothing is decoded, which is precisely why there is no
    ///     retry here: there is no parse to fail.
    /// </remarks>
    /// <param name="request">The question or instruction. Must not be null or blank.</param>
    /// <param name="role">
    ///     The capability tier to serve this turn, or null to take the default for
    ///     <see cref="ModelActivity.Run" />.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the call.</param>
    /// <returns>The assistant's prose reply, which has also joined the conversation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="request" /> is null, empty or blank.</exception>
    /// <exception cref="ModelUnavailableException">Thrown when no model could be reached.</exception>
    public async Task<string> RunAsync(string request, ModelRole? role, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);

        _conversation.Add(new ChatMessage(ChatRole.User, request));

        var turn = await CompleteAsync(
                ModelActivity.Run,
                role ?? ModelRoles.DefaultRoleFor(ModelActivity.Run),
                _conversation.ToArray(),
                _tools,
                cancellationToken)
            .ConfigureAwait(false);

        _conversation.Add(new ChatMessage(ChatRole.Assistant, turn.Text));
        return turn.Text;
    }

    /// <summary>
    ///     Asks a one-shot question carrying the response schema, with no tools, and decodes the reply into
    ///     <typeparamref name="T" />.
    /// </summary>
    /// <remarks>
    ///     The probe reads everything the conversation has established so far, but neither its question nor its
    ///     answer joins it: a probe is an extraction from the reasoning, not a step in it, and letting transport
    ///     JSON into the conversation would put a schema in front of every later turn. Tools are withheld because
    ///     a turn that must emit one object should be extracting from what it already has, not going back to the
    ///     repository.
    ///     <para>
    ///         A reply that does not decode is re-prompted with the model's own raw text and the parse error, so
    ///         the model sees its own mistake. When the budget is exhausted the probe throws rather than returning
    ///         anything: no partially populated result reaches a caller.
    ///     </para>
    /// </remarks>
    /// <typeparam name="T">The typed result. Its public properties are described to the model as the schema.</typeparam>
    /// <param name="question">The question to answer from the conversation so far. Must not be null or blank.</param>
    /// <param name="role">
    ///     The capability tier to serve this turn, or null to take the default for
    ///     <see cref="ModelActivity.Probe" />.
    /// </param>
    /// <param name="cancellationToken">Token that cancels the call.</param>
    /// <returns>The fully decoded result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="question" /> is null, empty or blank.</exception>
    /// <exception cref="ModelUnavailableException">Thrown when no model could be reached.</exception>
    /// <exception cref="ModelParseException">
    ///     Thrown when no reply within the retry budget decoded into <typeparamref name="T" />.
    /// </exception>
    public async Task<T> ProbeAsync<T>(string question, ModelRole? role, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        LastProbeAttempts = 0;

        var resolved = role ?? ModelRoles.DefaultRoleFor(ModelActivity.Probe);

        // A private working copy: the bad reply and the correction that follows it are plumbing, and threading
        // them back into the conversation would leave every later turn reading a failed exchange.
        List<ChatMessage> working =
        [
            .. _conversation,
            new ChatMessage(ChatRole.User, ComposeProbeMessage<T>(question))
        ];

        JsonException? lastError = null;
        for (var attempt = 0; attempt <= _maxParseRetries; attempt++)
        {
            LastProbeAttempts = attempt + 1;

            // No tools on a probe turn: an empty grant, never an absent one.
            var turn = await CompleteAsync(
                    ModelActivity.Probe, resolved, working.ToArray(), [], cancellationToken)
                .ConfigureAwait(false);

            try
            {
                return JsonSerializer.Deserialize<T>(ExtractJsonObject(turn.Text), DecodeOptions)
                    ?? throw new JsonException("the reply decoded to null.");
            }
            catch (JsonException exception)
            {
                lastError = exception;

                working.Add(new ChatMessage(ChatRole.Assistant, turn.Text));
                working.Add(new ChatMessage(
                    ChatRole.User,
                    $"That reply could not be parsed as JSON: {exception.Message} " +
                    "Reply with only the corrected JSON object and nothing else."));
            }
        }

        throw new ModelParseException(
            $"the reply could not be parsed as {typeof(T).Name} within {_maxParseRetries + 1} attempts: " +
            lastError!.Message,
            lastError);
    }

    /// <summary>
    ///     Completes one turn through the seam and transcribes it, whatever it produced.
    /// </summary>
    /// <remarks>
    ///     Every path out of this method that reached an endpoint has already written a transcript: the reply
    ///     path, the failure path, and the path where the caller withdrew mid-flight. That is the whole of the
    ///     capture guarantee — it is a property of there being no other way to reach an endpoint from here,
    ///     rather than of every call site remembering to record.
    ///     <para>
    ///         Resolving the role comes before the try, and therefore before the guarantee begins: a role whose
    ///         every candidate has been retired throws out of here having written nothing. That is correct — no
    ///         model was consulted, so there is no interaction to transcribe, and inventing a record of one
    ///         would put an exchange that never happened into the evidence.
    ///     </para>
    /// </remarks>
    private async Task<ChatTurnResult> CompleteAsync(
        ModelActivity activity,
        ModelRole role,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<AITool> tools,
        CancellationToken cancellationToken)
    {
        // Resolved here, at turn assembly, and nowhere earlier: this is the moment a model is genuinely about
        // to be consulted, so it is the only moment the provider may be asked what it offers.
        var model = await _roles.ResolveModelAsync(role, cancellationToken).ConfigureAwait(false);
        var at = DateTimeOffset.UtcNow;

        try
        {
            var turn = await _roles
                .Resolve(role)
                .CompleteAsync(new ChatTurnRequest(messages, tools, _maxOutputTokens, model), cancellationToken)
                .ConfigureAwait(false);

            Transcribe(at, activity, role, model, messages, turn.Text, turn.Usage, ModelTranscript.Replied, null);
            InvocationScope.Current?.Observe(turn.Usage);
            return turn;
        }
        catch (Exception exception)
        {
            // A withdrawn turn is transcribed on the same path as a refused or unreachable one, deliberately:
            // all three are interactions that produced no reply, and which of them happened is what the recorded
            // failure says.
            Transcribe(at, activity, role, model, messages, null, null, ModelTranscript.Failed, exception.Message);
            InvocationScope.Current?.Observe(null);
            throw;
        }
    }

    private void Transcribe(
        DateTimeOffset at,
        ModelActivity activity,
        ModelRole role,
        string model,
        IReadOnlyList<ChatMessage> messages,
        string? reply,
        ModelUsage? usage,
        string result,
        string? failure) =>
        _roles.Transcripts.Append(new ModelTranscript(
            at,
            activity.ToString(),
            role.ToString(),
            model,
            [.. messages.Select(message => new TranscriptMessage(message.Role.Value, message.Text))],
            reply,
            usage,
            result,
            failure));

    /// <remarks>
    ///     Question first, schema last: the block the model must obey is the final thing it reads before it
    ///     answers, which is the ordering the whole Toolkit exists to make expressible.
    /// </remarks>
    private static string ComposeProbeMessage<T>(string question) =>
        $"""
         {question}

         <schema>
         {SchemaDescriber.Describe<T>()}
         </schema>

         Return only the JSON object.
         """;

    /// <remarks>
    ///     Tolerant rather than strict, because the failures a model actually produces around a correct answer
    ///     are a leaked reasoning block, a markdown fence and a sentence of preamble — none of which are
    ///     disagreements about the answer. Whatever sits between the outermost braces is handed to a real parser
    ///     verbatim; when there is no brace pair the trimmed text goes through unchanged so the parser reports the
    ///     real error rather than this helper masking it.
    /// </remarks>
    private static string ExtractJsonObject(string payload)
    {
        var closeTag = payload.IndexOf(ThinkCloseTag, StringComparison.OrdinalIgnoreCase);
        var trimmed = (closeTag >= 0 ? payload[(closeTag + ThinkCloseTag.Length)..] : payload).Trim();

        var start = trimmed.IndexOf('{', StringComparison.Ordinal);
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }
}
