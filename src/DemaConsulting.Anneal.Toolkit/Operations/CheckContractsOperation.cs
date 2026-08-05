using DemaConsulting.Anneal.Toolkit.Enforcement;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Testing;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Checks that every clause of the repository's contract names a boundary test, that the test exists, and
///     that it passed.
/// </summary>
/// <remarks>
///     A thin composition over <see cref="ContractCheck" />: it reads the invocation, runs the check, and
///     renders what was found. Nothing about contracts, tests or results is decided here, so a second caller
///     wanting the same facts — a verdict auditor, a coverage report — reaches for the check rather than for
///     this operation.
///     <para>
///         It declares <see cref="OperationCategory.Enforcement" /> because its answer is decidable from the
///         repository alone and cannot change on unchanged input, which is what makes it safe to gate on.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, though a single instance reads the file
///         system and therefore sees whatever is on disk at the moment it runs.
///     </para>
/// </remarks>
public sealed class CheckContractsOperation : IOperation
{
    private readonly string _repositoryRoot;

    /// <summary>
    ///     Creates an operation that checks the current working directory, which is the repository root when
    ///     the tool is invoked as a repository's own tool.
    /// </summary>
    public CheckContractsOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation that checks an explicit repository root.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository to check. Must not be null or blank; it need not hold an architecture tree, in which
    ///     case the check reports that there is nothing to check.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public CheckContractsOperation(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    /// <inheritdoc />
    public string Name => "check-contracts";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Enforcement;

    /// <inheritdoc />
    public string Summary => "Check that every contract clause names a boundary test that exists and passed";

    /// <inheritdoc />
    /// <remarks>
    ///     None. This check reads the repository and compares names, so no capability tier could change what
    ///     it finds — which is also what makes it safe to gate on.
    /// </remarks>
    public ModelRole? RequiredRole => null;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal check-contracts [-ArchitectureRoot <dir>] [-TestRoots <list>] " +
        "[-TestFilePatterns <list>] [-ContractTestFolder <name>] [-TestAttributes <list>] " +
        "[-TestDeclarationPattern <regex>] [-TestResults <glob>] [-TestResultFormat trx|text] " +
        "[-TestProfiles <record>]... [-Strict] - checks every contract clause in the architecture tree " +
        "against the tests that prove it.";

    /// <inheritdoc />
    /// <remarks>
    ///     Reports <see cref="OperationOutcome.UsageError" /> when the invocation cannot be read at all, and
    ///     <see cref="OperationOutcome.Failed" /> when the contract does not hold. A discovery profile that
    ///     does not parse is reported as a check failure rather than a usage error: it is a statement about
    ///     the repository's layout, made in a file the repository owns, and demoting it to a misuse of the
    ///     command would put it in a different place from every other finding a reader is looking for.
    ///     <para>
    ///         It carries no finding. The verdict is the outcome and the lines it renders; a structured
    ///         payload would promise a caller a taxonomy that the messages, which the skill documentation
    ///         quotes verbatim, deliberately do not have. The work is synchronous — it reads files and
    ///         compares names — so the task it returns is already complete.
    ///     </para>
    /// </remarks>
    public Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        // Withdrawn before it began is still withdrawn: nothing is read and no outcome is invented.
        cancellationToken.ThrowIfCancellationRequested();

        // No usage line is written here: the dispatcher renders Usage - the single declared source - on the
        // usage-error path, so the text a caller sees after a misuse cannot drift from what help prints.
        var options = Parse(arguments);
        if (options is null)
            return Task.FromResult(new OperationResult(OperationOutcome.UsageError));

        var report = ContractCheck.Run(options);
        Render(report, output);

        return Task.FromResult(
            new OperationResult(report.Passed ? OperationOutcome.Succeeded : OperationOutcome.Failed));
    }

    /// <remarks>
    ///     The option names are the discovery field names, so one vocabulary describes a repository's test
    ///     layout whether it is written on a command line or inside a profile record. Only fields the caller
    ///     actually names are recorded, because a field's presence — not just its value — decides whether
    ///     combining it with a profile record is a contradiction.
    /// </remarks>
    /// <returns>Null when the invocation cannot be read.</returns>
    private ContractCheckOptions? Parse(IReadOnlyList<string> arguments)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var profiles = new List<string>();
        var architectureRoot = "docs/architecture";
        var strict = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            if (string.Equals(argument, "-Strict", StringComparison.OrdinalIgnoreCase))
            {
                strict = true;
                continue;
            }

            if (!argument.StartsWith('-') || index + 1 >= arguments.Count) return null;

            var name = argument[1..];
            var value = arguments[++index];

            if (string.Equals(name, "ArchitectureRoot", StringComparison.OrdinalIgnoreCase))
            {
                architectureRoot = value;
                continue;
            }

            if (string.Equals(name, "TestProfiles", StringComparison.OrdinalIgnoreCase))
            {
                profiles.Add(value);
                continue;
            }

            var field = TestDiscoveryProfile.FieldNames.FirstOrDefault(
                known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

            // A repeated field is refused rather than resolved. Which one won would be invisible in the
            // invocation that is meant to document the repository's layout.
            if (field is null || !fields.TryAdd(field, value)) return null;
        }

        return new ContractCheckOptions
        {
            RepositoryRoot = _repositoryRoot,
            ArchitectureRoot = architectureRoot,
            SuppliedFields = fields,
            ProfileRecords = profiles,
            Strict = strict
        };
    }

    /// <remarks>
    ///     Warnings precede errors so that the findings which fail the build are the last thing a reader
    ///     sees, next to the exit status they explain.
    /// </remarks>
    private static void Render(ContractCheckReport report, TextWriter output)
    {
        output.WriteLine("Checking: system contracts...");

        switch (report.Stage)
        {
            case ContractCheckStage.NothingToCheck:
                output.WriteLine(
                    $"  No contract clauses found under {report.ArchitectureRoot} - nothing to check.");
                return;

            case ContractCheckStage.Checked:
                output.WriteLine($"  {report.ClauseCount} clauses, {report.TestLinkCount} test links checked.");
                break;

            case ContractCheckStage.ProfilesRejected:
            default:
                break;
        }

        foreach (var warning in report.Warnings) output.WriteLine($"  warning: {warning}");
        foreach (var error in report.Errors) output.WriteLine($"  error: {error}");
    }
}
