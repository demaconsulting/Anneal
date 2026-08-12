using DemaConsulting.Anneal.Toolkit.Architecture;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     Capability-complete Effort-parameterized worker: it may plan, author contract and architecture
///     documentation, edit code and tests, and then fire only the heavier obligations the actual diff proves were
///     needed.
/// </summary>
/// <remarks>
///     The worker is deliberately shaped as one Effort-parameterized pipeline: a deterministic preflight selector
///     decides whether a plan and/or documentation-first pass runs before code, and a deterministic postflight
///     selector decides which heavier checks the actual diff warrants. Effort tunes repair budgets and the initial
///     producing-step model-tier suggestion; it does not fork the control flow.
///     <para>
///         Fail-closed posture: when the postflight diff cannot be read, or the patch is present but its touched-file
///         list cannot be parsed, the worker escalates rather than silently concluding no heavier obligation applied.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
internal sealed class GeneralWorker
{
    private const string VerifierQuestionBase =
        """
        Judge whether this change satisfies the requested work, conforms to every contract clause it touches, and
        leaves .anneal/architecture/ accurate for what was actually built.
        Using the diffEvidence you already have, consider whether any deletions or rewrites inside an
        otherwise-in-scope, already-existing architecture document look disproportionate to or disconnected from the
        declared task — for example, a whole-file overwrite that removes a Decisions section or other unrelated prose
        the task never asked to revise. Report such a finding as a concern owned by Documentation.
        Report the verdict 'RepairRequired' with one concern per fix needed, each owned by Documentation, Code, or
        Tenet, or 'Passed' when nothing needs fixing. Report the verdict 'RerouteRequired', with your reasoning in
        the advisory notes, only when the change still needs a higher-order human decision — for example a migration-
        scale re-cut of boundaries that should not be settled inside one worker run.
        """;

    private readonly string? _buildScript;
    private readonly string _repositoryRoot;
    private readonly Effort _effort;
    private readonly string _plannerCharter;
    private readonly string _documentAuthorCharter;
    private readonly string _developerCharter;
    private readonly DeterministicCheck _buildCheck;
    private readonly DeterministicCheck _contractCheck;
    private readonly DiffCheck _diffCheck;
    private readonly Verifier _verifier;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunGitCommand? _runGit;
    private readonly EffortProfile _effortProfile;
    private readonly int _documentAuthorTargetFileCountBudget;
    private readonly int _maxPlanSteps;
    private readonly RecordStore? _recordStore;
    private readonly GeneralWorkerPreflightBehavior _preflightBehavior;
    private readonly bool _runArchDocAgreementGate;

