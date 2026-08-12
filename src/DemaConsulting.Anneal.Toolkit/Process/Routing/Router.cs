using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Process.Routing;

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

    /// <summary>A selected worker completed the work, or every phase of a decomposed Massive item did.</summary>
    /// <param name="Summary">What was changed. On a decomposed run, the aggregated files and summaries of every phase.</param>
    /// <param name="Effort">The classified Effort — Small, Medium, Large, or Massive — the route oracle reached alongside selecting this worker.</param>
    /// <param name="PhaseOutcomes">
    ///     Each decomposed phase's own outcome, in the order the phases were routed, when this item was Massive and
    ///     was decomposed. Never null; empty when this item was not decomposed.
    /// </param>
    internal sealed record Completed(ChangeSetSummary Summary, Effort Effort, IReadOnlyList<PhaseOutcome>? PhaseOutcomes = null) : RouterOutcome
    {
        /// <summary>Each decomposed phase's own outcome. Never null; empty when this item was not decomposed.</summary>
        public IReadOnlyList<PhaseOutcome> PhaseOutcomes { get; init; } = PhaseOutcomes ?? [];
    }

    /// <summary>The run could not route or complete the work; see the failure report for why.</summary>
    /// <param name="FailureReport">What was tried, what was learned, and a recommended next step.</param>
    internal sealed record Report(RouteFailureReport FailureReport) : RouterOutcome;
}

