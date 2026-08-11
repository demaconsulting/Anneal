using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Process.Workers;

/// <summary>
///     The Contract Change path: <see cref="DocumentAuthor" /> updates the affected system contract document(s)
///     and prunes their section docs, <see cref="Developer" /> implements code and tests against the clauses that
///     just changed, two <see cref="DeterministicCheck" /> steps run <c>build.ps1</c> and a non-strict contract check,
///     and a model-backed <see cref="Verifier" /> judges contract conformance, scope honesty, tree accuracy, and
///     tenet conformance against that evidence before either finishing or spending one of three independent
///     one-shot repair budgets.
/// </summary>
/// <remarks>
///     Unlike <see cref="SmallFixWorker" />, this worker's repair is not one owner spending one shared budget —
///     <c>.anneal/architecture/process.md</c> § Decisions' "ownership-directed" repair means a documentation finding
///     repairs through <see cref="DocumentAuthor" />, a code finding through <see cref="Developer" />, and a
///     <see cref="VerificationOwner.Tenet" /> finding also through <see cref="Developer" /> — a tenet violation is
///     fixed by changing code or configuration to conform to <c>.anneal/work/constraints.md</c> and the affected contracts,
///     which is still a code-shaped fix, and no separate "tenet author" primitive exists — each against its own
///     one-shot budget, documentation first, then code, then tenet, when a verdict names more than one.
///     <see cref="RepairLoop{TState}" />
///     itself is not instantiated here: its shape closes over exactly one <c>execute</c> step bounded by one
///     counter, and this worker needs its <c>execute</c> step to be chosen dynamically, per pass, from the
///     <see cref="VerificationOwner" /> a <see cref="Verifier" />'s <see cref="VerificationConcern" />s just
///     named — a shape the generic primitive
///     does not parametrize. The state machine below reproduces its exact contract by hand instead: a repair is
///     spent only from the budget the finding names, an escalation or evidence-insufficient verdict stops
///     immediately without spending a budget, and a budget spent with the same repair type still failing reports
///     <see cref="OperationOutcome.Failed" /> — see the Apply Report for why composing two <see cref="RepairLoop{TState}" />
///     instances instead was rejected.
///     <para>
///         <b>When this worker reroutes.</b> Three paths surface a <see cref="WorkerRunResult.Reroute" />: (1)
///         <see cref="DocumentAuthor" /> or <see cref="Developer" /> itself names a better owner while authoring,
///         exactly as <see cref="SmallFixWorker" /> surfaces <see cref="Developer" />'s own reroute unchanged; (2)
///         the <see cref="Verifier" /> reaches <see cref="VerificationVerdict.RerouteRequired" /> because the
///         change should have been classified Structural Change instead of Contract Change; (3) the same
///         verdict, reached instead because the verifier's reasoning surfaces a contradiction with a stated README
///         Assumption that implies a re-cut of the repository's boundaries or Migration-scale work. Both (2) and
///         (3) are instructions given to the verifier's own charter and question, not two separately-detected
///         conditions in this worker's own code — see the Apply Report's judgment call on this.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
internal sealed class ContractChangeWorker
{
    /// <summary>The repository-relative build/test script this worker's first deterministic check runs, or null.</summary>
    private readonly string? _buildScript;

    /// <summary>
    ///     The narrower question a <see cref="Verifier" /> answers once its deterministic evidence has passed.
    ///     Names both reroute conditions explicitly, so a single <see cref="VerificationVerdict.RerouteRequired" />
    ///     verdict carries whichever reason applies, rather than this worker trying to detect either condition
    ///     itself from prose.
    /// </summary>
    private const string VerifierQuestionBase =
        """
        Judge whether this change conforms to every contract clause it touches, is honestly scoped as Contract
        Change rather than Structural Change, and leaves .anneal/architecture/ accurate for what was
        actually built. Also check the change against .anneal/work/constraints.md's Satisfied constraints and the boundaries
        of every system contract it touches; report any violation as a concern owned by Tenet, with a FixText
        naming the specific constraint or contract boundary crossed and what must change to stop crossing it.
        Report the verdict 'RerouteRequired', with your reasoning in the advisory notes, when
        either: (1) the change actually needed a system-boundary move and should have been classified Structural
        Change instead of Contract Change; or (2) your reasoning surfaces a contradiction with a stated
        README Assumption that implies the repository needs a re-cut of its boundaries or Migration-scale work,
        not a routine contract change. Otherwise, report 'RepairRequired' with one concern per fix needed, each
        owned by Documentation, Code, or Tenet, or 'Passed' when nothing needs fixing.
        """;

