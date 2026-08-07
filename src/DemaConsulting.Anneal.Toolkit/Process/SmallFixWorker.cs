using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;

namespace DemaConsulting.Anneal.Toolkit.Process;

/// <summary>
///     The cheap-path worker: <see cref="Developer" /> authors the change, <see cref="DeterministicCheck" /> runs
///     <c>build.ps1</c>, and one local repair pass sends the exact deterministic failures back to
///     <see cref="Developer" /> before the worker gives up and finishes failed. No <see cref="Planner" />, no
///     <see cref="DocumentAuthor" />, no model-backed <see cref="Verifier" /> — this worker is deliberately the
///     path that pays for neither planning nor a second model-backed judgement pass.
/// </summary>
/// <remarks>
///     Composed from <see cref="RepairLoop{TState}" /> bounded to one repair attempt, closing over
///     <see cref="Developer.DevelopAsync" /> as the execute step and a deterministic mapping of
///     <see cref="DeterministicCheck" />'s own <see cref="CheckFinding" /> as the verify step — never a
///     <see cref="Verifier" />, because judging "did the deterministic build check pass" needs no model call at
///     all, and composing one here would be exactly the "universal quality pass" `docs/architecture/process.md`
///     § Decisions already rejected once for a different reason.
///     <para>
///         <b>When this worker reroutes.</b> This worker adds no reroute logic of its own: a reroute is
///         <see cref="Developer" />'s own <see cref="DevelopmentResult.Reroute" /> finding, surfaced unchanged. That
///         happens when <see cref="Developer" /> itself judges, from the instruction and what it found while
///         authoring, that the change: (1) needs a contract clause to change (Contract Change worker's job, not
///         this one's); (2) needs a system-boundary move (Structural Change worker's job); or (3) is actually
///         template synchronization (Template Sync worker's job). This worker does not need new logic to detect
///         any of these — it only needs to pass <see cref="Developer" />'s own reroute decision through to the
///         <see cref="Router" /> rather than grinding a repair against a build check that was never going to save
///         a misclassified change.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
internal sealed class SmallFixWorker
{
    /// <summary>The repository-relative build/test script this worker's deterministic check runs.</summary>
    private const string BuildScript = "build.ps1";

    private readonly Developer _developer;
    private readonly DeterministicCheck _check;
    private readonly RepairLoop<DevelopmentResult> _repairLoop;

    /// <summary>
    ///     Binds a Small Fix worker to a repository and the charter its authoring pass carries.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository authored into and checked. Must not be null or blank.
    /// </param>
    /// <param name="developerCharter">
    ///     The system message <see cref="Developer" />'s authoring pass carries. Must not be null.
    /// </param>
    /// <param name="maxRepairAttempts">
    ///     The most local repair attempts spent sending <c>build.ps1</c>'s exact failures back to
    ///     <see cref="Developer" /> before the worker reports failed. Must be zero or greater; defaults to 1, per
    ///     this worker's bound to a single local repair pass.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this worker's whole behavior is exercisable without a network call.
    /// </param>
    /// <param name="runScript">
    ///     Runs the repository's <c>build.ps1</c>, or null to run it through the PowerShell host. Injected so the
    ///     deterministic check is exercisable without a real build.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="developerCharter" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxRepairAttempts" /> is negative.</exception>
    public SmallFixWorker(
        string repositoryRoot,
        string developerCharter,
        int maxRepairAttempts = 1,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? runScript = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(developerCharter);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRepairAttempts);

