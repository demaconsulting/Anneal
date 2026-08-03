namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     The capability tier a model call asks for, naming what the call needs rather than which model answers it.
/// </summary>
/// <remarks>
///     An operation declares a role and never a model, so a repository can point a tier at a different model by
///     editing configuration instead of waiting for a Toolkit release. The three tiers are the smallest set that
///     still expresses the only distinction that has mattered in practice: a question cheap enough to ask
///     freely, a conversation worth paying for, and open-ended work where capability dominates cost.
/// </remarks>
public enum ModelRole
{
    /// <summary>Cheapest tier, for a one-shot question whose answer is short and closed.</summary>
    Light,

    /// <summary>Middle tier, for a conversation that accumulates a working history.</summary>
    Medium,

    /// <summary>Most capable tier, for open-ended work where the answer is not bounded in advance.</summary>
    Heavy
}

/// <summary>
///     What a call is doing, which is what decides the role it gets when the caller does not name one.
/// </summary>
/// <remarks>
///     The mapping from activity to role lives in one place — <see cref="ModelRoles.DefaultRoleFor" /> — rather
///     than as a default argument at each call site, because a default repeated per call site is a default that
///     drifts. Every call may still override the role; the activity only decides what happens when it does not.
/// </remarks>
public enum ModelActivity
{
    /// <summary>A one-shot schema-bearing question whose typed answer joins no conversation.</summary>
    Probe,

    /// <summary>A request and reply that both join a conversation.</summary>
    Run,

    /// <summary>Work with no bounded answer, where the cheaper tiers are false economy.</summary>
    OpenEnded
}
