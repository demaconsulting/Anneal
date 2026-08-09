using System.Text.RegularExpressions;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Verifies a completed change against its declared scope — contract conformance, scope honesty, and
///     architecture tree accuracy — without authoring anything.
/// </summary>
/// <remarks>
///     <c>docs/architecture/toolkit/verify-change.md</c> is the contract this implements. It gives
///     <c>scope-check.agent.md</c>'s standalone review job a compiled equivalent, composed from the same
///     primitives <see cref="Process.ContractChangeWorker" /> and <see cref="Process.StructuralChangeWorker" />
///     already use for their own verification half — <see cref="DiffCheck" />, <see cref="DeterministicCheck" />,
///     and <see cref="Verifier" /> — run here alone, for a change a worker did not itself just make, instead of
///     composed with <see cref="DocumentAuthor" /> and <see cref="Developer" />. <c>scope-check.agent.md</c>
///     itself retires once this action is live-validated.
///     <para>
///         <b>Why this needed one genuinely new primitive.</b> <see cref="Verifier" /> hard-fails on any failing
///         deterministic <see cref="CheckFinding" /> before a model is ever consulted — exactly right for
///         <see cref="Process.ContractChangeWorker" /> and <see cref="Process.StructuralChangeWorker" />, which
///         only ever run their strict contract check against a change they just authored themselves. A standalone
///         review is different: <c>scope-check.agent.md</c> treats an unfulfilled test obligation in a system the
///         change did not touch as a pre-existing, advisory issue rather than a failure of this change. Judging
///         "did this diff touch that system" is itself a deterministic, mechanical fact, so it is resolved here,
///         before any <see cref="CheckFinding" /> is constructed, rather than by teaching <see cref="Verifier" /> a
///         severity concept every other caller would have to reason about and never use.
///         <see cref="Primitives.DiffCheck" /> supplies the fact this filtering needs — and the ground truth for
///         scope honesty itself — since every existing "changed file" signal in this Toolkit
///         (<c>DevelopmentEnvelope.FilesChanged</c>, <see cref="Process.RepositoryFacts.ChangedFileHints" />) is a
///         caller- or model-supplied hint, never one read from the repository.
///     </para>
///     <para>
///         It declares <see cref="OperationCategory.Advisory" />, matching exactly how <c>scope-check.agent.md</c>
///         is used today: it reports back to whichever agent or person invoked it, which decides what to do with
///         the report, rather than gating anything itself. Making this operation <see cref="OperationCategory.Enforcement" />
///         instead would make a model-backed verdict able to fail a build — the first time any operation has
///         done so — which would directly implicate <c>TOOLKIT-I3</c>'s suspension and is a separate, deliberate
///         design decision this operation does not make.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share; a run reads the repository and starts a
///         <c>git</c> process, neither of which this operation itself synchronizes across concurrent callers.
///     </para>
/// </remarks>
public sealed partial class VerifyChangeOperation : IOperation
{
    /// <summary>The system message the model-backed <see cref="Verifier" /> pass carries for a <c>verify-change</c> run.</summary>
    private const string VerifierCharter =
        """
        You are reviewing a completed change against its declared scope: contract conformance, scope honesty, and
        architecture tree accuracy. You are not authoring anything - judge what was done, and report concerns for
        someone else to fix. You have no tools; judge only from the evidence given.
        """;

    /// <summary>
    ///     The narrower question the verifier answers once both deterministic checks have passed, mirroring
    ///     <c>scope-check.agent.md</c>'s own three questions.
    /// </summary>
    private const string VerifierQuestion =
        """
        Judge this change against three questions, from the diff given as evidence:

        1. Does every system whose boundary the diff touches still do what its contract says - no undeclared
           consumer-observable boundary behavior added, no clause narrowed or removed without being declared
           breaking, and contract tests exercising only the public boundary?
        2. Was the declared scope honest - a Small Fix change leaves every pre-existing contract test unmodified,
           a Contract Change or Structural Change updates the contract before the code that fulfills it, and no
           contract change is split across lower-scope commits?
        3. Does the architecture tree still describe reality at the level it claims for every document the diff
           touched - no stale document, no level restated at its parent, no orphaned section document, and every
           navigation link still resolving?

        Report 'RepairRequired' with one concern per fix needed, each owned by Documentation, Code, or Tenet, or
        'Passed' when nothing needs fixing. Advisory findings that do not affect any of the three questions above
        - length observations, drift flags - belong in your advisory notes, never as a concern.
        """;

