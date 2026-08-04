using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.Anneal.Toolkit.Model.Providers;

/// <summary>
///     The one provider behind the model seam: an <see cref="IChatEndpoint" /> over the GitHub Copilot SDK's
///     session API, authenticating as the ambient Copilot account of the calling session.
/// </summary>
/// <remarks>
///     One <see cref="CompleteAsync" /> call maps to one Copilot session: the system messages fold into the
///     session's system message, the rest of the conversation renders into a single role-prefixed prompt, and
///     assistant text is collected until the session goes idle. There is no token to supply and nowhere to put
///     one — the SDK reads its own credential store, which is what makes an unauthenticated machine fail loudly
///     here instead of silently somewhere else.
///     <para>
///         The session's tool allowlist is <em>always</em> non-null. This is the whole of
///         <c>TOOLKIT-I1</c> and the innocuous-looking default is the dangerous one: the Copilot CLI ships
///         built-in file and shell tools that mutate a working tree, a null allowlist imposes no restriction and
///         exposes all of them, and an empty list means zero tools. The list is therefore derived
///         unconditionally — empty when the turn grants none — so the built-ins are suppressed on every turn,
///         including a probe turn that grants nothing. Each granted tool additionally carries the SDK's
///         <c>is_override</c> flag, without which the session's tool loop rejects a name that collides with a
///         built-in.
///     </para>
///     <para>
///         Thread safety: safe to reuse across sequential calls; the underlying client is started once under a
///         gate. Only <see cref="CompleteAsync" /> touches the network — construction does not, which is what
///         keeps <see cref="BuildSessionConfig" /> a thing a test can assert offline.
///     </para>
/// </remarks>
public sealed class CopilotEndpoint : IChatEndpoint, IAsyncDisposable
{
    private readonly CopilotClient _client;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private bool _started;

    /// <summary>
    ///     Creates an endpoint over the Copilot SDK. Which model it drives is decided per turn, by the
    ///     repository configuration behind the capability role the caller asked for.
    /// </summary>
    public CopilotEndpoint()
    {
        // No token is passed and none is read from the environment: the SDK authenticates as whoever is logged
        // in to Copilot on this machine, which is the account the invoking agent is already running under.
        _client = new CopilotClient(new CopilotClientOptions { UseLoggedInUser = true });
    }

