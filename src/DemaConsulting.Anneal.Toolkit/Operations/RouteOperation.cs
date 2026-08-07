using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Routes a real work item to this repository's own compiled worker catalog — Small Fix, Contract Change, or
///     Structural Change — through a real <see cref="Router" />, and runs whichever worker the routing oracle
///     selects.
/// </summary>
/// <remarks>
///     This is the first action that ever constructs a <see cref="Router" /> outside a throwaway test harness.
///     Every worker proven so far had only ever run inside interior tests against a fake endpoint; <c>route</c> is
///     the seam that lets a real caller — a person, a script, or eventually another compiled process — hand this
///     repository a genuine piece of work and have the routing oracle itself decide, against a real model, which
///     compiled path runs it.
///     <para>
///         It declares <see cref="OperationCategory.Authoring" />, the same category <see cref="LintFixOperation" />
///         declares: a selected worker edits the repository, and nothing that edits the repository may also decide
///         whether the build passes. The category is Authoring even on a run that ends up only researching or
///         failing to route, because the operation as a whole is capable of writing, and a caller must not have to
///         read which path a given invocation happened to take before it can know whether a failure gates.
///     </para>
///     <para>
///         <b>Charters are this action's own judgement call, not a rediscovery of one written elsewhere.</b> No
///         prose agent's instructions are lifted verbatim the way <see cref="LintFixOperation" /> lifted
///         <c>lint-fix.agent.md</c>'s guidance, because the Router and its three workers have never had a prose
///         equivalent — <c>dispatch</c> and <c>apply</c> together play a comparable role today, but their
///         instructions are written for a conversational agent reading a whole repository's standards tree, not for
///         a bounded typed question a route oracle answers once. The charters below are authored fresh, naming each
///         catalog worker by the exact key <see cref="Router" /> matches on and stating plainly that naming no
///         route is a correct answer — never a paraphrase of a document this pass could instead have pointed at.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
public sealed class RouteOperation : IOperation
{
    /// <summary>
    ///     The system message the route oracle carries: the worker catalog available, by its exact catalog key,
    ///     and that naming no route is a correct answer.
    /// </summary>
    private const string RouteCharter =
        """
        You are the routing oracle for this repository's own compiled worker catalog. You are handed a work item
        and repository facts gathered deterministically, and you answer one narrow question: which worker should
        run this work, whether a bounded look-around is needed first, or that no route exists.

        The catalog has exactly three workers, named by these exact keys:

        - "small-fix": the cheap path for an interior change bounded to files nobody outside them depends on - no
          contract clause changes, no architecture document changes. It gets one local repair pass against a
          failing build before it gives up.
        - "contract-change": a change that adds, alters, or removes a system contract clause. The worker updates
          the affected contract document(s) first, then implements code and tests against the clauses that just
          changed, verified against both a deterministic build/test check and a strict contract check.
        - "structural-change": a change that moves a system boundary itself - splitting, merging, or creating a
          system, or otherwise reshaping docs/architecture/ beyond one system's own contract. It plans before
          authoring, and its documentation budget is wider than contract-change's.

        Naming no route is a correct answer, not a failure, when the work item names a Migration proposal this
        repository has not yet approved, needs an interactive conversation only a person can hold (for example
        architecture-design work), or is genuinely unclassifiable on the evidence you have. Say so plainly, and
        name the specific next step only a person can take when you know one.

        If you lack the facts to choose honestly, ask for a bounded, narrow look-around before answering - do not
        guess at a classification you cannot support.
        """;

    /// <summary>The system message a bounded research pass carries when the route oracle asks for one.</summary>
    private const string ResearchCharter =
        """
        You are performing a bounded, read-only look-around to answer one narrow question the routing oracle
        needs before it can classify a work item honestly. You have read-only tools; use them on the real
        repository rather than reasoning from memory, and name every file you consult. Report what you found and
        whether it is enough to answer the question. Refusing to conclude you have enough is a correct answer
        when you do not.
        """;

    /// <summary>
    ///     The system message a single-shot <see cref="Planner" /> question carries, used only by the Structural
    ///     Change worker.
    /// </summary>
    private const string PlannerCharter =
        """
        You decide whether the work in front of you needs a written plan before it is authored, given only the
        brief you are handed and no tool access. A plan naming the files this change will need to touch, a
        preference for direct execution because the change is simple enough not to need one, or a reroute to a
        different catalog worker are all correct answers.
        """;

