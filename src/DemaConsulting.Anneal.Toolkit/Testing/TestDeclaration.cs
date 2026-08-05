namespace DemaConsulting.Anneal.Toolkit.Testing;

/// <summary>
///     A test found in the repository's sources, and where it was found.
/// </summary>
/// <remarks>
///     Location is recorded, not just existence, so a check can tell "no such test" apart from "that test
///     exists, but it is an interior test". Those are different failures with different remedies, and
///     collapsing them leaves an author guessing.
///     <para>Thread safety: not safe for concurrent mutation; built by one scan and read afterwards.</para>
/// </remarks>
public sealed class TestDeclaration
{
    private readonly List<string> _expectedFolders = [];
    private readonly List<string> _files = [];

    internal TestDeclaration(string name)
    {
        Name = name;
    }

    /// <summary>
    ///     The declared test name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Whether the test was declared in a contract test location, and so may prove a clause.
    /// </summary>
    public bool IsContractTest { get; private set; }

    /// <summary>
    ///     The file names the test was declared in, in discovery order, so a message can point at them.
    /// </summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>
    ///     The contract test folder names the profiles that found this test expect a boundary test to sit in.
    ///     Carried on the declaration so a repository running several profiles names the right folder when a
    ///     clause points at an interior test. Empty when no profile that found it splits by location.
    /// </summary>
    public IReadOnlyList<string> ExpectedFolders => _expectedFolders;

    internal void Record(string fileName, bool isContractTest, string? expectedFolder)
    {
        IsContractTest |= isContractTest;
        _files.Add(fileName);

        if (expectedFolder is not null && !_expectedFolders.Contains(expectedFolder, StringComparer.Ordinal))
            _expectedFolders.Add(expectedFolder);
    }

    internal void Absorb(TestDeclaration other)
    {
        IsContractTest |= other.IsContractTest;
        _files.AddRange(other._files);

        foreach (var folder in other._expectedFolders)
            if (!_expectedFolders.Contains(folder, StringComparer.Ordinal))
                _expectedFolders.Add(folder);
    }
}