    /// <inheritdoc />
    /// <exception cref="ModelUnavailableException">
    ///     Thrown when the SDK cannot start, the session cannot be created, or the session reports an error.
    ///     Every provider-side failure is translated here so that an operation reports a cause without knowing
    ///     the provider.
    /// </exception>
    public async Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await CompleteCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Deliberately broad. Anything the SDK, the transport or the credential store can raise means the
            // same thing to a caller - the judgement was not obtained - and translating it here is what stops an
            // operation quietly treating a transport fault as an answer.
            throw new ModelUnavailableException(
                $"no model could be reached through the GitHub Copilot SDK using model '{request.Model}': " +
                exception.Message,
                exception);
        }
    }

    /// <summary>
    ///     Releases the underlying SDK client and the start gate.
    /// </summary>
    /// <returns>A task that completes once the client has been disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
        _startGate.Dispose();
    }

    /// <summary>
    ///     Builds the session configuration for a turn without opening a session or touching the network.
    /// </summary>
    /// <remarks>
    ///     Exposed as a static seam precisely so the read-only, always-explicit tool grant can be
    ///     <em>asserted</em> rather than hoped for: a test builds the configuration for a tool-granting turn and
    ///     for a no-tool turn and reads <c>AvailableTools</c> off both.
    /// </remarks>
    /// <param name="request">The assembled turn, whose <c>Model</c> names the model to drive. Must not be null.</param>
    /// <returns>
    ///     The configuration, whose <c>AvailableTools</c> is never null: exactly the granted tools' names, or an
    ///     empty list when the turn grants none.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is null.</exception>
    public static SessionConfig BuildSessionConfig(ChatTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);

        // Only function-backed tools can be run by the SDK; each is wrapped so its declaration carries the
        // is_override flag, without which the session rejects a granted tool whose name collides with a built-in.
        var granted = request.Tools
            .OfType<AIFunction>()
            .Select(function => (AIFunction)new BuiltInToolOverride(function))
            .ToList();

        return new SessionConfig
        {
            Model = request.Model,
            SystemMessage = new SystemMessageConfig { Content = ComposeSystemMessage(request.Messages) },
            Tools = [.. granted.Cast<AIFunctionDeclaration>()],

            // Always non-null. A null allowlist imposes no restriction and exposes the Copilot CLI's own
            // built-in mutating file and shell tools; an empty list means zero tools. Deriving it
            // unconditionally is what suppresses the built-ins on every turn, including this stage's no-tool
            // probe turns.
            AvailableTools = [.. granted.Select(function => function.Name)],

            // The real ceiling, enforced by the provider rather than merely asked for in words. A reasoning
            // model given an open question and no ceiling will generate until it exhausts the context window.
            // The SDK flags this override experimental, so the guarantee is only as stable as the SDK: if it
            // is withdrawn the compiler says so here rather than the bound quietly disappearing.
#pragma warning disable GHCP001
            ModelCapabilities = new ModelCapabilitiesOverride
            {
                Limits = new ModelCapabilitiesOverrideLimits { MaxOutputTokens = request.MaxOutputTokens }
            },
#pragma warning restore GHCP001

            // The Toolkit grants only read-only tools, so approving the SDK's permission requests approves
            // nothing that can change the repository. PermissionHandler is flagged experimental by the SDK.
#pragma warning disable GHCP001
            OnPermissionRequest = PermissionHandler.ApproveAll
#pragma warning restore GHCP001
        };
    }

    /// <summary>
    ///     Folds every system-role message into the single system message a Copilot session accepts.
    /// </summary>
    /// <param name="messages">The turn messages. Must not be null.</param>
    /// <returns>The folded system text, empty when the turn carries no system message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="messages" /> is null.</exception>
    public static string ComposeSystemMessage(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return string.Join(
            '\n',
            messages
                .Where(message => message.Role == ChatRole.System && !string.IsNullOrEmpty(message.Text))
                .Select(message => message.Text));
    }

    /// <summary>
    ///     Renders the non-system messages into the single role-prefixed prompt a Copilot session takes, and
    ///     states the output ceiling in words.
    /// </summary>
    /// <remarks>
    ///     The ceiling is also enforced as a transport limit through
    ///     <c>SessionConfig.ModelCapabilities.Limits.MaxOutputTokens</c>. Restating it in words is deliberate
    ///     belt and braces: a hard limit truncates a reply mid-sentence, whereas a model told the budget in
    ///     advance tends to finish its thought inside it, which is what a caller actually wants.
    /// </remarks>
    /// <param name="request">The assembled turn. Must not be null.</param>
    /// <returns>The prompt text for the turn.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is null.</exception>
    public static string ComposePrompt(ChatTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder();
        foreach (var message in request.Messages)
        {
            if (message.Role == ChatRole.System || string.IsNullOrEmpty(message.Text))
                continue;

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(message.Role.Value).Append(": ").Append(message.Text);
        }

        if (builder.Length > 0)
            builder.Append('\n');

        builder.Append(CultureInfo.InvariantCulture,
            $"Keep the reply under approximately {request.MaxOutputTokens} tokens.");

        return builder.ToString();
    }

    private async Task<ChatTurnResult> CompleteCoreAsync(
        ChatTurnRequest request, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

        var config = BuildSessionConfig(request);
        var prompt = ComposePrompt(request);

        await using var session = await _client.CreateSessionAsync(config, cancellationToken).ConfigureAwait(false);

        var output = new StringBuilder();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // What the turn consumed, accumulated as the provider reports it. A turn that granted tools makes
        // several API calls, so the usage of one turn is their total rather than the last one's.
        long inputTokens = 0;
        long outputTokens = 0;
        var usageReported = false;

        using var subscription = session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent assistant:
                    lock (output)
                    {
                        output.Append(assistant.Data.Content);
                    }

                    break;
                case AssistantUsageEvent usage:
                    lock (output)
                    {
                        usageReported = true;
                        inputTokens += usage.Data.InputTokens ?? 0;
                        outputTokens += usage.Data.OutputTokens ?? 0;
                    }

                    break;
                case SessionIdleEvent:
                    completion.TrySetResult(null);
                    break;
                case SessionErrorEvent error:
                    completion.TrySetException(new InvalidOperationException(error.Data.Message));
                    break;
                default:
                    // Progress and tool-activity events carry nothing the caller decodes; the assistant text is
                    // the answer.
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = prompt }, cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        lock (output)
        {
            // Null when the provider reported nothing, never a zeroed total: "not reported" and "cost nothing"
            // are different facts, and a transcript that confused them would understate what a run spent.
            return new ChatTurnResult(
                output.ToString().Trim(),
                usageReported ? new ModelUsage(inputTokens, outputTokens) : null);
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_started)
            return;

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                await _client.StartAsync(cancellationToken).ConfigureAwait(false);
                _started = true;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    /// <remarks>
    ///     Carries the SDK's <c>is_override</c> flag on a granted tool's declaration, marking it as an
    ///     intentional replacement of any same-named Copilot CLI built-in. Without the flag the session's tool
    ///     loop rejects the collision outright, which presents as a tool that is simply never called. Every other
    ///     member delegates unchanged so the SDK can still invoke the real function.
    /// </remarks>
    private sealed class BuiltInToolOverride(AIFunction inner) : AIFunction
    {
        // Mirrors the SDK-internal key, which is not visible outside its assembly.
        private const string OverridesBuiltInToolKey = "is_override";

        private readonly IReadOnlyDictionary<string, object?> _additionalProperties =
            new Dictionary<string, object?>(inner.AdditionalProperties) { [OverridesBuiltInToolKey] = true };

        public override string Name => inner.Name;

        public override string Description => inner.Description;

        public override JsonElement JsonSchema => inner.JsonSchema;

        public override JsonElement? ReturnJsonSchema => inner.ReturnJsonSchema;

        public override JsonSerializerOptions JsonSerializerOptions => inner.JsonSerializerOptions;

        public override MethodInfo? UnderlyingMethod => inner.UnderlyingMethod;

        public override IReadOnlyDictionary<string, object?> AdditionalProperties => _additionalProperties;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            inner.InvokeAsync(arguments, cancellationToken);
    }
}
