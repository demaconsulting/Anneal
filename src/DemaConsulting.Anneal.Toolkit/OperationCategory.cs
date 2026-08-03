namespace DemaConsulting.Anneal.Toolkit;

/// <summary>
///     The kind of work an operation performs, which is what decides whether its failure may fail a build.
/// </summary>
/// <remarks>
///     Gating is a property of this declaration rather than of an exit code, so that a research or advisory
///     operation cannot become a build gate by accident. Adding a category obliges every caller that switches
///     on one to handle it, which is why the set is deliberately small and closed.
/// </remarks>
public enum OperationCategory
{
    /// <summary>
    ///     Reaches a verdict a build may be failed on. Only this category gates.
    /// </summary>
    Enforcement,

    /// <summary>
    ///     Answers a question put by an agent or a person. Its outcome informs a decision and never blocks one.
    /// </summary>
    Research,

    /// <summary>
    ///     Reports something worth knowing that nobody is obliged to act on.
    /// </summary>
    Advisory,

    /// <summary>
    ///     Produces or edits content for a caller to review. It cannot gate, because a build failing on
    ///     generated prose would make the generator authoritative over the author.
    /// </summary>
    Authoring
}
