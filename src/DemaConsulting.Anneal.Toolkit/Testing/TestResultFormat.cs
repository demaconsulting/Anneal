namespace DemaConsulting.Anneal.Toolkit.Testing;

/// <summary>
///     The form a test result file takes.
/// </summary>
public enum TestResultFormat
{
    /// <summary>
    ///     Visual Studio test results, or the JUnit XML most other runners emit; the format is detected from
    ///     the file rather than asserted by the caller.
    /// </summary>
    Trx,

    /// <summary>
    ///     One result per line, an outcome token then the test name, as in
    ///     <c>Passed clean repository passes</c>. The form a script-level suite records, which no result
    ///     schema covers.
    /// </summary>
    Text
}
