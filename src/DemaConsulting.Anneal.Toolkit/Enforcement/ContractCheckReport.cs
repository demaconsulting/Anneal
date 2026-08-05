namespace DemaConsulting.Anneal.Toolkit.Enforcement;

/// <summary>
///     How far a contract check got.
/// </summary>
public enum ContractCheckStage
{
    /// <summary>
    ///     The discovery profiles could not be understood, so nothing was checked. A partial set would report
    ///     clauses as unverified for a reason that is not theirs.
    /// </summary>
    ProfilesRejected,

    /// <summary>
    ///     The architecture tree declares no clauses, so there was nothing to check. This is a normal state
    ///     for a repository adopting the check before it has a contract.
    /// </summary>
    NothingToCheck,

    /// <summary>
    ///     The clauses were checked against the repository's tests.
    /// </summary>
    Checked
}

/// <summary>
///     What a contract check found.
/// </summary>
/// <remarks>
///     Findings are kept as ordered messages rather than as a structured taxonomy. The messages are the
///     contract this check has with its callers: a person reading a failing build, and a skill document that
///     tells them what each one means. Structuring them would invite them to be reworded.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="Stage">How far the check got.</param>
/// <param name="ArchitectureRoot">The architecture root that was read, for a message naming where nothing was found.</param>
/// <param name="ClauseCount">How many clauses were found.</param>
/// <param name="TestLinkCount">How many clause-to-test links were checked.</param>
/// <param name="Warnings">Findings that do not fail the check, in the order found.</param>
/// <param name="Errors">Findings that fail the check, in the order found.</param>
public sealed record ContractCheckReport(
    ContractCheckStage Stage,
    string ArchitectureRoot,
    int ClauseCount,
    int TestLinkCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    ///     Whether the contract holds. A check that found no errors passes, including one that found nothing
    ///     to check — a repository is not failed for having no contract yet.
    /// </summary>
    public bool Passed => Errors.Count == 0;
}
