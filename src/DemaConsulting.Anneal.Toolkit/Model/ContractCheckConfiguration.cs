using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     The arguments a repository's own strict contract check is run with, read from the same repository-owned
///     configuration file <see cref="ModelConfiguration" /> reads.
/// </summary>
/// <remarks>
///     A repository whose clauses are all verified the same way — one language, one test framework, one result
///     format — needs no configuration: <c>-Strict</c> alone reproduces <see cref="Operations.CheckContractsOperation" />'s
///     own C# xUnit defaults. A repository checking several discovery shapes in one run, the way this repository
///     checks its own C# boundary tests alongside its root-level PowerShell fixture suites, states the arguments
///     it needs — typically repeated <c>-TestProfiles</c> entries — once, here, rather than the Toolkit guessing
///     a repository's shape or a caller re-deriving it at every call site.
///     <para>
///         Thread safety: immutable and safe to share.
///     </para>
/// </remarks>
/// <param name="Arguments">
///     The arguments passed to <see cref="Operations.CheckContractsOperation" />, most-preferred defaults first.
///     Never null; empty is treated as "no arguments" rather than an error.
/// </param>
public sealed record ContractCheckConfiguration(IReadOnlyList<string> Arguments)
{
    /// <summary>
    ///     The path, relative to a repository root, of the file this configuration is read from. The same file
    ///     <see cref="ModelConfiguration" /> reads, under a sibling top-level property, so a repository's own
    ///     runtime configuration lives in one place rather than several.
    /// </summary>
    public const string RelativePath = ModelConfiguration.RelativePath;

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     The arguments used by a repository that ships no configuration file: a plain <c>-Strict</c>
    ///     check against <see cref="Operations.CheckContractsOperation" />'s own C# xUnit defaults.
    /// </summary>
    public static ContractCheckConfiguration Default { get; } = new(["-Strict"]);

    /// <summary>
    ///     Reads the contract-check arguments for a repository, falling back to <see cref="Default" /> when the
    ///     file is absent or names no arguments.
    /// </summary>
    /// <remarks>
    ///     A missing file is not an error, for the same reason it is not one for <see cref="ModelConfiguration" />:
    ///     a repository content with the default single-profile check should not have to carry a file saying so.
    ///     A file that exists but cannot be read or parsed is an error rather than a silent fallback, because
    ///     running a different check than the one a reader believes was configured is the failure this whole
    ///     process treats as worse than stopping.
    /// </remarks>
    /// <param name="repositoryRoot">
    ///     The repository root the relative configuration path is resolved against. Must not be null or blank.
    /// </param>
    /// <returns>The configured arguments, or <see cref="Default" /> when the file omits them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the configuration file exists but cannot be read or parsed. The message names the file
    ///     and the reason.
    /// </exception>
    public static ContractCheckConfiguration Load(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var path = Path.Combine(repositoryRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            return Default;

        ConfigDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ConfigDocument>(File.ReadAllText(path), ReadOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"contract-check configuration '{RelativePath}' could not be read: {exception.Message}", exception);
        }

        var arguments = document?.ContractCheck?.Arguments?
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .ToArray();

        return arguments is { Length: > 0 } ? new ContractCheckConfiguration(arguments) : Default;
    }

    /// <remarks>
    ///     Modeled as its own document type, rather than folded into <see cref="ModelConfiguration" />'s, so
    ///     neither section's shape constrains the other's.
    /// </remarks>
    private sealed record ConfigDocument
    {
        [JsonPropertyName("contractCheck")]
        public ContractCheckSection? ContractCheck { get; init; }
    }

    private sealed record ContractCheckSection
    {
        public string[]? Arguments { get; init; }
    }
}
