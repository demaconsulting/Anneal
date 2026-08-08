using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>
///     What a routing run concluded: the work was completed by a selected worker, or the run reports why it could
///     not route or complete the work.
/// </summary>
/// <remarks>
///     Mirrors the same two-shape split every primitive-level union in this pass draws — a success carries a
///     different payload than a failure — but here the two payload types genuinely differ (a change set versus a
///     failure report), so <see cref="Completed" /> and <see cref="Report" /> are kept as separate cases rather
///     than one record with two optional fields.
/// </remarks>
internal abstract record RouterOutcome
{
    private RouterOutcome()
    {
    }

    /// <summary>A selected worker completed the work.</summary>
    /// <param name="Summary">What was changed.</param>
    internal sealed record Completed(ChangeSetSummary Summary) : RouterOutcome;

    /// <summary>The run could not route or complete the work; see the failure report for why.</summary>
    /// <param name="FailureReport">What was tried, what was learned, and a recommended next step.</param>
    internal sealed record Report(RouteFailureReport FailureReport) : RouterOutcome;
}

/// <summary>
///     Routes a work item to a bounded worker catalog by asking a narrow typed question of a route oracle,
///     spending two independent budgets — research iterations and worker reroutes — rather than one shared one.
/// </summary>
/// <remarks>
///     This is the compiled Router <c>docs/architecture/process.md</c> § Composition and § Decisions describe: one
///     narrow typed question per pass (select a worker, ask for bounded research, or report no route), never a
///     universal plan-build-review loop. The two budgets are independent because a research pass (the router
///     lacked facts) and a reroute (a worker learned mid-execution that the classification was wrong) are
///     different failures; sharing one counter would let a cheap research step starve a legitimate reroute.
///     <para>
///         A <see cref="WorkerBrief" /> is projected from the <see cref="RoutingLedger" /> by ordinary code, never
///         by asking an oracle to summarize — see <see cref="WorkerBrief.FromLedger" />.
///     </para>
///     <para>
///         Every pass — the route oracle's own answer, a research run, a worker run — is recorded as a
///     <see cref="ProcessStepRecord" />, correlated by an opaque parent-invocation identifier minted once at the
///         start of the run and threaded through every record and into the <see cref="WorkerBrief" /> handed to a
///         worker.
///     </para>
///     <para>Thread safety: instances are immutable and safe to share, but a run mutates its own ledger and may edit the working tree through the worker it selects.</para>
/// </remarks>
internal sealed class Router
{
    private readonly string _repositoryRoot;
    private readonly string _routeCharter;
    private readonly string _researchCharter;
    private readonly IReadOnlyList<WorkerCatalogEntry> _catalog;
    private readonly RecordStore _recordStore;
    private readonly int _maxResearchIterations;
    private readonly int _maxWorkerReroutes;
    private readonly Oracle<RouteDecisionEnvelope> _routeOracle;
    private readonly Research _research;

