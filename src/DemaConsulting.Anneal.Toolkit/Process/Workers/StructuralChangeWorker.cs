using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     The Structural Change path: a single-shot <see cref="Planner" /> decides whether the change needs a
///     plan at all, its steps (when there is a plan) compose the instructions <see cref="DocumentAuthor" /> and
///     <see cref="Developer" /> author against — a structural change routinely touches <c>overview.md</c> plus
///     multiple system documents, not the single document Contract Change assumes — followed by the same two
///     <see cref="DeterministicCheck" /> steps Contract Change already runs (<c>build.ps1</c>, a non-strict contract
///     check) and a model-backed <see cref="Verifier" /> pass judging contract
///     conformance.
/// </summary>
/// <remarks>
///     Extends <see cref="ContractChangeWorker" />'s hand-rolled repair contract by one more owner and one more
///     budget, rather than composing a <see cref="RepairLoop{TState}" />, for the same reason Contract Change
///     already found: the repair step is chosen dynamically per verdict among four owners now
///     (<see cref="Planner" />, <see cref="DocumentAuthor" />, <see cref="Developer" />, and a tenet-check repair
///     that also routes through <see cref="Developer" />), not fixed at construction. A documentation, code, or
///     tenet finding repairs through the owner the verdict names — a tenet finding through <see cref="Developer" />,
///     the same primitive a code finding uses, since fixing a tenet violation means changing code or configuration
///     to conform to <c>.anneal/work/constraints.md</c> and the affected contracts — spending that owner's own one-shot budget,
///     exactly as Contract Change already does. A verifier finding whose
///     verdict is <see cref="VerificationVerdict.RepairRequired" /> but whose <see cref="VerificationFinding.Concerns" />
///     names none of <see cref="VerificationOwner.Documentation" />, <see cref="VerificationOwner.Code" />, or
///     <see cref="VerificationOwner.Tenet" /> is
///     different in kind — the plan's own decomposition was wrong, not its execution — and spends a fourth,
///     independent one-shot budget: a second
///     and final <see cref="Planner" /> call, informed by what the first attempt got wrong, after which
///     <see cref="DocumentAuthor" /> → <see cref="Developer" /> → checks → <see cref="Verifier" /> restart from
///     the top. The documentation, code, and tenet repair budgets are <b>not</b> reset when this happens — they
///     are independent counters that simply carry whatever they had left across the re-plan, per
///     <c>.anneal/work/active-plan.md</c>'s S9 entry.
///     <para>
///         <b>When this worker reroutes.</b> Four paths surface a <see cref="WorkerRunResult.Reroute" />: (1)
///         the <see cref="Planner" /> itself concludes the work does not belong to a structural worker at all,
///         surfaced before any document or code work is attempted; (2) <see cref="DocumentAuthor" /> or
///         <see cref="Developer" /> names a better owner while authoring, exactly as <see cref="ContractChangeWorker" />
///         surfaces either primitive's own reroute unchanged; (3) the <see cref="Verifier" /> reaches
///         <see cref="VerificationVerdict.RerouteRequired" /> because this change does not belong to a structural
///         worker after all. A <see cref="Planner" />-reached <see cref="PlanningDecision.Reroute" /> and a
///         <see cref="Verifier" />-reached <see cref="VerificationVerdict.RerouteRequired" /> are both
///         <see cref="OperationOutcome.Succeeded" /> at the primitive layer — a primitive successfully answering
///         its own question, per <c>.anneal/architecture/toolkit.md</c> § Decisions — so both map onto this worker's
///         own <see cref="WorkerRunResult.Reroute" />, never <see cref="OperationOutcome.Failed" />.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
internal sealed class StructuralChangeWorker
{
    /// <summary>The repository-relative build/test script this worker's first deterministic check runs, or null.</summary>
    private readonly string? _buildScript;

