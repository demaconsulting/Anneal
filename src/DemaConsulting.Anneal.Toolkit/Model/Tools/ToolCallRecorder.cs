using System.Reflection;
using System.Text.Json;
using DemaConsulting.Anneal.Toolkit.Recording;
using Microsoft.Extensions.AI;

namespace DemaConsulting.Anneal.Toolkit.Model.Tools;

/// <summary>
///     Wraps granted tools so that every invocation a model makes is transcribed with its arguments and its
///     outcome, and keeps the refusals where the operation driving the conversation can read them.
/// </summary>
/// <remarks>
///     Wrapping is how the guarantee is made structural rather than remembered. Nothing at a call site decides
///     to record: the only tools that ever cross the seam are wrapped ones, so a tool call that was not
///     transcribed is a tool that was never granted. That matters most where the provider runs the tool loop
///     itself, because then the calls happen inside the SDK and no code above the seam sees them at all.
///     <para>
///         Refusals are additionally kept in memory because they are a control-flow fact and not only evidence.
///         An operation whose worker was denied a protected-path write needs to know that within the run, in
///         order to escalate rather than grind its budget; re-reading the transcript file to find out would make
///         the operation depend on its own bookkeeping having succeeded, which the record store deliberately
///         does not promise.
///     </para>
///     <para>
///         Thread safety: safe for concurrent invocation. A provider that runs several tool calls at once
///         appends and accumulates under a lock.
///     </para>
/// </remarks>
internal sealed class ToolCallRecorder
{
    private static readonly JsonSerializerOptions ArgumentOptions = new(JsonSerializerDefaults.Web);

    private readonly RecordStore _store;
    private readonly Lock _gate = new();
    private readonly List<ToolCallTranscript> _refusals = [];

    /// <param name="store">Where each transcript is appended. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store" /> is null.</exception>
    internal ToolCallRecorder(RecordStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    ///     The refused calls observed so far, of either kind, newest last.
    /// </summary>
    internal IReadOnlyList<ToolCallTranscript> Refusals
    {
        get
        {
            lock (_gate)
            {
                return [.. _refusals];
            }
        }
    }

    /// <summary>
    ///     The refused calls that were writes to a protected configuration file or repository script, newest
    ///     last.
    /// </summary>
    /// <remarks>
    ///     Kept apart from <see cref="Refusals" /> because only this kind means "the user must decide". An
    ///     operation escalating on any refusal would tell the user a protected file needs their approval when
    ///     all that happened was a worker asking for a path outside the repository.
    /// </remarks>
    internal IReadOnlyList<ToolCallTranscript> ProtectedRefusals
    {
        get
        {
            lock (_gate)
            {
                return [.. _refusals.Where(refusal => refusal.Result == ToolReply.RefusedProtected)];
            }
        }
    }

    /// <summary>
    ///     Wraps every function-backed tool so its invocations are transcribed.
    /// </summary>
    /// <remarks>
    ///     A tool that is not an <see cref="AIFunction" /> cannot be invoked by anything the Toolkit drives, so
    ///     it passes through unwrapped rather than being dropped: hiding it would be a scoping decision made in
    ///     the wrong place.
    /// </remarks>
    /// <param name="tools">The granted tools. Must not be null.</param>
    /// <returns>The tools, each function-backed one wrapped.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tools" /> is null.</exception>
    internal IReadOnlyList<AITool> Wrap(IReadOnlyList<AITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return [.. tools.Select(tool => tool is AIFunction function ? new Transcribed(function, this) : tool)];
    }

    private void Record(string tool, AIFunctionArguments arguments, string result, string outcome)
    {
        var transcript = new ToolCallTranscript(
            DateTimeOffset.UtcNow, tool, Render(arguments), result, outcome);

        lock (_gate)
        {
            if (ToolReply.IsRefusal(result))
                _refusals.Add(transcript);
        }

        _store.Append(transcript);
    }

    /// <remarks>
    ///     Rendered rather than typed, because a model may supply anything the provider's tool loop accepted and
    ///     a transcript that could fail to serialize would be a transcript that is missing exactly where the
    ///     surprising call was. A value that will not serialize is written as its text.
    /// </remarks>
    private static string Render(AIFunctionArguments arguments)
    {
        try
        {
            return JsonSerializer.Serialize(
                arguments.ToDictionary(entry => entry.Key, entry => entry.Value), ArgumentOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return JsonSerializer.Serialize(
                arguments.ToDictionary(entry => entry.Key, entry => entry.Value?.ToString()), ArgumentOptions);
        }
    }

    /// <remarks>
    ///     Every member delegates unchanged so the provider still sees the real function's name, description and
    ///     schema; only the invocation is intercepted. Faulting is recorded and then rethrown rather than
    ///     converted into a reply, because swallowing it here would hide a Toolkit defect behind a message the
    ///     model would try to reason about.
    /// </remarks>
    private sealed class Transcribed(AIFunction inner, ToolCallRecorder recorder) : AIFunction
    {
        public override string Name => inner.Name;

        public override string Description => inner.Description;

        public override JsonElement JsonSchema => inner.JsonSchema;

        public override JsonElement? ReturnJsonSchema => inner.ReturnJsonSchema;

        public override JsonSerializerOptions JsonSerializerOptions => inner.JsonSerializerOptions;

        public override MethodInfo? UnderlyingMethod => inner.UnderlyingMethod;

        public override IReadOnlyDictionary<string, object?> AdditionalProperties => inner.AdditionalProperties;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            object? reply;
            try
            {
                reply = await inner.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A withdrawn call is the caller's decision, not the tool's outcome, and inventing a record of
                // one would put a call that never completed into the evidence.
                throw;
            }
            catch (Exception exception)
            {
                recorder.Record(inner.Name, arguments, ToolReply.Faulted, exception.Message);
                throw;
            }

            var text = reply?.ToString() ?? string.Empty;
            recorder.Record(inner.Name, arguments, ToolReply.Classify(text), text);
            return reply;
        }
    }
}
