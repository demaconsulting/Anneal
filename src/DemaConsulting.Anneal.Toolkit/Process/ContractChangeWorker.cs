using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Recording;

namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>
///     The Contract Change path: <see cref="DocumentAuthor" /> updates the affected system contract document(s)
///     and prunes their section docs, <see cref="Developer" /> implements code and tests against the clauses that
///     just changed, two <see cref="DeterministicCheck" /> steps run <c>build.ps1</c> and a strict contract check,
///     and a model-backed <see cref="Verifier" /> judges contract conformance, scope honesty, and tree accuracy
///     against that evidence before either finishing or spending one of two independent one-shot repair budgets.
/// </summary>
/// <remarks>
///     Unlike <see cref="SmallFixWorker" />, this worker's repair is not one owner spending one shared budget —
///     <c>docs/architecture/process.md</c> § Decisions' "ownership-directed" repair means a documentation finding
///     repairs through <see cref="DocumentAuthor" /> and a code finding through <see cref="Developer" />, each
///     against its own one-shot budget, documentation first when a verdict names both. <see cref="RepairLoop{TState}" />
///     itself is not instantiated here: its shape closes over exactly one <c>execute</c> step bounded by one
///     counter, and this worker needs its <c>execute</c> step to be chosen dynamically, per pass, from the
///     <see cref="VerificationVerdict" /> a <see cref="Verifier" /> just reached — a shape the generic primitive
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
    /// <summary>The repository-relative build/test script this worker's first deterministic check runs.</summary>
    private const string BuildScript = "build.ps1";

    /// <summary>The repository-relative contract-check script this worker's second deterministic check runs.</summary>
    private const string ContractCheckScript = "check-contracts.ps1";

    /// <summary>
    ///     The fixed standards injected into every <see cref="DocumentAuthor" /> call, mirroring <c>AGENTS.md</c>'s
    ///     own "Standards Application" table for the documentation half of this worker's split.
    /// </summary>
    private static readonly string[] DocumentAuthorStandards =
        ["architecture-documentation.md", "system-contracts.md"];

    /// <summary>
    ///     The fixed standards injected into every <see cref="Developer" /> call, mirroring <c>AGENTS.md</c>'s own
    ///     "Standards Application" table for the code half of this worker's split: coding and C# language always,
    ///     plus testing and C# testing since this worker's own charter has <see cref="Developer" /> "implement code
    ///     and tests against the clauses that just changed".
    /// </summary>
    private static readonly string[] DeveloperStandards =
        ["coding-principles.md", "csharp-language.md", "testing-principles.md", "csharp-testing.md"];

    /// <summary>
    ///     The narrower question a <see cref="Verifier" /> answers once its deterministic evidence has passed.
    ///     Names both reroute conditions explicitly, so a single <see cref="VerificationVerdict.RerouteRequired" />
    ///     verdict carries whichever reason applies, rather than this worker trying to detect either condition
    ///     itself from prose.
    /// </summary>
    private const string VerifierQuestion =
        """
        Judge whether this change conforms to every contract clause it touches, is honestly scoped as Contract
        Change rather than Structural Change, and leaves docs/architecture/ accurate for what was
        actually built. Report the verdict 'RerouteRequired', with your reasoning in the required fixes, when
        either: (1) the change actually needed a system-boundary move and should have been classified Structural
        Change instead of Contract Change; or (2) your reasoning surfaces a contradiction with a stated
        README Assumption that implies the repository needs a re-cut of its boundaries or Migration-scale work,
        not a routine contract change.
        """;

    private readonly string _repositoryRoot;
    private readonly DocumentAuthor _documentAuthor;
    private readonly Developer _developer;
    private readonly DeterministicCheck _buildCheck;
    private readonly DeterministicCheck _contractCheck;
    private readonly Verifier _verifier;
    private readonly int _maxDocumentationRepairAttempts;
    private readonly int _maxCodeRepairAttempts;
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
    ///     The most documentation-repair attempts spent when a verdict names
    ///     <see cref="VerificationVerdict.DocumentationRepairRequired" /> or
    ///     <see cref="VerificationVerdict.BothRepairsRequired" />, independent of
    ///     <paramref name="maxCodeRepairAttempts" />. Must be zero or greater; defaults to 1, per this worker's
    ///     bound to one documentation repair.
    /// </param>
    /// <param name="maxCodeRepairAttempts">
    ///     The most code-repair attempts spent when a verdict names <see cref="VerificationVerdict.CodeRepairRequired" />
    ///     or <see cref="VerificationVerdict.BothRepairsRequired" />, independent of
    ///     <paramref name="maxDocumentationRepairAttempts" />. Must be zero or greater; defaults to 1, per this
    ///     worker's bound to one code repair.
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
    ///     Runs the repository's strict contract check, or null to run <c>check-contracts.ps1 -Strict</c> through
    ///     the PowerShell host. <see cref="DeterministicCheck" />'s own <c>selector</c> parameter is evidence
    ///     metadata only and is never forwarded to the script it runs, so a fixed <c>-Strict</c> argument is baked
    ///     into the default delegate here rather than threaded through that parameter. Injected so the check is
    ///     exercisable without a real script.
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
    ///     Thrown when <paramref name="maxDocumentationRepairAttempts" /> or <paramref name="maxCodeRepairAttempts" />
    ///     is negative.
    /// </exception>
    public ContractChangeWorker(
        string repositoryRoot,
        string documentAuthorCharter,
        string developerCharter,
        string verifierCharter,
        int maxDocumentationRepairAttempts = 1,
        int maxCodeRepairAttempts = 1,
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

        var root = Path.GetFullPath(repositoryRoot);

        _repositoryRoot = root;
        _documentAuthor = new DocumentAuthor(root, documentAuthorCharter, endpointFor: endpointFor);
        _developer = new Developer(root, developerCharter, endpointFor: endpointFor);
        _buildCheck = new DeterministicCheck(root, runScript: buildRunScript);
        _contractCheck = new DeterministicCheck(
            root,
            runScript: contractCheckRunScript ??
                       ((script, ct) => new PowerShellScripts(root).RunAsync(script, ["-Strict"], ct)));
        _verifier = new Verifier(root, verifierCharter, endpointFor: endpointFor);
        _maxDocumentationRepairAttempts = maxDocumentationRepairAttempts;
        _maxCodeRepairAttempts = maxCodeRepairAttempts;
        _recordStore = recordStore;
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
    ///     when a repair needed a protected path; <see cref="OperationOutcome.Failed" /> with no finding when a
    ///     repair budget was spent with its named finding still open, when the verifier judged its evidence
    ///     insufficient, or when no model could be reached.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="brief" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<WorkerRunResult>> RunAsync(WorkerBrief brief, CancellationToken cancellationToken)
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
                ComposeCodeInstruction(brief, documentChanges!), parentInvocationId, "Developer", cancellationToken)
            .ConfigureAwait(false);
        if (developerTerminal is not null)
            return developerTerminal;

        var documentation = documentChanges!;
        var code = initialCode!;

        var documentationRepairBudget = _maxDocumentationRepairAttempts;
        var codeRepairBudget = _maxCodeRepairAttempts;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var buildCheck = await _buildCheck
                .RunAsync("build.ps1 check", BuildScript, null, cancellationToken)
                .ConfigureAwait(false);
            RecordStep(parentInvocationId, "DeterministicCheck:build.ps1", buildCheck.Outcome);

            var contractCheck = await _contractCheck
                .RunAsync("check-contracts -Strict check", ContractCheckScript, null, cancellationToken)
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
                return new StepResult<WorkerRunResult>(
                    OperationOutcome.Succeeded, new WorkerRunResult.Completed(Merge(documentation, code)), []);

            if (verified.Outcome == OperationOutcome.Escalated)
                return new StepResult<WorkerRunResult>(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Reroute(RerouteReason(verified.Finding), [.. verified.Finding?.RequiredFixes ?? []], null),
                    []);

            if (verified.Outcome == OperationOutcome.Refused)
                return new StepResult<WorkerRunResult>(OperationOutcome.Failed, null, verified.Notes);

            var verdict = verified.Finding?.Verdict;
            var fixes = verified.Finding?.RequiredFixes ?? [];

            var needsDocumentationRepair =
                verdict is VerificationVerdict.DocumentationRepairRequired or VerificationVerdict.BothRepairsRequired;
            var needsCodeRepair =
                verdict is VerificationVerdict.CodeRepairRequired or VerificationVerdict.BothRepairsRequired;

            if (needsDocumentationRepair)
            {
                if (documentationRepairBudget <= 0)
                    return new StepResult<WorkerRunResult>(
                        OperationOutcome.Failed,
                        null,
                        [new ProcessNote(
                            "the documentation-repair budget was already spent when another documentation finding arrived")]);

                documentationRepairBudget--;

                var (documentRepairTerminal, repairedDocument) = await RunDocumentAuthorAsync(
                        ComposeRepairInstruction(ComposeDocumentInstruction(brief), fixes),
                        parentInvocationId,
                        "DocumentAuthor:repair",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (documentRepairTerminal is not null)
                    return documentRepairTerminal;
                documentation = repairedDocument!;

                // No primitive reports whether a documentation repair altered an obligation the code must now
                // satisfy, so the code pass is re-run unconditionally to stay in sync rather than guessed at.
                // This re-run is not spent from the code-repair budget: it is necessitated by the documentation
                // repair, not a separate finding the verifier raised - see the Apply Report's judgment call.
                var (resyncTerminal, resyncCode) = await RunDeveloperAsync(
                        ComposeCodeInstruction(brief, documentation),
                        parentInvocationId,
                        "Developer:resync",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (resyncTerminal is not null)
                    return resyncTerminal;
                code = resyncCode!;

                continue;
            }

            if (needsCodeRepair)
            {
                if (codeRepairBudget <= 0)
                    return new StepResult<WorkerRunResult>(
                        OperationOutcome.Failed,
                        null,
                        [new ProcessNote(
                            "the code-repair budget was already spent when another code finding arrived")]);

                codeRepairBudget--;

                var (codeRepairTerminal, repairedCode) = await RunDeveloperAsync(
                        ComposeRepairInstruction(ComposeCodeInstruction(brief, documentation), fixes),
                        parentInvocationId,
                        "Developer:repair",
                        cancellationToken)
                    .ConfigureAwait(false);
                if (codeRepairTerminal is not null)
                    return codeRepairTerminal;
                code = repairedCode!;

                continue;
            }

            // Every named verdict is handled above; an unnamed one is treated as a blocking failure rather than
            // silently passing.
            return new StepResult<WorkerRunResult>(OperationOutcome.Failed, null, verified.Notes);
        }
    }

    private async Task<(StepResult<WorkerRunResult>? Terminal, DocumentChangeSet? Changes)> RunDocumentAuthorAsync(
        string instruction, string parentInvocationId, string stepName, CancellationToken cancellationToken)
    {
        var result = await _documentAuthor.AuthorAsync(instruction, cancellationToken).ConfigureAwait(false);
        RecordStep(parentInvocationId, stepName, result.Outcome);

        if (result.Outcome == OperationOutcome.Escalated)
            return (new StepResult<WorkerRunResult>(OperationOutcome.Escalated, null, result.Notes), null);

        if (result.Outcome != OperationOutcome.Succeeded || result.Finding is null)
            return (new StepResult<WorkerRunResult>(result.Outcome, null, result.Notes), null);

        if (result.Finding is DocumentAuthoringResult.Reroute reroute)
            return (
                new StepResult<WorkerRunResult>(
                    OperationOutcome.Succeeded, new WorkerRunResult.Reroute(reroute.Why, [], null), []),
                null);

        return (null, ((DocumentAuthoringResult.Authored)result.Finding).Changes);
    }

    private async Task<(StepResult<WorkerRunResult>? Terminal, ChangeSetSummary? Changes)> RunDeveloperAsync(
        string instruction, string parentInvocationId, string stepName, CancellationToken cancellationToken)
    {
        var result = await _developer.DevelopAsync(instruction, cancellationToken).ConfigureAwait(false);
        RecordStep(parentInvocationId, stepName, result.Outcome);

        if (result.Outcome == OperationOutcome.Escalated)
            return (new StepResult<WorkerRunResult>(OperationOutcome.Escalated, null, result.Notes), null);

        if (result.Outcome != OperationOutcome.Succeeded || result.Finding is null)
            return (new StepResult<WorkerRunResult>(result.Outcome, null, result.Notes), null);

        if (result.Finding is DevelopmentResult.Reroute reroute)
            return (
                new StepResult<WorkerRunResult>(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Reroute(reroute.Why, [], reroute.SuggestedWorker), []),
                null);

        return (null, ((DevelopmentResult.Completed)result.Finding).Summary);
    }

    private void RecordStep(string parentInvocationId, string step, OperationOutcome outcome) =>
        _recordStore?.Append(
            new ProcessStepRecord(DateTimeOffset.UtcNow, parentInvocationId, step, outcome.ToString(), null, null));

    private static ChangeSetSummary Merge(DocumentChangeSet document, ChangeSetSummary code) =>
        new([.. document.FilesChanged, .. code.FilesChanged], $"{document.Summary} {code.Summary}".Trim());

    private static string RerouteReason(VerificationFinding? finding) =>
        finding is null || finding.RequiredFixes.Count == 0
            ? "the verifier concluded this change needs to be rerouted, with no further reason recorded"
            : string.Join("; ", finding.RequiredFixes);

    private string ComposeDocumentInstruction(WorkerBrief brief) =>
        $"""
         {brief.OriginalWorkItem}

         Update the affected system contract document(s) under docs/architecture/ for this change, and prune
         any section document whose content no longer earns its place. Do not touch code or tests.

         Why this worker was selected: {brief.ScopeHint}

         <research-findings>
         {RenderResearch(brief)}
         </research-findings>

         <prior-reroutes>
         {RenderReroutes(brief)}
         </prior-reroutes>

         <standards>
         {WorkerStandards.Render(_repositoryRoot, DocumentAuthorStandards)}
         </standards>
         """;

    private string ComposeCodeInstruction(WorkerBrief brief, DocumentChangeSet documentChanges) =>
        $"""
         {brief.OriginalWorkItem}

         Implement code and tests against the contract clauses this change just updated:
         {documentChanges.Summary}

         Files the documentation pass touched: {(documentChanges.FilesChanged.Count == 0 ? "none" : string.Join(", ", documentChanges.FilesChanged))}

         Why this worker was selected: {brief.ScopeHint}

         <research-findings>
         {RenderResearch(brief)}
         </research-findings>

         <standards>
         {WorkerStandards.Render(_repositoryRoot, DeveloperStandards)}
         </standards>
         """;

    private static string ComposeRepairInstruction(string originalInstruction, IReadOnlyList<string> requiredFixes) =>
        requiredFixes.Count == 0
            ? originalInstruction
            : $"""
               {originalInstruction}

               The previous attempt's verification reported these required fixes:
               {string.Join("\n", requiredFixes)}

               Repair the issue.
               """;

    private static string RenderResearch(WorkerBrief brief) =>
        brief.RelevantResearchFindings.Count == 0
            ? "none"
            : string.Join("\n", brief.RelevantResearchFindings.Select(finding => $"- {finding.Answer}"));

    private static string RenderReroutes(WorkerBrief brief) =>
        brief.PriorReroutes.Count == 0
            ? "none"
            : string.Join("\n", brief.PriorReroutes.Select(reroute => $"- from '{reroute.WorkerKey}': {reroute.Why}"));
}