        _developer = new Developer(repositoryRoot, developerCharter, endpointFor: endpointFor);
        _check = new DeterministicCheck(repositoryRoot, runScript: runScript);
        _repairLoop = new RepairLoop<DevelopmentResult>(maxRepairAttempts);
    }

    /// <summary>
    ///     Runs the worker against a deterministically-projected brief.
    /// </summary>
    /// <param name="brief">What to change, and the context the router gathered for it.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> with <see cref="WorkerRunResult.Completed" /> when the change
    ///     was authored and <c>build.ps1</c> passed, with or without the one local repair; <see cref="OperationOutcome.Succeeded" />
    ///     with <see cref="WorkerRunResult.Reroute" /> when <see cref="Developer" /> named a better owner;
    ///     <see cref="OperationOutcome.Escalated" /> when a repair needed a protected path;
    ///     <see cref="OperationOutcome.Failed" /> with no finding when the repair budget was spent with
    ///     <c>build.ps1</c> still failing, or when no model could be reached.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="brief" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<WorkerRunResult>> RunAsync(WorkerBrief brief, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);

        cancellationToken.ThrowIfCancellationRequested();

        var instruction = ComposeInstruction(brief);

        // The initial state is never read by Execute (which builds its result from `instruction` and
        // `requiredFixes` alone, not from a prior state), so a null starting point is safe here.
        var final = await _repairLoop
            .RunAsync(
                null!,
                (state, requiredFixes, ct) => Execute(instruction, requiredFixes, ct),
                Verify,
                cancellationToken)
            .ConfigureAwait(false);

        return final switch
        {
            { Outcome: OperationOutcome.Succeeded, Finding: DevelopmentResult.Completed completed } =>
                new StepResult<WorkerRunResult>(
                    OperationOutcome.Succeeded, new WorkerRunResult.Completed(completed.Summary), []),

            { Outcome: OperationOutcome.Refused, Finding: DevelopmentResult.Reroute reroute } =>
                new StepResult<WorkerRunResult>(
                    OperationOutcome.Succeeded,
                    new WorkerRunResult.Reroute(reroute.Why, [], reroute.SuggestedWorker),
                    []),

            _ => new StepResult<WorkerRunResult>(final.Outcome, null, final.Notes)
        };
    }

    private Task<StepResult<DevelopmentResult>> Execute(
        string instruction, IReadOnlyList<string> requiredFixes, CancellationToken cancellationToken)
    {
        var composed = requiredFixes.Count == 0
            ? instruction
            : $"""
               {instruction}

               The previous attempt's deterministic build check ({BuildScript}) reported:
               {string.Join("\n", requiredFixes)}

               Repair the issue.
               """;

        return _developer.DevelopAsync(composed, cancellationToken);
    }

    /// <remarks>
    ///     A <see cref="DevelopmentResult.Reroute" /> state is terminal here, not a repair candidate — verifying
    ///     "did the build pass" makes no sense against a change the developer itself said belongs elsewhere. It is
    ///     reported as <see cref="OperationOutcome.Refused" /> so <see cref="RepairLoop{TState}" /> stops
    ///     immediately without spending repair budget, per its own contract for <see cref="OperationOutcome.Refused" />
    ///     and <see cref="OperationOutcome.Escalated" /> verification results, while still preserving the state (the
    ///     reroute finding itself) for <see cref="RunAsync" /> to read back out.
    /// </remarks>
    private async Task<StepResult<VerificationFinding>> Verify(DevelopmentResult state, CancellationToken cancellationToken)
    {
        if (state is DevelopmentResult.Reroute)
            return new StepResult<VerificationFinding>(
                OperationOutcome.Refused, null, [new ProcessNote("the developer named a better owner for this change")]);

        var check = await _check
            .RunAsync("build.ps1 check", BuildScript, null, cancellationToken)
            .ConfigureAwait(false);

        return check.Outcome == OperationOutcome.Succeeded
            ? new StepResult<VerificationFinding>(
                OperationOutcome.Succeeded,
                new VerificationFinding
                {
                    Verdict = VerificationVerdict.Passed,
                    RequiredFixes = [],
                    AdvisoryNotes = [],
                    EvidenceSufficient = true
                },
                [])
            : new StepResult<VerificationFinding>(
                OperationOutcome.Failed,
                new VerificationFinding
                {
                    Verdict = VerificationVerdict.CodeRepairRequired,
                    RequiredFixes = [check.Finding?.Summary ?? "build.ps1 failed"],
                    AdvisoryNotes = [],
                    EvidenceSufficient = true
                },
                check.Notes);
    }

    private static string ComposeInstruction(WorkerBrief brief)
    {
        var research = brief.RelevantResearchFindings.Count == 0
            ? "none"
            : string.Join("\n", brief.RelevantResearchFindings.Select(finding => $"- {finding.Answer}"));

        var reroutes = brief.PriorReroutes.Count == 0
            ? "none"
            : string.Join("\n", brief.PriorReroutes.Select(reroute => $"- from '{reroute.WorkerKey}': {reroute.Why}"));

        return $"""
                {brief.OriginalWorkItem}

                Why this worker was selected: {brief.ScopeHint}

                <research-findings>
                {research}
                </research-findings>

                <prior-reroutes>
                {reroutes}
                </prior-reroutes>
                """;
    }
}
