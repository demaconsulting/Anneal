using System.ComponentModel;

namespace DemaConsulting.Anneal.Toolkit.Process.Decomposition;

/// <summary>
///     How much work a routed item takes — lines, files, modules touched — independent of what it promises. The
///     closed four-value vocabulary <c>change-classification.md</c> § Effort defines.
/// </summary>
internal enum Effort
{
    /// <summary>A few lines; obviously correct. No plan needed.</summary>
    [Description("a few lines, obviously correct - no plan needed")]
    Small,

    /// <summary>Multiple files, one system; roughly 50-200 lines. A lightweight plan.</summary>
    [Description("multiple files, one system, roughly 50-200 lines - a lightweight plan")]
    Medium,

    /// <summary>The interiors of multiple systems. A full plan plus a Tenet Check.</summary>
    [Description("interiors of multiple systems - a full plan plus a Tenet Check against CONSTRAINTS.md and affected contracts")]
    Large,

    /// <summary>Cannot execute as one unit; must be decomposed into phases first.</summary>
    [Description("cannot execute as one unit - must be decomposed into phases first")]
    Massive
}