    /// <summary>
    ///     Binds a router to a repository, its worker catalog, and the budgets it enforces.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository routed over, whose configuration names the models behind the capability roles. Must not
    ///     be null or blank.
    /// </param>
    /// <param name="routeCharter">
    ///     The system message the route oracle carries: the worker catalog available, and that naming no route is
    ///     a correct answer. Must not be null.
    /// </param>
    /// <param name="researchCharter">The system message a bounded research pass carries. Must not be null.</param>
    /// <param name="catalog">
    ///     The worker catalog this router selects from. Must not be null or empty; for this pass, exactly one
    ///     entry — Small Fix.
    /// </param>
    /// <param name="recordStore">Where this run's <see cref="ProcessStepRecord" />s are appended. Must not be null.</param>
    /// <param name="maxResearchIterations">
    ///     The most bounded research passes this run may spend before failing closed. Independent of
    ///     <paramref name="maxWorkerReroutes" />. Must be zero or greater; defaults to 3.
    /// </param>
    /// <param name="maxWorkerReroutes">
    ///     The most worker reroutes this run may spend before failing closed. Independent of
    ///     <paramref name="maxResearchIterations" />. Must be zero or greater; defaults to 2.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this type's whole behavior is exercisable without a network call.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="repositoryRoot" /> is null, empty or blank, or when <paramref name="catalog" /> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="routeCharter" />, <paramref name="researchCharter" />, <paramref name="catalog" />,
    ///     or <paramref name="recordStore" /> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxResearchIterations" /> or <paramref name="maxWorkerReroutes" /> is negative.
    /// </exception>
    public Router(
        string repositoryRoot,
        string routeCharter,
        string researchCharter,
        IReadOnlyList<WorkerCatalogEntry> catalog,
        RecordStore recordStore,
        int maxResearchIterations = 3,
        int maxWorkerReroutes = 2,
        Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(routeCharter);
        ArgumentNullException.ThrowIfNull(researchCharter);
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Count == 0)
            throw new ArgumentException("A router needs at least one worker in its catalog.", nameof(catalog));
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResearchIterations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxWorkerReroutes);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _routeCharter = routeCharter;
        _researchCharter = researchCharter;
        _catalog = catalog;
        _recordStore = recordStore;
        _maxResearchIterations = maxResearchIterations;
        _maxWorkerReroutes = maxWorkerReroutes;
        _routeOracle = new Oracle<RouteDecisionEnvelope>(_repositoryRoot, routeCharter, endpointFor: endpointFor);
        _research = new Research(_repositoryRoot, researchCharter, endpointFor: endpointFor);
    }

    /// <summary>
    ///     Routes and runs a work item to completion, bounded by the two independent budgets this router enforces.
    /// </summary>
    /// <param name="workItem">The work item to route. Must not be null or blank.</param>
    /// <param name="changedFileHints">Changed-file hints to fold into the gathered repository facts, or null.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> with the completed change when a selected worker finished the
    ///     work; <see cref="OperationOutcome.Failed" /> with a <see cref="RouteFailureReport" /> when no route
    ///     exists, a budget was exhausted, or a worker itself failed, and no human-only next step was named;
    ///     <see cref="OperationOutcome.Escalated" /> with a <see cref="RouteFailureReport" /> when the route oracle
    ///     named a specific step only a person can take. In both failure cases,
    ///     <see cref="RouteFailureReport.ChangeBeforeStopping" /> is non-null when the selected worker wrote files
    ///     to disk before its interrupted outcome.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="workItem" /> is null, empty or blank.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<RouterOutcome>> RunAsync(
        string workItem, IReadOnlyList<string>? changedFileHints, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem);

        cancellationToken.ThrowIfCancellationRequested();

        var parentInvocationId = Guid.NewGuid().ToString();
        var ledger = new RoutingLedger
        {
            OriginalWorkItem = workItem,
            Facts = RepositoryFacts.Gather(_repositoryRoot, workItem, changedFileHints),
            InitialContextArtifacts = []
        };

        var researchBudget = _maxResearchIterations;
        var rerouteBudget = _maxWorkerReroutes;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var askResult = await _routeOracle
                .AskAsync("Route this work item.", BuildOracleContext(ledger), cancellationToken)
                .ConfigureAwait(false);

            RecordStep(parentInvocationId, "RouteOracle", askResult.Outcome, researchBudget, rerouteBudget);

            // Only a genuine communication failure (no envelope was ever decoded) stops here. A Refused
            // outcome still carries a decoded envelope - see Oracle<TDecision>.AskAsync - and for
            // RouteDecisionKind.NeedResearch, HasSufficientEvidence: false is not a refusal to answer at
            // all: it is the oracle honestly reporting the exact condition RouteCharter tells it to report
            // ("ask for a bounded, narrow look-around ... do not guess"). Discarding that envelope here,
            // as an earlier version of this method did, silently turned every honest research request into
            // an immediate failure and left the research budget below never spent on the path it exists
            // for - caught by a live run against a real model, where the fake-endpoint tests never
            // exercised this because their own NeedResearch fixture hardcoded HasSufficientEvidence: true.
            if (askResult.Outcome is OperationOutcome.Failed)
                return Fail(ledger, "the route oracle could not be asked or could not answer on the evidence given");

            var envelope = askResult.Finding!;
            var decision = Map(envelope);

            ledger.RouteAttempts.Add(
                new RouteAttempt(
                    DateTimeOffset.UtcNow,
                    "Route this work item.",
                    envelope.Kind.ToString(),
                    envelope.Why,
                    string.IsNullOrWhiteSpace(envelope.HumanOnlyNextStep) ? null : envelope.HumanOnlyNextStep));

            switch (decision)
            {
                case RouteDecision.NoRoute noRoute:
                    return Fail(ledger, noRoute.Why, noRoute.HumanOnlyNextStep);

                case RouteDecision.NeedResearch needResearch:
                    if (researchBudget <= 0)
                        return Fail(ledger, "the research budget was exhausted before this question could be asked");

                    researchBudget--;
                    ledger.ClassificationHypothesis ??= needResearch.Why;

                    var researched = await _research
                        .InvestigateAsync(needResearch.Question, cancellationToken)
                        .ConfigureAwait(false);

                    RecordStep(parentInvocationId, "Research", researched.Outcome, researchBudget, rerouteBudget);

                    if (researched.Finding is not null)
                        ledger.ResearchHistory.Add(researched.Finding);

                    continue;

                // A SelectWorker envelope reached with Refused means the oracle named a worker while
                // itself reporting insufficient evidence to commit to that answer - a genuinely
                // contradictory reply, unlike NeedResearch above, where reporting insufficient evidence is
                // the expected and honest signal. This is failed closed rather than run, exactly as it was
                // before this method started distinguishing NeedResearch from a true refusal.
                case RouteDecision.SelectWorker when askResult.Outcome != OperationOutcome.Succeeded:
                    return Fail(
                        ledger, "the route oracle named a worker but reported insufficient evidence to commit to it");

                case RouteDecision.SelectWorker selectWorker:
                    ledger.ClassificationHypothesis = selectWorker.Why;

                    if (!TryFindWorker(selectWorker.WorkerKey, out var entry))
                        return Fail(
                            ledger,
                            $"the route oracle selected worker '{selectWorker.WorkerKey}', which is not in this router's catalog");

                    var brief = WorkerBrief.FromLedger(ledger, parentInvocationId, selectWorker.Why);
                    var workerResult = await entry.Runner(brief, cancellationToken).ConfigureAwait(false);

                    RecordStep(
                        parentInvocationId, $"Worker:{selectWorker.WorkerKey}", workerResult.Outcome, researchBudget,
                        rerouteBudget);

                    switch (workerResult.Finding)
                    {
                        case WorkerRunResult.Completed completed when workerResult.Outcome == OperationOutcome.Succeeded:
                            return new StepResult<RouterOutcome>(
                                OperationOutcome.Succeeded, new RouterOutcome.Completed(completed.Summary), []);

                        case WorkerRunResult.Reroute reroute:
                            if (rerouteBudget <= 0)
                                return Fail(
                                    ledger, "the worker-reroute budget was exhausted before this reroute could be honored");

                            rerouteBudget--;
                            ledger.WorkerReroutes.Add(
                                new WorkerReroute(
                                    selectWorker.WorkerKey, reroute.Why, reroute.EvidenceRefs, reroute.SuggestedWorker));
                            continue;

                        default:
                            return workerResult.Outcome == OperationOutcome.Escalated
                                ? new StepResult<RouterOutcome>(
                                    OperationOutcome.Escalated,
                                    new RouterOutcome.Report(
                                        BuildReport(
                                            ledger,
                                            "the selected worker escalated to a person",
                                            changeBeforeStopping: workerResult.Interrupted)),
                                    workerResult.Notes)
                                : Fail(ledger, "the selected worker could not complete the work",
                                    changeBeforeStopping: workerResult.Interrupted);
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown route decision.");
            }
        }
    }

    private void RecordStep(
        string parentInvocationId, string step, OperationOutcome outcome, int researchBudget, int rerouteBudget) =>
        _recordStore.Append(
            new ProcessStepRecord(
                DateTimeOffset.UtcNow, parentInvocationId, step, outcome.ToString(), researchBudget, rerouteBudget));

    private bool TryFindWorker(string workerKey, out WorkerCatalogEntry entry)
    {
        foreach (var candidate in _catalog)
        {
            if (!string.Equals(candidate.Descriptor.Key, workerKey, StringComparison.OrdinalIgnoreCase))
                continue;
            entry = candidate;
            return true;
        }

        entry = null!;
        return false;
    }

    private static RouteDecision Map(RouteDecisionEnvelope envelope) => envelope.Kind switch
    {
        RouteDecisionKind.SelectWorker => new RouteDecision.SelectWorker(envelope.WorkerKey, envelope.Why),
        RouteDecisionKind.NeedResearch =>
            new RouteDecision.NeedResearch(envelope.Question, envelope.ResearchScope, envelope.Why),
        RouteDecisionKind.NoRoute => new RouteDecision.NoRoute(
            envelope.Why, string.IsNullOrWhiteSpace(envelope.HumanOnlyNextStep) ? null : envelope.HumanOnlyNextStep),
        _ => throw new ArgumentOutOfRangeException(nameof(envelope), envelope.Kind, "Unknown route decision kind.")
    };

    private static IReadOnlyList<string> BuildOracleContext(RoutingLedger ledger)
    {
        List<string> context =
        [
            $"Work item: {ledger.OriginalWorkItem}",
            $"README Direction facts: {(ledger.Facts.ReadmeDirectionFacts.Count == 0 ? "none" : string.Join("; ", ledger.Facts.ReadmeDirectionFacts))}",
            $"MIGRATION.md present: {ledger.Facts.MigrationPresent}; current stage: {ledger.Facts.MigrationCurrentStage ?? "none"}",
            $"Relevant architecture nodes: {(ledger.Facts.RelevantArchitectureNodes.Count == 0 ? "none" : string.Join(", ", ledger.Facts.RelevantArchitectureNodes))}",
            $"Changed-file hints: {(ledger.Facts.ChangedFileHints.Count == 0 ? "none" : string.Join(", ", ledger.Facts.ChangedFileHints))}",
            $"Requests template sync: {ledger.Facts.RequestsTemplateSync}",
            $"Keyword implication: {ledger.Facts.Implication}"
        ];

        context.AddRange(
            ledger.ResearchHistory.Select(
                finding => $"Research — {finding.Question}: {finding.Answer} ({finding.Implications})"));

        context.AddRange(
            ledger.WorkerReroutes.Select(
                reroute => $"Reroute from '{reroute.WorkerKey}': {reroute.Why}"));

        return context;
    }

    private static StepResult<RouterOutcome> Fail(
        RoutingLedger ledger,
        string reason,
        string? humanOnlyNextStep = null,
        ChangeSetBeforeStopping? changeBeforeStopping = null)
    {
        var namedStep = humanOnlyNextStep ?? ledger.RouteAttempts
            .AsEnumerable()
            .Reverse()
            .Select(attempt => attempt.HumanOnlyNextStep)
            .FirstOrDefault(step => !string.IsNullOrWhiteSpace(step));

        var report = BuildReport(ledger, reason, namedStep, changeBeforeStopping);

        return string.IsNullOrWhiteSpace(namedStep)
            ? new StepResult<RouterOutcome>(OperationOutcome.Failed, new RouterOutcome.Report(report), [])
            : new StepResult<RouterOutcome>(OperationOutcome.Escalated, new RouterOutcome.Report(report), []);
    }

    private static RouteFailureReport BuildReport(
        RoutingLedger ledger,
        string reason,
        string? namedStep = null,
        ChangeSetBeforeStopping? changeBeforeStopping = null)
    {
        List<string> tried =
        [
            .. ledger.RouteAttempts.Select(attempt => $"asked the route oracle: {attempt.DecisionKind} — {attempt.Rationale}"),
            .. ledger.ResearchHistory.Select(finding => $"researched: {finding.Question}"),
            .. ledger.WorkerReroutes.Select(reroute => $"ran worker '{reroute.WorkerKey}', which rerouted: {reroute.Why}")
        ];

        var learned = ledger.ResearchHistory.Count == 0 && ledger.WorkerReroutes.Count == 0
            ? reason
            : string.Join(
                " ",
                ledger.ResearchHistory.Select(finding => finding.Implications)
                    .Concat(ledger.WorkerReroutes.Select(reroute => reroute.Why)));

        List<RejectedWorker> rejected =
            [.. ledger.WorkerReroutes.Select(reroute => new RejectedWorker(reroute.WorkerKey, reroute.Why))];

        var recommendedNextStep = string.IsNullOrWhiteSpace(namedStep)
            ? $"a person should review this routing run manually: {reason}"
            : namedStep;

        return new RouteFailureReport(tried, learned, rejected, recommendedNextStep, changeBeforeStopping);
    }
}
