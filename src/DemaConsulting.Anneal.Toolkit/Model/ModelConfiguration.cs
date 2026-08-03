using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     The model behind each capability role, read from a repository's own configuration file.
/// </summary>
/// <remarks>
///     Roles appear in the Toolkit's contract; the models behind them do not. Keeping the mapping in a file a
///     repository owns means substituting a model is an edit, not a Toolkit release — and it means this type
///     holds names only, never credentials: the provider authenticates as the ambient Copilot account of the
///     calling session, so there is no token to configure and deliberately nowhere to put one.
/// </remarks>
/// <param name="Light">The model serving <see cref="ModelRole.Light" />. Never null or blank.</param>
/// <param name="Medium">The model serving <see cref="ModelRole.Medium" />. Never null or blank.</param>
/// <param name="Heavy">The model serving <see cref="ModelRole.Heavy" />. Never null or blank.</param>
public sealed record ModelConfiguration(string Light, string Medium, string Heavy)
{
    /// <summary>
    ///     The path, relative to a repository root, of the file this configuration is read from.
    /// </summary>
    public const string RelativePath = ".anneal/config.json";

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     Reads the role-to-model mapping for a repository, falling back to the built-in defaults for anything
    ///     the repository does not state.
    /// </summary>
    /// <remarks>
    ///     A missing file is not an error: a repository that is content with the defaults should not have to
    ///     carry a file saying so. A file that exists but cannot be read or parsed is an error, because the
    ///     alternative — silently running against different models than the file names — is the kind of
    ///     invisible substitution this system treats as worse than stopping.
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
            Blank(models?.Light) ? Default.Light : models!.Light!,
            Blank(models?.Medium) ? Default.Medium : models!.Medium!,
            Blank(models?.Heavy) ? Default.Heavy : models!.Heavy!);
    }

    /// <summary>
    ///     The mapping used by a repository that ships no configuration file.
    /// </summary>
    /// <remarks>
    ///     The defaults name a small, a mid and a large model rather than pointing every role at one, so that a
    ///     repository which never writes a configuration file still gets the cost shape the roles exist to
    ///     express.
    /// </remarks>
    public static ModelConfiguration Default { get; } = new("gpt-5.4-mini", "gpt-5.4", "claude-sonnet-4.5");

    /// <summary>
    ///     Returns the model serving a role.
    /// </summary>
    /// <param name="role">The capability tier.</param>
    /// <returns>The configured model name for that tier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="role" /> is not a defined role.</exception>
    public string ModelFor(ModelRole role) => role switch
    {
        ModelRole.Light => Light,
        ModelRole.Medium => Medium,
        ModelRole.Heavy => Heavy,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown model role.")
    };

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

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
        public string? Light { get; init; }

        public string? Medium { get; init; }

        public string? Heavy { get; init; }
    }
}