    /// <summary>
    ///     The narrower question a <see cref="Verifier" /> answers once its deterministic evidence has passed.
    ///     Names both the repair verdicts and the two failure classes this worker distinguishes — a
    ///     <see cref="VerificationVerdict.RepairRequired" /> finding with no <see cref="VerificationOwner.Documentation" />,
    ///     <see cref="VerificationOwner.Code" />, or <see cref="VerificationOwner.Tenet" /> concern, against a
    ///     documentation, code, or tenet repair, and
    ///     <see cref="VerificationVerdict.RerouteRequired" /> against either repair path — rather than this worker
    ///     trying to infer which applies from prose.
    /// </summary>
    private const string VerifierQuestion =
        """
        Judge whether this structural change conforms to every contract clause it touches and leaves
        .anneal/architecture/ accurate for what was actually built. Also check the change against .anneal/work/constraints.md's
        Satisfied constraints and the boundaries of every system contract it touches; report any violation as a
        concern owned by Tenet, with a FixText naming the specific constraint or contract boundary crossed and
        what must change to stop crossing it. Report the verdict 'RepairRequired' with an
        empty list of concerns, and your reasoning in the advisory notes, when the plan's own decomposition was
        wrong - the wrong systems were touched, a needed split, merge, or new node was missed, or the steps taken
        do not add up to the change asked for - as distinct from a documentation, code, or tenet defect in an
        otherwise-correctly-decomposed change, which is a documentation, code, or tenet concern instead. Report the
        verdict 'RerouteRequired', with your reasoning in the advisory notes, when this change does not belong to
        a structural worker at all.
        """;

    /// <summary>
    ///     The fixed standards injected into every <see cref="Planner" /> call — <see cref="Planner" /> is the
    ///     one place this worker decides scope/plan shape, so it is the place a re-plan needs classification
    ///     guidance most.
    /// </summary>
    private static readonly string[] PlannerStandards = ["change-classification.md"];

    private readonly string _repositoryRoot;
    private readonly Planner _planner;
    private readonly DocumentAuthor _documentAuthor;
    private readonly Developer _developer;
    private readonly DeterministicCheck _buildCheck;
    private readonly DeterministicCheck _contractCheck;
    private readonly Verifier _verifier;
    private readonly int _maxDocumentationRepairAttempts;
    private readonly int _maxCodeRepairAttempts;
    private readonly int _maxTenetRepairAttempts;
    private readonly int _maxReplanAttempts;
    private readonly RecordStore? _recordStore;

