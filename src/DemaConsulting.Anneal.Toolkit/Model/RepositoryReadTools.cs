using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace DemaConsulting.Anneal.Toolkit.Model;

/// <summary>
///     The read-only tool grant a model-backed operation gives a model: read a file, list a directory, search
///     the working tree — and nothing else.
/// </summary>
/// <remarks>
///     There is deliberately no tool here that runs a command or writes a file, and no way to add one through
///     configuration. A probe is a question about a repository; a repository that can be edited by the thing
///     answering questions about it is a repository whose answers cannot be trusted. Every tool resolves its
///     path against the repository root and refuses anything that escapes it, so a model that asks for a system
///     path is told no rather than obeyed.
///     <para>
///         Thread safety: the returned functions hold no mutable state and are safe to invoke concurrently; they
///         read whatever is on disk at the moment they run.
///     </para>
/// </remarks>
public static class RepositoryReadTools
{
    /// <summary>The most lines a single read returns, so one tool call cannot consume the context window.</summary>
    private const int MaxLinesPerRead = 400;

    /// <summary>The most entries a listing returns, bounding a call made against a large directory.</summary>
    private const int MaxEntriesPerListing = 300;

    /// <summary>The most matches a search returns, bounding a call made with an over-broad pattern.</summary>
    private const int MaxSearchMatches = 40;

    /// <summary>How long a single search may run before it is abandoned, guarding against a pathological pattern.</summary>
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Builds the complete read-only tool set bound to a repository root.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The directory every path is resolved against and outside which every request is refused. Must not be
    ///     null or blank; it need not exist, in which case every call reports nothing found.
    /// </param>
    /// <returns>
    ///     The granted tools, in a stable order. Never null and never empty — a caller that wants no tools passes
    ///     none rather than asking for an empty grant here.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public static IReadOnlyList<AITool> CreateAll(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);

        return
        [
            AIFunctionFactory.Create(
                (string path, int start, int max) => ReadFile(root, path, start, max),
                "read_file",
                "Read a text file from the repository. 'path' is relative to the repository root; 'start' is the " +
                "first line number (1-based, 0 means 1); 'max' is how many lines to return (0 means as many as " +
                "are allowed). Returns numbered lines."),

            AIFunctionFactory.Create(
                (string path, int depth) => ListFiles(root, path, depth),
                "list_files",
                "List the files and directories under a repository-relative path. 'path' empty means the " +
                "repository root; 'depth' is how many levels to descend (0 means 1)."),

            AIFunctionFactory.Create(
                (string pattern, string path, string extension) => SearchFiles(root, pattern, path, extension),
                "search_files",
                "Search the repository for a regular expression. 'path' limits the search to a " +
                "repository-relative subtree (empty means the whole repository); 'extension' limits it to files " +
                "with that extension, for example '.md' (empty means every text file). Returns file, line number " +
                "and the matching line.")
        ];
    }

    /// <summary>
    ///     The names of the granted tools, in the order <see cref="CreateAll" /> returns them.
    /// </summary>
    /// <remarks>
    ///     Published so a caller — and the contract test for the read-only invariant — can state the permitted
    ///     surface without constructing the tools, and so a new tool cannot be added without the name appearing
    ///     here where it will be noticed.
    /// </remarks>
    public static IReadOnlyList<string> Names { get; } = ["read_file", "list_files", "search_files"];

    private static string ReadFile(string root, string path, int start, int max)
    {
        var resolved = Resolve(root, path);
        if (resolved is null)
            return "refused: that path is outside the repository.";

        if (!File.Exists(resolved))
            return "not found.";

        var lines = File.ReadAllLines(resolved);
        var first = Math.Max(start, 1);
        var count = Math.Clamp(max <= 0 ? MaxLinesPerRead : max, 1, MaxLinesPerRead);

        var builder = new StringBuilder();
        for (var index = first - 1; index < lines.Length && index < first - 1 + count; index++)
            builder.Append(index + 1).Append(": ").AppendLine(lines[index]);

        return builder.Length == 0 ? "(no lines in that range)" : builder.ToString();
    }

    private static string ListFiles(string root, string path, int depth)
    {
        var resolved = Resolve(root, path);
        if (resolved is null)
            return "refused: that path is outside the repository.";

        if (!Directory.Exists(resolved))
            return "not found.";

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = depth > 1,
            MaxRecursionDepth = Math.Max(depth, 1) - 1,
            IgnoreInaccessible = true
        };

        var entries = Directory
            .EnumerateFileSystemEntries(resolved, "*", options)
            .Where(entry => !IsInExcludedDirectory(root, entry))
            .Select(entry => Path.GetRelativePath(root, entry).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .Take(MaxEntriesPerListing)
            .ToList();

        return entries.Count == 0 ? "(empty)" : string.Join('\n', entries);
    }

    private static string SearchFiles(string root, string pattern, string path, string extension)
    {
        var resolved = Resolve(root, path);
        if (resolved is null)
            return "refused: that path is outside the repository.";

        if (!Directory.Exists(resolved))
            return "not found.";

        Regex expression;
        try
        {
            expression = new Regex(pattern, RegexOptions.CultureInvariant, SearchTimeout);
        }
        catch (ArgumentException exception)
        {
            return $"refused: that is not a valid regular expression ({exception.Message}).";
        }

        var filter = string.IsNullOrWhiteSpace(extension)
            ? "*"
            : "*" + (extension.StartsWith('.') ? extension : "." + extension);

        var matches = new List<string>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        foreach (var file in Directory.EnumerateFiles(resolved, filter, options))
        {
            if (matches.Count >= MaxSearchMatches)
                break;

            if (IsInExcludedDirectory(root, file))
                continue;

            AppendMatches(root, file, expression, matches);
        }

        return matches.Count == 0 ? "(no matches)" : string.Join('\n', matches);
    }

    /// <remarks>
    ///     A read that fails is not a search failure: an unreadable or binary file is skipped so one such file
    ///     cannot turn a whole search into an error the model then has to reason about.
    /// </remarks>
    private static void AppendMatches(string root, string file, Regex expression, List<string> matches)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        for (var index = 0; index < lines.Length && matches.Count < MaxSearchMatches; index++)
        {
            try
            {
                if (expression.IsMatch(lines[index]))
                    matches.Add($"{relative}:{index + 1}: {lines[index].Trim()}");
            }
            catch (RegexMatchTimeoutException)
            {
                return;
            }
        }
    }

    /// <remarks>
    ///     Build output and vendored dependencies are excluded because they are enormous, are not what any
    ///     question about a repository is about, and would otherwise consume the whole result budget before the
    ///     model ever saw a source file.
    /// </remarks>
    private static bool IsInExcludedDirectory(string root, string entry)
    {
        var relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
        return relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("node_modules/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains(".venv/", StringComparison.OrdinalIgnoreCase);
    }

    /// <returns>
    ///     The absolute path, or null when the request escapes the repository root. Null rather than an
    ///     exception because a model asking for a path outside the tree is a refusal to report back to it, not a
    ///     fault in the operation.
    /// </returns>
    private static string? Resolve(string root, string path)
    {
        var relative = (path ?? string.Empty).Trim();
        if (relative.Length == 0)
            return root;

        var normalized = relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, normalized));

        // The root itself is inside the repository - a model that asks for "." is asking a reasonable question -
        // so it is admitted explicitly rather than falling foul of the prefix test.
        var trimmed = root.TrimEnd(Path.DirectorySeparatorChar);
        return full.Equals(trimmed, StringComparison.Ordinal) ||
            full.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? full
                : null;
    }
}