    /// <summary>The system message a <see cref="DocumentAuthor" /> authoring pass carries.</summary>
    private const string DocumentAuthorCharter =
        """
        You are updating documentation against the classification the router already decided - a system contract
        document, its section documents, or both, whichever this change's own scope requires. Prune a section
        document that no longer earns its place rather than leaving it stale.

        You have tools to read the repository and to edit files in it. Use them on the real files rather than
        reasoning from memory: read a file before you edit it.

        Work strictly within the declared scope. If, while authoring, you discover the change actually needs a
        different catalog worker, say so and name the one you believe is right rather than silently widening your
        own scope. Some files are protected and your edit tools will refuse them; a refusal is a real answer, not
        an obstacle to route around.
        """;

    /// <summary>The system message a <see cref="Developer" /> authoring pass carries.</summary>
    private const string DeveloperCharter =
        """
        You are implementing a change against the classification the router already decided. You have tools to
        read the repository and to edit files in it. Use them on the real files rather than reasoning from
        memory: read a file before you edit it, and copy the snippet you replace verbatim from what you read.

        Work strictly within the scope the brief above describes. If, while authoring, you discover the change
        actually needs a different catalog worker - a contract clause to change, a system boundary to move - say
        so and name the worker you believe is right rather than silently widening your own scope.

        Some files are protected and your edit tools will refuse them. A refusal is a real answer: if the correct
        change needs a protected file changed, say so plainly and stop rather than editing around it.
        """;

    /// <summary>The system message a model-backed <see cref="Verifier" /> pass carries.</summary>
    private const string VerifierCharter =
        """
        You judge whether the produced change conforms to what it was supposed to do, reading the staged
        deterministic evidence first. Only reach for your own judgement once every supplied check has passed, and
        only to answer what the deterministic evidence cannot: whether the work is otherwise correct for its
        declared intent. Refusing to judge on insufficient evidence is a correct answer.
        """;

    private readonly string _repositoryRoot;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunRepositoryScript? _buildRunScript;
    private readonly RunRepositoryScript? _contractCheckRunScript;

