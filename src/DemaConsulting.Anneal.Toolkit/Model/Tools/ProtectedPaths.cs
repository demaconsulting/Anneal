namespace DemaConsulting.Anneal.Toolkit.Model.Tools;

/// <summary>
///     The files a granted edit tool refuses to write, whatever a model asks: the protected configuration files
///     and repository scripts named in <c>AGENTS.md</c> § Key Configuration Files.
/// </summary>
/// <remarks>
///     These files decide what the checks themselves check. A worker that may edit them can make a failing lint
///     pass by widening the linter rather than by fixing the code, and it can do so while reporting truthfully
///     that it fixed the failure — the report would be accurate and the repository would be worse. Refusing the
///     write is what turns the prose rule "respect all protected configuration files" into a wall: a model that
///     ignores an instruction produces a plausible claim of having complied, and a model that is refused
///     produces a recorded fact.
///     <para>
///         Refusal is not the end of the road. The refusal message names the path and says the user must approve
///         the change, which is exactly what lets an operation escalate — reporting "the correct repair is a
///         protected-file change" as an outcome distinct from failure — rather than grinding its budget editing
///         sources to dodge a misconfigured linter.
///     </para>
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
public static class ProtectedPaths
{
    /// <summary>
    ///     The repository-relative paths no granted tool may write, in the order <c>AGENTS.md</c> lists them.
    /// </summary>
    /// <remarks>
    ///     Written out rather than derived from a pattern, because the list is a decision recorded in
    ///     <c>AGENTS.md</c> and a pattern would silently acquire or lose members as the repository grows files
    ///     that happen to match it.
    /// </remarks>
    public static IReadOnlyList<string> Names { get; } =
    [
        "fix.ps1",
        "lint.ps1",
        "build.ps1",
        "check-contracts.ps1",
        ".editorconfig",
        ".cspell.yaml",
        ".markdownlint-cli2.yaml",
        ".yamllint.yaml"
    ];

    private static readonly HashSet<string> Denied = new(Names, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     States whether a repository-relative path names a protected configuration file or repository script.
    /// </summary>
    /// <remarks>
    ///     Matched case-insensitively and only at the repository root, which is where <c>AGENTS.md</c> places
    ///     every one of them. A same-named file deeper in the tree — a nested <c>.editorconfig</c> under a
    ///     sample directory, say — is ordinary content, and refusing it would be a rule this repository never
    ///     stated.
    /// </remarks>
    /// <param name="relativePath">
    ///     The path relative to the repository root, in either separator form. Null or blank is not protected —
    ///     it is not a file at all, and the containment check has already refused it.
    /// </param>
    /// <returns>True when the path is protected and must not be written.</returns>
    public static bool IsProtected(string? relativePath) =>
        !string.IsNullOrWhiteSpace(relativePath) && Denied.Contains(Normalize(relativePath));

    /// <summary>
    ///     The message a tool returns to a model when it refuses to write a protected path.
    /// </summary>
    /// <remarks>
    ///     Phrased so the model can act on it rather than retry it: it names the path, says why the write was
    ///     refused, and states that the user must approve the change. A bare "denied" would leave a worker
    ///     guessing, and the guess it makes is to edit something else instead.
    /// </remarks>
    /// <param name="tool">The tool that refused. Must not be null or blank.</param>
    /// <param name="relativePath">The path the model asked to write. Must not be null or blank.</param>
    /// <returns>The refusal text, carrying the <see cref="ToolReply.ProtectedPrefix" /> this refusal carries.</returns>
    /// <exception cref="ArgumentException">Thrown when either argument is null, empty or blank.</exception>
    public static string Refusal(string tool, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return ToolReply.ProtectedPrefix +
            $"{tool} will not write '{relativePath}': it is a protected configuration file or repository " +
            "script, and changing it needs the user's approval. Do not work around it by editing something " +
            "else; report that this change is required and stop.";
    }

    /// <remarks>
    ///     A leading <c>./</c> is stripped because a model writes it freely and it names the same file; the
    ///     separator is folded for the same reason. Nothing else is normalized — <c>..</c> never reaches here,
    ///     having already been refused by <see cref="RepositoryPath" />.
    /// </remarks>
    private static string Normalize(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        return normalized.TrimStart('/');
    }
}
