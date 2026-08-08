using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;

namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>
///     The default way <see cref="ContractChangeWorker" /> and <see cref="StructuralChangeWorker" /> run their
///     strict contract-check step when a caller supplies no <see cref="RunRepositoryScript" /> override.
/// </summary>
/// <remarks>
///     Calls <see cref="CheckContractsOperation" /> in process rather than shelling out to a repository script,
///     because the check it performs is already loaded code in the very process running it: spawning a second
///     <c>dotnet</c> or <c>pwsh</c> process to reach the same answer would only add a process-start cost, a PATH
///     dependency, and a second place the invocation could be spelled wrong. It replaced a default that shelled
///     out to a repository-root <c>check-contracts.ps1</c> file — a script every downstream repository's template
///     carried, but this repository, by longstanding design, does not, which made every self-hosted route run
///     against this repository fail this step immediately on a missing file rather than on the actual state of
///     its contracts.
///     <para>
///         The arguments come from <see cref="ContractCheckConfiguration" />, a repository-owned file, rather
///         than a fixed <c>-Strict</c> baked in here: a repository whose clauses are verified through more than
///         one discovery shape — this repository checks its own C# boundary tests alongside its root-level
///         PowerShell fixture suites — states its own arguments once, in configuration, instead of the Toolkit
///         guessing or every caller re-deriving them.
///     </para>
/// </remarks>
internal static class ContractCheckRunner
{
    /// <summary>
    ///     Runs the repository's strict contract check in process and reports it the same shape a shelled-out
    ///     script would have.
    /// </summary>
    /// <param name="repositoryRoot">The repository to check. Must not be null or blank.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     A <see cref="ScriptRun" /> whose exit code is zero when the check succeeded and non-zero otherwise,
    ///     and whose output is everything <see cref="CheckContractsOperation" /> rendered.
    /// </returns>
    public static async Task<ScriptRun> RunAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var arguments = ContractCheckConfiguration.Load(repositoryRoot).Arguments;

        var writer = new StringWriter();
        var result = await new CheckContractsOperation(repositoryRoot)
            .ExecuteAsync(arguments, writer, cancellationToken)
            .ConfigureAwait(false);

        var exitCode = result.Outcome == OperationOutcome.Succeeded ? 0 : 1;
        return new ScriptRun(exitCode, writer.ToString());
    }
}