    /// <summary>
    ///     Creates an operation over the current working directory, running the repository's own scripts through
    ///     the PowerShell host and consulting the configured models.
    /// </summary>
    public RouteOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root and, optionally, substituted providers and
    ///     script runners.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository routed over, outside which every tool call is refused, and whose configuration names the
    ///     models behind the capability roles. Must not be null or blank.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK. Drives
    ///     the route oracle, any research pass, and every model call the selected worker makes: it substitutes the
    ///     provider and never the mapping, which model serves a role staying the repository configuration's
    ///     decision on every path.
    /// </param>
    /// <param name="buildRunScript">
    ///     Runs the repository's <c>build.ps1</c> for a worker's deterministic check, or null to run it through
    ///     the PowerShell host. Injected so the whole run is exercisable without a real build.
    /// </param>
    /// <param name="contractCheckRunScript">
    ///     Runs the repository's strict contract check for a worker's deterministic check, or null to run
    ///     <c>check-contracts.ps1 -Strict</c> through the PowerShell host. Injected so the whole run is exercisable
    ///     without a real script.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public RouteOperation(
        string repositoryRoot,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? buildRunScript = null,
        RunRepositoryScript? contractCheckRunScript = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _endpointFor = endpointFor;
        _buildRunScript = buildRunScript;
        _contractCheckRunScript = contractCheckRunScript;
    }

    /// <inheritdoc />
    public string Name => "route";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Authoring;

    /// <inheritdoc />
    public string Summary => "Route a work item to the compiled worker catalog and run whichever worker is selected";

    /// <inheritdoc />
    /// <remarks>
    ///     The most capable role. A selected worker may write to the working tree through <see cref="Developer" /> or
    ///     <see cref="DocumentAuthor" />, both of which run at <see cref="ModelRole.Heavy" />, so this action names
    ///     the most demanding role any path through it can reach - the same reasoning
    ///     <see cref="LintFixOperation" /> already states for its own declaration.
    /// </remarks>
    public ModelRole? RequiredRole => ModelRole.Heavy;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal route <work item> [<changed-file-hint> ...] - routes <work item> through a real " +
        "Router to the compiled worker catalog (small-fix, contract-change, structural-change) and runs " +
        "whichever worker is selected. <work item> is a single argument describing the task in plain text; any " +
        "further arguments are changed-file hints folded into the routing facts. Succeeds when a worker " +
        "completes the work, escalates when the routing oracle or a worker names a step only you can take, and " +
        "fails when no route exists, a budget is exhausted, or the selected worker could not complete the work.";

    /// <inheritdoc />
    /// <remarks>
    ///     Expects at least one argument: the work item, given positionally and first. Every argument after it is
    ///     a changed-file hint. Reports <see cref="OperationOutcome.UsageError" /> when the work item is missing or
    ///     blank.
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
        IReadOnlyList<string> changedFileHints = [.. arguments.Skip(1)];

        var recordStore = new RecordStore(_repositoryRoot);
        var router = new Router(
            _repositoryRoot,
            RouteCharter,
            ResearchCharter,
            BuildCatalog(recordStore),
            recordStore,
            endpointFor: _endpointFor);

        output.WriteLine($"route: routing \"{workItem}\"...");

        var result = await router.RunAsync(workItem, changedFileHints, cancellationToken).ConfigureAwait(false);

        return result.Finding switch
        {
            RouterOutcome.Completed completed when result.Outcome == OperationOutcome.Succeeded =>
                Completed(output, completed),
            RouterOutcome.Report report => Reported(output, result.Outcome, report),
            // Router.RunAsync's own contract reaches only Succeeded+Completed or Failed/Escalated+Report; this
            // guards against a third RouterOutcome case being added there without this projection following it.
            _ => new OperationResult(OperationOutcome.Failed)
        };
    }

    /// <remarks>
    ///     Assembles the production worker catalog: all three landed workers (<see cref="SmallFixWorker" />,
    ///     <see cref="ContractChangeWorker" />, <see cref="StructuralChangeWorker" />), each registered under the
    ///     exact catalog key its own interior tests and <see cref="RouteCharter" /> already use, replacing the
    ///     single-entry catalogs every prior worker's own tests built in isolation.
    /// </remarks>
    private WorkerCatalogEntry[] BuildCatalog(RecordStore recordStore)
    {
        var smallFix = new SmallFixWorker(
            _repositoryRoot, DeveloperCharter, endpointFor: _endpointFor, runScript: _buildRunScript);

        var contractChange = new ContractChangeWorker(
            _repositoryRoot,
            DocumentAuthorCharter,
            DeveloperCharter,
            VerifierCharter,
            endpointFor: _endpointFor,
            buildRunScript: _buildRunScript,
            contractCheckRunScript: _contractCheckRunScript,
            recordStore: recordStore);

        var structuralChange = new StructuralChangeWorker(
            _repositoryRoot,
            PlannerCharter,
            DocumentAuthorCharter,
            DeveloperCharter,
            VerifierCharter,
            endpointFor: _endpointFor,
            buildRunScript: _buildRunScript,
            contractCheckRunScript: _contractCheckRunScript,
            recordStore: recordStore);

        return
        [
            new WorkerCatalogEntry(
                new WorkerDescriptor("small-fix", "the cheap path for an interior change with no contract or architecture-document change"),
                smallFix.RunAsync),
            new WorkerCatalogEntry(
                new WorkerDescriptor("contract-change", "a change that adds, alters, or removes a system contract clause"),
                contractChange.RunAsync),
            new WorkerCatalogEntry(
                new WorkerDescriptor("structural-change", "a change that moves a system boundary itself, planned before it is authored"),
                structuralChange.RunAsync)
        ];
    }

    private static OperationResult Completed(TextWriter output, RouterOutcome.Completed completed)
    {
        output.WriteLine($"route: completed - {completed.Summary.Summary}");
        foreach (var file in completed.Summary.FilesChanged)
            output.WriteLine($"  {file}");

        return new OperationResult(
            OperationOutcome.Succeeded,
            new RouteReport(completed.Summary.FilesChanged, completed.Summary.Summary, [], string.Empty, [], string.Empty));
    }

    private static OperationResult Reported(TextWriter output, OperationOutcome outcome, RouterOutcome.Report report)
    {
        output.WriteLine(
            outcome == OperationOutcome.Escalated
                ? "route: escalated - this needs a decision only you can make."
                : "route: failed - no worker completed this work.");

        foreach (var tried in report.FailureReport.WhatWasTried)
            output.WriteLine($"  tried: {tried}");

        output.WriteLine($"route: recommended next step - {report.FailureReport.RecommendedNextStep}");

        return new OperationResult(
            outcome,
            new RouteReport(
                [],
                string.Empty,
                report.FailureReport.WhatWasTried,
                report.FailureReport.WhatWasLearned,
                [.. report.FailureReport.RejectedWorkers.Select(rejected => $"{rejected.WorkerKey}: {rejected.Why}")],
                report.FailureReport.RecommendedNextStep));
    }
}
