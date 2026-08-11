using System.ComponentModel;
using DemaConsulting.Anneal.Toolkit.Process.Routing;

namespace DemaConsulting.Anneal.Toolkit.Process.Decomposition;

/// <summary>
///     The kind of edit a <see cref="Phase" /> is declared to make, the closed vocabulary mirroring the
///     "categories of edit permitted" a Maintenance bound already declares per <c>change-classification.md</c>
///     § Maintenance ("Bounded before it starts. Declare the file set, the categories of edit permitted, and a
///     stopping point.").
/// </summary>
internal enum EditCategory
{
    /// <summary>Prose documentation — a README, a user guide, a subsystem document.</summary>
    [Description("prose documentation")]
    Documentation,

    /// <summary>Production code.</summary>
    [Description("production code")]
    Code,

    /// <summary>Test code, interior or contract.</summary>
    [Description("test code")]
    Tests,

    /// <summary>Build, lint, or repository configuration.</summary>
    [Description("build, lint, or repository configuration")]
    Config
}

/// <summary>
///     One generated unit of decomposed work: a slice of a Massive-Effort item narrow enough to route and execute
///     on its own.
/// </summary>
/// <remarks>
///     A <see cref="Router" /> never invents a phase itself — it is handed one by a decomposition pass and routes
///     it back through <see cref="Router.RunAsync" /> exactly as it would a top-level work item, per
///     <c>.anneal/architecture/toolkit/route.md</c> § Decisions ("Decomposition recurses through <c>Router</c> itself,
///     with a depth parameter, rather than a separate decomposer type").
///     <para>
///         <see cref="Depth" /> counts how many decompositions have already happened to produce this phase, not
///         how many more are allowed — it is the same value <see cref="Router.RunAsync" />'s own <c>depth</c>
///         parameter was passed as when this phase was generated, so the depth cap of two
///         (<c>change-classification.md</c> § Massive Effort Must Be Decomposed) is enforced by comparing this
///         value where the phase is next routed, never by state the phase itself tracks separately.
///     </para>
/// </remarks>
/// <param name="WorkItem">What this phase does, stated the same way a top-level work item is. Never null or blank.</param>
/// <param name="FileScope">
///     The glob or path patterns this phase declares it will touch. Never null; never larger than, and never equal
///     to, the file scope already cleared for the Massive item this phase was decomposed from.
/// </param>
/// <param name="EditCategory">The kind of edit this phase makes.</param>
/// <param name="Depth">
///     How many decompositions already produced this phase: 0 for a phase from a Massive item's own first
///     decomposition, 1 for a phase from decomposing one of those. Never reaches 2 — a depth-1 phase that itself
///     classifies Massive escalates instead of decomposing further.
/// </param>
internal sealed record Phase(
    string WorkItem,
    IReadOnlyList<string> FileScope,
    EditCategory EditCategory,
    int Depth);
