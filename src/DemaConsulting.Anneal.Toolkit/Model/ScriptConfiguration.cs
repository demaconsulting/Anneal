using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     The repository-relative fix, build, and lint scripts every deterministic check runs, read from a
///     repository's own configuration file.
/// </summary>
/// <remarks>
///     Every repository this Toolkit ships against today happens to have <c>fix.ps1</c>, <c>build.ps1</c> and
///     <c>lint.ps1</c> at its root, so those names are the default. Neither is universal: a repository in a
///     different ecosystem may have none of them, a different name for one, or a single script that plays two
///     roles. Rather than the Toolkit guessing a repository's shape, a repository states what it has here, once,
///     the same pattern <see cref="ContractCheckConfiguration" /> already uses.
///     <para>
///         A script this configuration resolves to null is not a misconfiguration - it is a repository honestly
///         reporting it has no such step, and every caller treats that the same way: the check passes trivially,
///         because there is nothing to fail.
///     </para>
///     <para>
///         Thread safety: immutable and safe to share.
///     </para>
/// </remarks>
/// <param name="Fix">The repository-relative auto-fix script, or null when the repository has none.</param>
/// <param name="Build">The repository-relative build/test script, or null when the repository has none.</param>
/// <param name="Lint">The repository-relative lint/check script, or null when the repository has none.</param>
public sealed record ScriptConfiguration(string? Fix, string? Build, string? Lint)
{
    /// <summary>
    ///     The path, relative to a repository root, of the file this configuration is read from. The same file
    ///     <see cref="ModelConfiguration" /> and <see cref="ContractCheckConfiguration" /> read.
    /// </summary>
    public const string RelativePath = ModelConfiguration.RelativePath;

    private const string DefaultFix = "fix.ps1";
    private const string DefaultBuild = "build.ps1";
    private const string DefaultLint = "lint.ps1";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     Reads the fix/build/lint script names for a repository, resolving each to the repository's own default
    ///     script when the file does not state one and that default exists on disk, or to null when neither
    ///     applies.
    /// </summary>
    /// <remarks>
    ///     A missing configuration file is not an error, for the same reason it is not one for
    ///     <see cref="ModelConfiguration" />: a repository content with the defaults should not have to carry a
    ///     file saying so. A file that exists but cannot be read or parsed is an error rather than a silent
    ///     fallback, for the same reason as every other section of this file. An explicitly configured script name
    ///     is trusted as given and never checked for existence here - a repository that names a script which does
    ///     not exist finds out when the check runs it, not through a silently substituted skip.
    /// </remarks>
    /// <param name="repositoryRoot">
    ///     The repository root the relative configuration path and every default script name are resolved
    ///     against. Must not be null or blank.
    /// </param>
    /// <returns>The configured scripts, with a default or null filling any the file omits.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the configuration file exists but cannot be read or parsed. The message names the file and
    ///     the reason.
    /// </exception>
    public static ScriptConfiguration Load(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var path = Path.Combine(repositoryRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        ScriptsSection? scripts = null;
        if (File.Exists(path))
        {
            ConfigDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<ConfigDocument>(File.ReadAllText(path), ReadOptions);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"script configuration '{RelativePath}' could not be read: {exception.Message}", exception);
            }

            scripts = document?.Scripts;
        }

        return new ScriptConfiguration(
            Resolve(repositoryRoot, scripts?.Fix, DefaultFix),
            Resolve(repositoryRoot, scripts?.Build, DefaultBuild),
            Resolve(repositoryRoot, scripts?.Lint, DefaultLint));
    }

    /// <remarks>
    ///     An explicitly empty string states "this repository has none of these", so it resolves to null exactly
    ///     as an absent default does when the default file is not on disk - the two are indistinguishable to
    ///     every caller, and both mean "nothing to run".
    /// </remarks>
    private static string? Resolve(string repositoryRoot, string? configured, string defaultName)
    {
        if (configured is not null)
            return string.IsNullOrWhiteSpace(configured) ? null : configured;

        var defaultPath = Path.Combine(repositoryRoot, defaultName);
        return File.Exists(defaultPath) ? defaultName : null;
    }

    /// <remarks>
    ///     Modeled as its own document type, rather than folded into <see cref="ModelConfiguration" />'s, so
    ///     neither section's shape constrains the other's.
    /// </remarks>
    private sealed record ConfigDocument
    {
        [JsonPropertyName("scripts")]
        public ScriptsSection? Scripts { get; init; }
    }

    private sealed record ScriptsSection
    {
        public string? Fix { get; init; }

        public string? Build { get; init; }

        public string? Lint { get; init; }
    }
}
