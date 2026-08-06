using Microsoft.Extensions.AI;

namespace DemaConsulting.Anneal.Toolkit.Model.Tools;

/// <summary>
///     The named grouping of the tools a model may be granted, and the selection that scopes an operation to
///     only the groups it was granted.
/// </summary>
/// <remarks>
///     Scoping is by <em>selection</em>, never by a runtime gate. An operation names the groups it was granted,
///     <see cref="SelectTools" /> returns those groups' functions, and a tool from a group that was not
///     granted is simply
///     absent from the set handed to the model — so there is nothing to talk past, argue with, or forget to
///     check. A gate can be reasoned around by a sufficiently determined prompt; an absent tool cannot be called.
///     <para>
///         The groups partition by risk. <see cref="Read" /> cannot change the repository;
///         <see cref="Edit" /> can. There is deliberately no shell group and none is planned for this stage: the
///         processes that need to run <c>fix.ps1</c> and <c>lint.ps1</c> run them as their own control flow, and
///         a worker that can run commands can do anything and then report plausibly that it did not.
///     </para>
///     <para>
///         Which groups an operation receives is the operation's declaration, not this type's. This type defines
///         the groups and the selection; it takes no view on who deserves what.
///     </para>
///     <para>
///         Thread safety: the tool sets are built once at construction and never mutated, so selection is safe
///         to call concurrently. The tools themselves touch the filesystem and carry their own caveats.
///     </para>
/// </remarks>
public sealed class ToolGroups
{
    /// <summary>The read group: the read-only repository surface — read a file, list a directory, search.</summary>
    public const string Read = "read";

    /// <summary>The edit group: the privileged surface that writes files.</summary>
    public const string Edit = "edit";

    private readonly Dictionary<string, IReadOnlyList<AITool>> _groups;

    /// <summary>
    ///     Builds the tool groups bound to one repository root.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The root every tool resolves its paths against and outside which every request is refused. Must not
    ///     be null or blank; it need not exist, in which case a read reports nothing found.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public ToolGroups(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        _groups = new Dictionary<string, IReadOnlyList<AITool>>(StringComparer.OrdinalIgnoreCase)
        {
            [Read] = RepositoryReadTools.CreateAll(RepositoryRoot),
            [Edit] = RepositoryEditTools.CreateAll(RepositoryRoot)
        };
    }

    /// <summary>
    ///     Every defined group name, in the order selection returns their tools.
    /// </summary>
    public static IReadOnlyList<string> GroupNames { get; } = [Read, Edit];

    /// <summary>
    ///     The repository these groups are bound to.
    /// </summary>
    public string RepositoryRoot { get; }

    /// <summary>
    ///     The tool names a group holds, so a caller can state the surface a grant opens without building it.
    /// </summary>
    /// <param name="group">The group name, matched case-insensitively.</param>
    /// <returns>The tool names in that group, or an empty list when no such group exists.</returns>
    public static IReadOnlyList<string> NamesIn(string? group) =>
        string.Equals(group, Read, StringComparison.OrdinalIgnoreCase) ? RepositoryReadTools.Names :
        string.Equals(group, Edit, StringComparison.OrdinalIgnoreCase) ? RepositoryEditTools.Names :
        [];

    /// <summary>
    ///     Selects the tools of the granted groups, in a deterministic order.
    /// </summary>
    /// <remarks>
    ///     An unknown group name is ignored rather than rejected, because the set handed to the model is what
    ///     matters and a name matching no group grants nothing. The order follows <see cref="GroupNames" /> so a
    ///     turn's tool set does not depend on the order a caller happened to list its grants in.
    /// </remarks>
    /// <param name="grantedGroups">The group names the operation was granted. Must not be null; may be empty.</param>
    /// <returns>The granted tools. Empty when nothing was granted, which is a grant of nothing rather than an absent grant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="grantedGroups" /> is null.</exception>
    public IReadOnlyList<AITool> SelectTools(IEnumerable<string> grantedGroups)
    {
        ArgumentNullException.ThrowIfNull(grantedGroups);

        var granted = new HashSet<string>(grantedGroups, StringComparer.OrdinalIgnoreCase);
        return
        [
            .. GroupNames
                .Where(group => granted.Contains(group))
                .SelectMany(group => _groups[group])
        ];
    }

    /// <summary>
    ///     The names of the tools reachable under a set of grants.
    /// </summary>
    /// <param name="grantedGroups">The group names the operation was granted. Must not be null.</param>
    /// <returns>The tool names in scope, compared case-insensitively.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="grantedGroups" /> is null.</exception>
    public IReadOnlySet<string> ToolNamesInScope(IEnumerable<string> grantedGroups) =>
        SelectTools(grantedGroups)
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
