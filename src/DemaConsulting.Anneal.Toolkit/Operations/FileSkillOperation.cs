using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Skills;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Writes one repository-local skill file under <c>.anneal/skills/</c> in the shared markdown-plus-front-
///     matter shape.
/// </summary>
/// <remarks>
///     This is the repository-local authoring half of <c>docs/architecture/toolkit/skills.md</c>: a caller files a
///     curated lesson deliberately, and the operation writes exactly one skill file under the repository's own
///     <c>.anneal/skills/</c> directory. It consults no model and performs no ranking or judgement; its whole job
///     is to validate the arguments, confine the resolved path to the local skill directory, refuse a collision,
///     and write the declared content in the shared shape.
///     <para>
///         It declares <see cref="OperationCategory.Authoring" /> because it edits repository content for later
///         consumption. That is the Runtime's authoring category exactly: produced content a caller reviews, never
///         a verdict a build may be failed on.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but two concurrent runs against one repository
///         can still race on the same destination file.
///     </para>
/// </remarks>
public sealed class FileSkillOperation : IOperation
{
    private readonly string _repositoryRoot;

    /// <summary>
    ///     Creates an operation over the current working directory.
    /// </summary>
    public FileSkillOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root.
    /// </summary>
    /// <param name="repositoryRoot">The repository written into. Must not be null, empty, or blank.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty, or blank.</exception>
    public FileSkillOperation(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    /// <inheritdoc />
    public string Name => "file-skill";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Authoring;

    /// <inheritdoc />
    public string Summary => "Write one repository-local skill file under .anneal/skills/";

    /// <inheritdoc />
    public ModelRole? RequiredRole => null;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal file-skill --id <id> --tags <tag[,tag...]> --summary <summary> --body <body> - " +
        "writes one repository-local skill file at .anneal/skills/<id>.md in the shared front-matter shape. " +
        "The id must be a single path segment (no '/' or '\\', not '.' or '..'). Fails when the target file " +
        "already exists or the resolved path would fall outside .anneal/skills/.";

    /// <inheritdoc />
    /// <remarks>
    ///     Expects the four required named arguments <c>--id</c>, <c>--tags</c>, <c>--summary</c>, and
    ///     <c>--body</c>, each followed by one value. Missing or blank <c>id</c>, <c>summary</c>, or
    ///     <c>body</c>, or an empty tag list, is a usage error.
    /// </remarks>
    public Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        if (!TryParseArguments(arguments, out var parsed))
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));

        if (string.IsNullOrWhiteSpace(parsed.Id) ||
            string.IsNullOrWhiteSpace(parsed.Summary) ||
            string.IsNullOrWhiteSpace(parsed.Body) ||
            parsed.Tags.Count == 0)
        {
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));
        }

        Skill skill;
        try
        {
            skill = new Skill(parsed.Id, parsed.Tags, parsed.Summary, parsed.Body);
        }
        catch (ArgumentException)
        {
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));
        }

        var skillsRoot = Path.Combine(_repositoryRoot, ".anneal", "skills");
        var destination = Path.Combine(skillsRoot, $"{skill.Id}.md");
        if (!IsUnderSkillsTree(destination))
        {
            output.WriteLine(
                $"file-skill: failed - the resolved path for '{skill.Id}' falls outside .anneal/skills/.");
            return Task.FromResult(new OperationResult(OperationOutcome.Failed));
        }

        if (File.Exists(destination))
        {
            output.WriteLine($"file-skill: failed - '{RelativeToRepository(destination)}' already exists.");
            return Task.FromResult(new OperationResult(OperationOutcome.Failed));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, SkillFile.Write(skill));

        var writtenPath = RelativeToRepository(destination);
        output.WriteLine($"file-skill: wrote {writtenPath}");
        return Task.FromResult(new OperationResult(
            OperationOutcome.Succeeded,
            new FileSkillReport(writtenPath, skill.Id)));
    }

    private bool IsUnderSkillsTree(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(_repositoryRoot, fullPath).Replace('\\', '/');
        return !relative.StartsWith("..", StringComparison.Ordinal) &&
               relative.StartsWith(".anneal/skills/", StringComparison.OrdinalIgnoreCase);
    }

    private string RelativeToRepository(string path) =>
        Path.GetRelativePath(_repositoryRoot, Path.GetFullPath(path)).Replace('\\', '/');

    private static bool TryParseArguments(IReadOnlyList<string> arguments, out ParsedArguments parsed)
    {
        parsed = new ParsedArguments(null, [], null, null);

        if (arguments.Count != 8)
            return false;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            var value = arguments[index + 1];

            if (!name.StartsWith("--", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(name, value))
            {
                return false;
            }
        }

        if (!values.TryGetValue("--id", out var id) ||
            !values.TryGetValue("--tags", out var tagsValue) ||
            !values.TryGetValue("--summary", out var summary) ||
            !values.TryGetValue("--body", out var body))
        {
            return false;
        }

        var tags = tagsValue
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        parsed = new ParsedArguments(id, tags, summary, body);
        return true;
    }

    private sealed record ParsedArguments(string? Id, IReadOnlyList<string> Tags, string? Summary, string? Body);
}
