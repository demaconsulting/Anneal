using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     The candidate models behind each capability role, in preference order, read from a repository's own
///     configuration file.
/// </summary>
/// <remarks>
///     Roles appear in the Toolkit's contract; the models behind them do not. Keeping the mapping in a file a
///     repository owns means substituting a model is an edit, not a Toolkit release — and it means this type
///     holds names only, never credentials: the provider authenticates as the ambient Copilot account of the
///     calling session, so there is no token to configure and deliberately nowhere to put one.
///     <para>
///         A role names a list rather than a single model because the forcing case is rot rather than choice. A
///         single name breaks every repository that has not written its own configuration the day the provider
///         retires that model, and only a Toolkit release fixes it. An ordered list lets a newer model lead with
///         an older one held as a rearguard, so a retirement degrades instead of breaking. Which candidate
///         actually answers is settled elsewhere, by asking the provider what the account is offered — never by
///         calling a model and reading a failure, which cannot tell a retired model from a rate limit.
///     </para>
///     <para>
///         Thread safety: immutable and safe to share.
///     </para>
/// </remarks>
/// <param name="Light">
///     The candidates serving <see cref="ModelRole.Light" />, most preferred first. Never null and never empty.
/// </param>
/// <param name="Medium">
///     The candidates serving <see cref="ModelRole.Medium" />, most preferred first. Never null and never empty.
/// </param>
/// <param name="Heavy">
///     The candidates serving <see cref="ModelRole.Heavy" />, most preferred first. Never null and never empty.
/// </param>
public sealed record ModelConfiguration(
    IReadOnlyList<string> Light,
    IReadOnlyList<string> Medium,
    IReadOnlyList<string> Heavy)
{
    /// <summary>
    ///     The path, relative to a repository root, of the file this configuration is read from.
    /// </summary>
    public const string RelativePath = ".anneal/config.json";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     Reads the role-to-candidates mapping for a repository, falling back to the built-in defaults for
    ///     anything the repository does not state.
    /// </summary>
    /// <remarks>
    ///     A missing file is not an error: a repository that is content with the defaults should not have to
    ///     carry a file saying so. A file that exists but cannot be read or parsed is an error, because the
    ///     alternative — silently running against different models than the file names — is the kind of
    ///     invisible substitution this system treats as worse than stopping. A role naming an empty list is
    ///     treated as a role the file omits, for the same reason an absent one is: an empty list states no
    ///     preference, and the defaults are the stated preference of last resort.
    /// </remarks>
    /// <param name="repositoryRoot">
    ///     The repository root the relative configuration path is resolved against. Must not be null or blank.
    /// </param>
    /// <returns>The configured mapping, with defaults filling any role the file omits.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ModelUnavailableException">
    ///     Thrown when the configuration file exists but cannot be read or parsed. The message names the file
    ///     and the reason.
    /// </exception>
    public static ModelConfiguration Load(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var path = Path.Combine(repositoryRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return Default;

        ModelsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ModelsDocument>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new ModelUnavailableException(
                $"model configuration '{RelativePath}' could not be read: {exception.Message}", exception);
        }

        var models = document?.Models;
        return new ModelConfiguration(
            Candidates(models?.Light, Default.Light),
            Candidates(models?.Medium, Default.Medium),
            Candidates(models?.Heavy, Default.Heavy));
    }

    /// <summary>
    ///     The mapping used by a repository that ships no configuration file.
    /// </summary>
    /// <remarks>
    ///     The defaults name a small, a mid and a large tier rather than pointing every role at one, so that a
    ///     repository which never writes a configuration file still gets the cost shape the roles exist to
    ///     express. Each tier leads with the newest model of its size and keeps an older one behind it as a
    ///     rearguard, so a retirement costs a repository the newer model rather than the whole role.
    /// </remarks>
    public static ModelConfiguration Default { get; } = new(
        ["gpt-5.4-mini", "gpt-5.4"],
        ["gpt-5.5", "gpt-5.4"],
        ["claude-sonnet-4.6", "claude-sonnet-5", "gpt-5.5"]);

    /// <summary>
    ///     Returns the candidate models serving a role, most preferred first.
    /// </summary>
    /// <param name="role">The capability tier.</param>
    /// <returns>The configured candidates for that tier. Never empty.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    public IReadOnlyList<string> CandidatesFor(ModelRole role) => role switch
    {
        ModelRole.Light => Light,
        ModelRole.Medium => Medium,
        ModelRole.Heavy => Heavy,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown model role.")
    };

    /// <remarks>
    ///     Blank entries are dropped rather than carried, so a placeholder left in the file cannot become a
    ///     candidate no provider will ever offer — which the loud failure would then name back to the reader as
    ///     though the repository had meant it.
    /// </remarks>
    private static IReadOnlyList<string> Candidates(IReadOnlyList<string>? stated, IReadOnlyList<string> fallback)
    {
        if (stated is null)
            return fallback;

        var named = stated.Where(entry => !string.IsNullOrWhiteSpace(entry)).ToArray();
        return named.Length == 0 ? fallback : named;
    }

    /// <remarks>
    ///     Modeled as a nested document rather than a flat one so the file has room to grow a sibling section
    ///     without the role names having to move.
    /// </remarks>
    private sealed record ModelsDocument
    {
        [JsonPropertyName("models")]
        public RoleModels? Models { get; init; }
    }

    private sealed record RoleModels
    {
        public string[]? Light { get; init; }

        public string[]? Medium { get; init; }

        public string[]? Heavy { get; init; }
    }
}