    /// <remarks>
    ///     Matches the exact message <c>Enforcement.ContractCheck</c> renders for an unfulfilled planned
    ///     obligation, so this run can tell that specific failure kind apart from every other contract-check
    ///     error, which always remains blocking regardless of which system it names.
    /// </remarks>
    [GeneratedRegex(
        @"^\s*error:\s*(?<doc>[^:]+):\s*clause\s+\S+\s+has an unfulfilled test obligation",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex UnfulfilledObligationLine();

    private readonly string _repositoryRoot;
    private readonly DiffCheck _diffCheck;
    private readonly DeterministicCheck _buildCheck;
    private readonly Verifier _verifier;
    private readonly RunRepositoryScript _contractCheckRunScript;

    /// <summary>
    ///     Creates an operation over the current working directory, consulting the configured models.
    /// </summary>
    public VerifyChangeOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root and, optionally, substituted collaborators.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository verified, outside which no check reads or writes. Must not be null or blank.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this operation's whole behavior is exercisable without a network call.
    /// </param>
    /// <param name="runGit">
    ///     Runs one <c>git</c> invocation, or null to run it through the real <c>git</c> executable. Injected so
    ///     the diff read is exercisable without a real repository.
    /// </param>
    /// <param name="buildRunScript">
    ///     Runs the repository's <c>build.ps1</c>, or null to run it through the PowerShell host. Injected so the
    ///     deterministic check is exercisable without a real build.
    /// </param>
    /// <param name="contractCheckRunScript">
    ///     Runs the repository's strict contract check, or null to run it through <see cref="ContractCheckRunner" />.
    ///     Injected so the check is exercisable without a real script.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public VerifyChangeOperation(
        string repositoryRoot,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunGitCommand? runGit = null,
        RunRepositoryScript? buildRunScript = null,
        RunRepositoryScript? contractCheckRunScript = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _diffCheck = new DiffCheck(_repositoryRoot, runGit: runGit);
        _buildCheck = new DeterministicCheck(_repositoryRoot, runScript: buildRunScript);
        _verifier = new Verifier(_repositoryRoot, VerifierCharter, endpointFor: endpointFor);
        _contractCheckRunScript = contractCheckRunScript ??
                                  ((_, ct) => ContractCheckRunner.RunAsync(_repositoryRoot, ct));
    }

    /// <inheritdoc />
    public string Name => "verify-change";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Advisory;

    /// <inheritdoc />
    public string Summary =>
        "Verify a completed change against its declared scope - contract conformance, scope honesty, and " +
        "architecture tree accuracy - without authoring anything";

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="Verifier" /> runs at <see cref="ModelRole.Light" /> by default, and this operation
    ///     substitutes no other role, so <see cref="ModelRole.Light" /> is the most demanding role its one path
    ///     can reach - the same reasoning <see cref="StageContractOperation" /> states for its own declaration.
    /// </remarks>
    public ModelRole? RequiredRole => ModelRole.Light;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal verify-change [<base-ref>] - verifies the current change against its declared " +
        "scope. Diffs uncommitted work against HEAD when <base-ref> is omitted, or {base-ref}...HEAD otherwise. " +
        "Runs build.ps1 and a strict check-contracts pass, setting aside unfulfilled test obligations in systems " +
        "the diff did not touch as pre-existing, then asks a verifier to judge contract conformance, scope " +
        "honesty, and tree accuracy. Succeeds when both checks and the verifier pass; refuses when the diff or " +
        "the verifier's evidence was insufficient; escalates when the verifier finds the classification itself " +
        "needs to change; fails when a check did not pass, the verifier found a concern, or no model could be " +
        "reached. Never edits the repository and never gates a build - it reports, and its caller decides.";

    /// <inheritdoc />
    /// <remarks>
    ///     Expects at most one argument: the base reference. Reports <see cref="OperationOutcome.UsageError" />
    ///     when more than one is given.
    /// </remarks>
    public async Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        // No usage line is written here: the dispatcher renders Usage - the single declared source - on the
        // usage-error path, so what a caller sees after a misuse cannot drift from what help prints.
        if (arguments.Count > 1)
            return new OperationResult(OperationOutcome.UsageError);

        var baseRef = arguments.Count == 1 ? arguments[0] : null;

        output.WriteLine("verify-change: reading the diff...");
        var diff = await _diffCheck.RunAsync(baseRef, cancellationToken).ConfigureAwait(false);
        var diffAvailable = diff.Finding?.Available ?? false;
        var changedFiles = diff.Finding?.ChangedFiles ?? [];
        var patch = diff.Finding?.Patch ?? string.Empty;

        if (!diffAvailable)
            output.WriteLine("verify-change: the diff could not be read; every finding below is judged without it.");

        output.WriteLine("verify-change: running build.ps1...");
        var buildCheck = await _buildCheck
            .RunAsync("build.ps1 check", "build.ps1", null, cancellationToken)
            .ConfigureAwait(false);

        output.WriteLine("verify-change: running check-contracts -Strict...");
        var contractRun = await _contractCheckRunScript("check-contracts -Strict", cancellationToken)
            .ConfigureAwait(false);

        var (contractFinding, advisoryPreexisting) = ClassifyContractCheck(contractRun, changedFiles, diffAvailable);

        List<CheckFinding> evidence = [];
        if (buildCheck.Finding is not null) evidence.Add(buildCheck.Finding);
        evidence.Add(contractFinding);

        var question = ComposeQuestion(changedFiles, patch);

        var verified = await _verifier
            .VerifyAsync(VerificationIntent.ContractConformance, evidence, question, cancellationToken)
            .ConfigureAwait(false);

        var advisoryNotes = new List<string>(advisoryPreexisting);
        if (verified.Finding is not null)
            advisoryNotes.AddRange(verified.Finding.AdvisoryNotes);

        var report = new VerifyChangeReport(
            diffAvailable,
            changedFiles,
            buildCheck.Finding?.Passed ?? false,
            contractFinding.Passed,
            verified.Finding is { Concerns.Count: > 0 }
                ? [.. verified.Finding.Concerns.Select(concern => $"{concern.Owner}: {concern.FixText}")]
                : [],
            advisoryNotes);

        Render(report, output);

        return new OperationResult(verified.Outcome, report);
    }

