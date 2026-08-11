using Microsoft.Extensions.AI;

namespace DemaConsulting.Anneal.Toolkit.Model.Tools;

/// <summary>
///     The privileged tool grant: create a file, replace a file, make a targeted edit, delete a file — and nothing
///     else.
/// </summary>
/// <remarks>
///     This is the group that makes a writing process possible, and it is deliberately the smallest surface that
///     does. There is no tool here that runs a command, moves a file, or reaches the network: a worker that can run
///     commands can do anything and then report plausibly that it did not, which is the failure the whole system
///     exists to remove. Control flow — running <c>fix.ps1</c> and <c>lint.ps1</c> — belongs to the operation, not
///     to the model.
///     <para>
///         Every path is resolved through <see cref="RepositoryPath" /> and refused if it escapes, and every
///         write is checked against <see cref="ProtectedPaths" /> and refused if it lands on a protected
///         configuration file or repository script. Both refusals come back to the model as readable text, so a
///         worker that was denied knows it was denied and can say so — which is what an operation escalates on.
///     </para>
///     <para>
///         A deletion is always preceded by archiving the file's content to
///         <c>.anneal/logs/deleted-&lt;timestamp&gt;.patch</c> in unified-diff format. This archive sits alongside
///         interrupted-diff snapshots so that any deletion is recoverable regardless of the file's git-tracked state.
///     </para>
///     <para>
///         Thread safety: the returned functions hold no mutable state, but they write the filesystem; two
///         concurrent edits to one file race exactly as two processes would. One session at a time.
///     </para>
/// </remarks>
public static class RepositoryEditTools
{
    /// <summary>
    ///     The names of the granted tools, in the order <see cref="CreateAll" /> returns them.
    /// </summary>
    /// <remarks>
    ///     Published so a caller — and the contract test for the tool-grant invariant — can state the permitted
    ///     surface without constructing the tools, and so a new tool cannot be added without its name appearing
    ///     here where it will be noticed.
    /// </remarks>
    public static IReadOnlyList<string> Names { get; } = ["create_file", "replace_file", "edit_file", "delete_file"];

    /// <summary>
    ///     Builds the complete edit tool set bound to a repository root.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The directory every path is resolved against and outside which every write is refused. Must not be
    ///     null or blank.
    /// </param>
    /// <returns>The granted tools, in a stable order. Never null and never empty.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public static IReadOnlyList<AITool> CreateAll(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);

