using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     Judges produced work against staged deterministic evidence first, and only reaches for a narrow model
///     judgement when the deterministic evidence alone cannot settle the verdict.
/// </summary>
/// <remarks>
///     "Deterministic-first" is not a hint here, it is the control flow: any failing <see cref="CheckFinding" />
///     in the evidence handed in decides <see cref="VerificationVerdict.RepairRequired" /> (with every concern
///     owned by <see cref="VerificationOwner.Code" />) without a model
///     call at all, per <c>docs/architecture/process.md</c> § Decisions, "Verification is staged
///     deterministic-first, model-second, so most failures never reach a model-backed <c>Verifier</c> at all." A
///     model is consulted only once every supplied check has passed, to judge the narrower question deterministic
///     evidence cannot answer on its own — whether the work is otherwise correct for its declared
///     <see cref="VerificationIntent" />.
///     <para>
///         No tools are granted for the model pass: a verifier judges from the evidence it was staged, the same
///         way an <see cref="Oracle{TDecision}" /> answers from what it is given, rather than going to look for
///         more — that is <see cref="Research" />'s job, staged before verification runs if it is needed.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but each call opens its own conversation.
///     </para>
/// </remarks>
internal sealed class Verifier
{
    private readonly string _repositoryRoot;
    private readonly string _charter;
    private readonly ModelRole _role;
    private readonly int _evidenceBudget;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;

    /// <summary>
    ///     Binds a verifier to a repository and the charter its model-backed pass carries.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository whose configuration names the models behind the capability roles. Must not be null or
    ///     blank.
    /// </param>
    /// <param name="charter">
    ///     The system message the model pass carries: what is being judged, and that refusing to judge on
    ///     insufficient evidence is a correct answer. Must not be null.
    /// </param>
    /// <param name="role">The capability tier the model pass runs at. Defaults to <see cref="ModelRole.Light" />.</param>
    /// <param name="evidenceBudget">
    ///     The most deterministic findings folded into the model question before the excess is dropped. Must be
    ///     greater than zero; defaults to 10.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this primitive's whole behavior is exercisable without a network call.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="charter" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="evidenceBudget" /> is not greater than zero.</exception>
    public Verifier(
        string repositoryRoot,
        string charter,
        ModelRole role = ModelRole.Light,
        int evidenceBudget = 10,
        Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(charter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(evidenceBudget);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _charter = charter;
        _role = role;
        _evidenceBudget = evidenceBudget;
        _endpointFor = endpointFor;
    }

    /// <summary>
    ///     Verifies produced work against staged deterministic evidence and a narrow model question.
    /// </summary>
    /// <param name="intent">What kind of judgement this pass is answering.</param>
    /// <param name="deterministicEvidence">
    ///     The deterministic checks already run against the work, most recent last. Must not be null; may be
    ///     empty when no deterministic check applies.
    /// </param>
    /// <param name="question">
    ///     The narrower question deterministic evidence alone cannot answer, asked only once every supplied check
    ///     has passed. Must not be null or blank.
    /// </param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Failed" /> with a <see cref="VerificationVerdict.RepairRequired" />
    ///     finding (every concern owned by <see cref="VerificationOwner.Code" />), with no model consulted, when
    ///     any supplied deterministic check failed;
    ///     <see cref="OperationOutcome.Refused" /> with the decoded finding when the model judged its evidence
    ///     insufficient; <see cref="OperationOutcome.Succeeded" /> when the verdict is
    ///     <see cref="VerificationVerdict.Passed" />; <see cref="OperationOutcome.Escalated" /> when the verdict is
    ///     <see cref="VerificationVerdict.RerouteRequired" />; <see cref="OperationOutcome.Failed" /> with the
    ///     decoded finding for every other verdict, and with no finding when no model could be reached or no reply
    ///     decoded.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deterministicEvidence" /> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="question" /> is null, empty or blank.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<VerificationFinding>> VerifyAsync(
        VerificationIntent intent,
        IReadOnlyList<CheckFinding> deterministicEvidence,
        string question,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deterministicEvidence);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        cancellationToken.ThrowIfCancellationRequested();

        // Deterministic-first: a failing check decides the verdict with no model consulted at all.
        var failing = deterministicEvidence.Where(finding => !finding.Passed).ToList();
        if (failing.Count > 0)
        {
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

        var roles = new ModelRoles(_repositoryRoot, _endpointFor);
        var session = new ModelSession(roles, _charter, tools: null);

        try
        {
            var envelope = await session
                .ProbeAsync<VerificationFinding>(Compose(intent, deterministicEvidence, question), _role, cancellationToken)
                .ConfigureAwait(false);

            if (!envelope.EvidenceSufficient)
                return new StepResult<VerificationFinding>(
                    OperationOutcome.Refused, envelope, [new ProcessNote("evidence was insufficient to judge")]);

            return envelope.Verdict switch
            {
                VerificationVerdict.Passed =>
                    new StepResult<VerificationFinding>(OperationOutcome.Succeeded, envelope, []),
                VerificationVerdict.RerouteRequired =>
                    new StepResult<VerificationFinding>(
                        OperationOutcome.Escalated,
                        envelope,
                        [new ProcessNote("the classification underneath this work needs a person to resolve")]),
                _ => new StepResult<VerificationFinding>(OperationOutcome.Failed, envelope, [])
            };
        }
        catch (ModelUnavailableException exception)
        {
            return new StepResult<VerificationFinding>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
        catch (ModelParseException exception)
        {
            return new StepResult<VerificationFinding>(
                OperationOutcome.Failed, null, [new ProcessNote(exception.Message)]);
        }
    }

    private string Compose(VerificationIntent intent, IReadOnlyList<CheckFinding> evidence, string question)
    {
        var trimmed = evidence.Count <= _evidenceBudget ? evidence : evidence.TakeLast(_evidenceBudget);
        var rendered = string.Join(
            "\n", trimmed.Select(check => $"- {check.Name}: passed ({check.Summary})"));

        return $"""
                Verification intent: {intent}

                {question}

                <deterministic-evidence>
                {(rendered.Length == 0 ? "none" : rendered)}
                </deterministic-evidence>
                """;
    }
}
