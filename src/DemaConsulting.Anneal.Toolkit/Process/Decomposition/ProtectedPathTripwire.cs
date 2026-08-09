using DemaConsulting.Anneal.Toolkit.Process.Routing;

namespace DemaConsulting.Anneal.Toolkit.Process.Decomposition;

/// <summary>
///     A mechanical, deterministic check for whether a <see cref="Phase" />'s declared file scope touches one of
///     the files <c>change-classification.md</c> § Massive Effort Must Be Decomposed names as an unconditional
///     escalation trigger, whatever the cumulative check concludes.
/// </summary>
/// <remarks>
///     Pure and model-free by design — TOOLKIT-27 forces the same escalation outcome regardless of any oracle's
///     conclusion, so the check that decides whether it fires must not itself be able to disagree from one call
///     to the next. This mirrors <see cref="RepositoryFacts" />'s own "every fact here is computed by reading
///     files and matching text, never by a model call" discipline, applied to one narrow question instead of a
///     whole ledger of them.
/// </remarks>
internal static class ProtectedPathTripwire
{
    private static readonly string[] ProtectedFiles = ["readme.md", "constraints.md", "backlog.md"];
    private const string ProtectedDirectory = "docs/architecture/";

    /// <summary>
    ///     Reports the first protected path a phase's declared file scope touches, or null when it touches none.
    /// </summary>
    /// <param name="fileScope">
    ///     The glob or path patterns to check, matched by simple containment against the protected paths — never
    ///     interpreted as a real glob, because whether a pattern names a protected file is decided the same way a
    ///     human reading the pattern would decide it, not by whether it would expand to match one at runtime.
    /// </param>
    /// <returns>
    ///     The first entry of <paramref name="fileScope" /> that names a protected path, or null when none does.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileScope" /> is null.</exception>
    public static string? FindTrippedPath(IReadOnlyList<string> fileScope)
    {
        ArgumentNullException.ThrowIfNull(fileScope);

        foreach (var entry in fileScope)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            var normalized = entry.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

            if (ProtectedFiles.Any(protectedFile =>
                    normalized == protectedFile || normalized.EndsWith("/" + protectedFile, StringComparison.Ordinal)))
                return entry;

            if (normalized.StartsWith(ProtectedDirectory, StringComparison.Ordinal) ||
                normalized.Contains("/" + ProtectedDirectory, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    /// <summary>Whether a phase's declared file scope touches any protected path.</summary>
    /// <param name="fileScope">The glob or path patterns to check. Must not be null.</param>
    /// <returns>True when <see cref="FindTrippedPath" /> would return a non-null path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileScope" /> is null.</exception>
    public static bool Trips(IReadOnlyList<string> fileScope) => FindTrippedPath(fileScope) is not null;
}
