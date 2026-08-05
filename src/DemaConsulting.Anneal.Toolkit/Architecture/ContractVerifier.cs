using System.Text.RegularExpressions;

namespace DemaConsulting.Anneal.Toolkit.Architecture;

/// <summary>
///     One test a contract clause names as proving it — the string written between backticks after
///     <c>*Verified by:*</c>.
/// </summary>
/// <remarks>
///     A verifier is kept as the author wrote it rather than reduced to a test name on the way in, because
///     every message about a broken promise has to quote the clause back to its author in the form the clause
///     uses. The two readings a check needs are derived from it instead.
///     <para>Thread safety: immutable and safe to share.</para>
/// </remarks>
/// <param name="Text">
///     The verifier exactly as the clause writes it: a code identifier, possibly namespace-qualified, or a
///     named case quoted inside a file reference such as <c>suite.ps1: "clean repository passes"</c>.
/// </param>
public sealed partial record ContractVerifier(string Text)
{
    /// <summary>
    ///     The name a test declaration must carry to satisfy this verifier.
    /// </summary>
    /// <remarks>
    ///     A quoted case name is taken whole, including its spaces and punctuation. Splitting it on the colon
    ///     that separates it from its file would leave a fragment no declaration can match, which reads as a
    ///     missing test rather than as the misreading it is.
    /// </remarks>
    public string TestName =>
        QuotedCase().Match(Text) is { Success: true } quoted
            ? quoted.Groups[1].Value
            : Text.Split('.', ':')[^1];

    /// <summary>
    ///     Whether this verifier is a planned obligation rather than a test that is meant to exist yet.
    /// </summary>
    /// <remarks>
    ///     The obligation is the placeholder form — <c>TODO.</c> or <c>TODO_</c> at the start — and not the
    ///     word. Case-sensitive and anchored deliberately: a real test named <c>TodoItemsAreReturned</c> is
    ///     not an obligation, nor is a genuine case named <c>"TODO obligation is an error"</c>, nor is a
    ///     clause verified by a suite file called <c>TODO-suite.ps1</c>. Exempting any of those would let a
    ///     clause pass on a promise nobody checked.
    /// </remarks>
    public bool IsPlannedObligation => PlannedObligation().IsMatch(Text);

    /// <summary>
    ///     Renders the verifier as the clause wrote it, so a message quotes the author back to themselves.
    /// </summary>
    public override string ToString() => Text;

    [GeneratedRegex("\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedCase();

    [GeneratedRegex(@"^\s*TODO[._]", RegexOptions.CultureInvariant)]
    private static partial Regex PlannedObligation();
}