    public GeneralWorker(
        string repositoryRoot,
        Effort effort,
        string plannerCharter,
        string documentAuthorCharter,
        string developerCharter,
        string verifierCharter,
        int? maxDocumentationRepairAttempts = null,
        int? maxCodeRepairAttempts = null,
        int? maxTenetRepairAttempts = null,
        int documentAuthorTargetFileCountBudget = 8,
        int maxPlanSteps = 12,
        GeneralWorkerPreflightBehavior preflightBehavior = GeneralWorkerPreflightBehavior.Automatic,
        bool runArchDocAgreementGate = true,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? buildRunScript = null,
        RunRepositoryScript? contractCheckRunScript = null,
        RunGitCommand? runGit = null,
        RecordStore? recordStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(plannerCharter);
        ArgumentNullException.ThrowIfNull(documentAuthorCharter);
        ArgumentNullException.ThrowIfNull(developerCharter);
        ArgumentNullException.ThrowIfNull(verifierCharter);
        if (maxDocumentationRepairAttempts.HasValue)
            ArgumentOutOfRangeException.ThrowIfNegative(maxDocumentationRepairAttempts.Value);
        if (maxCodeRepairAttempts.HasValue)
            ArgumentOutOfRangeException.ThrowIfNegative(maxCodeRepairAttempts.Value);
        if (maxTenetRepairAttempts.HasValue)
            ArgumentOutOfRangeException.ThrowIfNegative(maxTenetRepairAttempts.Value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(documentAuthorTargetFileCountBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPlanSteps);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _effort = effort;
        _plannerCharter = plannerCharter;
        _documentAuthorCharter = documentAuthorCharter;
        _developerCharter = developerCharter;
        _buildCheck = new DeterministicCheck(_repositoryRoot, runScript: buildRunScript);
        _contractCheck = new DeterministicCheck(
            _repositoryRoot,
            runScript: contractCheckRunScript ?? ((_, ct) => ContractCheckRunner.RunAsync(_repositoryRoot, ct, strict: false)));
        _diffCheck = new DiffCheck(_repositoryRoot, runGit: runGit);
        _verifier = new Verifier(_repositoryRoot, verifierCharter, endpointFor: endpointFor);
        _endpointFor = endpointFor;
        _runGit = runGit;
        _effortProfile = CreateEffortProfile(
            effort,
            maxDocumentationRepairAttempts,
            maxCodeRepairAttempts,
            maxTenetRepairAttempts);
        _documentAuthorTargetFileCountBudget = documentAuthorTargetFileCountBudget;
        _maxPlanSteps = maxPlanSteps;
        _recordStore = recordStore;
        _preflightBehavior = preflightBehavior;
        _runArchDocAgreementGate = runArchDocAgreementGate;
        _buildScript = ScriptConfiguration.Load(_repositoryRoot).Build;
    }

    public async Task<WorkerExecutionResult> RunAsync(WorkerBrief brief, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        cancellationToken.ThrowIfCancellationRequested();

        var parentInvocationId = brief.ParentInvocationId;
        var preflight = SelectPreflightObligations(brief);
        RecordStep(parentInvocationId, $"Preflight:{preflight.StepName}", OperationOutcome.Succeeded);

        ImplementationPlan? plan = null;
        var documentationRole = preflight.SuggestedRoles.DocumentAuthorRole;
        var codeRole = preflight.SuggestedRoles.DeveloperRole;
        var tenetRole = preflight.SuggestedRoles.DeveloperRole;
        if (preflight.NeedsPlan)
        {
            var (planTerminal, selectedPlan) = await RunPlannerAsync(
                    ComposePlanningQuestion(brief, preflight),
                    preflight.SuggestedRoles.PlannerRole,
                    parentInvocationId,
                    "Planner",
                    cancellationToken)
                .ConfigureAwait(false);
            if (planTerminal is not null)
                return planTerminal;

            plan = selectedPlan;
        }

        DocumentChangeSet? documentation = null;
        if (preflight.NeedsDocumentationFirst)
        {
            var (documentTerminal, documentChanges) = await RunDocumentAuthorAsync(
                    ComposeDocumentInstruction(brief, plan, preflight),
                    documentationRole,
                    parentInvocationId,
                    "DocumentAuthor",
                    cancellationToken)
                .ConfigureAwait(false);
            if (documentTerminal is not null)
                return documentTerminal;

            documentation = documentChanges!;
        }

        var (developerTerminal, initialCode) = await RunDeveloperAsync(
                ComposeCodeInstruction(brief, plan, documentation, preflight),
                codeRole,
                parentInvocationId,
                "Developer",
                cancellationToken,
                documentation)
            .ConfigureAwait(false);
        if (developerTerminal is not null)
            return developerTerminal;

        var code = initialCode!;
        var documentationRepairBudget = _effortProfile.DocumentationRepairBudget;
        var codeRepairBudget = _effortProfile.CodeRepairBudget;
        var tenetRepairBudget = _effortProfile.TenetRepairBudget;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var buildCheck = await _buildCheck
                .RunAsync("build.ps1 check", _buildScript, null, cancellationToken, brief.ChangedFileHints)
                .ConfigureAwait(false);
            RecordStep(parentInvocationId, "DeterministicCheck:build.ps1", buildCheck.Outcome);

            var diffResult = await _diffCheck.RunAsync(null, cancellationToken).ConfigureAwait(false);
            RecordStep(parentInvocationId, "DiffCheck", diffResult.Outcome);

            if (diffResult.Outcome != OperationOutcome.Succeeded || diffResult.Finding is not { Available: true } observedDiff)
                return new WorkerExecutionResult(
                    OperationOutcome.Escalated,
                    null,
                    ComposeInterrupted(documentation, code),
                    [new ProcessNote("the postflight diff could not be read, so required obligations could not be classified honestly")]);

            var diff = DiffCheck.ExcludingAnnealBookkeeping(observedDiff);
            var assessment = AssessPostflight(diff);
            if (assessment.HasAmbiguousDiffSurface)
                return new WorkerExecutionResult(
                    OperationOutcome.Escalated,
                    null,
                    ComposeInterrupted(documentation, code),
                    [new ProcessNote(
                        "the postflight diff contained edits but no parseable changed-file headers, so obligations could not be classified honestly")]);

            var dangerousProtectedPath = FindDangerousProtectedPath(diff.ChangedFiles);
            if (dangerousProtectedPath is not null)
                return new WorkerExecutionResult(
                    OperationOutcome.Escalated,
                    null,
                    ComposeInterrupted(documentation, code),
                    [new ProcessNote(
                        $"the actual change touched protected path '{dangerousProtectedPath}', which needs your approval")]);

            List<ProcessNote> gateNotes = [];
            IReadOnlyList<string> gateCorrectedFiles = [];
            if (_runArchDocAgreementGate && assessment.RunArchDocAgreement)
            {
                var output = new StringWriter();
                var gate = new ArchDocAgreementGate(_repositoryRoot, endpointFor: _endpointFor, runGit: _runGit);
                var gateOutcome = await gate.RunAsync(output, "general-worker", cancellationToken).ConfigureAwait(false);
                RecordStep(parentInvocationId, "ArchDocAgreementGate", OperationOutcome.Succeeded);
                gateCorrectedFiles = gateOutcome.CorrectedDocuments;
                gateNotes.AddRange(ReadNotes(output));

                if (gateCorrectedFiles.Count > 0 || gateOutcome.Findings.Count > 0)
                {
                    var refreshedDiff = await _diffCheck.RunAsync(null, cancellationToken).ConfigureAwait(false);
                    RecordStep(parentInvocationId, "DiffCheck:after-arch-gate", refreshedDiff.Outcome);
                    if (refreshedDiff.Outcome != OperationOutcome.Succeeded ||
                        refreshedDiff.Finding is not { Available: true } refreshed)
                        return new WorkerExecutionResult(
                            OperationOutcome.Escalated,
                            null,
                            ComposeInterrupted(documentation, code),
                            [.. gateNotes, new ProcessNote("the diff after the architecture-agreement obligation could not be read honestly")]);

                    diff = DiffCheck.ExcludingAnnealBookkeeping(refreshed);
                }
            }

            List<CheckFinding> evidence = [];
            if (buildCheck.Finding is not null)
                evidence.Add(buildCheck.Finding);

            if (assessment.RunContractCheck)
            {
                var contractCheck = await _contractCheck
                    .RunAsync("check-contracts check", WorkerHelpers.ContractCheckScript, null, cancellationToken, brief.ChangedFileHints)
                    .ConfigureAwait(false);
                RecordStep(parentInvocationId, "DeterministicCheck:check-contracts", contractCheck.Outcome);
                if (contractCheck.Finding is not null)
                    evidence.Add(contractCheck.Finding);
            }

            StepResult<VerificationFinding> verified;
            if (assessment.SkipVerifier)
            {
                verified = TryVerifyDeterministically(evidence) ??
                    new StepResult<VerificationFinding>(
                        OperationOutcome.Succeeded,
                        new VerificationFinding
                        {
                            Verdict = VerificationVerdict.Passed,
                            Concerns = [],
                            AdvisoryNotes = [],
                            EvidenceSufficient = true
                        },
                        []);
                RecordStep(
                    parentInvocationId,
                    verified.Outcome == OperationOutcome.Succeeded ? "Verifier:skipped" : "Verifier:deterministic-only",
                    verified.Outcome);
            }
            else
            {
                verified = await _verifier
                    .VerifyAsync(
                        VerificationIntent.ContractConformance,
                        evidence,
                        assessment.RunTenetCheck ? VerifierQuestionBase + brief.RenderTenetSection() : VerifierQuestionBase,
                        cancellationToken,
                        diff)
                    .ConfigureAwait(false);
                RecordStep(parentInvocationId, "Verifier", verified.Outcome);
            }

            if (verified.Outcome == OperationOutcome.Succeeded &&
                verified.Finding?.Verdict == VerificationVerdict.Passed)
                return new WorkerExecutionResult(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Completed(ComposeSummary(documentation, code, gateCorrectedFiles)),
                    null,
                    gateNotes);

            if (verified.Outcome == OperationOutcome.Escalated)
                return new WorkerExecutionResult(
                    OperationOutcome.Escalated,
                    null,
                    ComposeInterrupted(documentation, code),
                    [.. gateNotes, .. verified.Notes, .. verified.Finding?.AdvisoryNotes.Select(note => new ProcessNote(note)) ?? []]);

            if (verified.Outcome == OperationOutcome.Refused)
                return new WorkerExecutionResult(
                    OperationOutcome.Failed,
                    null,
                    ComposeInterrupted(documentation, code),
                    [.. gateNotes, .. verified.Notes]);

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
                        ComposeInterrupted(documentation, code),
                        [.. gateNotes, new ProcessNote(
                            "the documentation-repair budget was already spent when another documentation finding arrived")]);

                documentationRepairBudget--;
                documentationRole = EscalateRole(documentationRole);

                var (documentRepairTerminal, repairedDocument) = await RunDocumentAuthorAsync(
                        WorkerHelpers.ComposeRepairInstruction(
                            ComposeDocumentInstruction(brief, plan, preflight),
                            documentationFixes),
                        documentationRole,
                        parentInvocationId,
                        "DocumentAuthor:repair",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (documentRepairTerminal is not null)
                    return documentRepairTerminal with
                    {
                        Interrupted = documentRepairTerminal.Interrupted ?? ComposeInterrupted(documentation, code)
                    };
                documentation = repairedDocument!;

                var (resyncTerminal, resyncCode) = await RunDeveloperAsync(
                        ComposeCodeInstruction(brief, plan, documentation, preflight),
                        codeRole,
                        parentInvocationId,
                        "Developer:resync",
                        cancellationToken,
                        documentation)
                    .ConfigureAwait(false);
                if (resyncTerminal is not null)
                    return resyncTerminal with
                    {
                        Interrupted = resyncTerminal.Interrupted ?? ComposeInterrupted(documentation, code)
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
                        ComposeInterrupted(documentation, code),
                        [.. gateNotes, new ProcessNote(
                            "the code-repair budget was already spent when another code finding arrived")]);

                codeRepairBudget--;
                codeRole = EscalateRole(codeRole);

                var (codeRepairTerminal, repairedCode) = await RunDeveloperAsync(
                        WorkerHelpers.ComposeRepairInstruction(
                            ComposeCodeInstruction(brief, plan, documentation, preflight),
                            codeFixes),
                        codeRole,
                        parentInvocationId,
                        "Developer:repair",
                        cancellationToken,
                        documentation)
                    .ConfigureAwait(false);
                if (codeRepairTerminal is not null)
                    return codeRepairTerminal with
                    {
                        Interrupted = codeRepairTerminal.Interrupted ?? ComposeInterrupted(documentation, code)
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
                        ComposeInterrupted(documentation, code),
                        [.. gateNotes, new ProcessNote(
                            "the tenet-repair budget was already spent when another tenet finding arrived")]);

                tenetRepairBudget--;
                tenetRole = EscalateRole(tenetRole);

                var (tenetRepairTerminal, repairedTenetCode) = await RunDeveloperAsync(
                        WorkerHelpers.ComposeRepairInstruction(
                            ComposeCodeInstruction(brief, plan, documentation, preflight),
                            tenetFixes),
                        tenetRole,
                        parentInvocationId,
                        "Developer:tenet-repair",
                        cancellationToken,
                        documentation)
                    .ConfigureAwait(false);
                if (tenetRepairTerminal is not null)
                    return tenetRepairTerminal with
                    {
                        Interrupted = tenetRepairTerminal.Interrupted ?? ComposeInterrupted(documentation, code)
                    };
                code = repairedTenetCode!;

                continue;
            }

            return new WorkerExecutionResult(
                OperationOutcome.Failed,
                null,
                ComposeInterrupted(documentation, code),
                [.. gateNotes, .. verified.Notes]);
        }
    }

    private async Task<(WorkerExecutionResult? Terminal, ImplementationPlan? Plan)> RunPlannerAsync(
        string question,
        ModelRole role,
        string parentInvocationId,
        string stepName,
        CancellationToken cancellationToken)
    {
        var result = await CreatePlanner(role).PlanAsync(question, [], cancellationToken).ConfigureAwait(false);
        RecordStep(parentInvocationId, stepName, result.Outcome);

        if (result.Outcome != OperationOutcome.Succeeded || result.Finding is null)
            return (new WorkerExecutionResult(result.Outcome == OperationOutcome.Refused ? OperationOutcome.Failed : result.Outcome, null, null, result.Notes), null);

        return result.Finding switch
        {
            PlanningDecision.Plan plan => (null, plan.Steps),
            PlanningDecision.DirectExecutionIsBetter => (null, null),
            PlanningDecision.Reroute reroute =>
                (new WorkerExecutionResult(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Reroute(reroute.Why, [], null),
                    null,
                    []),
                    null),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Finding, "Unknown planning decision.")
        };
    }

    private async Task<(WorkerExecutionResult? Terminal, DocumentChangeSet? Changes)> RunDocumentAuthorAsync(
        string instruction,
        ModelRole role,
        string parentInvocationId,
        string stepName,
        CancellationToken cancellationToken)
    {
        var result = await CreateDocumentAuthor(role).AuthorAsync(instruction, cancellationToken).ConfigureAwait(false);
        RecordStep(parentInvocationId, stepName, result.Outcome);

        if (result.Outcome == OperationOutcome.Escalated)
        {
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
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Reroute(reroute.Why, [], null),
                    null,
                    []),
                null);

        return (null, ((DocumentAuthoringResult.Authored)result.Finding).Changes);
    }

    private async Task<(WorkerExecutionResult? Terminal, ChangeSetSummary? Changes)> RunDeveloperAsync(
        string instruction,
        ModelRole role,
        string parentInvocationId,
        string stepName,
        CancellationToken cancellationToken,
        DocumentChangeSet? priorDocumentation = null)
    {
        var result = await CreateDeveloper(role).DevelopAsync(instruction, cancellationToken).ConfigureAwait(false);
        RecordStep(parentInvocationId, stepName, result.Outcome);

        if (result.Outcome == OperationOutcome.Escalated)
        {
            var completed = result.Finding as DevelopmentResult.Completed;
            ChangeSetBeforeStopping? interrupted = completed is not null
                ? ComposeInterrupted(
                    priorDocumentation,
                    new ChangeSetSummary(completed.Summary.FilesChanged, completed.Summary.Summary))
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
                    new WorkerRunResult.Reroute(reroute.Why, [], reroute.SuggestedWorker),
                    null,
                    []),
                null);

        return (null, ((DevelopmentResult.Completed)result.Finding).Summary);
    }

    private void RecordStep(string parentInvocationId, string step, OperationOutcome outcome) =>
        _recordStore?.Append(
            new ProcessStepRecord(DateTimeOffset.UtcNow, parentInvocationId, step, outcome.ToString(), null, null));

    private Planner CreatePlanner(ModelRole role) =>
        new(_repositoryRoot, _plannerCharter, maxPlanSteps: _maxPlanSteps, role: role, endpointFor: _endpointFor);

    private DocumentAuthor CreateDocumentAuthor(ModelRole role) =>
        new(
            _repositoryRoot,
            _documentAuthorCharter,
            role: role,
            targetFileCountBudget: _documentAuthorTargetFileCountBudget,
            scopeDriftCheckInterval: 1,
            endpointFor: _endpointFor,
            runGit: _runGit);

    private Developer CreateDeveloper(ModelRole role) =>
        new(_repositoryRoot, _developerCharter, role: role, endpointFor: _endpointFor, runGit: _runGit);

    private string ComposePlanningQuestion(WorkerBrief brief, PreflightObligationDecision preflight) =>
        $"""
         {brief.OriginalWorkItem}

         Decide whether this {_effort} general-worker run needs an explicit plan before authoring because the request
         frames a multi-system or architecture-shaping change, or whether direct execution is still better.

         Why the deterministic preflight asked for a plan: {preflight.Reason}

         Why this worker was selected: {brief.ScopeHint}

         <research-findings>
         {brief.RenderResearch()}
         </research-findings>

         <prior-reroutes>
         {brief.RenderReroutes()}
         </prior-reroutes>

         <standards>
         {WorkerStandards.Render(_repositoryRoot, "change-classification.md")}
         </standards>
         """;

    private string ComposeDocumentInstruction(
        WorkerBrief brief,
        ImplementationPlan? plan,
        PreflightObligationDecision preflight) =>
        $"""
         {brief.OriginalWorkItem}

         Author any contract-clause or architecture-document changes this {_effort} general-worker run needs under
         .anneal/architecture/. Do not touch code or tests in this pass.

         Why the deterministic preflight asked for documentation first: {preflight.Reason}

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

    private string ComposeCodeInstruction(
        WorkerBrief brief,
        ImplementationPlan? plan,
        DocumentChangeSet? documentChanges,
        PreflightObligationDecision preflight)
    {
        var documentationContext = documentChanges is null
            ? "No documentation-first pass ran before this code pass. If the implementation reveals a contract or architecture document still needs a narrow update, you may make it — keep it consistent with the request and any plan above."
            : $"""
               The documentation pass already updated:
               {documentChanges.Summary}

               Files the documentation pass touched: {(documentChanges.FilesChanged.Count == 0 ? "none" : string.Join(", ", documentChanges.FilesChanged))}
               """;

        return $"""
                {brief.OriginalWorkItem}

                Implement the change in code and tests for this {_effort} general-worker run, keeping any contract
                or architecture edits you make aligned with the request and the already-authored documentation when
                one exists.

                {documentationContext}

                Why the deterministic preflight chose this authoring shape: {preflight.Reason}

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
    }

    private static string RenderPlan(ImplementationPlan? plan) =>
        plan is null
            ? "No plan was produced for this change; work directly from the brief above."
            : $"""
               Follow this plan: {plan.Summary}
               {string.Join("\n", plan.Steps.Select(step => $"- {step}"))}
               """;

    private PreflightObligationDecision SelectPreflightObligations(WorkerBrief brief)
    {
        if (_preflightBehavior == GeneralWorkerPreflightBehavior.CodeOnly)
            return new PreflightObligationDecision(
                NeedsDocumentationFirst: false,
                NeedsPlan: false,
                Reason: "the caller fixed this run to code-only authoring before GeneralWorker started",
                StepName: "CodeOnly",
                SuggestedRoles: _effortProfile.SuggestedRoles);

        var subject = string.Join("\n", new[] { brief.OriginalWorkItem }.Concat(brief.ChangedFileHints)).ToLowerInvariant();
        var suggestedRoles = _effortProfile.SuggestedRoles;

        var namesArchitectureDoc = ContainsAny(
            subject,
            ".anneal/architecture/",
            "architecture doc",
            "architecture document",
            "system document",
            "overview.md",
            "route.md",
            "process.md");

        var namesContractSurface = ContainsAny(
            subject,
            "contract clause",
            "contract section",
            "## contract",
            "verified by",
            "system contract",
            "change the contract",
            "update the contract");

        var namesStructuralShape = ContainsAny(
            subject,
            "system boundary",
            "structural change",
            "split the system",
            "merge the system",
            "new system",
            "rename the system",
            "multi-system",
            "cross-system",
            "overview.md");

        if (namesStructuralShape)
            return new PreflightObligationDecision(
                NeedsDocumentationFirst: true,
                NeedsPlan: true,
                Reason: "the request framing already names a multi-system or architecture-shaping change",
                StepName: "PlanAndDocument",
                SuggestedRoles: suggestedRoles);

        if (namesArchitectureDoc || namesContractSurface)
            return new PreflightObligationDecision(
                NeedsDocumentationFirst: true,
                NeedsPlan: false,
                Reason: "the request framing already names a contract or architecture-document change",
                StepName: "Document",
                SuggestedRoles: suggestedRoles);

        return new PreflightObligationDecision(
            NeedsDocumentationFirst: false,
            NeedsPlan: false,
            Reason: "the request framing does not itself imply documentation-first or planning obligations",
            StepName: "CodeOnly",
            SuggestedRoles: suggestedRoles);
    }

    private static bool ContainsAny(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.Ordinal));

    private PostflightAssessment AssessPostflight(DiffFinding diff)
    {
        var substantiveDiff = DiffCheck.ExcludingAnnealBookkeeping(diff);
        var substantiveChangedFiles = substantiveDiff.ChangedFiles;

        var architectureChanges = substantiveChangedFiles
            .Where(path => path.StartsWith(".anneal/architecture/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var touchesContractSection = architectureChanges.Any(path =>
            ArchitectureCoverage.PatchTouchesContractSection(substantiveDiff.Patch, path));

        var docsOnlySurface = substantiveChangedFiles.Count > 0 &&
                              substantiveChangedFiles.All(IsDocumentationPath);
        var touchesNonDocumentationSurface = substantiveChangedFiles.Any(path => !IsDocumentationPath(path));

        return new PostflightAssessment(
            RunContractCheck: touchesContractSection,
            RunArchDocAgreement: architectureChanges.Count > 0 || touchesNonDocumentationSurface,
            RunTenetCheck: TouchesPublicSignature(substantiveDiff.Patch),
            HasAmbiguousDiffSurface: diff.Patch.Length > 0 && diff.ChangedFiles.Count == 0,
            SkipVerifier: docsOnlySurface && !touchesContractSection);
    }

    private static StepResult<VerificationFinding>? TryVerifyDeterministically(
        IReadOnlyList<CheckFinding> deterministicEvidence)
    {
        var failing = deterministicEvidence.Where(finding => !finding.Passed).ToList();
        if (failing.Count == 0)
            return null;

        var finding = new VerificationFinding
        {
            Verdict = VerificationVerdict.RepairRequired,
            Concerns =
                [.. failing.Select(check => new VerificationConcern
                {
                    Owner = VerificationOwner.Code,
                    FixText = $"{check.Name}: {check.Summary}"
                })],
            AdvisoryNotes = [],
            EvidenceSufficient = true
        };

        return new StepResult<VerificationFinding>(OperationOutcome.Failed, finding, []);
    }

    private static EffortProfile CreateEffortProfile(
        Effort effort,
        int? documentationRepairBudgetOverride,
        int? codeRepairBudgetOverride,
        int? tenetRepairBudgetOverride)
    {
        var defaults = effort switch
        {
            Effort.Small => new EffortProfile(
                DocumentationRepairBudget: 0,
                CodeRepairBudget: 1,
                TenetRepairBudget: 0,
                SuggestedRoles: new ProducedStepRoles(ModelRole.Light, ModelRole.Medium, ModelRole.Medium)),
            Effort.Medium => new EffortProfile(
                DocumentationRepairBudget: 1,
                CodeRepairBudget: 1,
                TenetRepairBudget: 0,
                SuggestedRoles: new ProducedStepRoles(ModelRole.Medium, ModelRole.Medium, ModelRole.Medium)),
            Effort.Large => new EffortProfile(
                DocumentationRepairBudget: 1,
                CodeRepairBudget: 1,
                TenetRepairBudget: 1,
                SuggestedRoles: new ProducedStepRoles(ModelRole.Medium, ModelRole.Heavy, ModelRole.Heavy)),
            _ => throw new NotSupportedException(
                $"{nameof(GeneralWorker)} does not execute {effort} work; decompose it before selecting this worker.")
        };

        return defaults with
        {
            DocumentationRepairBudget = documentationRepairBudgetOverride ?? defaults.DocumentationRepairBudget,
            CodeRepairBudget = codeRepairBudgetOverride ?? defaults.CodeRepairBudget,
            TenetRepairBudget = tenetRepairBudgetOverride ?? defaults.TenetRepairBudget
        };
    }

    private static ModelRole EscalateRole(ModelRole role) => role switch
    {
        ModelRole.Light => ModelRole.Medium,
        ModelRole.Medium => ModelRole.Heavy,
        _ => ModelRole.Heavy
    };

    private static bool IsDocumentationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith(".anneal/architecture/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TouchesPublicSignature(string patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
            return false;

        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || (line[0] != '+' && line[0] != '-'))
                continue;
            if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
                continue;

            var content = line[1..].TrimStart();
            if (!content.StartsWith("public ", StringComparison.Ordinal))
                continue;

            if (content.Contains('(') ||
                content.StartsWith("public class ", StringComparison.Ordinal) ||
                content.StartsWith("public interface ", StringComparison.Ordinal) ||
                content.StartsWith("public record ", StringComparison.Ordinal) ||
                content.StartsWith("public struct ", StringComparison.Ordinal) ||
                content.StartsWith("public enum ", StringComparison.Ordinal) ||
                content.StartsWith("public delegate ", StringComparison.Ordinal) ||
                (content.Contains(" { get;") && !content.StartsWith("public override ", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private static string? FindDangerousProtectedPath(IReadOnlyList<string> changedFiles)
    {
        var dangerous = changedFiles
            .Where(path => !path.StartsWith(".anneal/architecture/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return dangerous.Count == 0 ? null : ProtectedPathTripwire.FindTrippedPath(dangerous);
    }

    private static IReadOnlyList<ProcessNote> ReadNotes(StringWriter output) =>
        output
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .Select(line => new ProcessNote(line))
            .ToList();

    private static ChangeSetSummary ComposeSummary(
        DocumentChangeSet? documentation,
        ChangeSetSummary code,
        IReadOnlyList<string> gateCorrectedFiles)
    {
        var summary = documentation is null
            ? new ChangeSetSummary(code.FilesChanged, code.Summary)
            : WorkerHelpers.Merge(documentation, code);

        if (gateCorrectedFiles.Count == 0)
            return summary;

        var files = summary.FilesChanged
            .Concat(gateCorrectedFiles)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ChangeSetSummary(files, $"{summary.Summary} corrected stale architecture wording".Trim());
    }

    private static ChangeSetBeforeStopping? ComposeInterrupted(DocumentChangeSet? documentation, ChangeSetSummary code)
    {
        if (documentation is null)
            return new ChangeSetBeforeStopping(code.FilesChanged, code.Summary);

        return WorkerHelpers.MergeInterrupted(documentation, code);
    }

    private sealed record PreflightObligationDecision(
        bool NeedsDocumentationFirst,
        bool NeedsPlan,
        string Reason,
        string StepName,
        ProducedStepRoles SuggestedRoles);

    private sealed record PostflightAssessment(
        bool RunContractCheck,
        bool RunArchDocAgreement,
        bool RunTenetCheck,
        bool HasAmbiguousDiffSurface,
        bool SkipVerifier);

    private sealed record ProducedStepRoles(
        ModelRole PlannerRole,
        ModelRole DocumentAuthorRole,
        ModelRole DeveloperRole);

    private sealed record EffortProfile(
        int DocumentationRepairBudget,
        int CodeRepairBudget,
        int TenetRepairBudget,
        ProducedStepRoles SuggestedRoles);
}

internal enum GeneralWorkerPreflightBehavior
{
    Automatic,
    CodeOnly
}