/// <summary>
///     Routes a work item to a bounded worker catalog by asking a narrow typed question of a route oracle,
///     spending two independent budgets — research iterations and worker reroutes — rather than one shared one.
/// </summary>
/// <remarks>
///     This is the compiled Router <c>.anneal/architecture/process.md</c> § Composition and § Decisions describe: one
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
    /// <summary>The most decompositions any single Massive item may recurse through before it must escalate instead — the depth cap of two <c>change-classification.md</c> § Massive Effort Must Be Decomposed sets.</summary>
    private const int MaxDecompositionDepth = 2;

    private readonly string _repositoryRoot;
    private readonly string _routeCharter;
    private readonly string _researchCharter;
    private readonly string _decompositionCharter;
    private readonly string _cumulativeCheckCharter;
    private readonly IReadOnlyList<WorkerCatalogEntry> _catalog;
    private readonly RecordStore _recordStore;
    private readonly int _maxResearchIterations;
    private readonly int _maxWorkerReroutes;
    private readonly Oracle<RouteDecisionEnvelope> _routeOracle;
    private readonly Oracle<PhaseDecompositionEnvelope> _decompositionOracle;
    private readonly Oracle<CumulativeCheckEnvelope> _cumulativeCheckOracle;
    private readonly Research _research;
    private readonly DiffCheck _diffCheck;

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
    /// <param name="decompositionCharter">
    ///     The system message a decomposition pass carries when a Massive item must be split into phases before
    ///     any of them is routed. Must not be null.
    /// </param>
    /// <param name="cumulativeCheckCharter">
    ///     The system message the mandatory cumulative-check pass carries, asked once over a whole proposed phase
    ///     set before any phase is routed. Must not be null.
    /// </param>
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
    /// <param name="runGit">
    ///     Runs one <c>git</c> invocation for the working-tree diff check, or null to run it through the real
    ///     <c>git</c> executable. Injected so the diff-grounding behavior is exercisable without a real repository.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="repositoryRoot" /> is null, empty or blank, or when <paramref name="catalog" /> is empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="routeCharter" />, <paramref name="researchCharter" />,
    ///     <paramref name="decompositionCharter" />, <paramref name="cumulativeCheckCharter" />,
    ///     <paramref name="catalog" />, or <paramref name="recordStore" /> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxResearchIterations" /> or <paramref name="maxWorkerReroutes" /> is negative.
    /// </exception>
    public Router(
        string repositoryRoot,
        string routeCharter,
        string researchCharter,
        string decompositionCharter,
        string cumulativeCheckCharter,
        IReadOnlyList<WorkerCatalogEntry> catalog,
        RecordStore recordStore,
        int maxResearchIterations = 3,
        int maxWorkerReroutes = 2,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunGitCommand? runGit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(routeCharter);
        ArgumentNullException.ThrowIfNull(researchCharter);
        ArgumentNullException.ThrowIfNull(decompositionCharter);
        ArgumentNullException.ThrowIfNull(cumulativeCheckCharter);
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Count == 0)
            throw new ArgumentException("A router needs at least one worker in its catalog.", nameof(catalog));
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResearchIterations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxWorkerReroutes);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _routeCharter = routeCharter;
        _researchCharter = researchCharter;
        _decompositionCharter = decompositionCharter;
        _cumulativeCheckCharter = cumulativeCheckCharter;
        _catalog = catalog;
        _recordStore = recordStore;
        _maxResearchIterations = maxResearchIterations;
        _maxWorkerReroutes = maxWorkerReroutes;
        _routeOracle = new Oracle<RouteDecisionEnvelope>(_repositoryRoot, routeCharter, endpointFor: endpointFor);
        _decompositionOracle =
            new Oracle<PhaseDecompositionEnvelope>(_repositoryRoot, decompositionCharter, endpointFor: endpointFor);
        _cumulativeCheckOracle =
            new Oracle<CumulativeCheckEnvelope>(_repositoryRoot, cumulativeCheckCharter, endpointFor: endpointFor);
        _research = new Research(_repositoryRoot, researchCharter, endpointFor: endpointFor);
        _diffCheck = new DiffCheck(_repositoryRoot, runGit: runGit);
    }

    /// <summary>
    ///     Routes and runs a work item to completion, bounded by the two independent budgets this router enforces.
    /// </summary>
    /// <param name="workItem">The work item to route. Must not be null or blank.</param>
    /// <param name="changedFileHints">Changed-file hints to fold into the gathered repository facts, or null.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <param name="depth">
    ///     How many decompositions already produced this call's own work item: 0 for a top-level call, threaded one
    ///     deeper on every recursive re-route of a decomposed <see cref="Phase" /> rather than remembered as state
    ///     the router itself tracks — see <c>.anneal/architecture/toolkit/route.md</c> § Decisions ("The depth cap of
    ///     two is carried the same way the existing budgets already are — as a bound threaded on the call"). At
    ///     <c>2</c>, a Massive classification escalates instead of decomposing further.
    /// </param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> with the completed change when a selected worker finished the
    ///     work, or when every phase of a decomposed Massive item completed; <see cref="OperationOutcome.Failed" />
    ///     with a <see cref="RouteFailureReport" /> when no route exists, a budget was exhausted, a worker itself
    ///     failed, or a decomposed phase did not complete, and no human-only next step was named;
    ///     <see cref="OperationOutcome.Escalated" /> with a <see cref="RouteFailureReport" /> when the route oracle
    ///     named a specific step only a person can take, when a phase's declared file scope touches a protected
    ///     path, when the cumulative check finds the phase set's union crosses a hidden boundary, or when the
    ///     depth cap was reached. In both failure cases, <see cref="RouteFailureReport.ChangeBeforeStopping" /> is
    ///     non-null when the selected worker wrote files to disk before its interrupted outcome.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="workItem" /> is null, empty or blank.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<RouterOutcome>> RunAsync(
        string workItem, IReadOnlyList<string>? changedFileHints, CancellationToken cancellationToken, int depth = 0)
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
                    ledger.EffortHypothesis = noRoute.Effort;
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
                    ledger.EffortHypothesis = selectWorker.Effort;

                    // TOOLKIT-26: a Massive item is never handed to a worker directly - it is decomposed into
                    // phases and re-routed through this same method, one depth deeper, rather than run here.
                    if (selectWorker.Effort == Effort.Massive)
                        return await DecomposeAsync(
                                ledger, parentInvocationId, depth, researchBudget, rerouteBudget, cancellationToken)
                            .ConfigureAwait(false);

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
                                OperationOutcome.Succeeded,
                                new RouterOutcome.Completed(completed.Summary, selectWorker.Effort), []);

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
                            var grounded = await GroundInterruptedAsync(
                                workerResult.Interrupted, cancellationToken).ConfigureAwait(false);
                            return workerResult.Outcome == OperationOutcome.Escalated
                                ? new StepResult<RouterOutcome>(
                                    OperationOutcome.Escalated,
                                    new RouterOutcome.Report(
                                        BuildReport(
                                            ledger,
                                            "the selected worker escalated to a person",
                                            changeBeforeStopping: grounded)),
                                    workerResult.Notes)
                                : Fail(ledger, "the selected worker could not complete the work",
                                    changeBeforeStopping: grounded);
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(decision), decision, "Unknown route decision.");
            }
        }
    }

    /// <remarks>
    ///     Only called after a worker actually ran (the default switch arm). Other failure paths — budget
    ///     exhaustion, no worker selected — are not grounded because no worker wrote anything to the working tree,
    ///     so there is no gap between what the worker reported and what git would show.
    ///
    ///     Reconciliation rules:
    ///     - Diff unavailable: keep worker-reported interrupted unchanged.
    ///     - Diff available but empty: keep worker-reported interrupted unchanged — an empty diff paired with a
    ///       real worker summary (e.g. a legitimate revert) is more informative than blanking it.
    ///     - Diff has files but worker reported null: synthesize a new <see cref="ChangeSetBeforeStopping"/> from
    ///       the diff's file list with a summary explaining the worker did not report changes.
    ///     - Both have data: keep the worker's summary text but replace FilesChanged with the diff's authoritative
    ///       list, and note the mismatch was reconciled.
    /// </remarks>
    private async Task<ChangeSetBeforeStopping?> GroundInterruptedAsync(
        ChangeSetBeforeStopping? workerInterrupted, CancellationToken cancellationToken)
    {
        var diffResult = await _diffCheck.RunAsync(null, cancellationToken).ConfigureAwait(false);
        if (diffResult.Outcome != OperationOutcome.Succeeded || !diffResult.Finding!.Available)
            return workerInterrupted;

        var diffFiles = diffResult.Finding.ChangedFiles;

        if (diffFiles.Count == 0)
            return workerInterrupted;

        if (workerInterrupted is null)
            return new ChangeSetBeforeStopping(
                diffFiles,
                "The worker did not report changes before stopping, but git diff shows files were modified.");

        // Both have data: the diff's file list is authoritative; the worker's summary is preserved.
        return workerInterrupted with
        {
            FilesChanged = diffFiles,
            Summary = workerInterrupted.Summary +
                      " (reconciled: diff-reported file list replaced worker-reported list)"
        };
    }

    /// <summary>
    ///     Decomposes a Massive item into phases and re-routes each one through this same method, one depth
    ///     deeper - see <c>.anneal/architecture/toolkit/route.md</c> §§ TOOLKIT-26 through TOOLKIT-28.
    /// </summary>
    /// <remarks>
    ///     Order matters here and follows the contract's own order: the depth cap is checked before anything else
    ///     is spent, the mechanical strict-subset containment check and the deterministic tripwire both run before
    ///     the cumulative-check oracle is asked at all, and the phase set is only ever routed once every one of
    ///     those has cleared - never decomposed to dodge the mandatory cumulative check, per
    ///     <c>change-classification.md</c> § Discipline.
    /// </remarks>
    private async Task<StepResult<RouterOutcome>> DecomposeAsync(
        RoutingLedger ledger,
        string parentInvocationId,
        int depth,
        int researchBudget,
        int rerouteBudget,
        CancellationToken cancellationToken)
    {
        // TOOLKIT-28: a phase produced by a second decomposition may not decompose again - reaching depth 2
        // means this item is itself the result of two prior decompositions, so it escalates instead.
        if (depth >= MaxDecompositionDepth)
            return Fail(
                ledger,
                "this Massive item was reached by decomposing a Massive item's own phase a second time; the depth cap of two forbids decomposing it further",
                "a person must split this work item manually - the automatic decomposition depth cap of two was reached");

        var decompositionResult = await _decompositionOracle
            .AskAsync(
                BuildDecompositionInstruction(ledger),
                BuildDecompositionContext(ledger), cancellationToken)
            .ConfigureAwait(false);

        RecordStep(parentInvocationId, "Decomposition", decompositionResult.Outcome, researchBudget, rerouteBudget);

        if (decompositionResult.Outcome != OperationOutcome.Succeeded)
            return Fail(ledger, "the decomposition pass could not be completed for this Massive item");

        var envelope = decompositionResult.Finding!;
        if (envelope.Kind == PhaseDecompositionKind.CannotDecompose)
            return Fail(ledger, envelope.Why);

        if (envelope.PhaseWorkItems.Count == 0 ||
            envelope.PhaseWorkItems.Count != envelope.PhaseFileScopes.Count ||
            envelope.PhaseWorkItems.Count != envelope.PhaseEditCategories.Count)
            return Fail(
                ledger,
                "the decomposition pass returned a malformed phase set (empty, or its phase arrays disagree in length)");

        List<Phase> phases = [];
        for (var i = 0; i < envelope.PhaseWorkItems.Count; i++)
        {
            if (!Enum.TryParse<EditCategory>(envelope.PhaseEditCategories[i], ignoreCase: true, out var category))
                return Fail(
                    ledger,
                    $"the decomposition pass named an unknown edit category '{envelope.PhaseEditCategories[i]}' for phase '{envelope.PhaseWorkItems[i]}'");

            IReadOnlyList<string> scope =
                [.. envelope.PhaseFileScopes[i].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

            phases.Add(new Phase(envelope.PhaseWorkItems[i], scope, category, depth));
        }

        // TOOLKIT-26: every generated phase's declared file scope is a strict subset of the file scope the
        // original item's own classification already cleared - a mechanical containment check, no oracle
        // needed. When the original item cleared no explicit file scope of its own (no changed-file hints were
        // given), there is nothing declared to be a strict subset of, so this check is vacuously satisfied -
        // a judgement call, see route.md apply report.
        var clearedScope = ledger.Facts.ChangedFileHints;
        if (clearedScope.Count > 0)
        {
            foreach (var phase in phases)
            {
                var isStrictSubset = phase.FileScope.Count > 0 &&
                                      phase.FileScope.Count < clearedScope.Count &&
                                      phase.FileScope.All(entry => clearedScope.Contains(entry, StringComparer.OrdinalIgnoreCase));

                if (!isStrictSubset)
                    return Fail(
                        ledger,
                        $"phase '{phase.WorkItem}' declares a file scope that is not a strict subset of the file scope already cleared for this item");
            }
        }

        // TOOLKIT-27: any phase touching a protected path forces escalation unconditionally, regardless of
        // what the cumulative check below concludes - checked, and acted on, before that oracle is even asked.
        foreach (var phase in phases)
        {
            var trippedPath = ProtectedPathTripwire.FindTrippedPath(phase.FileScope);
            if (trippedPath is not null)
                return Fail(
                    ledger,
                    $"phase '{phase.WorkItem}' declares a file scope touching the protected path '{trippedPath}'",
                    $"a person must review phase '{phase.WorkItem}': its declared file scope touches '{trippedPath}', which no decomposition may touch automatically");
        }

        // TOOLKIT-26: the whole proposed phase set is cleared by a mandatory cumulative check before any
        // phase is routed.
        var cumulativeResult = await _cumulativeCheckOracle
            .AskAsync(
                "Does this whole proposed phase set's union cross a boundary no single phase crosses alone?",
                BuildPhaseSetContext(ledger, phases), cancellationToken)
            .ConfigureAwait(false);

        RecordStep(parentInvocationId, "CumulativeCheck", cumulativeResult.Outcome, researchBudget, rerouteBudget);

        if (cumulativeResult.Outcome != OperationOutcome.Succeeded)
            return Fail(ledger, "the mandatory cumulative check could not be completed for this phase set");

        var cumulativeEnvelope = cumulativeResult.Finding!;
        if (cumulativeEnvelope.Kind == CumulativeCheckKind.Escalate)
            return Fail(
                ledger,
                $"the cumulative check found this phase set's union crosses a boundary no single phase crosses alone: {cumulativeEnvelope.Why}",
                string.IsNullOrWhiteSpace(cumulativeEnvelope.HumanOnlyNextStep)
                    ? "a person should re-evaluate this decomposition; the cumulative check found a hidden boundary crossing across the phase set"
                    : cumulativeEnvelope.HumanOnlyNextStep);

        // Cleared: re-route each phase through this same method, one depth deeper, and aggregate the
        // per-phase outcomes into one overall outcome for the original Massive item - successful only if
        // every phase completes.
        List<PhaseOutcome> phaseOutcomes = [];
        List<string> aggregatedFiles = [];
        List<string> aggregatedSummaries = [];

        foreach (var phase in phases)
        {
            var phaseResult = await RunAsync(phase.WorkItem, phase.FileScope, cancellationToken, depth + 1)
                .ConfigureAwait(false);

            switch (phaseResult.Finding)
            {
                case RouterOutcome.Completed phaseCompleted when phaseResult.Outcome == OperationOutcome.Succeeded:
                    phaseOutcomes.Add(
                        new PhaseOutcome(phase.WorkItem, phaseResult.Outcome.ToString(), phaseCompleted.Summary.Summary));
                    aggregatedFiles.AddRange(phaseCompleted.Summary.FilesChanged);
                    aggregatedSummaries.Add($"{phase.WorkItem}: {phaseCompleted.Summary.Summary}");
                    continue;

                case RouterOutcome.Report phaseReport:
                    phaseOutcomes.Add(
                        new PhaseOutcome(
                            phase.WorkItem, phaseResult.Outcome.ToString(), phaseReport.FailureReport.RecommendedNextStep));

                    return new StepResult<RouterOutcome>(
                        phaseResult.Outcome,
                        new RouterOutcome.Report(
                            phaseReport.FailureReport with
                            {
                                WhatWasLearned =
                                    $"phase '{phase.WorkItem}' did not complete: {phaseReport.FailureReport.WhatWasLearned}",
                                PhaseOutcomes = phaseOutcomes
                            }),
                        []);

                default:
                    return Fail(ledger, $"phase '{phase.WorkItem}' reached an unexpected outcome");
            }
        }

        return new StepResult<RouterOutcome>(
            OperationOutcome.Succeeded,
            new RouterOutcome.Completed(
                new ChangeSetSummary(aggregatedFiles, string.Join(" ", aggregatedSummaries)),
                Effort.Massive,
                phaseOutcomes),
            []);
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
        RouteDecisionKind.SelectWorker => new RouteDecision.SelectWorker(envelope.WorkerKey, envelope.Why, envelope.Effort),
        RouteDecisionKind.NeedResearch =>
            new RouteDecision.NeedResearch(envelope.Question, envelope.ResearchScope, envelope.Why),
        RouteDecisionKind.NoRoute => new RouteDecision.NoRoute(
            envelope.Why, string.IsNullOrWhiteSpace(envelope.HumanOnlyNextStep) ? null : envelope.HumanOnlyNextStep,
            envelope.Effort),
        _ => throw new ArgumentOutOfRangeException(nameof(envelope), envelope.Kind, "Unknown route decision kind.")
    };

    private static IReadOnlyList<string> BuildOracleContext(RoutingLedger ledger)
    {
        List<string> context =
        [
            $"Work item: {ledger.OriginalWorkItem}",
            $"Vision facts: {(ledger.Facts.VisionFacts.Count == 0 ? "none" : string.Join("; ", ledger.Facts.VisionFacts))}",
            $".anneal/work/active-plan.md present: {ledger.Facts.MigrationPresent}; current stage: {ledger.Facts.MigrationCurrentStage ?? "none"}",
            $"Relevant architecture nodes: {(ledger.Facts.RelevantArchitectureNodes.Count == 0 ? "none" : string.Join(", ", ledger.Facts.RelevantArchitectureNodes))}",
            $"Changed-file hints: {(ledger.Facts.ChangedFileHints.Count == 0 ? "none" : string.Join(", ", ledger.Facts.ChangedFileHints))}",
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

    private static string BuildDecompositionInstruction(RoutingLedger ledger) =>
        ledger.Facts.ChangedFileHints.Count == 0
            ? "Decompose this Massive work item into phases. No explicit changed-file scope was declared for the original item, so propose narrow repository-relative phase scopes and do not refuse solely because there is no prior scope to be a strict subset of."
            : "Decompose this Massive work item into phases, each a strict subset of the authoritative changed-file scope already cleared for it.";

    private static IReadOnlyList<string> BuildDecompositionContext(RoutingLedger ledger)
    {
        List<string> context =
        [
            $"Work item: {ledger.OriginalWorkItem}",
            $"Vision facts: {(ledger.Facts.VisionFacts.Count == 0 ? "none" : string.Join("; ", ledger.Facts.VisionFacts))}",
            $".anneal/work/active-plan.md present: {ledger.Facts.MigrationPresent}; current stage: {ledger.Facts.MigrationCurrentStage ?? "none"}",
            $"Relevant architecture nodes: {(ledger.Facts.RelevantArchitectureNodes.Count == 0 ? "none" : string.Join(", ", ledger.Facts.RelevantArchitectureNodes))}",
            ledger.Facts.ChangedFileHints.Count == 0
                ? "Cleared file scope boundary: none was explicitly declared for the original item. You may still decompose it by naming narrow repository-relative phase scopes; the router will not require a strict-subset comparison against a missing boundary."
                : $"Cleared file scope boundary: treat this exact changed-file-hint list as the authoritative already-cleared scope for the original item, and make every phase's declared file scope a strict subset of it: {string.Join(", ", ledger.Facts.ChangedFileHints)}",
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

    private static IReadOnlyList<string> BuildPhaseSetContext(RoutingLedger ledger, IReadOnlyList<Phase> phases)
    {
        List<string> context =
        [
            $"Original Massive work item: {ledger.OriginalWorkItem}",
            $"Original cleared file scope: {(ledger.Facts.ChangedFileHints.Count == 0 ? "none declared" : string.Join(", ", ledger.Facts.ChangedFileHints))}",
            $"Proposed phase count: {phases.Count}"
        ];

        context.AddRange(
            phases.Select(
                (phase, index) =>
                    $"Phase {index + 1}: {phase.WorkItem} — scope: {string.Join(", ", phase.FileScope)} — category: {phase.EditCategory}"));

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

        return new RouteFailureReport(tried, learned, rejected, recommendedNextStep, ledger.EffortHypothesis, changeBeforeStopping);
    }
}
