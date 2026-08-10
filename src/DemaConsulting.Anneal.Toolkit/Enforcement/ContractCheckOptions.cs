namespace DemaConsulting.Anneal.Toolkit.Enforcement;

/// <summary>
///     What a contract check was asked to check.
/// </summary>
/// <remarks>
///     The discovery fields are carried as the names a caller writes rather than as typed properties, because
///     the same seven things can be said once for a single-framework repository or several times through
///     profile records, and stating them twice here would be two places for the defaults to drift apart.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
public sealed record ContractCheckOptions
{
    /// <summary>
    ///     The repository to check. Every relative path is resolved against it.
    /// </summary>
    public required string RepositoryRoot { get; init; }

    /// <summary>
    ///     Root of the architecture tree holding the system documents, relative to the repository root.
    /// </summary>
    public string ArchitectureRoot { get; init; } = ".anneal/architecture";

    /// <summary>
    ///     Discovery fields the caller supplied individually, keyed by the field names
    ///     <see cref="Testing.TestDiscoveryProfile.FieldNames" /> lists. Omitted fields take their defaults.
    /// </summary>
    public IReadOnlyDictionary<string, string> SuppliedFields { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Discovery profile records, for a repository whose tests are not all of one kind. Supplying these
    ///     together with any individual field is an error rather than a merge.
    /// </summary>
    public IReadOnlyList<string> ProfileRecords { get; init; } = [];

    /// <summary>
    ///     Whether unfulfilled obligations and absent test results are errors rather than warnings. A tree
    ///     being bootstrapped needs them to be warnings; a tree claiming to be finished does not.
    /// </summary>
    public bool Strict { get; init; }
}
