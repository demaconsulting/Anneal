using DemaConsulting.Anneal.Toolkit.Process.Routing;

namespace DemaConsulting.Anneal.Toolkit.Process.Decomposition;

/// <summary>
///     What one decomposed <see cref="Phase" /> concluded once re-routed through <see cref="Router.RunAsync" />: its
///     own outcome and a brief account of what it did or why it did not complete.
/// </summary>
/// <remarks>
///     Aggregated onto the enclosing <see cref="RouterOutcome" /> so a caller of a decomposed Massive item's run
///     can see which phase did what, rather than only the item's own single collapsed verdict — see
///     <c>.anneal/architecture/toolkit/route.md</c> §§ TOOLKIT-26 through TOOLKIT-28.
/// </remarks>
/// <param name="WorkItem">The phase's own work-item description.</param>
/// <param name="Outcome">The phase's own <see cref="OperationOutcome" />, rendered as its name.</param>
/// <param name="Summary">
///     What the phase changed, when it completed, or its recommended next step, when it did not.
/// </param>
internal sealed record PhaseOutcome(string WorkItem, string Outcome, string Summary);
