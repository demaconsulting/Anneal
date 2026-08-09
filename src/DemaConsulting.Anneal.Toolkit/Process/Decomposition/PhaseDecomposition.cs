using System.ComponentModel;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Routing;

namespace DemaConsulting.Anneal.Toolkit.Process.Decomposition;

/// <summary>
///     What a decomposition pass concluded for a Massive-Effort work item: the phase set to route in its place, or
///     that the item cannot honestly be decomposed on the evidence given.
/// </summary>
/// <remarks>
///     A dedicated, minimal oracle question rather than a reuse of <see cref="Primitives.Planner" />:
///     <see cref="Primitives.PlanningDecision.Plan" /> carries an <see cref="Primitives.ImplementationPlan" /> of
///     ordered prose steps with no per-step file scope or edit category, which is exactly the data TOOLKIT-26's
///     containment check and TOOLKIT-27's tripwire both need per phase. Bending <see cref="Primitives.Planner" />
///     to also carry that would either widen its own single-shot contract for every existing caller or fork it
///     into a second type in every way that matters, so a narrow sibling question — the same size as
///     <see cref="CumulativeCheckDecision" /> and reached through the same <see cref="Primitives.Oracle{TDecision}" />
///     shape — is the smaller, more consistent change.
/// </remarks>
internal abstract record PhaseDecompositionDecision
{
    private PhaseDecompositionDecision()
    {
    }

    /// <summary>The item was decomposed into a phase set.</summary>
    /// <param name="Phases">The proposed phases, each a strict subset of the item's own cleared file scope.</param>
    internal sealed record Decomposed(IReadOnlyList<Phase> Phases) : PhaseDecompositionDecision;

    /// <summary>The item could not honestly be decomposed on the evidence given.</summary>
    /// <param name="Why">Why decomposition could not be reached, in a sentence a person can check.</param>
    internal sealed record CannotDecompose(string Why) : PhaseDecompositionDecision;
}

/// <summary>The closed vocabulary a <see cref="PhaseDecompositionEnvelope" /> decodes its kind as.</summary>
internal enum PhaseDecompositionKind
{
    /// <summary>A phase set was reached.</summary>
    [Description("a phase set was reached")]
    Decomposed,

    /// <summary>The item could not honestly be decomposed.</summary>
    [Description("the item could not honestly be decomposed on the evidence given")]
    CannotDecompose
}

/// <summary>
///     What a decomposition probe decoded, before it is mapped onto <see cref="PhaseDecompositionDecision" />.
/// </summary>
/// <remarks>
///     Flat and parallel-array by design, the same shallow shape <see cref="Model.SchemaDescriber" /> already
///     forces on every other envelope in this pass: a phase's own three properties are carried as three
///     same-length arrays — <see cref="PhaseWorkItems" />, <see cref="PhaseFileScopes" /> (each entry a single
///     phase's own patterns joined by <c>;</c>), and <see cref="PhaseEditCategories" /> — rather than as a nested
///     list of phase objects, which <see cref="Model.SchemaDescriber" /> has no way to describe to the model in the
///     first place. <see cref="Router" /> validates the arrays agree in length before trusting any of them.
/// </remarks>
internal sealed record PhaseDecompositionEnvelope : IOracleDecision
{
    /// <summary>Which case of <see cref="PhaseDecompositionDecision" /> this reply reaches.</summary>
    public required PhaseDecompositionKind Kind { get; init; }

    /// <summary>Why, whatever <see cref="Kind" /> is reached. Never empty.</summary>
    public required string Why { get; init; }

    /// <summary>
    ///     Each phase's own work-item description, when <see cref="Kind" /> is
    ///     <see cref="PhaseDecompositionKind.Decomposed" />; empty otherwise.
    /// </summary>
    public required IReadOnlyList<string> PhaseWorkItems { get; init; }

    /// <summary>
    ///     Each phase's own declared file scope, one entry per phase with its patterns joined by <c>;</c>, when
    ///     <see cref="Kind" /> is <see cref="PhaseDecompositionKind.Decomposed" />; empty otherwise.
    /// </summary>
    public required IReadOnlyList<string> PhaseFileScopes { get; init; }

    /// <summary>
    ///     Each phase's own edit category, named by its exact <see cref="EditCategory" /> member name, when
    ///     <see cref="Kind" /> is <see cref="PhaseDecompositionKind.Decomposed" />; empty otherwise.
    /// </summary>
    public required IReadOnlyList<string> PhaseEditCategories { get; init; }

    /// <inheritdoc />
    public required bool HasSufficientEvidence { get; init; }
}
