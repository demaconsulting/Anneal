using DemaConsulting.Anneal.Toolkit.Files;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Runs a declared Maintenance work item directly against <see cref="SmallFixWorker" />, within a declared
///     file-scope bound this operation mechanically enforces after the worker runs.
/// </summary>
/// <remarks>
///     <c>docs/architecture/toolkit/maintain.md</c> is the contract this implements. Maintenance is Small Fix by
///     definition — <c>change-classification.md</c> says so in the same sentence that defines the mode — so a
///     caller invoking <c>maintain</c> has already fixed the work's Scope before this action is ever reached. This
///     operation therefore constructs no <see cref="Router" /> and asks no routing oracle to reclassify Effort or
///     select a worker; it runs the declared work directly against <see cref="SmallFixWorker" />, the same
///     "one oracle pass, not two" reasoning <c>route.md</c> § Decisions already applies to Effort, extended here to
///     the coarser question of which worker runs at all.
///     <para>
///         <b>What this operation adds beyond composing an existing worker.</b> <c>change-classification.md</c>
///         requires Maintenance to be "bounded before it starts" and to "never edit the architecture tree,
///         <c>CONSTRAINTS.md</c>, or <c>BACKLOG.md</c>". Both rules are enforced here as mechanical, post-run checks
///         against what <see cref="SmallFixWorker" /> actually changed — never against a model's own self-report,
///         and never skipped because the worker itself reported success. The tripwire
///         (<see cref="ProtectedPathTripwire" />) and the containment check both always run, and either can force
///         an escalation independently of the other or of what the worker itself concluded.
///     </para>
///     <para>
///         It declares <see cref="OperationCategory.Authoring" />, the same category <see cref="RouteOperation" />
///         declares, for the same reason: <see cref="SmallFixWorker" /> edits the repository through
///         <see cref="Developer" />, and nothing that edits the repository may also decide whether a build passes.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
public sealed class MaintainOperation : IOperation
{
    /// <summary>The system message <see cref="Developer" />'s authoring pass carries for a Maintenance run.</summary>
    private const string DeveloperCharter =
        """
        You are implementing a Maintenance work item: improving what is already there without changing what it
        promises. You have tools to read the repository and to edit files in it. Use them on the real files
        rather than reasoning from memory: before concluding a named path does not exist, read it directly
        with your read-file tool or list its containing directory - never conclude a file is missing from a
        text search alone, since your text-search tool searches file contents, not file names, and can
        report no match for a file that is right there to be read.

        Maintenance may never edit the architecture tree (docs/architecture/), CONSTRAINTS.md, or BACKLOG.md.
        Discovering an architectural problem while you work is a finding to report, never a license to act on
        it - if the correct fix would need one of those files changed, say so and name a better owner rather
        than editing it. Some files are protected and your edit tools will refuse them; a refusal is a real
        answer, not an obstacle to route around.

        If, while working, you discover this item actually needs a contract change or a system-boundary move,
        say so and name the worker you believe is right rather than silently widening your own scope.
        """;

    private readonly string _repositoryRoot;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunRepositoryScript? _buildRunScript;

