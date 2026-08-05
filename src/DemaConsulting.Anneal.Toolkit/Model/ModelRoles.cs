using DemaConsulting.Anneal.Toolkit.Model.Providers;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     Resolves a capability role to the concrete model that serves it and to the endpoint that drives it, and
///     owns the default role for each kind of call.
/// </summary>
/// <remarks>
///     This is the seam that keeps every operation model-agnostic: a call asks for a <see cref="ModelRole" />
///     and never names a model. The candidates behind a role come from the repository's own configuration file,
///     so substituting one is an edit a repository makes rather than a Toolkit release — which is why this type
///     is built from a repository root and not from a mapping a caller hands it. An operation that could supply
///     its own mapping would be an operation that had resolved the role itself. Which candidate answers is
///     settled by asking the provider what the account is offered, and only at the moment a turn is sent.
///     <para>
///         It is also where a repository's model transcripts are destined. There is exactly one way to construct
///         this type and it requires the repository whose transcripts are being kept, so there is no
///         construction path — and therefore no flag, argument or configuration key — that leaves capture off.
///     </para>
///     <para>
///         Thread safety: <em>not</em> safe for concurrent use. A role's resolution is memoized on first use, so
///         two threads resolving at once would race the cache. One instance per operation, which is how every
///         operation already builds it.
///     </para>
/// </remarks>
public sealed class ModelRoles
{
    /// <remarks>
    ///     One client per process rather than one per role. Starting the SDK is expensive, and three copies of
    ///     it would authenticate three times to serve turns that are made one at a time.
    /// </remarks>
    private static readonly Lazy<CopilotEndpoint> SharedCopilot =
        new(() => new CopilotEndpoint(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Func<ModelRole, IChatEndpoint> _endpointFor;
    private readonly ModelConfiguration _configuration;

    /// <remarks>
    ///     One resolution per role per instance. The offered set does not change under a running invocation, and
    ///     a role resolved once per turn would pay a round trip for an answer it already had.
    /// </remarks>
    private readonly Dictionary<ModelRole, string> _resolved = [];

    /// <summary>
    ///     Binds the capability roles for a repository, reading the models behind them from that repository's
    ///     own configuration.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository whose configuration names the models and whose transcripts are kept. Must not be null
    ///     or blank.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so the whole of the Toolkit's own machinery — role resolution, the two-pass ordering,
    ///     decoding, retry and transcript capture — is exercisable without a network call. It never decides
    ///     which model answers: that is the configuration's alone.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ModelUnavailableException">
    ///     Thrown when the repository's configuration file exists but cannot be read or parsed.
    /// </exception>
    public ModelRoles(string repositoryRoot, Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        _configuration = ModelConfiguration.Load(RepositoryRoot);
        _endpointFor = endpointFor ?? DefaultEndpointFor;
        Transcripts = new RecordStore(RepositoryRoot);
    }

    /// <summary>
    ///     The repository these roles were resolved for.
    /// </summary>
    public string RepositoryRoot { get; }

    /// <summary>
    ///     Where the transcript of every interaction over this seam is appended.
    /// </summary>
    public RecordStore Transcripts { get; }

    /// <summary>
    ///     The role a call of the given kind gets when its caller names none.
    /// </summary>
    /// <remarks>
    ///     A probe asks a closed question of material the conversation already established, which the cheapest
    ///     tier answers as well as any other; a run reasons over the repository and is worth the middle tier;
    ///     open-ended work has no bounded answer, so the capable tier is cheaper than the retries the others
    ///     would cost.
    /// </remarks>
    /// <param name="activity">The kind of call being made.</param>
    /// <returns>The role that serves that activity by default.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="activity" /> is not a defined activity.</exception>
    public static ModelRole DefaultRoleFor(ModelActivity activity) => activity switch
    {
        ModelActivity.Probe => ModelRole.Light,
        ModelActivity.Run => ModelRole.Medium,
        ModelActivity.OpenEnded => ModelRole.Heavy,
        _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "Unknown model activity.")
    };

    /// <summary>
    ///     Returns the candidate models the repository's configuration puts behind a role, most preferred first.
    /// </summary>
    /// <remarks>
    ///     Exposed alongside <see cref="ResolveModelAsync" /> so a caller can see what a role was allowed to
    ///     choose from without provoking the enquiry that chooses. Reading it makes no provider call.
    /// </remarks>
    /// <param name="role">The requested capability tier.</param>
    /// <returns>The configured candidates for that tier. Never empty.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    public IReadOnlyList<string> CandidatesFor(ModelRole role) => _configuration.CandidatesFor(role);

    /// <summary>
    ///     Resolves a role to the first of its configured candidates the account is actually offered.
    /// </summary>
    /// <remarks>
    ///     This is the only place the provider is asked what it offers, and it is called only when a turn is
    ///     about to be sent. Nothing resolves in a constructor and nothing resolves eagerly, so a deterministic
    ///     operation — one that consults no model — makes no provider call and acquires no network dependency.
    ///     That separation is why the deterministic checks are the ones that may gate a build.
    ///     <para>
    ///         Availability is asked about and never inferred from a failed call: the seam flattens every
    ///         provider-side error into one exception, so falling back on failure would silently downgrade a
    ///         heavy-role judgement whenever the network hiccuped.
    ///     </para>
    ///     <para>
    ///         The result is memoized per role, so a multi-turn conversation enquires once.
    ///     </para>
    /// </remarks>
    /// <param name="role">The requested capability tier.</param>
    /// <param name="cancellationToken">Token that cancels the availability enquiry.</param>
    /// <returns>The concrete model that will answer for that role.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    /// <exception cref="ModelUnavailableException">
    ///     Thrown when the provider states what it offers and none of the role's candidates is among them. The
    ///     message names the role, the candidates tried in order, and the configuration file to change them in.
    /// </exception>
    public async Task<string> ResolveModelAsync(ModelRole role, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown model role.");

        if (_resolved.TryGetValue(role, out var already))
            return already;

        var candidates = _configuration.CandidatesFor(role);
        var offered = await OfferedByAsync(_endpointFor(role), cancellationToken).ConfigureAwait(false);

        var selected = Select(role, candidates, offered);
        _resolved[role] = selected;
        return selected;
    }

    /// <remarks>
    ///     A failed enquiry is not a gate. Enumeration exists to stop the Toolkit guessing at a retired model,
    ///     so treating its failure as a failed resolution would turn an optimization into a new way for a
    ///     working run to stop — and the fault that broke the enquiry will break the turn a moment later, where
    ///     it is reported with the cause it actually had. An empty answer means "nothing was established" and
    ///     leads to the same first-candidate guess the Toolkit made before this enquiry existed. Cancellation is
    ///     not swallowed: a caller who withdrew is not a provider that could not answer.
    /// </remarks>
    private static async Task<IReadOnlyCollection<string>> OfferedByAsync(
        IChatEndpoint endpoint, CancellationToken cancellationToken)
    {
        try
        {
            return await endpoint.AvailableModelsAsync(cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <remarks>
    ///     Only a provider that named something can convict a candidate of absence, which is why an empty
    ///     offered set takes the first candidate rather than throwing: silence is not a statement that the
    ///     account has no models.
    /// </remarks>
    private static string Select(ModelRole role, IReadOnlyList<string> candidates, IReadOnlyCollection<string> offered)
    {
        if (offered.Count == 0)
            return candidates[0];

        var available = candidates.FirstOrDefault(offered.Contains);
        if (available is not null)
            return available;

        // Loud, and one line away from fixed: a role whose every candidate has been retired is a real state,
        // and the message says which role, what was tried, and where to change it.
        throw new ModelUnavailableException(
            $"the {role} role has no available model: none of its candidates " +
            $"{string.Join(", ", candidates.Select(candidate => $"'{candidate}'"))} " +
            $"is offered to this account. Name a model the account has in '{ModelConfiguration.RelativePath}'.");
    }

    /// <summary>
    ///     Returns the endpoint that drives a role.
    /// </summary>
    /// <param name="role">The requested capability tier.</param>
    /// <returns>The endpoint bound to that role.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    public IChatEndpoint Resolve(ModelRole role)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown model role.");

        return _endpointFor(role);
    }

    /// <remarks>
    ///     Every role is served by the Copilot SDK because there is one provider; what varies per role is the
    ///     model, and that travels with the turn rather than being fixed here.
    /// </remarks>
    private static IChatEndpoint DefaultEndpointFor(ModelRole role) => SharedCopilot.Value;
}
