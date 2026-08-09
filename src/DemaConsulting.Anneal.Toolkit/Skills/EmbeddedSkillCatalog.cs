using System.Reflection;

namespace DemaConsulting.Anneal.Toolkit.Skills;

/// <summary>
///     Loads the built-in Toolkit-wide skill catalog from embedded markdown resources into memory once per process.
/// </summary>
/// <remarks>
///     This mirrors the embedded-resource mechanism used by Jeeves' knowledge-card catalog, adapted to Anneal's
///     markdown-plus-front-matter file shape instead of JSON: the catalog lives as data in the assembly rather
///     than as hard-coded C#, is loaded once into an in-memory list, and fails loudly when any embedded skill is
///     malformed. Quietly dropping a shipped skill would make the search surface lie about what the Toolkit knows.
/// </remarks>
internal static class EmbeddedSkillCatalog
{
    private static readonly Lazy<IReadOnlyList<Skill>> Catalog = new(() => new SkillCatalogLoader().Load());

    /// <summary>
    ///     The built-in Toolkit-wide skills, loaded from embedded resources the first time the catalog is read.
    /// </summary>
    public static IReadOnlyList<Skill> All => Catalog.Value;
}

/// <summary>
///     Loads the embedded Toolkit-wide skill files from an assembly.
/// </summary>
internal sealed class SkillCatalogLoader
{
    private readonly Assembly _assembly;

    /// <summary>
    ///     Creates a loader over the assembly whose embedded resources hold the Toolkit-wide catalog.
    /// </summary>
    /// <param name="assembly">
    ///     The assembly to enumerate for embedded skills; defaults to the loader's own assembly, which carries the
    ///     production catalog.
    /// </param>
    public SkillCatalogLoader(Assembly? assembly = null)
    {
        _assembly = assembly ?? typeof(SkillCatalogLoader).Assembly;
    }

    /// <summary>
    ///     Loads every embedded skill card in deterministic order.
    /// </summary>
    /// <returns>The parsed skills, ordered by id.</returns>
    /// <exception cref="SkillLoadException">
    ///     Thrown when an embedded skill cannot be opened, parsed, or validated.
    /// </exception>
    public IReadOnlyList<Skill> Load()
    {
        List<Skill> skills = [];

        foreach (var resourceName in _assembly.GetManifestResourceNames()
                     .Where(name => name.Contains(".Skills.Cards.", StringComparison.OrdinalIgnoreCase) &&
                                    name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            using var stream = _assembly.GetManifestResourceStream(resourceName)
                               ?? throw Invalid(resourceName, "the embedded resource stream could not be opened");
            using var reader = new StreamReader(stream);
            var markdown = reader.ReadToEnd();

            try
            {
                skills.Add(SkillFile.Read(markdown, resourceName));
            }
            catch (SkillFormatException exception)
            {
                throw Invalid(resourceName, exception.Message, exception);
            }
        }

        return skills
            .OrderBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SkillLoadException Invalid(string resourceName, string reason, Exception? innerException = null)
    {
        var message = $"Embedded skill '{resourceName}' is invalid: {reason}.";
        return innerException is null ? new SkillLoadException(message) : new SkillLoadException(message, innerException);
    }
}

/// <summary>
///     The failure raised when an embedded Toolkit-wide skill cannot be loaded.
/// </summary>
internal sealed class SkillLoadException : Exception
{
    /// <summary>Creates the exception with a message describing the invalid embedded skill.</summary>
    /// <param name="message">Human-readable description of the failure.</param>
    public SkillLoadException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying cause.</summary>
    /// <param name="message">Human-readable description of the failure.</param>
    /// <param name="innerException">The underlying parse or validation error.</param>
    public SkillLoadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
