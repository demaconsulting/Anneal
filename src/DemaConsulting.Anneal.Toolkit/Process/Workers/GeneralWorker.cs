using DemaConsulting.Anneal.Toolkit.Architecture;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Recording;
using System.ComponentModel;
using System.Text.RegularExpressions;

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
    private const int ContractClauseWideScopeChangedFileThreshold = 3;

    private static readonly Regex DiffHeaderPattern = new(
        @"^\+\+\+ b/(?<path>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PublicMemberDeclarationPattern = new(
        @"^public\s+(?:static\s+|virtual\s+|abstract\s+|sealed\s+|async\s+|extern\s+|unsafe\s+|partial\s+|new\s+|override\s+)*[A-Za-z_][\w<>?,\[\].]*\s+[A-Za-z_][\w]*\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    private const string PreflightJudgmentCharter =
        """
        You are GeneralWorker's narrow preflight oracle. Classify only the expected file-touch scope and whether the
        work item can proceed inside this worker or must escalate before authoring. Do not design a plan, propose
        implementation steps, or decide which existing preflight branch should run.

        Scope vocabulary:
        - Docs: the expected touch is documentation or architecture-contract prose only.
        - Code: the expected touch includes production code and may include supporting docs.
        - Test: the expected touch is tests only or test fixtures/harnesses only.

        Conclusion vocabulary:
        - TenetViolation: escalate when the request appears to contradict a repository tenet.
        - VisionViolation: escalate when the request appears to contradict the repository vision or intended product direction.
        - InsufficientSpecificity: escalate when the request is too underspecified to choose an honest file-touch scope.
        - Proceed: the request is specific enough and does not visibly violate tenets or vision.
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
        var (preflightEscalation, preflight) = await SelectPreflightObligationsAsync(brief, cancellationToken)
            .ConfigureAwait(false);
        if (preflightEscalation is not null)
            return preflightEscalation;
        RecordStep(parentInvocationId, $"Preflight:{preflight!.StepName}", OperationOutcome.Succeeded);

        var (preflightTerminal, state) = await RunPreflightAsync(brief, preflight, parentInvocationId, cancellationToken)
            .ConfigureAwait(false);
        if (preflightTerminal is not null)
            return preflightTerminal;

        return await RunPostflightAsync(brief, preflight, parentInvocationId, state!, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(WorkerExecutionResult? Terminal, AuthoringRunState? State)> RunPreflightAsync(
        WorkerBrief brief,
        PreflightObligationDecision preflight,
        string parentInvocationId,
        CancellationToken cancellationToken)
    {
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
                return (planTerminal, null);

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
                return (documentTerminal, null);

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
            return (developerTerminal, null);

        return (
            null,
            new AuthoringRunState(
                plan,
                documentation,
                initialCode!,
                documentationRole,
                codeRole,
                tenetRole,
                _effortProfile.DocumentationRepairBudget,
                _effortProfile.CodeRepairBudget,
                _effortProfile.TenetRepairBudget));
    }

    private async Task<WorkerExecutionResult> RunPostflightAsync(
        WorkerBrief brief,
        PreflightObligationDecision preflight,
        string parentInvocationId,
        AuthoringRunState state,
        CancellationToken cancellationToken)
    {
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
                    ComposeInterrupted(state.Documentation, state.Code),
                    [new ProcessNote("the postflight diff could not be read, so required obligations could not be classified honestly")]);

            var diff = DiffCheck.ExcludingAnnealBookkeeping(observedDiff);
            var assessment = AssessPostflight(diff);
            if (assessment.HasAmbiguousDiffSurface)
                return new WorkerExecutionResult(
                    OperationOutcome.Escalated,
                    null,
                    ComposeInterrupted(state.Documentation, state.Code),
                    [new ProcessNote(
                        "the postflight diff contained edits but no parseable changed-file headers, so obligations could not be classified honestly")]);

            var dangerousProtectedPath = FindDangerousProtectedPath(diff.ChangedFiles);
            if (dangerousProtectedPath is not null)
                return new WorkerExecutionResult(
                    OperationOutcome.Escalated,
                    null,
                    ComposeInterrupted(state.Documentation, state.Code),
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
                            ComposeInterrupted(state.Documentation, state.Code),
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

            var verified = await VerifyPostflightAsync(brief, assessment, evidence, diff, parentInvocationId, cancellationToken)
                .ConfigureAwait(false);

            if (verified.Outcome == OperationOutcome.Succeeded &&
                verified.Finding?.Verdict == VerificationVerdict.Passed)
                return new WorkerExecutionResult(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Completed(ComposeSummary(state.Documentation, state.Code, gateCorrectedFiles)),
                    null,
                    gateNotes);

            if (verified.Outcome == OperationOutcome.Escalated)
                return new WorkerExecutionResult(
                    OperationOutcome.Escalated,
                    null,
                    ComposeInterrupted(state.Documentation, state.Code),
                    [.. gateNotes, .. verified.Notes, .. verified.Finding?.AdvisoryNotes.Select(note => new ProcessNote(note)) ?? []]);

            if (verified.Outcome == OperationOutcome.Refused)
                return new WorkerExecutionResult(
                    OperationOutcome.Failed,
                    null,
                    ComposeInterrupted(state.Documentation, state.Code),
                    [.. gateNotes, .. verified.Notes]);

            var repair = SelectPostflightRepair(verified.Finding);
            if (repair is not null)
            {
                var repairTerminal = await RunPostflightRepairAsync(
                        repair,
                        brief,
                        preflight,
                        parentInvocationId,
                        state,
                        gateNotes,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (repairTerminal is not null)
                    return repairTerminal;

                continue;
            }

            return new WorkerExecutionResult(
                OperationOutcome.Failed,
                null,
                ComposeInterrupted(state.Documentation, state.Code),
                [.. gateNotes, .. verified.Notes]);
        }
    }

    private async Task<StepResult<VerificationFinding>> VerifyPostflightAsync(
        WorkerBrief brief,
        PostflightAssessment assessment,
        IReadOnlyList<CheckFinding> evidence,
        DiffFinding diff,
        string parentInvocationId,
        CancellationToken cancellationToken)
    {
        if (assessment.SkipVerifier)
        {
            var deterministicResult = TryVerifyDeterministically(evidence) ??
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
                deterministicResult.Outcome == OperationOutcome.Succeeded ? "Verifier:skipped" : "Verifier:deterministic-only",
                deterministicResult.Outcome);
            return deterministicResult;
        }

        var verified = await _verifier
            .VerifyAsync(
                VerificationIntent.ContractConformance,
                evidence,
                assessment.RunTenetCheck ? VerifierQuestionBase + brief.RenderTenetSection() : VerifierQuestionBase,
                cancellationToken,
                diff)
            .ConfigureAwait(false);
        RecordStep(parentInvocationId, "Verifier", verified.Outcome);
        return verified;
    }

    private static PostflightRepairRequest? SelectPostflightRepair(VerificationFinding? finding)
    {
        if (finding?.Verdict != VerificationVerdict.RepairRequired)
            return null;

        var documentationFixes = RepairFixesFor(finding, VerificationOwner.Documentation);
        if (documentationFixes.Count > 0)
            return new PostflightRepairRequest(VerificationOwner.Documentation, documentationFixes);

        var codeFixes = RepairFixesFor(finding, VerificationOwner.Code);
        if (codeFixes.Count > 0)
            return new PostflightRepairRequest(VerificationOwner.Code, codeFixes);

        var tenetFixes = RepairFixesFor(finding, VerificationOwner.Tenet);
        return tenetFixes.Count > 0 ? new PostflightRepairRequest(VerificationOwner.Tenet, tenetFixes) : null;
    }

    private static IReadOnlyList<string> RepairFixesFor(VerificationFinding finding, VerificationOwner owner) =>
        finding.Concerns
            .Where(concern => concern.Owner == owner)
            .Select(concern => concern.FixText)
            .ToList();

    private async Task<WorkerExecutionResult?> RunPostflightRepairAsync(
        PostflightRepairRequest repair,
        WorkerBrief brief,
        PreflightObligationDecision preflight,
        string parentInvocationId,
        AuthoringRunState state,
        IReadOnlyList<ProcessNote> gateNotes,
        CancellationToken cancellationToken)
    {
        if (!TrySpendRepairBudget(state, repair.Owner, out var spentBudgetNote))
            return new WorkerExecutionResult(
                OperationOutcome.Failed,
                null,
                ComposeInterrupted(state.Documentation, state.Code),
                [.. gateNotes, new ProcessNote(spentBudgetNote)]);

        if (repair.Owner == VerificationOwner.Documentation)
            return await RunDocumentationRepairAsync(repair, brief, preflight, parentInvocationId, state, cancellationToken)
                .ConfigureAwait(false);

        var role = repair.Owner == VerificationOwner.Code
            ? state.CodeRole = EscalateRole(state.CodeRole)
            : state.TenetRole = EscalateRole(state.TenetRole);
        var stepName = repair.Owner == VerificationOwner.Code ? "Developer:repair" : "Developer:tenet-repair";

        var (terminal, repairedCode) = await RunDeveloperAsync(
                WorkerHelpers.ComposeRepairInstruction(
                    ComposeCodeInstruction(brief, state.Plan, state.Documentation, preflight),
                    repair.Fixes),
                role,
                parentInvocationId,
                stepName,
                cancellationToken,
                state.Documentation)
            .ConfigureAwait(false);
        if (terminal is not null)
            return WithInterrupted(terminal, state);

        state.Code = repairedCode!;
        return null;
    }

    private async Task<WorkerExecutionResult?> RunDocumentationRepairAsync(
        PostflightRepairRequest repair,
        WorkerBrief brief,
        PreflightObligationDecision preflight,
        string parentInvocationId,
        AuthoringRunState state,
        CancellationToken cancellationToken)
    {
        state.DocumentationRole = EscalateRole(state.DocumentationRole);

        var (documentRepairTerminal, repairedDocument) = await RunDocumentAuthorAsync(
                WorkerHelpers.ComposeRepairInstruction(
                    ComposeDocumentInstruction(brief, state.Plan, preflight),
                    repair.Fixes),
                state.DocumentationRole,
                parentInvocationId,
                "DocumentAuthor:repair",
                cancellationToken)
            .ConfigureAwait(false);
        if (documentRepairTerminal is not null)
            return WithInterrupted(documentRepairTerminal, state);
        state.Documentation = repairedDocument!;

        var (resyncTerminal, resyncCode) = await RunDeveloperAsync(
                ComposeCodeInstruction(brief, state.Plan, state.Documentation, preflight),
                state.CodeRole,
                parentInvocationId,
                "Developer:resync",
                cancellationToken,
                state.Documentation)
            .ConfigureAwait(false);
        if (resyncTerminal is not null)
            return WithInterrupted(resyncTerminal, state);

        state.Code = resyncCode!;
        return null;
    }

    private static bool TrySpendRepairBudget(
        AuthoringRunState state,
        VerificationOwner owner,
        out string spentBudgetNote)
    {
        switch (owner)
        {
            case VerificationOwner.Documentation:
                spentBudgetNote = "the documentation-repair budget was already spent when another documentation finding arrived";
                if (state.DocumentationRepairBudget <= 0)
                    return false;
                state.DocumentationRepairBudget--;
                return true;
            case VerificationOwner.Code:
                spentBudgetNote = "the code-repair budget was already spent when another code finding arrived";
                if (state.CodeRepairBudget <= 0)
                    return false;
                state.CodeRepairBudget--;
                return true;
            case VerificationOwner.Tenet:
                spentBudgetNote = "the tenet-repair budget was already spent when another tenet finding arrived";
                if (state.TenetRepairBudget <= 0)
                    return false;
                state.TenetRepairBudget--;
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown verification owner.");
        }
    }

    private static WorkerExecutionResult WithInterrupted(WorkerExecutionResult result, AuthoringRunState state) =>
        result with
        {
            Interrupted = result.Interrupted ?? ComposeInterrupted(state.Documentation, state.Code)
        };

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

    private static string ComposePreflightJudgmentQuestion(WorkerBrief brief) =>
        $"""
         Classify this work item's expected file-touch scope and pre-authoring conclusion.

         Work item:
         {brief.OriginalWorkItem}

         Effort: {brief.Effort}
         Why this worker was selected: {brief.ScopeHint}
         Classification hypothesis: {brief.ClassificationHypothesis ?? "none"}

         Changed-file hints:
         {RenderLines(brief.ChangedFileHints)}

         Research findings:
         {brief.RenderResearch()}

         Prior reroutes:
         {brief.RenderReroutes()}

         Repository tenets:
         {RenderLines(brief.TenetFacts)}

         Constraint refs:
         {RenderLines(brief.ConstraintRefs)}
         """;

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

    private static string RenderLines(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join("\n", values.Select(value => $"- {value}"));

    /// <remarks>
    ///     Calls the schema-enforced preflight oracle and returns the judgment, or null when the oracle
    ///     could not produce a valid schema-conforming reply after its internal retry budget.
    /// </remarks>
    private async Task<PreflightJudgment?> JudgePreflightAsync(WorkerBrief brief, CancellationToken cancellationToken)
    {
        var oracle = new Oracle<PreflightJudgment>(_repositoryRoot, PreflightJudgmentCharter, endpointFor: _endpointFor);
        var result = await oracle
            .AskAsync("Judge this GeneralWorker preflight.", [ComposePreflightJudgmentQuestion(brief)], cancellationToken)
            .ConfigureAwait(false);
        return result.Outcome == OperationOutcome.Succeeded ? result.Finding : null;
    }

    /// <remarks>
    ///     The oracle drives scope (NeedsDocumentationFirst) and the escalation gate (Conclusion).
    ///     NeedsPlan is intentionally kept out of the oracle's hands: it is governed by Effort and
    ///     changed-file count alone, matching the deterministic contract already in TOOLKIT-65.
    /// </remarks>
    private async Task<(WorkerExecutionResult? Escalation, PreflightObligationDecision? Decision)> SelectPreflightObligationsAsync(
        WorkerBrief brief,
        CancellationToken cancellationToken)
    {
        if (_preflightBehavior == GeneralWorkerPreflightBehavior.CodeOnly)
            return (null, new PreflightObligationDecision(
                NeedsDocumentationFirst: false,
                NeedsPlan: false,
                Reason: "the caller fixed this run to code-only authoring before GeneralWorker started",
                StepName: "CodeOnly",
                SuggestedRoles: _effortProfile.SuggestedRoles));

        var judgment = await JudgePreflightAsync(brief, cancellationToken).ConfigureAwait(false);
        if (judgment is null)
            return (new WorkerExecutionResult(
                OperationOutcome.Failed,
                null,
                null,
                [new ProcessNote("the preflight oracle could not produce a schema-valid judgment")]),
                null);
        var suggestedRoles = _effortProfile.SuggestedRoles;

        // Escalate before touching any file when the oracle finds a policy or specificity problem.
        if (judgment.Conclusion != PreflightConclusion.Proceed)
        {
            var conclusionName = judgment.Conclusion.ToString();
            return (new WorkerExecutionResult(
                OperationOutcome.Escalated,
                null,
                null,
                [new ProcessNote($"preflight oracle returned {conclusionName}: the run was stopped before authoring any file")]),
                null);
        }

        var needsDocumentationFirst = judgment.Scope == PreflightScope.Docs;

        // NeedsPlan is driven by Effort and changed-file count, not by the oracle scope.
        // Large/Massive effort with no changed-file hints means potentially unbounded scope, which
        // requires a plan just as wide changed-file count does.
        var namesWideChangedFileScope = brief.ChangedFileHints
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() >= ContractClauseWideScopeChangedFileThreshold;
        var namesLargeOrMassiveEffort = brief.Effort is Effort.Large or Effort.Massive;
        var hasNoFileHints = !brief.ChangedFileHints.Any();

        var needsPlan = needsDocumentationFirst && (namesWideChangedFileScope || (namesLargeOrMassiveEffort && hasNoFileHints));

        var (stepName, reason) = (needsDocumentationFirst, needsPlan, namesWideChangedFileScope) switch
        {
            (true, true, true) => ("PlanAndDocument", "the request framing names a contract change with wide changed-file scope"),
            (true, true, false) => ("PlanAndDocument", "the request framing names a Large or Massive effort change with no file-scope hints"),
            (true, false, _) => ("Document", "the oracle classified this as a documentation-scope change"),
            _ => ("CodeOnly", "the oracle did not classify this as a documentation-scope change")
        };

        return (null, new PreflightObligationDecision(
            NeedsDocumentationFirst: needsDocumentationFirst,
            NeedsPlan: needsPlan,
            Reason: reason,
            StepName: stepName,
            SuggestedRoles: suggestedRoles));
    }

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

        string? currentPath = null;
        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var header = DiffHeaderPattern.Match(line);
            if (header.Success)
            {
                currentPath = header.Groups["path"].Value;
                continue;
            }

            if (line.Length == 0 || (line[0] != '+' && line[0] != '-'))
                continue;
            if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
                continue;
            if (!IsProductionCodePath(currentPath))
                continue;

            var content = line[1..].TrimStart();
            if (!content.StartsWith("public ", StringComparison.Ordinal))
                continue;

            if (IsPublicTypeDeclaration(content) ||
                IsPublicMemberDeclaration(content) ||
                (content.Contains(" { get;", StringComparison.Ordinal) && !content.StartsWith("public override ", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private static bool IsProductionCodePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalized = path.Replace('\\', '/');
        return normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
               !normalized.StartsWith("test/", StringComparison.OrdinalIgnoreCase) &&
               !normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("/test/", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) &&
               !normalized.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase) &&
               !normalized.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPublicTypeDeclaration(string content) =>
        content.StartsWith("public class ", StringComparison.Ordinal) ||
        content.StartsWith("public interface ", StringComparison.Ordinal) ||
        content.StartsWith("public record ", StringComparison.Ordinal) ||
        content.StartsWith("public struct ", StringComparison.Ordinal) ||
        content.StartsWith("public enum ", StringComparison.Ordinal) ||
        content.StartsWith("public delegate ", StringComparison.Ordinal);

    private static bool IsPublicMemberDeclaration(string content) =>
        !content.StartsWith("public override ", StringComparison.Ordinal) &&
        PublicMemberDeclarationPattern.IsMatch(content);

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

    private sealed class AuthoringRunState
    {
        public AuthoringRunState(
            ImplementationPlan? plan,
            DocumentChangeSet? documentation,
            ChangeSetSummary code,
            ModelRole documentationRole,
            ModelRole codeRole,
            ModelRole tenetRole,
            int documentationRepairBudget,
            int codeRepairBudget,
            int tenetRepairBudget)
        {
            Plan = plan;
            Documentation = documentation;
            Code = code;
            DocumentationRole = documentationRole;
            CodeRole = codeRole;
            TenetRole = tenetRole;
            DocumentationRepairBudget = documentationRepairBudget;
            CodeRepairBudget = codeRepairBudget;
            TenetRepairBudget = tenetRepairBudget;
        }

        public ImplementationPlan? Plan { get; set; }

        public DocumentChangeSet? Documentation { get; set; }

        public ChangeSetSummary Code { get; set; }

        public ModelRole DocumentationRole { get; set; }

        public ModelRole CodeRole { get; set; }

        public ModelRole TenetRole { get; set; }

        public int DocumentationRepairBudget { get; set; }

        public int CodeRepairBudget { get; set; }

        public int TenetRepairBudget { get; set; }
    }

    private sealed record PostflightRepairRequest(VerificationOwner Owner, IReadOnlyList<string> Fixes);

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

/// <summary>
///     The schema-enforced answer returned by GeneralWorker's narrow preflight oracle.
/// </summary>
internal sealed record PreflightJudgment : IOracleDecision
{
    /// <summary>The expected file-touch surface the work item most directly implies.</summary>
    public required PreflightScope Scope { get; init; }

    /// <summary>Whether the work can proceed or must escalate before authoring.</summary>
    public required PreflightConclusion Conclusion { get; init; }

    bool IOracleDecision.HasSufficientEvidence => true;
}

/// <summary>The expected file-touch surface for a preflight judgement.</summary>
internal enum PreflightScope
{
    /// <summary>Documentation or architecture-contract prose only.</summary>
    [Description("documentation or architecture-contract prose only")]
    Docs,

    /// <summary>Production code, with or without supporting documentation.</summary>
    [Description("production code, with or without supporting documentation")]
    Code,

    /// <summary>Tests, test fixtures, or test harnesses only.</summary>
    [Description("tests, test fixtures, or test harnesses only")]
    Test
}

/// <summary>The pre-authoring conclusion for a preflight judgement.</summary>
internal enum PreflightConclusion
{
    /// <summary>The work item appears to contradict a repository tenet.</summary>
    [Description("escalate because the request appears to contradict a repository tenet")]
    TenetViolation,

    /// <summary>The work item appears to contradict the repository vision.</summary>
    [Description("escalate because the request appears to contradict the repository vision")]
    VisionViolation,

    /// <summary>The work item is too underspecified for an honest file-touch classification.</summary>
    [Description("escalate because the request is too underspecified to classify honestly")]
    InsufficientSpecificity,

    /// <summary>The work item is specific enough and does not visibly violate tenets or vision.</summary>
    [Description("proceed with the worker run")]
    Proceed
}

internal enum GeneralWorkerPreflightBehavior
{
    Automatic,
    CodeOnly
}