    /// <summary>
    ///     Binds a Structural Change worker to a repository and the charters its composed primitives carry.
    /// </summary>
    /// <param name="repositoryRoot">The repository authored into and checked. Must not be null or blank.</param>
    /// <param name="plannerCharter">
    ///     The system message the single-shot <see cref="Planner" /> pass carries: what "the work" is, and that a
    ///     plan, a preference for direct execution, or a reroute are all correct answers. Must not be null.
    /// </param>
    /// <param name="documentAuthorCharter">
    ///     The system message <see cref="DocumentAuthor" />'s pass carries: what document(s) it may update, and
    ///     that pruning a section doc that no longer earns its place is part of the job. Must not be null.
    /// </param>
    /// <param name="developerCharter">
    ///     The system message <see cref="Developer" />'s pass carries: implement code and tests against the plan
    ///     and documentation the earlier passes just produced. Must not be null.
    /// </param>
    /// <param name="verifierCharter">
    ///     The system message the model-backed <see cref="Verifier" /> pass carries. Must not be null.
    /// </param>
    /// <param name="maxDocumentationRepairAttempts">
    ///     The most documentation-repair attempts spent when a verdict's concerns name
    ///     <see cref="VerificationOwner.Documentation" />, independent of the other three budgets. Must be
    ///     zero or greater; defaults to 1, kept equal to Contract Change's precedent pending live evidence.
    /// </param>
    /// <param name="maxCodeRepairAttempts">
    ///     The most code-repair attempts spent when a verdict's concerns name <see cref="VerificationOwner.Code" />,
    ///     independent of the other three budgets. Must be
    ///     zero or greater; defaults to 1, kept equal to Contract Change's precedent pending live evidence.
    /// </param>
    /// <param name="maxTenetRepairAttempts">
    ///     The most tenet-repair attempts spent when a verdict's concerns name <see cref="VerificationOwner.Tenet" />,
    ///     independent of the other three budgets. A tenet finding repairs through <see cref="Developer" /> — the
    ///     same primitive a code finding uses — since fixing a tenet violation means changing code or
    ///     configuration to conform to <c>.anneal/work/constraints.md</c> and the affected contracts. Must be zero or greater;
    ///     defaults to 1, kept equal to Contract Change's precedent pending live evidence.
    /// </param>
    /// <param name="maxReplanAttempts">
    ///     The most times a <see cref="VerificationVerdict.RepairRequired" /> verdict with no documentation, code,
    ///     or tenet concern spends a second
    ///     <see cref="Planner" /> call, independent of the other three budgets. Must be zero or greater; defaults to
    ///     1, per this stage's one re-plan budget.
    /// </param>
    /// <param name="documentAuthorTargetFileCountBudget">
    ///     The most files <see cref="DocumentAuthor" /> may touch in one authoring pass before it is treated as
    ///     having grown past its own bound. Must be greater than zero; defaults to 8, raised from
    ///     <see cref="DocumentAuthor" />'s own default of 3 because a structural change routinely touches
    ///     <c>overview.md</c> plus multiple system documents, not the single document Contract Change assumes.
    /// </param>
    /// <param name="maxPlanSteps">
    ///     The most steps a <see cref="Planner" /> plan may contain before it is treated as having failed to stay
    ///     narrow. Must be greater than zero; defaults to 12, raised from <see cref="Planner" />'s own generic
    ///     default of 8 for the same reason <paramref name="documentAuthorTargetFileCountBudget" /> was already
    ///     raised: a genuinely-scoped structural change decomposes into more granular steps (inspect the tree, edit
    ///     each affected contract document, add the new system's code, edit each dependent system's code, add or
    ///     move tests, run the build) than a single-system Contract Change plan needs, and the un-widened default
    ///     was found, live, to fail-closed two live routing trials (S11's discovery log) whose own plans reached
    ///     exactly 10 steps for a change genuinely scoped to one new system and its two existing neighbors.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this worker's whole behavior is exercisable without a network call.
    /// </param>
    /// <param name="buildRunScript">
    ///     Runs the repository's <c>build.ps1</c>, or null to run it through the PowerShell host. Injected so the
    ///     deterministic check is exercisable without a real build.
    /// </param>
    /// <param name="contractCheckRunScript">
    ///     Runs the repository's non-strict contract check, or null to run it through <see cref="ContractCheckRunner" />
    ///     — <see cref="Operations.CheckContractsOperation" /> called in process with <c>-Strict</c> filtered out,
    ///     so pre-existing staged TODO obligations unrelated to this change do not block the run while real test
    ///     failures still do. <see cref="DeterministicCheck" />'s own <c>selector</c> parameter is evidence
    ///     metadata only and is never forwarded to the script it runs, so the default arguments are resolved
    ///     inside the default delegate rather than threaded through that parameter.
    ///     Injected so the check is exercisable without a real script.
    /// </param>
    /// <param name="recordStore">
    ///     Where this worker's own <see cref="ProcessStepRecord" />s are appended, correlated by the
    ///     <see cref="WorkerBrief.ParentInvocationId" /> a <see cref="Router" /> minted for the run, or null to
    ///     record nothing beyond the single <c>Worker:{key}</c> step the <see cref="Router" /> itself already
    ///     records.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="plannerCharter" />, <paramref name="documentAuthorCharter" />,
    ///     <paramref name="developerCharter" />, or <paramref name="verifierCharter" /> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxDocumentationRepairAttempts" />, <paramref name="maxCodeRepairAttempts" />,
    ///     <paramref name="maxTenetRepairAttempts" />, or <paramref name="maxReplanAttempts" /> is negative, or when
    ///     <paramref name="documentAuthorTargetFileCountBudget" /> or <paramref name="maxPlanSteps" /> is not
    ///     greater than zero.
    /// </exception>
    public StructuralChangeWorker(
        string repositoryRoot,
        string plannerCharter,
        string documentAuthorCharter,
        string developerCharter,
        string verifierCharter,
        int maxDocumentationRepairAttempts = 1,
        int maxCodeRepairAttempts = 1,
        int maxTenetRepairAttempts = 1,
        int maxReplanAttempts = 1,
        int documentAuthorTargetFileCountBudget = 8,
        int maxPlanSteps = 12,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? buildRunScript = null,
        RunRepositoryScript? contractCheckRunScript = null,
        RecordStore? recordStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(plannerCharter);
        ArgumentNullException.ThrowIfNull(documentAuthorCharter);
        ArgumentNullException.ThrowIfNull(developerCharter);
        ArgumentNullException.ThrowIfNull(verifierCharter);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDocumentationRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCodeRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTenetRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(maxReplanAttempts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(documentAuthorTargetFileCountBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPlanSteps);

        var root = Path.GetFullPath(repositoryRoot);

        _repositoryRoot = root;
        _planner = new Planner(root, plannerCharter, maxPlanSteps: maxPlanSteps, endpointFor: endpointFor);
        _documentAuthor = new DocumentAuthor(
            root, documentAuthorCharter, targetFileCountBudget: documentAuthorTargetFileCountBudget, endpointFor: endpointFor);
        _developer = new Developer(root, developerCharter, endpointFor: endpointFor);
        _buildCheck = new DeterministicCheck(root, runScript: buildRunScript);
        _contractCheck = new DeterministicCheck(
            root,
            runScript: contractCheckRunScript ?? ((_, ct) => ContractCheckRunner.RunAsync(root, ct, strict: false)));
        _verifier = new Verifier(root, verifierCharter, endpointFor: endpointFor);
        _maxDocumentationRepairAttempts = maxDocumentationRepairAttempts;
        _maxCodeRepairAttempts = maxCodeRepairAttempts;
        _maxTenetRepairAttempts = maxTenetRepairAttempts;
        _maxReplanAttempts = maxReplanAttempts;
        _recordStore = recordStore;
        _buildScript = ScriptConfiguration.Load(root).Build;
    }

    /// <summary>
    ///     Runs the worker against a deterministically-projected brief.
    /// </summary>
    /// <param name="brief">What to change, and the context the router gathered for it.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> with <see cref="WorkerRunResult.Completed" /> when the
    ///     documentation and code changes were authored and both deterministic checks and the verifier passed;
    ///     <see cref="OperationOutcome.Succeeded" /> with <see cref="WorkerRunResult.Reroute" /> when the
    ///     <see cref="Planner" />, <see cref="DocumentAuthor" />, <see cref="Developer" />, or the
    ///     <see cref="Verifier" /> named a better owner; <see cref="OperationOutcome.Escalated" /> when a repair
    ///     needed a protected path; <see cref="OperationOutcome.Failed" /> when a repair or re-plan budget was
    ///     spent with its named finding still open, when the verifier judged its evidence insufficient, or when no
    ///     model could be reached. Both Escalated and Failed populate <see cref="WorkerExecutionResult.Interrupted" />
    ///     when the enclosing method already holds real documentation/code state, so the caller can see which files
    ///     were already on disk.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="brief" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<WorkerExecutionResult> RunAsync(WorkerBrief brief, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);

        cancellationToken.ThrowIfCancellationRequested();

        var parentInvocationId = brief.ParentInvocationId;

        var (planTerminal, plan) = await RunPlannerAsync(
                ComposePlanningQuestion(brief, null), parentInvocationId, "Planner", cancellationToken)
            .ConfigureAwait(false);
        if (planTerminal is not null)
            return planTerminal;

        var documentationRepairBudget = _maxDocumentationRepairAttempts;
        var codeRepairBudget = _maxCodeRepairAttempts;
        var tenetRepairBudget = _maxTenetRepairAttempts;
        var replanBudget = _maxReplanAttempts;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (documentTerminal, documentChanges) = await RunDocumentAuthorAsync(
                    ComposeDocumentInstruction(brief, plan), parentInvocationId, "DocumentAuthor", cancellationToken)
                .ConfigureAwait(false);
            if (documentTerminal is not null)
                return documentTerminal;

            var (developerTerminal, initialCode) = await RunDeveloperAsync(
                    ComposeCodeInstruction(brief, plan, documentChanges!), parentInvocationId, "Developer", cancellationToken,
                    documentChanges)
                .ConfigureAwait(false);
            if (developerTerminal is not null)
                return developerTerminal;

            var documentation = documentChanges!;
            var code = initialCode!;

            var needsReplan = false;
            IReadOnlyList<string> replanFixes = [];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var buildCheck = await _buildCheck
                    .RunAsync("build.ps1 check", _buildScript, null, cancellationToken, brief.ChangedFileHints)
                    .ConfigureAwait(false);
                RecordStep(parentInvocationId, "DeterministicCheck:build.ps1", buildCheck.Outcome);

                var contractCheck = await _contractCheck
                    .RunAsync("check-contracts -Strict check", WorkerHelpers.ContractCheckScript, null, cancellationToken, brief.ChangedFileHints)
                    .ConfigureAwait(false);
                RecordStep(parentInvocationId, "DeterministicCheck:check-contracts", contractCheck.Outcome);

                List<CheckFinding> evidence = [];
                if (buildCheck.Finding is not null)
                    evidence.Add(buildCheck.Finding);
                if (contractCheck.Finding is not null)
                    evidence.Add(contractCheck.Finding);

                var verified = await _verifier
                    .VerifyAsync(VerificationIntent.ContractConformance, evidence, VerifierQuestion, cancellationToken)
                    .ConfigureAwait(false);
                RecordStep(parentInvocationId, "Verifier", verified.Outcome);

                if (verified.Outcome == OperationOutcome.Succeeded &&
                    verified.Finding?.Verdict == VerificationVerdict.Passed)
                    return new WorkerExecutionResult(
                        OperationOutcome.Succeeded, new WorkerRunResult.Completed(WorkerHelpers.Merge(documentation, code)), null, []);

                if (verified.Outcome == OperationOutcome.Escalated)
                    return new WorkerExecutionResult(
                        OperationOutcome.Succeeded,
                        new WorkerRunResult.Reroute(WorkerHelpers.RerouteReason(verified.Finding), [.. verified.Finding?.AdvisoryNotes ?? []], null),
                        null,
                        []);

                if (verified.Outcome == OperationOutcome.Refused)
                    return new WorkerExecutionResult(
                        OperationOutcome.Failed, null, WorkerHelpers.MergeInterrupted(documentation, code), verified.Notes);

                var verdict = verified.Finding?.Verdict;
                var concerns = verified.Finding?.Concerns ?? [];
                var documentationFixes = concerns
                    .Where(concern => concern.Owner == VerificationOwner.Documentation)
                    .Select(concern => concern.FixText)
                    .ToList();
                var codeFixes = concerns
                    .Where(concern => concern.Owner == VerificationOwner.Code)
                    .Select(concern => concern.FixText)
                    .ToList();
                var tenetFixes = concerns
                    .Where(concern => concern.Owner == VerificationOwner.Tenet)
                    .Select(concern => concern.FixText)
                    .ToList();

                var needsDocumentationRepair =
                    verdict == VerificationVerdict.RepairRequired && documentationFixes.Count > 0;
                var needsCodeRepair =
                    verdict == VerificationVerdict.RepairRequired && codeFixes.Count > 0;
                var needsTenetRepair =
                    verdict == VerificationVerdict.RepairRequired && tenetFixes.Count > 0;

                if (needsDocumentationRepair)
                {
                    if (documentationRepairBudget <= 0)
                        return new WorkerExecutionResult(
                            OperationOutcome.Failed,
                            null,
                            WorkerHelpers.MergeInterrupted(documentation, code),
                            [new ProcessNote(
                                "the documentation-repair budget was already spent when another documentation finding arrived")]);

                    documentationRepairBudget--;

                    var (documentRepairTerminal, repairedDocument) = await RunDocumentAuthorAsync(
                            WorkerHelpers.ComposeRepairInstruction(ComposeDocumentInstruction(brief, plan), documentationFixes),
                            parentInvocationId,
                            "DocumentAuthor:repair",
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (documentRepairTerminal is not null)
                        return documentRepairTerminal with
                        {
                            Interrupted = documentRepairTerminal.Interrupted ?? WorkerHelpers.MergeInterrupted(documentation, code)
                        };
                    documentation = repairedDocument!;

                    // No primitive reports whether a documentation repair altered an obligation the code must now
                    // satisfy, so the code pass is re-run unconditionally to stay in sync rather than guessed at -
                    // the same reasoning ContractChangeWorker already uses for this resync. This re-run is not
                    // spent from the code-repair budget: it is necessitated by the documentation repair, not a
                    // separate finding the verifier raised.
                    var (resyncTerminal, resyncCode) = await RunDeveloperAsync(
                            ComposeCodeInstruction(brief, plan, documentation),
                            parentInvocationId,
                            "Developer:resync",
                            cancellationToken,
                            documentation)
                        .ConfigureAwait(false);
                    if (resyncTerminal is not null)
                        return resyncTerminal with
                        {
                            Interrupted = resyncTerminal.Interrupted ?? WorkerHelpers.MergeInterrupted(documentation, code)
                        };
                    code = resyncCode!;

                    continue;
                }

                if (needsCodeRepair)
                {
                    if (codeRepairBudget <= 0)
                        return new WorkerExecutionResult(
                            OperationOutcome.Failed,
                            null,
                            WorkerHelpers.MergeInterrupted(documentation, code),
                            [new ProcessNote(
                                "the code-repair budget was already spent when another code finding arrived")]);

                    codeRepairBudget--;

                    var (codeRepairTerminal, repairedCode) = await RunDeveloperAsync(
                            WorkerHelpers.ComposeRepairInstruction(ComposeCodeInstruction(brief, plan, documentation), codeFixes),
                            parentInvocationId,
                            "Developer:repair",
                            cancellationToken,
                            documentation)
                        .ConfigureAwait(false);
                    if (codeRepairTerminal is not null)
                        return codeRepairTerminal with
                        {
                            Interrupted = codeRepairTerminal.Interrupted ?? WorkerHelpers.MergeInterrupted(documentation, code)
                        };
                    code = repairedCode!;

                    continue;
                }

                if (needsTenetRepair)
                {
                    if (tenetRepairBudget <= 0)
                        return new WorkerExecutionResult(
                            OperationOutcome.Failed,
                            null,
                            WorkerHelpers.MergeInterrupted(documentation, code),
                            [new ProcessNote(
                                "the tenet-repair budget was already spent when another tenet finding arrived")]);

                    tenetRepairBudget--;

                    // A tenet finding repairs through Developer, the same primitive a code finding uses: fixing a
                    // tenet violation means changing code or configuration to conform to .anneal/work/constraints.md and the
                    // affected contracts, which is still a code-shaped fix, and no separate "tenet author"
                    // primitive exists - see the Apply Report's judgment call.
                    var (tenetRepairTerminal, repairedTenetCode) = await RunDeveloperAsync(
                            WorkerHelpers.ComposeRepairInstruction(ComposeCodeInstruction(brief, plan, documentation), tenetFixes),
                            parentInvocationId,
                            "Developer:tenet-repair",
                            cancellationToken,
                            documentation)
                        .ConfigureAwait(false);
                    if (tenetRepairTerminal is not null)
                        return tenetRepairTerminal with
                        {
                            Interrupted = tenetRepairTerminal.Interrupted ?? WorkerHelpers.MergeInterrupted(documentation, code)
                        };
                    code = repairedTenetCode!;

                    continue;
                }

                // Judgment call (see the Apply Report): a `RepairRequired` verdict whose concerns name none of
                // Documentation, Code, or Tenet stands in for the old `StrategyRevisionRequired` verdict - the
                // plan's own decomposition was wrong, not its execution - since the verifier was instructed to
                // report this with an empty concerns list and its reasoning in the advisory notes instead of a
                // concern owned by one of the three owners.
                if (verdict == VerificationVerdict.RepairRequired && concerns.Count == 0)
                {
                    needsReplan = true;
                    replanFixes = verified.Finding?.AdvisoryNotes ?? [];
                    break;
                }

                // Every named verdict is handled above; an unnamed one is treated as a blocking failure rather
                // than silently passing.
                return new WorkerExecutionResult(
                    OperationOutcome.Failed, null, WorkerHelpers.MergeInterrupted(documentation, code), verified.Notes);
            }

            // The inner loop above only falls through to here via the replan break above - every other path
            // returns directly.
            if (!needsReplan)
                return new WorkerExecutionResult(OperationOutcome.Failed, null, null, []);

            if (replanBudget <= 0)
                return new WorkerExecutionResult(
                    OperationOutcome.Failed,
                    null,
                    null,
                    [new ProcessNote(
                        "the re-plan budget was already spent when another strategy-revision finding arrived")]);

            replanBudget--;

            var (replanTerminal, revisedPlan) = await RunPlannerAsync(
                    ComposePlanningQuestion(brief, replanFixes), parentInvocationId, "Planner:replan", cancellationToken)
                .ConfigureAwait(false);
            if (replanTerminal is not null)
                return replanTerminal;

            // No budget resets afterward: documentationRepairBudget, codeRepairBudget, and tenetRepairBudget
            // carry whatever they had left across this re-plan, per .anneal/work/active-plan.md's S9 entry.
            plan = revisedPlan;
        }
    }

    private async Task<(WorkerExecutionResult? Terminal, ImplementationPlan? Plan)> RunPlannerAsync(
        string question, string parentInvocationId, string stepName, CancellationToken cancellationToken)
    {
        var result = await _planner.PlanAsync(question, [], cancellationToken).ConfigureAwait(false);
        RecordStep(parentInvocationId, stepName, result.Outcome);

        if (result.Outcome != OperationOutcome.Succeeded || result.Finding is null)
            return (new WorkerExecutionResult(OperationOutcome.Failed, null, null, result.Notes), null);

        return result.Finding switch
        {
            PlanningDecision.Plan plan => (null, plan.Steps),
            PlanningDecision.DirectExecutionIsBetter => (null, null),
            PlanningDecision.Reroute reroute =>
                (new WorkerExecutionResult(
                    OperationOutcome.Succeeded, new WorkerRunResult.Reroute(reroute.Why, [], null), null, []),
                    null),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Finding, "Unknown planning decision.")
        };
    }

    private async Task<(WorkerExecutionResult? Terminal, DocumentChangeSet? Changes)> RunDocumentAuthorAsync(
        string instruction, string parentInvocationId, string stepName, CancellationToken cancellationToken)
    {
        var result = await _documentAuthor.AuthorAsync(instruction, cancellationToken).ConfigureAwait(false);
        RecordStep(parentInvocationId, stepName, result.Outcome);

        if (result.Outcome == OperationOutcome.Escalated)
        {
            // Preserve the underlying Authored finding when DocumentAuthor reports Escalated with one — it wrote
            // files before the protected-path refusal stopped it, and the caller needs to see those files.
            var authored = result.Finding as DocumentAuthoringResult.Authored;
            ChangeSetBeforeStopping? interrupted = authored is not null
                ? new ChangeSetBeforeStopping(authored.Changes.FilesChanged, authored.Changes.Summary)
                : null;
            return (new WorkerExecutionResult(OperationOutcome.Escalated, null, interrupted, result.Notes), null);
        }

        if (result.Outcome != OperationOutcome.Succeeded || result.Finding is null)
            return (new WorkerExecutionResult(result.Outcome, null, null, result.Notes), null);

        if (result.Finding is DocumentAuthoringResult.Reroute reroute)
            return (
                new WorkerExecutionResult(
                    OperationOutcome.Succeeded, new WorkerRunResult.Reroute(reroute.Why, [], null), null, []),
                null);

        return (null, ((DocumentAuthoringResult.Authored)result.Finding).Changes);
    }

    private async Task<(WorkerExecutionResult? Terminal, ChangeSetSummary? Changes)> RunDeveloperAsync(
        string instruction, string parentInvocationId, string stepName, CancellationToken cancellationToken,
        DocumentChangeSet? priorDocumentation = null)
    {
        var result = await _developer.DevelopAsync(instruction, cancellationToken).ConfigureAwait(false);
        RecordStep(parentInvocationId, stepName, result.Outcome);

        if (result.Outcome == OperationOutcome.Escalated)
        {
            // Preserve the underlying Completed finding when Developer reports Escalated with one — it wrote
            // files before the protected-path refusal stopped it. Merge with any prior documentation state so
            // the caller sees the full interrupted working-tree picture.
            var devCompleted = result.Finding as DevelopmentResult.Completed;
            ChangeSetBeforeStopping? interrupted = devCompleted is not null
                ? priorDocumentation is not null
                    ? new ChangeSetBeforeStopping(
                        [.. priorDocumentation.FilesChanged, .. devCompleted.Summary.FilesChanged],
                        $"{priorDocumentation.Summary} {devCompleted.Summary.Summary}".Trim())
                    : new ChangeSetBeforeStopping(devCompleted.Summary.FilesChanged, devCompleted.Summary.Summary)
                : priorDocumentation is not null
                    ? new ChangeSetBeforeStopping(priorDocumentation.FilesChanged, priorDocumentation.Summary)
                    : null;
            return (new WorkerExecutionResult(OperationOutcome.Escalated, null, interrupted, result.Notes), null);
        }

        if (result.Outcome != OperationOutcome.Succeeded || result.Finding is null)
            return (new WorkerExecutionResult(result.Outcome, null, null, result.Notes), null);

        if (result.Finding is DevelopmentResult.Reroute reroute)
            return (
                new WorkerExecutionResult(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Reroute(reroute.Why, [], reroute.SuggestedWorker), null, []),
                null);

        return (null, ((DevelopmentResult.Completed)result.Finding).Summary);
    }

    private void RecordStep(string parentInvocationId, string step, OperationOutcome outcome) =>
        _recordStore?.Append(
            new ProcessStepRecord(DateTimeOffset.UtcNow, parentInvocationId, step, outcome.ToString(), null, null));

    private string ComposePlanningQuestion(WorkerBrief brief, IReadOnlyList<string>? priorFindings) =>
        priorFindings is null or []
            ? $"""
               {brief.OriginalWorkItem}

               Decide whether this structural change needs a plan across which systems and tree levels change,
               whether a node is created or pruned, and what code follows - or whether it does not belong to a
               structural worker at all.

               Why this worker was selected: {brief.ScopeHint}

               <research-findings>
               {brief.RenderResearch()}
               </research-findings>

               <prior-reroutes>
               {brief.RenderReroutes()}
               </prior-reroutes>

               <standards>
               {WorkerStandards.Render(_repositoryRoot, PlannerStandards)}
               </standards>
               """
            : $"""
               {brief.OriginalWorkItem}

               A prior plan for this structural change was followed, but verification concluded the plan's own
               decomposition was wrong, not its execution. Produce a revised plan that fixes what the previous
               attempt got wrong:
               {string.Join("\n", priorFindings)}

               Why this worker was selected: {brief.ScopeHint}

               <standards>
               {WorkerStandards.Render(_repositoryRoot, PlannerStandards)}
               </standards>
               """;

    private string ComposeDocumentInstruction(WorkerBrief brief, ImplementationPlan? plan) =>
        $"""
         {brief.OriginalWorkItem}

         Update every system contract document and section document under .anneal/architecture/ this structural
         change affects - creating or pruning a node where the change requires it. Do not touch code or tests.

         {RenderPlan(plan)}

         Why this worker was selected: {brief.ScopeHint}

         <research-findings>
         {brief.RenderResearch()}
         </research-findings>

         <prior-reroutes>
         {brief.RenderReroutes()}
         </prior-reroutes>

         <standards>
         {WorkerStandards.Render(_repositoryRoot, WorkerHelpers.DocumentAuthorStandards)}
         </standards>

         <skills>
         {WorkerSkills.Render(_repositoryRoot, brief.OriginalWorkItem, brief.ChangedFileHints)}
         </skills>
         """;

    private string ComposeCodeInstruction(WorkerBrief brief, ImplementationPlan? plan, DocumentChangeSet documentChanges) =>
        $"""
         {brief.OriginalWorkItem}

         Implement code and tests against the tree this structural change just updated:
         {documentChanges.Summary}

         Files the documentation pass touched: {(documentChanges.FilesChanged.Count == 0 ? "none" : string.Join(", ", documentChanges.FilesChanged))}

         {RenderPlan(plan)}

         Why this worker was selected: {brief.ScopeHint}

         <research-findings>
         {brief.RenderResearch()}
         </research-findings>

         <standards>
         {WorkerStandards.Render(_repositoryRoot, WorkerHelpers.DeveloperStandards)}
         </standards>

         <skills>
         {WorkerSkills.Render(_repositoryRoot, brief.OriginalWorkItem, brief.ChangedFileHints)}
         </skills>
         """;

    private static string RenderPlan(ImplementationPlan? plan) =>
        plan is null
            ? "No plan was produced for this change; work directly from the brief above."
            : $"""
               Follow this plan: {plan.Summary}
               {string.Join("\n", plan.Steps.Select(step => $"- {step}"))}
               """;
}