    private static string VerifierQuestion(WorkerBrief brief) =>
        VerifierQuestionBase + brief.RenderTenetSection();

    private readonly string _repositoryRoot;
    private readonly DocumentAuthor _documentAuthor;
    private readonly Developer _developer;
    private readonly DeterministicCheck _buildCheck;
    private readonly DeterministicCheck _contractCheck;
    private readonly Verifier _verifier;
    private readonly int _maxDocumentationRepairAttempts;
    private readonly int _maxCodeRepairAttempts;
    private readonly int _maxTenetRepairAttempts;
    private readonly RecordStore? _recordStore;

    /// <summary>
    ///     Binds a Contract Change worker to a repository and the charters its composed primitives carry.
    /// </summary>
    /// <param name="repositoryRoot">The repository authored into and checked. Must not be null or blank.</param>
    /// <param name="documentAuthorCharter">
    ///     The system message <see cref="DocumentAuthor" />'s pass carries: what document(s) it may update, and
    ///     that pruning a section doc that no longer earns its place is part of the job. Must not be null.
    /// </param>
    /// <param name="developerCharter">
    ///     The system message <see cref="Developer" />'s pass carries: implement code and tests against the
    ///     clauses the documentation pass just updated. Must not be null.
    /// </param>
    /// <param name="verifierCharter">
    ///     The system message the model-backed <see cref="Verifier" /> pass carries. Must not be null.
    /// </param>
    /// <param name="maxDocumentationRepairAttempts">
    ///     The most documentation-repair attempts spent when a verdict's concerns name
    ///     <see cref="VerificationOwner.Documentation" />, independent of
    ///     <paramref name="maxCodeRepairAttempts" />. Must be zero or greater; defaults to 1, per this worker's
    ///     bound to one documentation repair.
    /// </param>
    /// <param name="maxCodeRepairAttempts">
    ///     The most code-repair attempts spent when a verdict's concerns name <see cref="VerificationOwner.Code" />,
    ///     independent of
    ///     <paramref name="maxDocumentationRepairAttempts" /> and <paramref name="maxTenetRepairAttempts" />. Must
    ///     be zero or greater; defaults to 1, per this worker's bound to one code repair.
    /// </param>
    /// <param name="maxTenetRepairAttempts">
    ///     The most tenet-repair attempts spent when a verdict's concerns name <see cref="VerificationOwner.Tenet" />,
    ///     independent of <paramref name="maxDocumentationRepairAttempts" /> and <paramref name="maxCodeRepairAttempts" />.
    ///     A tenet finding repairs through <see cref="Developer" /> — the same primitive a code finding uses —
    ///     since fixing a tenet violation means changing code or configuration to conform to <c>.anneal/work/constraints.md</c>
    ///     and the affected contracts, spent from this separate budget rather than <paramref name="maxCodeRepairAttempts" />
    ///     so a tenet finding cannot starve or be starved by a code finding in the same round. Must be zero or
    ///     greater; defaults to 1, per this worker's bound to one tenet repair.
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
    ///     records. This worker is heavier and branches more than <see cref="SmallFixWorker" />, so its own
    ///     composed steps are worth their own finer-grained record when a caller wants it.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="documentAuthorCharter" />, <paramref name="developerCharter" />, or
    ///     <paramref name="verifierCharter" /> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="maxDocumentationRepairAttempts" />, <paramref name="maxCodeRepairAttempts" />,
    ///     or <paramref name="maxTenetRepairAttempts" /> is negative.
    /// </exception>
    public ContractChangeWorker(
        string repositoryRoot,
        string documentAuthorCharter,
        string developerCharter,
        string verifierCharter,
        int maxDocumentationRepairAttempts = 1,
        int maxCodeRepairAttempts = 1,
        int maxTenetRepairAttempts = 1,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? buildRunScript = null,
        RunRepositoryScript? contractCheckRunScript = null,
        RecordStore? recordStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(documentAuthorCharter);
        ArgumentNullException.ThrowIfNull(developerCharter);
        ArgumentNullException.ThrowIfNull(verifierCharter);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDocumentationRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCodeRepairAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTenetRepairAttempts);