    /// <remarks>
    ///     Splits every rendered contract-check error into blocking or advisory: an unfulfilled-obligation error
    ///     naming a system whose file is not among <paramref name="changedFiles" /> is set aside as pre-existing,
    ///     per <c>scope-check.agent.md</c>'s own exception; every other error remains blocking. When the diff was
    ///     not available, the exception is never applied - every unfulfilled obligation blocks, the same as an
    ///     unmodified strict check-contracts run would report.
    /// </remarks>
    private static (CheckFinding Finding, IReadOnlyList<string> Advisory) ClassifyContractCheck(
        ScriptRun run, IReadOnlyList<string> changedFiles, bool diffAvailable)
    {
        if (run.ExitCode == 0)
            return (new CheckFinding("check-contracts -Strict", true, 0, Summarize(run.Output), ["check-contracts -Strict"]), []);

        var touchedDocuments = new HashSet<string>(
            changedFiles.Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase);

        var blocking = new List<string>();
        var advisory = new List<string>();

        foreach (var line in run.Output.Split('\n'))
        {
            var match = UnfulfilledObligationLine().Match(line);
            var isPreexisting = diffAvailable && match.Success && !touchedDocuments.Contains(match.Groups["doc"].Value.Trim());

            if (line.Contains("error:", StringComparison.OrdinalIgnoreCase))
                (isPreexisting ? advisory : blocking).Add(line.Trim());
        }

        var passed = blocking.Count == 0;
        var summary = passed
            ? "no blocking contract issues; all failures are pre-existing obligations in untouched systems"
            : string.Join(" | ", blocking);

        return (new CheckFinding(
            "check-contracts -Strict", passed, passed ? 0 : run.ExitCode, Summarize(summary),
            ["check-contracts -Strict"]), advisory);
    }

    private static string ComposeQuestion(IReadOnlyList<string> changedFiles, string patch) =>
        $"""
         {VerifierQuestion}

         <changed-files>
         {(changedFiles.Count == 0 ? "none" : string.Join("\n", changedFiles))}
         </changed-files>

         <diff>
         {(patch.Length == 0 ? "unavailable" : patch)}
         </diff>
         """;

    /// <remarks>Trimmed the same way <see cref="DeterministicCheck" />'s own summary is, for the same reason.</remarks>
    private static string Summarize(string text)
    {
        const int maxLength = 2000;
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }

    private static void Render(VerifyChangeReport report, TextWriter output)
    {
        output.WriteLine($"verify-change: build {(report.BuildPassed ? "PASS" : "FAIL")}");
        output.WriteLine($"verify-change: contract conformance {(report.ContractConformancePassed ? "PASS" : "FAIL")}");

        foreach (var concern in report.Concerns)
            output.WriteLine($"  concern: {concern}");
        foreach (var note in report.AdvisoryNotes)
            output.WriteLine($"  advisory: {note}");

        output.WriteLine(report.Concerns.Count == 0 && report.BuildPassed && report.ContractConformancePassed
            ? "verify-change: completed - no concerns found."
            : "verify-change: completed - see findings above.");
    }
}
