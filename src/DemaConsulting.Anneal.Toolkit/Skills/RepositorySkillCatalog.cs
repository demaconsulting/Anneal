namespace DemaConsulting.Anneal.Toolkit.Skills;

/// <summary>
///     Reads repository-local skills from <c>.anneal/skills/</c>.
/// </summary>
/// <remarks>
///     Repository-local skills are authored deliberately at runtime and stored as ordinary files, so loading them
///     is a direct directory read with no model involvement and no silent skipping of malformed files.
/// </remarks>
internal sealed class RepositorySkillCatalog
{
    private readonly string _repositoryRoot;

    /// <summary>
    ///     Creates a loader over one repository's local skill directory.
    /// </summary>
    /// <param name="repositoryRoot">The repository to read. Must not be null, empty, or blank.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty, or blank.</exception>
    public RepositorySkillCatalog(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    /// <summary>
    ///     Loads every repository-local skill file under <c>.anneal/skills/</c>.
    /// </summary>
    /// <returns>The parsed local skills, ordered by id.</returns>
    public IReadOnlyList<Skill> Load()
    {
        var directory = Path.Combine(_repositoryRoot, ".anneal", "skills");
        if (!Directory.Exists(directory))
            return [];

        List<Skill> skills = [];
        foreach (var path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var markdown = File.ReadAllText(path);
            skills.Add(SkillFile.Read(markdown, path));
        }

        return skills
            .OrderBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