        var root = Path.GetFullPath(repositoryRoot);

        _repositoryRoot = root;
        _documentAuthor = new DocumentAuthor(root, documentAuthorCharter, endpointFor: endpointFor);
        _developer = new Developer(root, developerCharter, endpointFor: endpointFor);
        _buildCheck = new DeterministicCheck(root, runScript: buildRunScript);
        _contractCheck = new DeterministicCheck(
            root,
            runScript: contractCheckRunScript ?? ((_, ct) => ContractCheckRunner.RunAsync(root, ct, strict: false)));
        _verifier = new Verifier(root, verifierCharter, endpointFor: endpointFor);
        _maxDocumentationRepairAttempts = maxDocumentationRepairAttempts;
        _maxCodeRepairAttempts = maxCodeRepairAttempts;
        _maxTenetRepairAttempts = maxTenetRepairAttempts;
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
    ///     <see cref="OperationOutcome.Succeeded" /> with <see cref="WorkerRunResult.Reroute" /> when
    ///     <see cref="DocumentAuthor" /> or <see cref="Developer" /> named a better owner, or the verifier
    ///     concluded the classification underneath this work needs to change; <see cref="OperationOutcome.Escalated" />
    ///     when a repair needed a protected path; <see cref="OperationOutcome.Failed" /> when a repair budget was
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

        var (documentTerminal, documentChanges) = await RunDocumentAuthorAsync(
                ComposeDocumentInstruction(brief), parentInvocationId, "DocumentAuthor", cancellationToken)
            .ConfigureAwait(false);
        if (documentTerminal is not null)
            return documentTerminal;

        var (developerTerminal, initialCode) = await RunDeveloperAsync(
                ComposeCodeInstruction(brief, documentChanges!), parentInvocationId, "Developer", cancellationToken,
                documentChanges)
            .ConfigureAwait(false);
        if (developerTerminal is not null)
            return developerTerminal;

        var documentation = documentChanges!;
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
                .VerifyAsync(VerificationIntent.ContractConformance, evidence, VerifierQuestion(brief), cancellationToken)
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
                    OperationOutcome.Failed,
                    null,
                    WorkerHelpers.MergeInterrupted(documentation, code),
                    verified.Notes);

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
                        WorkerHelpers.ComposeRepairInstruction(ComposeDocumentInstruction(brief), documentationFixes),
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
                // satisfy, so the code pass is re-run unconditionally to stay in sync rather than guessed at.
                // This re-run is not spent from the code-repair budget: it is necessitated by the documentation
                // repair, not a separate finding the verifier raised - see the Apply Report's judgment call.
                var (resyncTerminal, resyncCode) = await RunDeveloperAsync(
                        ComposeCodeInstruction(brief, documentation),
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
                        WorkerHelpers.ComposeRepairInstruction(ComposeCodeInstruction(brief, documentation), codeFixes),
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
                // affected contracts, which is still a code-shaped fix, and no separate "tenet author" primitive
                // exists - see the Apply Report's judgment call.
                var (tenetRepairTerminal, repairedTenetCode) = await RunDeveloperAsync(
                        WorkerHelpers.ComposeRepairInstruction(ComposeCodeInstruction(brief, documentation), tenetFixes),
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

            // Every named verdict is handled above; an unnamed one is treated as a blocking failure rather than
            // silently passing.
            return new WorkerExecutionResult(
                OperationOutcome.Failed, null, WorkerHelpers.MergeInterrupted(documentation, code), verified.Notes);
        }
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

    private string ComposeDocumentInstruction(WorkerBrief brief) =>
        $"""
         {brief.OriginalWorkItem}

         Update the affected system contract document(s) under .anneal/architecture/ for this change, and prune
         any subsystem document whose content no longer earns its place. Do not touch code or tests.

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

    private string ComposeCodeInstruction(WorkerBrief brief, DocumentChangeSet documentChanges) =>
        $"""
         {brief.OriginalWorkItem}

         Implement code and tests against the contract clauses this change just updated:
         {documentChanges.Summary}

         Files the documentation pass touched: {(documentChanges.FilesChanged.Count == 0 ? "none" : string.Join(", ", documentChanges.FilesChanged))}

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
