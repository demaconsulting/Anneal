using System.Text.RegularExpressions;
using DemaConsulting.Anneal.Toolkit.Files;

namespace DemaConsulting.Anneal.Toolkit.Testing;

/// <summary>
///     The tests a repository declares, looked up by name.
/// </summary>
/// <remarks>
///     Lookup is case-insensitive. A clause naming <c>cleanRepositoryPasses</c> where the test is
///     <c>CleanRepositoryPasses</c> has named the right test in the wrong case, and reporting it as missing
///     sends its author looking for a test that is sitting in front of them.
///     <para>Thread safety: not safe for concurrent mutation; built by scanning and read afterwards.</para>
/// </remarks>
public sealed class TestDeclarationIndex
{
    private readonly Dictionary<string, TestDeclaration> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     How many distinct tests were found.
    /// </summary>
    public int Count => _byName.Count;

    /// <summary>
    ///     The tests found, in no particular order.
    /// </summary>
    public IEnumerable<TestDeclaration> Declarations => _byName.Values;

    /// <summary>
    ///     Scans one profile's test sources for the tests they declare.
    /// </summary>
    /// <param name="repositoryRoot">The repository root the profile's roots are relative to. Must not be null or blank.</param>
    /// <param name="profile">The profile describing where to look and what a declaration looks like. Must not be null.</param>
    /// <returns>The tests that profile declares.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile" /> is null.</exception>
    /// <exception cref="RegexParseException">
    ///     Thrown when the profile's declaration pattern is not a valid expression.
    /// </exception>
    public static TestDeclarationIndex Scan(string repositoryRoot, TestDiscoveryProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(profile);

        var index = new TestDeclarationIndex();

        // An empty contract folder means the repository's layout has no interior and boundary split, so
        // location cannot disqualify a declaration.
        var splitsByLocation = !string.IsNullOrWhiteSpace(profile.ContractFolder);
        var contractLocation = splitsByLocation
            ? new Regex(
                $"[/\\\\]{Regex.Escape(profile.ContractFolder)}[/\\\\]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            : null;

        foreach (var file in RepositoryFiles.UnderRoots(repositoryRoot, profile.Roots, profile.FilePatterns))
        {
            var isContractTest = contractLocation is null || contractLocation.IsMatch(file.FullName);
            var text = File.ReadAllText(file.FullName);

            var names = profile.DeclarationPattern.Length > 0
                ? TestDeclarations.FromPattern(text, profile.DeclarationPattern)
                : TestDeclarations.FromAttributes(text, profile.Attributes);

            foreach (var name in names)
                index.For(name).Record(file.Name, isContractTest, splitsByLocation ? profile.ContractFolder : null);
        }

        return index;
    }

    /// <summary>
    ///     Pools several profiles' declarations into one index.
    /// </summary>
    /// <remarks>
    ///     Pooled because a clause is satisfied by whichever framework declares its test; a repository whose
    ///     boundary tests are split across two frameworks is not thereby less verified.
    /// </remarks>
    /// <param name="indexes">The per-profile indexes. Must not be null.</param>
    /// <returns>One index holding every declaration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="indexes" /> is null.</exception>
    public static TestDeclarationIndex Pool(IEnumerable<TestDeclarationIndex> indexes)
    {
        ArgumentNullException.ThrowIfNull(indexes);

        var pooled = new TestDeclarationIndex();

        foreach (var index in indexes)
            foreach (var declaration in index._byName.Values)
                pooled.For(declaration.Name).Absorb(declaration);

        return pooled;
    }

    /// <summary>
    ///     Finds a declared test by name.
    /// </summary>
    /// <param name="name">The test name to look for. Must not be null.</param>
    /// <returns>The declaration, or null when no test of that name was found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name" /> is null.</exception>
    public TestDeclaration? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.GetValueOrDefault(name);
    }

    private TestDeclaration For(string name)
    {
        if (_byName.TryGetValue(name, out var existing)) return existing;

        var created = new TestDeclaration(name);
        _byName[name] = created;
        return created;
    }
}
