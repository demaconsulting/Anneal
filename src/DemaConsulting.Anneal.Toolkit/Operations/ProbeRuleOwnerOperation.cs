using DemaConsulting.Anneal.Toolkit.Model;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Names the single file that owns a given rule, or refuses when the rule is stated in more than one place
///     or in none.
/// </summary>
/// <remarks>
///     This is the first model-backed operation, and it exists because the question is real: this process is
///     built on every rule having exactly one owner, and nothing until now could check it. It is also the
///     operation that makes refusal honest. A deterministic check cannot refuse — its inputs settle its
///     question — but "three files say this, so no one file owns it" is a true answer that is neither success
///     nor failure, and reporting it as either would be a lie a caller could not detect.
///     <para>
///         The work is done in two passes, and the ordering is the whole point. The first reads the repository
///         with read-only tools and no schema in sight, so the model reasons over actual files rather than over
///         a set of fields it is trying to fill. The second presents the schema last, with no tools, and decodes.
///         Asking for the structure up front is the known-degraded configuration this system was built to avoid.
///     </para>
///     <para>
///         It declares <see cref="OperationCategory.Research" />: its answer is a judgement obtained from a
///         model, it can differ between runs, and nothing that can differ between runs may fail a build.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but each call opens its own conversation.
///     </para>
/// </remarks>
public sealed class ProbeRuleOwnerOperation : IOperation
{
    /// <remarks>
    ///     States what the model is and what it may rely on, and — as important — what it may not do: guess. The
    ///     charter is where refusal is made an acceptable answer, because a model that believes it must produce
    ///     an owner will produce one.
    /// </remarks>
    private const string Charter =
        """
        You are examining a software repository to determine where a rule is written down.

        You have read-only tools. Use them to look at the real files rather than reasoning from memory.
        A rule "owned" by a file is stated there in full; a file that links to, or defers to, another file
        does not own the rule. Repeating a rule in a second file is a defect in this repository, so if you
        find a rule stated in more than one place, say so rather than choosing a favorite.

        Refusing is a correct answer. Never guess an owner you did not find.
        """;

    private readonly string _repositoryRoot;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;

