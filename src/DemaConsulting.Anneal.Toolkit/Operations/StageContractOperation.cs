using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Runs a work item directly against <see cref="DocumentAuthor" /> alone, to write or update a contract
///     clause in <c>docs/architecture/</c> using <c>system-contracts.md</c>'s <c>TODO.</c> planned-obligation
///     form, ahead of any implementation.
/// </summary>
/// <remarks>
///     <c>docs/architecture/toolkit/stage-contract.md</c> is the contract this implements. It gives
///     <c>architecture-update.agent.md</c>'s one remaining job — staging a contract clause ahead of
///     implementation, as a deliberate planned obligation, per <c>MIGRATION.md</c>'s S16 entry — a compiled
///     equivalent, mirroring how <see cref="MaintainOperation" /> gave <c>apply.agent.md</c>'s old Maintenance
///     job one. No new worker type is introduced: <see cref="DocumentAuthor" /> is the exact primitive
///     <see cref="Process.ContractChangeWorker" /> and <see cref="Process.StructuralChangeWorker" /> already use
///     for their own documentation half, run here alone instead of composed with <see cref="Developer" /> and
///     <see cref="Verifier" />, and instructed to stop before either would run.
///     <para>
///         This operation constructs no <see cref="Process.Router" /> and asks no routing oracle: a caller
///         invoking <c>stage-contract</c> has already decided the work is a staged, not-yet-implemented contract
///         clause, the same "Scope already fixed before this action is reached" reasoning
///         <see cref="MaintainOperation" /> already applies to Maintenance mode.
///     </para>
///     <para>
///         <b>What this operation adds beyond composing an existing primitive.</b> Two mechanical, post-run
///         checks against what <see cref="DocumentAuthor" /> actually changed, never against its own self-report:
///         every changed file must fall under <c>docs/architecture/</c> — the mirror image of
///         <see cref="ProtectedPathTripwire" />'s rule for Maintenance, since this action's whole job is to touch
///         the architecture tree and nothing else — and the staged clause must pass a non-strict
///         <c>check-contracts</c> run, proving it is well-formed even though it is deliberately unfulfilled.
///         Non-strict, not <see cref="ContractCheckRunner" />'s default, because a staged clause's unfulfilled
///         obligation is exactly what <c>-Strict</c> would otherwise promote from a warning to an error — see
///         <c>system-contracts.md</c>'s own "use <c>-Strict</c> once implementation is complete" rule.
///     </para>
///     <para>
///         It declares <see cref="OperationCategory.Authoring" />, the same category <see cref="RouteOperation" />
///         and <see cref="MaintainOperation" /> declare, for the same reason: <see cref="DocumentAuthor" /> edits
///         the repository, and nothing that edits the repository may also decide whether a build passes.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two workers would.
///     </para>
/// </remarks>
public sealed class StageContractOperation : IOperation
{
    /// <summary>The system message <see cref="DocumentAuthor" />'s pass carries for a <c>stage-contract</c> run.</summary>
    private const string DocumentAuthorCharter =
        """
        You are staging a contract clause ahead of implementation: the promise is written now, and the code
        that fulfills it is written later, by a separate pass. You have tools to read the repository and to
        edit files in it. Use them on the real files rather than reasoning from memory: before concluding a
        named path does not exist, read it directly with your read-file tool or list its containing directory
        - never conclude a file is missing from a text search alone.

        Update only the affected system contract document(s) under docs/architecture/ for this change, and
        prune any section document whose content no longer earns its place. Never touch code, tests, or any
        file outside docs/architecture/ - that is a different pass's job, run later.

        Every clause you add or change must name its verifier in the placeholder form system-contracts.md
        defines: an uppercase 'TODO.' or 'TODO_' opening the verifier string, followed by the name the test
        will eventually take (for example, `TODO.AcceptedRecordIsDurable`) - never a real test name, since no
        implementation exists yet for a real test to verify.

        If, while working, you discover this item does not actually need a staged, not-yet-implemented clause
        at all - because implementation is already possible in the same pass, or because it is not a contract
        change - say so and name the worker you believe is right rather than silently widening your own scope
        or authoring a clause nobody asked to stage.
        """;

    private readonly string _repositoryRoot;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;

