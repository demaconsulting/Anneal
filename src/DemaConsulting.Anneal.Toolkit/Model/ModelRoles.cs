using DemaConsulting.Anneal.Toolkit.Model.Providers;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     Resolves a capability role to the concrete model that serves it and to the endpoint that drives it, and
///     owns the default role for each kind of call.
/// </summary>
/// <remarks>
///     This is the seam that keeps every operation model-agnostic: a call asks for a <see cref="ModelRole" />
///     and never names a model. The model behind a role comes from the repository's own configuration file, so
///     substituting one is an edit a repository makes rather than a Toolkit release — which is why this type is
///     built from a repository root and not from a mapping a caller hands it. An operation that could supply its
///     own mapping would be an operation that had resolved the role itself.
///     <para>
///         It is also where a repository's model transcripts are destined. There is exactly one way to construct
///         this type and it requires the repository whose transcripts are being kept, so there is no
///         construction path — and therefore no flag, argument or configuration key — that leaves capture off.
///     </para>
///     <para>
///         Thread safety: immutable after construction and safe to share, provided the resolved endpoints are.
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
    ///     Returns the concrete model the repository's configuration puts behind a role.
    /// </summary>
    /// <param name="role">The requested capability tier.</param>
    /// <returns>The configured model name for that tier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    public string ModelFor(ModelRole role) => _configuration.ModelFor(role);

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
