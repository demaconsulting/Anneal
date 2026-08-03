namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     Resolves a capability role to the endpoint that serves it, and owns the default role for each kind of
///     call.
/// </summary>
/// <remarks>
///     This is the seam that keeps every operation model-agnostic: a call asks for a <see cref="ModelRole" />
///     and receives an <see cref="IChatEndpoint" />, never a model name and never a provider. Each role resolves
///     separately so a repository can serve a cheap tier from one model and an expensive one from another; the
///     common case of one model everywhere is served by the single-endpoint constructor.
///     <para>
///         Thread safety: immutable after construction and safe to share, provided the injected endpoints are.
///     </para>
/// </remarks>
public sealed class ModelRoles
{
    private readonly IChatEndpoint _light;
    private readonly IChatEndpoint _medium;
    private readonly IChatEndpoint _heavy;

    /// <summary>
    ///     Creates a resolver serving every role from one endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint behind all three roles. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint" /> is null.</exception>
    public ModelRoles(IChatEndpoint endpoint)
        : this(endpoint, endpoint, endpoint)
    {
    }

    /// <summary>
    ///     Creates a resolver serving each role from its own endpoint.
    /// </summary>
    /// <param name="light">The endpoint behind <see cref="ModelRole.Light" />. Must not be null.</param>
    /// <param name="medium">The endpoint behind <see cref="ModelRole.Medium" />. Must not be null.</param>
    /// <param name="heavy">The endpoint behind <see cref="ModelRole.Heavy" />. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when any endpoint is null.</exception>
    public ModelRoles(IChatEndpoint light, IChatEndpoint medium, IChatEndpoint heavy)
    {
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(medium);
        ArgumentNullException.ThrowIfNull(heavy);

        _light = light;
        _medium = medium;
        _heavy = heavy;
    }

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
    ///     Returns the endpoint that serves a role.
    /// </summary>
    /// <param name="role">The requested capability tier.</param>
    /// <returns>The endpoint bound to that role.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    public IChatEndpoint Resolve(ModelRole role) => role switch
    {
        ModelRole.Light => _light,
        ModelRole.Medium => _medium,
        ModelRole.Heavy => _heavy,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown model role.")
    };
}