    /// <summary>
    ///     Creates an operation that reads the current working directory and consults the configured models.
    /// </summary>
    /// <remarks>
    ///     The working directory is the repository root when the tool is invoked as a repository's own tool.
    /// </remarks>
    public ProbeRuleOwnerOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root and, optionally, a substituted provider.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository the probe reads, whose configuration names the models behind the capability roles, and
    ///     outside which every tool call is refused. Must not be null or blank.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so the operation's whole behavior — the two passes, decoding, retry and refusal — is
    ///     exercisable without a network call; a contract test that needed a live model would be a broken test,
    ///     not an acceptable one. It substitutes the provider and never the mapping: which model serves a role
    ///     stays the repository configuration's decision on every path.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public ProbeRuleOwnerOperation(string repositoryRoot, Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _endpointFor = endpointFor;
    }

    /// <inheritdoc />
    public string Name => "probe-rule-owner";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Research;

    /// <inheritdoc />
    public string Summary => "Name the single file that owns a rule, or refuse when it is stated in several or in none";

    /// <inheritdoc />
    /// <remarks>
    ///     The middle tier: the reasoning pass reads real files through tools and has to hold what it found
    ///     across several of them, which the cheapest tier does poorly and the capable tier does no better for
    ///     several times the price. Which model serves that tier is not stated here and cannot be — it is read
    ///     from the repository's configuration.
    /// </remarks>
    public ModelRole? RequiredRole => ModelRole.Medium;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal probe-rule-owner <rule> - names the single file that owns <rule>, stated as " +
        "plain text as you would state it to a colleague, or refuses when no one file owns it.";

    /// <inheritdoc />
    /// <remarks>
    ///     Expects exactly one argument: the rule, stated as a caller would state it to a colleague, and given
    ///     positionally rather than behind an option. Reports <see cref="OperationOutcome.UsageError" /> when
    ///     that argument is missing, blank or accompanied by anything else,
    ///     <see cref="OperationOutcome.Refused" /> when the rule has no single owner,
    ///     <see cref="OperationOutcome.Failed" /> when no model could be reached or no reply decoded, and
    ///     <see cref="OperationOutcome.Succeeded" /> only when one file was named.
    ///     <para>
    ///         The decoded <see cref="RuleOwnerAnswer" /> is carried back as the finding on every path that
    ///         obtained one, refusal included: what the probe concluded is data, and the lines written to the
    ///         writer are a rendering of it rather than the only way out of the operation.
    ///     </para>
    /// </remarks>
    public async Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        // Withdrawn before it began is still withdrawn: no model is consulted and no outcome is invented.
        cancellationToken.ThrowIfCancellationRequested();

        // No usage line is written here: the dispatcher renders Usage - the single declared source - on the
        // usage-error path, so the text a caller sees after a misuse cannot drift from what help prints.
        if (arguments.Count != 1 || string.IsNullOrWhiteSpace(arguments[0]))
            return new OperationResult(OperationOutcome.UsageError);

        var rule = arguments[0];

        try
        {
            return Report(output, rule, await Ask(rule, cancellationToken).ConfigureAwait(false));
        }
        catch (ModelUnavailableException exception)
        {
            // Named, and not softened into a deterministic guess: an operation that answers anyway is one whose
            // caller cannot tell which answer they received. No finding, because none was obtained.
            output.WriteLine($"probe-rule-owner: no judgement was obtained - {exception.Message}");
            return new OperationResult(OperationOutcome.Failed);
        }
        catch (ModelParseException exception)
        {
            output.WriteLine($"probe-rule-owner: no judgement was obtained - {exception.Message}");
            return new OperationResult(OperationOutcome.Failed);
        }
    }

    private async Task<RuleOwnerAnswer> Ask(string rule, CancellationToken cancellationToken)
    {
        // Roles are bound to models here and nowhere else in this operation: the mapping is read from the
        // repository's configuration, so this file names a tier and never a model.
        var roles = new ModelRoles(_repositoryRoot, _endpointFor);
        var session = new ModelSession(roles, Charter, RepositoryReadTools.CreateAll(_repositoryRoot));

        // Pass one: free-form, tools in scope, no schema. Nothing is decoded here, so there is nothing to
        // re-prompt against - the reply is reasoning, not transport. It runs at the tier this operation
        // declares it requires.
        await session.RunAsync(
                $"""
                 Find where this rule is stated in the repository:

                 {rule}

                 Search for it, read the files that look relevant, and report what each of them actually says
                 about it. Name the files by their repository-relative paths.
                 """,
                RequiredRole,
                cancellationToken)
            .ConfigureAwait(false);

        // Pass two: the schema, last, with no tools. The framework supplies the schema block; this question
        // cannot state it and cannot place it earlier. Extraction is framework work, so it takes the role the
        // seam defaults a probe to rather than the one this operation's own reasoning requires.
        return await session.ProbeAsync<RuleOwnerAnswer>(
                "From what you just found, report which single file owns the rule. If more than one file states " +
                "it, or no file does, say so and leave the owning file empty.",
                role: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <remarks>
    ///     The answer is returned beside the outcome as well as rendered, so a composing caller reads the typed
    ///     value rather than these lines. The outcome is not folded into it: a refusal is a fact about the
    ///     invocation, while the answer is what the probe found.
    /// </remarks>
    private static OperationResult Report(TextWriter output, string rule, RuleOwnerAnswer answer)
    {
        output.WriteLine($"probe-rule-owner: {rule}");
        output.WriteLine($"  evidence: {answer.Evidence}");

        var outcome = WriteVerdict(output, answer);
        return new OperationResult(outcome, answer);
    }

    private static OperationOutcome WriteVerdict(TextWriter output, RuleOwnerAnswer answer)
    {
        switch (answer.Ownership)
        {
            case RuleOwnership.SingleOwner when !string.IsNullOrWhiteSpace(answer.OwningFile):
                output.WriteLine($"  owner: {answer.OwningFile}");
                return OperationOutcome.Succeeded;

            // A single owner with no file named is not an answer, whatever the model called it, so it is
            // reported as the refusal it actually is rather than as a success with a blank owner.
            case RuleOwnership.SingleOwner:
                output.WriteLine("  refused: a single owner was claimed but no file was named.");
                return OperationOutcome.Refused;

            case RuleOwnership.StatedInSeveralPlaces:
                output.WriteLine("  refused: the rule is stated in more than one place, so no single file owns it.");
                return OperationOutcome.Refused;

            default:
                output.WriteLine("  refused: the rule is stated nowhere in this repository.");
                return OperationOutcome.Refused;
        }
    }

}