        return
        [
            AIFunctionFactory.Create(
                (string path, string content) => CreateFile(root, path, content),
                "create_file",
                "Create a new file with the given content. 'path' is relative to the repository root. Fails if " +
                "the file already exists."),

            AIFunctionFactory.Create(
                (string path, string content) => ReplaceFile(root, path, content),
                "replace_file",
                "Replace the entire contents of an existing file. 'path' is relative to the repository root. " +
                "Fails if the file does not exist."),

            AIFunctionFactory.Create(
                (string path, string oldStr, string newStr) => EditFile(root, path, oldStr, newStr),
                "edit_file",
                "Make a targeted edit to an existing file: replace the single occurrence of 'oldStr' - an " +
                "exact, verbatim snippet copied from a prior read_file result - with 'newStr'. Fails if oldStr " +
                "is absent or matches more than once. This is a plain text replacement, not a diff applier."),

            AIFunctionFactory.Create(
                (string path) => DeleteFile(root, path),
                "delete_file",
                "Delete an existing file. 'path' is relative to the repository root. Before deleting, the " +
                "file's content is archived to .anneal/logs/deleted-<timestamp>.patch so the deletion is " +
                "recoverable. Fails if the file does not exist.")
        ];
    }

    private static string CreateFile(string root, string? path, string? content)
    {
        if (Deny("create_file", root, path) is { } denial)
            return denial;

        RepositoryPath.TryResolveFile(root, path, out var full);
        if (File.Exists(full))
            return $"create_file: '{path}' already exists - use replace_file or edit_file to change it.";

        Directory.CreateDirectory(Path.GetDirectoryName(full!)!);
        File.WriteAllText(full!, content ?? string.Empty);
        return $"create_file: created '{path}' ({(content ?? string.Empty).Length} chars).";
    }

    private static string ReplaceFile(string root, string? path, string? content)
    {
        if (Deny("replace_file", root, path) is { } denial)
            return denial;

        RepositoryPath.TryResolveFile(root, path, out var full);
        if (!File.Exists(full))
            return $"replace_file: '{path}' does not exist - use create_file to create it.";

        File.WriteAllText(full!, content ?? string.Empty);
        return $"replace_file: replaced '{path}' ({(content ?? string.Empty).Length} chars).";
    }

    /// <remarks>
    ///     An ambiguous match is refused rather than resolved to the first occurrence. A model that supplied too
    ///     little context is asking to edit a place it has not identified, and picking one for it is how a
    ///     targeted edit silently lands somewhere else.
    /// </remarks>
    private static string EditFile(string root, string? path, string? oldStr, string? newStr)
    {
        if (string.IsNullOrEmpty(oldStr))
            return "edit_file: oldStr must not be empty.";

        if (Deny("edit_file", root, path) is { } denial)
            return denial;

        RepositoryPath.TryResolveFile(root, path, out var full);
        if (!File.Exists(full))
            return $"edit_file: '{path}' does not exist.";

        var content = File.ReadAllText(full!);
        var occurrences = Occurrences(content, oldStr);

        if (occurrences == 0)
            return $"edit_file: oldStr was not found in '{path}'.";

        if (occurrences > 1)
            return $"edit_file: oldStr matches {occurrences} places in '{path}'; it must match exactly once.";

        var index = content.IndexOf(oldStr, StringComparison.Ordinal);
        var replacement = newStr ?? string.Empty;
        File.WriteAllText(
            full!,
            string.Concat(content.AsSpan(0, index), replacement, content.AsSpan(index + oldStr.Length)));

        return $"edit_file: applied to '{path}' ({oldStr.Length} chars replaced with {replacement.Length}).";
    }

    /// <remarks>
    ///     The archive is written before the delete so that a crash or power loss between the two leaves the file
    ///     intact on disk — the worst outcome is a stale archive, not a silent data loss. The patch uses unified
    ///     diff format with a /dev/null target header, matching the shape git produces for a deleted file, so it
    ///     can be inspected and applied with standard patch tooling.
    /// </remarks>
    private static string DeleteFile(string root, string? path)
    {
        if (Deny("delete_file", root, path) is { } denial)
            return denial;

        RepositoryPath.TryResolveFile(root, path, out var full);
        if (!File.Exists(full))
            return $"delete_file: '{path}' does not exist.";

        var content = File.ReadAllText(full!);
        var archivePath = ArchiveDeletedFile(root, path!, content);

        File.Delete(full!);
        return $"delete_file: deleted '{path}' (archived to '{archivePath}').";
    }

    /// <returns>The repository-relative path of the written archive patch.</returns>
    private static string ArchiveDeletedFile(string root, string path, string content)
    {
        var logsDir = Path.Combine(root, ".anneal", "logs");
        Directory.CreateDirectory(logsDir);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var patchFileName = $"deleted-{timestamp}.patch";
        var patchPath = Path.Combine(logsDir, patchFileName);

        // Unified diff format for a deleted file: git uses this shape so the archive
        // is readable by standard patch tooling without any special knowledge of Anneal.
        var forwardSlashPath = path.Replace('\\', '/');
        var lines = content.Split('\n');
        var patchLines = new System.Text.StringBuilder();
        patchLines.AppendLine($"--- a/{forwardSlashPath}");
        patchLines.AppendLine("+++ /dev/null");
        patchLines.AppendLine($"@@ -1,{lines.Length} +0,0 @@");
        foreach (var line in lines)
            patchLines.AppendLine($"-{line}");

        File.WriteAllText(patchPath, patchLines.ToString());

        return $".anneal/logs/{patchFileName}";
    }

    /// <returns>The refusal to return to the model, or null when the write may proceed.</returns>
    private static string? Deny(string tool, string root, string? path)
    {
        if (!RepositoryPath.TryResolveFile(root, path, out var full))
            return ToolReply.OutsideRepository(tool, path);

        // Checked on the resolved path rather than on what the model typed, so "./fix.ps1", "sub/../fix.ps1"
        // and "fix.ps1" are one file rather than three chances to get past the list.
        var relative = RepositoryPath.Relative(root, full);
        return ProtectedPaths.IsProtected(relative) ? ProtectedPaths.Refusal(tool, relative) : null;
    }

    private static int Occurrences(string content, string value)
    {
        var count = 0;
        for (var index = content.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = content.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
