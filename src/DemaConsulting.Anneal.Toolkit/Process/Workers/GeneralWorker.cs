using DemaConsulting.Anneal.Toolkit.Architecture;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     Capability-complete Large-effort worker: it may plan, author contract and architecture documentation, edit
///     code and tests, and then fire only the heavier obligations the actual diff proves were needed.
/// </summary>
/// <remarks>
///     The worker is deliberately shaped as one Effort-parameterized pipeline whose Large tier is the full-capability
///     superset: a deterministic preflight selector decides whether a plan and/or documentation-first pass runs
///     before code, and a deterministic postflight selector decides which heavier checks the actual diff warrants.
///     Small and Medium are not implemented yet; they would be narrower configurations of this same pipeline rather
///     than a second design.
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
    private readonly Planner _planner;
    private readonly DocumentAuthor _documentAuthor;
    private readonly Developer _developer;
    private readonly DeterministicCheck _buildCheck;
    private readonly DeterministicCheck _contractCheck;
    private readonly DiffCheck _diffCheck;
    private readonly Verifier _verifier;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunGitCommand? _runGit;
    private readonly int _maxDocumentationRepairAttempts;
    private readonly int _maxCodeRepairAttempts;
    private readonly int _maxTenetRepairAttempts;
    private readonly RecordStore? _recordStore;

    public GeneralWorker(
        string repositoryRoot,
        Effort effort,
        string plannerCharter,
        string documentAuthorCharter,
        string developerCharter,
        string verifierCharter,
        int maxDocumentationRepairAttempts = 1,
        int maxCodeRepairAttempts = 1,
        int maxTenetRepairAttempts = 1,
        int documentAuthorTargetFileCountBudget = 8,
        int maxPlanSteps = 12,
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
        ArgumentOutOfRangeException.ThrowIfNegative(maxDocumentationRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCodeRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTenetRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(documentAuthorTargetFileCountBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPlanSteps);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _effort = effort;
        _planner = new Planner(_repositoryRoot, plannerCharter, maxPlanSteps: maxPlanSteps, endpointFor: endpointFor);
        _documentAuthor = new DocumentAuthor(
            _repositoryRoot,
            documentAuthorCharter,
            targetFileCountBudget: documentAuthorTargetFileCountBudget,
            scopeDriftCheckInterval: 1,
            endpointFor: endpointFor);
        _developer = new Developer(_repositoryRoot, developerCharter, endpointFor: endpointFor);
        _buildCheck = new DeterministicCheck(_repositoryRoot, runScript: buildRunScript);
        _contractCheck = new DeterministicCheck(
            _repositoryRoot,
            runScript: contractCheckRunScript ?? ((_, ct) => ContractCheckRunner.RunAsync(_repositoryRoot, ct, strict: false)));
        _diffCheck = new DiffCheck(_repositoryRoot, runGit: runGit);
        _verifier = new Verifier(_repositoryRoot, verifierCharter, endpointFor: endpointFor);
        _endpointFor = endpointFor;
        _runGit = runGit;
        _maxDocumentationRepairAttempts = maxDocumentationRepairAttempts;
        _maxCodeRepairAttempts = maxCodeRepairAttempts;
        _maxTenetRepairAttempts = maxTenetRepairAttempts;
        _recordStore = recordStore;
        _buildScript = ScriptConfiguration.Load(_repositoryRoot).Build;
    }

    public async Task<WorkerExecutionResult> RunAsync(WorkerBrief brief, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        cancellationToken.ThrowIfCancellationRequested();

        if (_effort != Effort.Large)
            throw new NotSupportedException(
                $"GeneralWorker currently supports only {Effort.Large} effort; received {_effort}.");

        var parentInvocationId = brief.ParentInvocationId;
        var preflight = SelectPreflightObligations(brief);
        RecordStep(parentInvocationId, $"Preflight:{preflight.StepName}", OperationOutcome.Succeeded);

        ImplementationPlan? plan = null;
        if (preflight.NeedsPlan)
        {
            var (planTerminal, selectedPlan) = await RunPlannerAsync(
                    ComposePlanningQuestion(brief, preflight),
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
                parentInvocationId,
                "Developer",
                cancellationToken,
                documentation)
            .ConfigureAwait(false);
        if (developerTerminal is not null)
            return developerTerminal;

        var code = initialCode!;
        var documentationRepairBudget = _maxDocumentationRepairAttempts;
        var codeRepairBudget = _maxCodeRepairAttempts;
        var tenetRepairBudget = _maxTenetRepairAttempts;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var buildCheck = await _buildCheck
                .RunAsync("build.ps1 check", _buildScript, null, cancellationToken, brief.ChangedFileHints)
                .ConfigureAwait(false);
            RecordStep(parentInvocationId, "DeterministicCheck:build.ps1", buildCheck.Outcome);

            var diffResult = await _diffCheck.RunAsync(null, cancellationToken).ConfigureAwait(false);
            RecordStep(parentInvocationId, "DiffCheck", diffResult.Outcome);

            if (diffResult.Outcome != OperationOutcome.Succeeded || diffResult.Finding is not { Available: true } diff)
                return new WorkerExecutionResult(
                    OperationOutcome.Escalated,
                    null,
                    ComposeInterrupted(documentation, code),
                    [new ProcessNote("the postflight diff could not be read, so required obligations could not be classified honestly")]);

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
            if (assessment.RunArchDocAgreement)
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

                    diff = refreshed;
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

            var verified = await _verifier
                .VerifyAsync(
                    VerificationIntent.ContractConformance,
                    evidence,
                    assessment.RunTenetCheck ? VerifierQuestionBase + brief.RenderTenetSection() : VerifierQuestionBase,
                    cancellationToken,
                    diff)
                .ConfigureAwait(false);
            RecordStep(parentInvocationId, "Verifier", verified.Outcome);

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

                var (documentRepairTerminal, repairedDocument) = await RunDocumentAuthorAsync(
                        WorkerHelpers.ComposeRepairInstruction(
                            ComposeDocumentInstruction(brief, plan, preflight),
                            documentationFixes),
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

                var (codeRepairTerminal, repairedCode) = await RunDeveloperAsync(
                        WorkerHelpers.ComposeRepairInstruction(
                            ComposeCodeInstruction(brief, plan, documentation, preflight),
                            codeFixes),
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

                var (tenetRepairTerminal, repairedTenetCode) = await RunDeveloperAsync(
                        WorkerHelpers.ComposeRepairInstruction(
                            ComposeCodeInstruction(brief, plan, documentation, preflight),
                            tenetFixes),
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
        string parentInvocationId,
        string stepName,
        CancellationToken cancellationToken)
    {
        var result = await _planner.PlanAsync(question, [], cancellationToken).ConfigureAwait(false);
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
        string parentInvocationId,
        string stepName,
        CancellationToken cancellationToken)
    {
        var result = await _documentAuthor.AuthorAsync(instruction, cancellationToken).ConfigureAwait(false);
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
        string parentInvocationId,
        string stepName,
        CancellationToken cancellationToken,
        DocumentChangeSet? priorDocumentation = null)
    {
        var result = await _developer.DevelopAsync(instruction, cancellationToken).ConfigureAwait(false);
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

    private string ComposePlanningQuestion(WorkerBrief brief, PreflightObligationDecision preflight) =>
        $"""
         {brief.OriginalWorkItem}

         Decide whether this Large general-worker run needs an explicit plan before authoring because the request
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

         Author any contract-clause or architecture-document changes this Large general-worker run needs under
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

                Implement the change in code and tests, keeping any contract or architecture edits you make aligned with
                the request and the already-authored documentation when one exists.

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

    private static PreflightObligationDecision SelectPreflightObligations(WorkerBrief brief)
    {
        var subject = string.Join("\n", new[] { brief.OriginalWorkItem }.Concat(brief.ChangedFileHints)).ToLowerInvariant();

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
                StepName: "PlanAndDocument");

        if (namesArchitectureDoc || namesContractSurface)
            return new PreflightObligationDecision(
                NeedsDocumentationFirst: true,
                NeedsPlan: false,
                Reason: "the request framing already names a contract or architecture-document change",
                StepName: "Document");

        return new PreflightObligationDecision(
            NeedsDocumentationFirst: false,
            NeedsPlan: false,
            Reason: "the request framing does not itself imply documentation-first or planning obligations",
            StepName: "CodeOnly");
    }

    private static bool ContainsAny(string text, params string[] fragments) =>
        fragments.Any(fragment => text.Contains(fragment, StringComparison.Ordinal));

    private PostflightAssessment AssessPostflight(DiffFinding diff)
    {
        var architectureChanges = diff.ChangedFiles
            .Where(path => path.StartsWith(".anneal/architecture/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var touchesContractSection = architectureChanges.Any(path =>
            ArchitectureCoverage.PatchTouchesContractSection(diff.Patch, path));

        var matchedArchitectureDocs = ArchDocAgreementGate.FindMatchedDocuments(
            _repositoryRoot,
            Path.Combine(_repositoryRoot, ".anneal", "architecture"),
            diff.ChangedFiles);

        return new PostflightAssessment(
            RunContractCheck: touchesContractSection,
            RunArchDocAgreement: architectureChanges.Count > 0 || matchedArchitectureDocs.Count > 0,
            RunTenetCheck: TouchesPublicSignature(diff.Patch),
            HasAmbiguousDiffSurface: diff.Patch.Length > 0 && diff.ChangedFiles.Count == 0);
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
        string StepName);

    private sealed record PostflightAssessment(
        bool RunContractCheck,
        bool RunArchDocAgreement,
        bool RunTenetCheck,
        bool HasAmbiguousDiffSurface);
}