    /// <summary>
    ///     Creates an operation over the current working directory, running the repository's own <c>build.ps1</c>
    ///     through the PowerShell host and consulting the configured models.
    /// </summary>
    public MaintainOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root and, optionally, substituted providers and a
    ///     script runner.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository run over, outside which every tool call is refused, and whose configuration names the
    ///     models behind the capability roles. Must not be null or blank.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this operation's whole behavior is exercisable without a network call.
    /// </param>
    /// <param name="buildRunScript">
    ///     Runs the repository's <c>build.ps1</c> for the worker's deterministic check, or null to run it through
    ///     the PowerShell host. Injected so the whole run is exercisable without a real build.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public MaintainOperation(
        string repositoryRoot,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? buildRunScript = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _endpointFor = endpointFor;
        _buildRunScript = buildRunScript;
    }

    /// <inheritdoc />
    public string Name => "maintain";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Authoring;

    /// <inheritdoc />
    public string Summary =>
        "Run a declared Maintenance work item directly against SmallFixWorker, within a declared file-scope bound";

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="SmallFixWorker" /> writes to the working tree through <see cref="Developer" />, which runs at
    ///     <see cref="ModelRole.Heavy" />, so this action names the most demanding role its one path can reach - the
    ///     same reasoning <see cref="RouteOperation" /> already states for its own declaration.
    /// </remarks>
    public ModelRole? RequiredRole => ModelRole.Heavy;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal maintain <work item> <file-scope-hint> [<file-scope-hint> ...] - runs <work item> " +
        "directly against SmallFixWorker, asking no routing oracle, since Maintenance mode already fixes Scope " +
        "to Small Fix before this action is invoked. At least one <file-scope-hint> is required: it declares the " +
        "bound this run's actual changes are mechanically checked against afterward, and naming none is a usage " +
        "error since unbounded Maintenance work has no bound to declare. Succeeds when the worker completes the " +
        "work within the declared bound; escalates when the worker names a reroute, a protected-path write is " +
        "refused, the actual changes exceed the declared bound, or the actual changes touch a protected path " +
        "(the architecture tree, CONSTRAINTS.md, or BACKLOG.md); fails when the worker's repair budget is " +
        "exhausted or no model could be reached.";

    /// <inheritdoc />
    /// <remarks>
    ///     Expects at least two arguments: the work item, given positionally and first, and one or more
    ///     file-scope-hint entries after it declaring the Maintenance bound. Reports
    ///     <see cref="OperationOutcome.UsageError" /> when the work item is missing or blank, or when no
    ///     file-scope-hint survives blank-filtering.
    /// </remarks>
    public async Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        // No usage line is written here: the dispatcher renders Usage - the single declared source - on the
        // usage-error path, so what a caller sees after a misuse cannot drift from what help prints.
        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
            return new OperationResult(OperationOutcome.UsageError);

        var workItem = arguments[0];
        IReadOnlyList<string> declaredBound =
            [.. arguments.Skip(1).Where(argument => !string.IsNullOrWhiteSpace(argument))];

        // TOOLKIT-29: naming no file-scope entries is a usage error - unbounded Maintenance work has no bound to
        // declare, and change-classification.md requires the bound to exist before the work starts.
        if (declaredBound.Count == 0)
            return new OperationResult(OperationOutcome.UsageError);

        var worker = new SmallFixWorker(
            _repositoryRoot, DeveloperCharter, endpointFor: _endpointFor, runScript: _buildRunScript);

        var brief = new WorkerBrief(
            Guid.NewGuid().ToString(),
            workItem,
            "Maintenance: Scope already fixed to Small Fix by change-classification.md before this run started.",
            [],
            [],
            $"this Maintenance work is bounded to: {string.Join(", ", declaredBound)}. Read each named path " +
            "directly rather than assuming it is missing.",
            []);

        output.WriteLine(
            $"maintain: running \"{workItem}\" within declared bound {string.Join("; ", declaredBound)}...");

        var result = await worker.RunAsync(brief, cancellationToken).ConfigureAwait(false);

        var actualChanged = ActualChangedFiles(result);
        var actualSummary = ActualSummary(result);

        // TOOLKIT-31: the tripwire runs against the worker's actual changed-file list - never only the declared
        // bound - and forces escalation unconditionally, regardless of what the containment check below
        // concludes for the same run, and regardless of what the worker itself reported.
        var trippedPath = ProtectedPathTripwire.FindTrippedPath(actualChanged);
        if (trippedPath is not null)
        {
            output.WriteLine(
                $"maintain: escalated - the actual changes touched the protected path '{trippedPath}'; a person " +
                "must review this run.");
            return new OperationResult(
                OperationOutcome.Escalated,
                new MaintainReport(actualChanged, actualSummary, declaredBound, null, trippedPath, null, null));
        }

        // TOOLKIT-30: every actual changed file must be contained by the declared bound - the same mechanical
        // strict-subset-style containment check Router.RunAsync already runs for TOOLKIT-26, applied here to the
        // worker's real output instead of a phase's declared intent.
        var outOfBoundsFile = actualChanged.FirstOrDefault(file => !IsContainedByBound(file, declaredBound));
        if (outOfBoundsFile is not null)
        {
            output.WriteLine(
                $"maintain: escalated - '{outOfBoundsFile}' falls outside the declared bound " +
                $"({string.Join("; ", declaredBound)}); a person must review this run.");
            return new OperationResult(
                OperationOutcome.Escalated,
                new MaintainReport(actualChanged, actualSummary, declaredBound, outOfBoundsFile, null, null, null));
        }

        // TOOLKIT-29: both mechanical checks cleared. A worker-named reroute still forces escalation here -
        // SmallFixWorker itself reports OperationOutcome.Succeeded for a Reroute finding, per its own
        // "successfully answering its own question" convention, but maintain has no Router to hand a reroute
        // onward to, so it escalates directly instead of reporting an unqualified success.
        if (result.Finding is WorkerRunResult.Reroute reroute)
        {
            output.WriteLine($"maintain: escalated - the worker named a better owner: {reroute.Why}");
            return new OperationResult(
                OperationOutcome.Escalated,
                new MaintainReport(
                    actualChanged, actualSummary, declaredBound, null, null, reroute.Why, reroute.SuggestedWorker));
        }

        if (result.Finding is WorkerRunResult.Completed completed)
        {
            foreach (var file in completed.Summary.FilesChanged)
                output.WriteLine($"  {file}");
            output.WriteLine($"maintain: completed - {completed.Summary.Summary}");
            return new OperationResult(
                OperationOutcome.Succeeded,
                new MaintainReport(
                    actualChanged, completed.Summary.Summary, declaredBound, null, null, null, null));
        }

        // Neither Completed nor Reroute: the worker's own outcome (Escalated or Failed) passes through
        // unchanged, since both mechanical checks already cleared.
        output.WriteLine(
            result.Outcome == OperationOutcome.Escalated
                ? "maintain: escalated - this needs a decision only you can make."
                : "maintain: failed - the worker did not complete this work.");

        return new OperationResult(
            result.Outcome,
            new MaintainReport(actualChanged, actualSummary, declaredBound, null, null, null, null));
    }

    private static IReadOnlyList<string> ActualChangedFiles(WorkerExecutionResult result) =>
        result.Finding switch
        {
            WorkerRunResult.Completed completed => completed.Summary.FilesChanged,
            _ => result.Interrupted?.FilesChanged ?? []
        };

    private static string ActualSummary(WorkerExecutionResult result) =>
        result.Finding switch
        {
            WorkerRunResult.Completed completed => completed.Summary.Summary,
            _ => result.Interrupted?.Summary ?? string.Empty
        };

    /// <remarks>
    ///     A changed file is contained by the declared bound when it matches one of its entries verbatim, or when
    ///     one of its entries is a glob naming it - the same <see cref="GlobPattern" /> a caller already writes a
    ///     bound entry in, matched against a real path rather than another declared-pattern string, since here one
    ///     side of the check is the worker's real output rather than a second declared scope.
    /// </remarks>
    private static bool IsContainedByBound(string file, IReadOnlyList<string> declaredBound) =>
        declaredBound.Any(entry =>
            string.Equals(entry, file, StringComparison.OrdinalIgnoreCase) || GlobPattern.Parse(entry).Matches(file));
}
