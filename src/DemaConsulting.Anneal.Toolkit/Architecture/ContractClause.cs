namespace DemaConsulting.Anneal.Toolkit.Architecture;

/// <summary>
///     One clause of a system's contract: an identified promise, and the tests the clause names as proving it.
/// </summary>
/// <remarks>
///     A clause is the unit of everything a contract can be asked about — whether it is verified, which tier a
///     change to it implies, which document owns it — so it carries where it was found as well as what it
///     says.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="Id">
///     The clause identifier, as written: <c>{SYSTEM}-nn</c> for a provided behavior or <c>{SYSTEM}-In</c>
///     for an invariant.
/// </param>
/// <param name="Section">
///     The contract subsection the clause was found under — <c>Provides</c> or <c>Invariants</c>.
/// </param>
/// <param name="DocumentName">The file name of the architecture document declaring it.</param>
/// <param name="Verifiers">
///     The tests the clause names, in the order written. Empty when the clause names none, which is a broken
///     promise rather than an absent one.
/// </param>
public sealed record ContractClause(
    string Id,
    string Section,
    string DocumentName,
    IReadOnlyList<ContractVerifier> Verifiers);
