namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     What one model interaction consumed, in tokens.
/// </summary>
/// <remarks>
///     Carried beside the reply rather than derived from it, because token counts are a fact the provider
///     reports and nothing above the seam can recompute. A provider that reports none yields a null usage
///     rather than a zeroed one: "not reported" and "cost nothing" are different facts, and recording the
///     second when the first is true would quietly understate what a run spent.
/// </remarks>
/// <param name="InputTokens">Tokens consumed by the prompt. Never negative.</param>
/// <param name="OutputTokens">Tokens generated in reply. Never negative.</param>
public sealed record ModelUsage(long InputTokens, long OutputTokens)
{
    /// <summary>
    ///     The usage of no interaction at all, which is the identity this type accumulates from.
    /// </summary>
    public static ModelUsage None { get; } = new(0, 0);

    /// <summary>
    ///     Returns the total of this usage and another.
    /// </summary>
    /// <param name="other">The usage to add. Must not be null.</param>
    /// <returns>The summed usage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other" /> is null.</exception>
    public ModelUsage Add(ModelUsage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new ModelUsage(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens);
    }
}