    /// <summary>
    ///     Creates an operation over the current working directory, consulting the configured models.
    /// </summary>
    public StageContractOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root and, optionally, a substituted model
    ///     endpoint provider.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository authored into, outside which every tool call is refused, and whose configuration
    ///     names the models behind the capability roles. Must not be null or blank.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK.
    ///     Injected so this operation's whole behavior is exercisable without a network call.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public StageContractOperation(string repositoryRoot, Func<ModelRole, IChatEndpoint>? endpointFor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _endpointFor = endpointFor;
    }

    /// <inheritdoc />
    public string Name => "stage-contract";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Authoring;

    /// <inheritdoc />
    public string Summary =>
        "Stage a contract clause ahead of implementation, as a TODO. planned obligation, using DocumentAuthor alone";

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="DocumentAuthor" /> runs at <see cref="ModelRole.Heavy" /> by default, and this operation
    ///     substitutes no other role, so <see cref="ModelRole.Heavy" /> is the most demanding role its one path
    ///     can reach - the same reasoning <see cref="RouteOperation" /> and <see cref="MaintainOperation" />
    ///     already state for their own declarations.
    /// </remarks>
    public ModelRole? RequiredRole => ModelRole.Heavy;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal stage-contract <work item> - runs <work item> directly against DocumentAuthor, " +
        "asking no routing oracle and running no Developer or Verifier pass, to write or update a contract " +
        "clause in docs/architecture/ using system-contracts.md's TODO. planned-obligation form, ahead of any " +
        "implementation. Succeeds when a clause was authored and a non-strict check-contracts run reports it " +
        "well-formed; escalates when DocumentAuthor names a reroute, a protected-path write is refused, or the " +
        "actual changes reach outside docs/architecture/; fails when the staged clause is not well-formed, " +
        "DocumentAuthor's file-count budget is exceeded, or no model could be reached.";

    /// <inheritdoc />
    /// <remarks>
    ///     Expects exactly one argument: the work item, given positionally. Reports
    ///     <see cref="OperationOutcome.UsageError" /> when it is missing or blank.
    /// </remarks>
    public async Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        // No usage line is written here: the dispatcher renders Usage - the single declared source - on the
        // usage-error path, so what a caller sees after a misuse cannot drift from what help prints.
        if (arguments.Count == 0 || string.IsNullOrWhiteSpace(arguments[0]))
            return new OperationResult(OperationOutcome.UsageError);

        var workItem = arguments[0];
        var author = new DocumentAuthor(_repositoryRoot, DocumentAuthorCharter, endpointFor: _endpointFor);

        output.WriteLine($"stage-contract: running \"{workItem}\"...");

        var result = await author.AuthorAsync(workItem, cancellationToken).ConfigureAwait(false);

        if (result.Outcome == OperationOutcome.Escalated)
        {
            output.WriteLine(
                "stage-contract: escalated - the correct change needs a protected file, which needs your " +
                "approval.");
            var authored = result.Finding as DocumentAuthoringResult.Authored;
            return new OperationResult(
                OperationOutcome.Escalated,
                new StageContractReport(
                    authored?.Changes.FilesChanged ?? [], authored?.Changes.Summary ?? string.Empty, null, null,
                    null));
        }

        if (result.Outcome != OperationOutcome.Succeeded || result.Finding is null)
        {
            output.WriteLine("stage-contract: failed - DocumentAuthor did not complete this work.");
            return new OperationResult(result.Outcome, new StageContractReport([], string.Empty, null, null, null));
        }

        if (result.Finding is DocumentAuthoringResult.Reroute reroute)
        {
            output.WriteLine($"stage-contract: escalated - DocumentAuthor named a better owner: {reroute.Why}");
            return new OperationResult(
                OperationOutcome.Escalated,
                new StageContractReport([], string.Empty, null, null, reroute.Why));
        }

        var change = ((DocumentAuthoringResult.Authored)result.Finding).Changes;

        // The mirror image of ProtectedPathTripwire's rule for Maintenance: this action's whole job is to
        // touch the architecture tree and nothing else, so a change reaching outside it is a stop condition
        // rather than an unqualified success, checked against the actual changed-file list, never against
        // DocumentAuthor's own self-report.
        var outOfScopeFile = change.FilesChanged.FirstOrDefault(file => !IsUnderArchitectureTree(file));
        if (outOfScopeFile is not null)
        {
            output.WriteLine(
                $"stage-contract: escalated - '{outOfScopeFile}' falls outside docs/architecture/; a person " +
                "must review this run.");
            return new OperationResult(
                OperationOutcome.Escalated,
                new StageContractReport(change.FilesChanged, change.Summary, outOfScopeFile, null, null));
        }

        // Non-strict: implementation is deliberately not yet complete, so an unfulfilled planned obligation
        // must not be promoted from a warning to an error. A non-zero exit here means the staged clause itself
        // is malformed (for example, not a well-formed clause ID, or missing a *Verified by:* line entirely),
        // which is a real defect this operation can catch mechanically.
        var check = await ContractCheckRunner.RunAsync(_repositoryRoot, cancellationToken, strict: false)
            .ConfigureAwait(false);
        if (check.ExitCode != 0)
        {
            output.WriteLine("stage-contract: failed - the staged clause is not well-formed:");
            output.WriteLine(check.Output);
            return new OperationResult(
                OperationOutcome.Failed,
                new StageContractReport(change.FilesChanged, change.Summary, null, check.Output, null));
        }

        foreach (var file in change.FilesChanged)
            output.WriteLine($"  {file}");
        output.WriteLine($"stage-contract: completed - {change.Summary}");
        return new OperationResult(
            OperationOutcome.Succeeded,
            new StageContractReport(change.FilesChanged, change.Summary, null, null, null));
    }

    private static bool IsUnderArchitectureTree(string file)
    {
        var normalized = file.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        return normalized.StartsWith("docs/architecture/", StringComparison.Ordinal);
    }
}
