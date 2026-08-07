using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Primitives;

/// <summary>
///     Implemented by an <see cref="Oracle{TDecision}" />'s decoded answer so the oracle can tell a genuine
///     judgement apart from one reached without enough evidence, without needing to know anything else about the
///     shape of the decision.
/// </summary>
/// <remarks>
///     A marker rather than an enum member on <see cref="OperationOutcome" />: refusal is a fact about the
///     invocation, decided once decoding succeeds, and every typed decision this pass or a later one defines
///     states that fact about itself the same way <see cref="Operations.RuleOwnerAnswer" /> already does for
///     <c>probe-rule-owner</c>.
/// </remarks>
internal interface IOracleDecision
{
    /// <summary>
    ///     Whether the evidence available to the probe was enough to reach this decision honestly.
    /// </summary>
    bool HasSufficientEvidence { get; }
}

/// <summary>
///     Asks a narrow typed judgement question of a model, and decodes the reply into <typeparamref name="TDecision" />.
/// </summary>
/// <remarks>
///     The thinnest possible composition of the Model Seam: a single <see cref="ModelSession.ProbeAsync{T}" />
///     call, schema last, with the caller's context artifacts folded into the question rather than left for the
///     model to go looking for. There is no <see cref="ModelSession.RunAsync" /> pass here and no tools are
///     granted — an oracle answers from what it is given, which is what keeps it narrow; a question that needs to
///     go look at the repository first is <see cref="Research" />, not this.
///     <para>
///         Thread safety: instances are immutable and safe to share, but each call opens its own conversation.
///     </para>
/// </remarks>
/// <typeparam name="TDecision">
///     The typed decision this oracle answers with. Its public properties are described to the model as the
///     probe's schema, and it must be able to state honestly whether it had enough evidence to answer.
/// </typeparam>
internal sealed class Oracle<TDecision> where TDecision : IOracleDecision
{
    private readonly string _repositoryRoot;
    private readonly string _charter;
    private readonly ModelRole _role;
    private readonly int _maxOutputTokens;
    private readonly int _maxParseRetries;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;

    /// <summary>
    ///     Binds an oracle to a repository and the charter every question of it carries.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository whose configuration names the models behind the capability roles. Must not be null or
    ///     blank.
    /// </param>
    /// <param name="charter">
    ///     The system message every question carries: who the model is being asked to be, and that refusing is a
    ///     correct answer when the evidence does not support one. Must not be null.
    /// </param>
    /// <param name="role">The capability tier this oracle's questions are served at. Defaults to <see cref="ModelRole.Light" />.</param>
    /// <param name="maxOutputTokens">
    ///     The context budget: the ceiling on generated output for the probe. Defaults to
    ///     <see cref="ModelSession.DefaultMaxOutputTokens" />.
    /// </param>
    /// <param name="maxParseRetries">
    ///     The most decode attempts a question makes after a reply fails to parse. Defaults to
    ///     <see cref="ModelSession.DefaultMaxParseRetries" />.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this primitive's whole behavior is exercisable without a network call.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="charter" /> is null.</exception>
    public Oracle(
        string repositoryRoot,
        string charter,
        ModelRole role = ModelRole.Light,
        int maxOutputTokens = ModelSession.DefaultMaxOutputTokens,
        int maxParseRetries = ModelSession.DefaultMaxParseRetries,
        Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(charter);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _charter = charter;
        _role = role;
        _maxOutputTokens = maxOutputTokens;
        _maxParseRetries = maxParseRetries;
        _endpointFor = endpointFor;
    }

    /// <summary>
    ///     Asks the question and reports the typed decision.
    /// </summary>
    /// <param name="question">The narrow question to answer. Must not be null or blank.</param>
    /// <param name="contextArtifacts">
    ///     The authoritative material the model may rely on, each entry folded into the question before the
    ///     schema. Must not be null; may be empty when the question is self-contained.
    /// </param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged.</param>
    /// <returns>
    ///     <see cref="OperationOutcome.Succeeded" /> with the decoded decision when
    ///     <see cref="IOracleDecision.HasSufficientEvidence" /> is true; <see cref="OperationOutcome.Refused" />
    ///     with the decoded decision when it is false, because the oracle answered honestly that its evidence did
    ///     not support one; <see cref="OperationOutcome.Failed" /> with no finding when no model could be reached
    ///     or no reply decoded within the retry budget.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="question" /> is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contextArtifacts" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<StepResult<TDecision>> AskAsync(
        string question, IReadOnlyList<string> contextArtifacts, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(contextArtifacts);

        cancellationToken.ThrowIfCancellationRequested();

        var roles = new ModelRoles(_repositoryRoot, _endpointFor);
        var session = new ModelSession(roles, _charter, tools: null, _maxOutputTokens, _maxParseRetries);

        try
        {
            var decision = await session
                .ProbeAsync<TDecision>(Compose(question, contextArtifacts), _role, cancellationToken)
                .ConfigureAwait(false);

            return decision.HasSufficientEvidence
                ? new StepResult<TDecision>(OperationOutcome.Succeeded, decision, [])
                : new StepResult<TDecision>(
                    OperationOutcome.Refused, decision, [new ProcessNote("evidence was insufficient to answer")]);
        }
        catch (ModelUnavailableException exception)
        {
            return new StepResult<TDecision>(OperationOutcome.Failed, default, [new ProcessNote(exception.Message)]);
        }
        catch (ModelParseException exception)
        {
            return new StepResult<TDecision>(OperationOutcome.Failed, default, [new ProcessNote(exception.Message)]);
        }
    }

    private static string Compose(string question, IReadOnlyList<string> contextArtifacts) =>
        contextArtifacts.Count == 0
            ? question
            : $"""
               {question}

               <context>
               {string.Join("\n---\n", contextArtifacts)}
               </context>
               """;
}
