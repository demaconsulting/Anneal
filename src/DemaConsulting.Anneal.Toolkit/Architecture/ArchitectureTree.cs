namespace DemaConsulting.Anneal.Toolkit.Architecture;

/// <summary>
///     The architecture tree read as a whole: every document that declares contract clauses, and the clauses
///     they declare.
/// </summary>
/// <remarks>
///     The tree is read recursively. A system's contract may be split across level 3 section documents once
///     the system outgrows one file, and a reader that only looked at the top level would silently stop
///     checking every clause that moved — the failure mode that makes splitting a large document unsafe.
///     <para>
///         Only level 2 documents are expected to declare a contract, so only they are reported when they
///         declare none. The <c>overview.md</c> document is excluded entirely: it introduces the systems and
///         owns no contract of its own.
///     </para>
///     <para>Thread safety: immutable and safe to share once read.</para>
/// </remarks>
public sealed class ArchitectureTree
{
    private ArchitectureTree(IReadOnlyList<ArchitectureDocument> documents)
    {
        Documents = documents;
    }

    /// <summary>
    ///     The documents read, level 2 documents first and in name order within each level.
    /// </summary>
    public IReadOnlyList<ArchitectureDocument> Documents { get; }

    /// <summary>
    ///     Every clause declared anywhere in the tree, in document order.
    /// </summary>
    public IEnumerable<ContractClause> Clauses => Documents.SelectMany(document => document.Clauses);

    /// <summary>
    ///     Reads every architecture document under a root.
    /// </summary>
    /// <param name="architectureRoot">
    ///     The architecture tree's root directory. Must not be null or blank. A root that does not exist
    ///     reads as an empty tree, which the caller reports as nothing to check rather than as a failure —
    ///     a repository may adopt the check before it has a tree.
    /// </param>
    /// <returns>The tree as read.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="architectureRoot" /> is null or blank.</exception>
    public static ArchitectureTree Read(string architectureRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architectureRoot);

        var root = new DirectoryInfo(architectureRoot);
        if (!root.Exists) return new ArchitectureTree([]);

        var documents = new List<ArchitectureDocument>();

        foreach (var file in root.EnumerateFiles("*.md").OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            if (string.Equals(file.Name, "overview.md", StringComparison.OrdinalIgnoreCase)) continue;
            documents.Add(ArchitectureDocument.Read(file.Name, File.ReadAllText(file.FullName), true));
        }

        foreach (var file in root
                     .EnumerateFiles("*.md", SearchOption.AllDirectories)
                     .Where(file => !string.Equals(file.DirectoryName, root.FullName, StringComparison.Ordinal))
                     .OrderBy(file => file.FullName, StringComparer.Ordinal))
            documents.Add(ArchitectureDocument.Read(file.Name, File.ReadAllText(file.FullName), false));

        return new ArchitectureTree(documents);
    }
}
