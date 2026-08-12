using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Workers;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Runs a work item directly against <see cref="DocumentAuthor" /> alone, to write or update a contract
///     clause in <c>.anneal/architecture/</c> using <c>system-contracts.md</c>'s <c>TODO.</c> planned-obligation
///     form, ahead of any implementation.
/// </summary>
/// <remarks>
///     <c>.anneal/architecture/toolkit/stage-contract.md</c> is the contract this implements. It gave
///     <c>architecture-update.agent.md</c>'s one remaining job — staging a contract clause ahead of
///     implementation, as a deliberate planned obligation, per <c>.anneal/work/active-plan.md</c>'s S16 entry — a
///     compiled
///     equivalent, mirroring how <see cref="MaintainOperation" /> gave <c>apply.agent.md</c>'s old Maintenance
///     job one; <c>architecture-update.agent.md</c> itself retired once this action was live-validated. No new
///     worker type is introduced: <see cref="DocumentAuthor" /> is the exact primitive
///     <see cref="Process.Workers.ContractChangeWorker" /> and <see cref="Process.Workers.StructuralChangeWorker" /> already use
///     for their own documentation half, run here alone instead of composed with <see cref="Developer" /> and
///     <see cref="Verifier" />, and instructed to stop before either would run.
///     <para>
///         This operation constructs no <see cref="Process.Routing.Router" /> and asks no routing oracle: a caller
///         invoking <c>stage-contract</c> has already decided the work is a staged, not-yet-implemented contract
///         clause, the same "Scope already fixed before this action is reached" reasoning
///         <see cref="MaintainOperation" /> already applies to Maintenance mode.
///     </para>
/// <para>
///         <b>What this operation adds beyond composing an existing primitive.</b> Two mechanical, post-run
///         checks against what <see cref="DocumentAuthor" /> reports having changed: every changed file must
///         fall under <c>.anneal/architecture/</c> — the mirror image of <see cref="ProtectedPathTripwire" />'s
///         rule for Maintenance, since this action's whole job is to touch the architecture tree and nothing
///         else — and a non-strict, repository-wide <c>check-contracts</c> run must exit clean afterward.
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

        Update only the affected system contract document(s) under .anneal/architecture/ for this change, and
        prune any subsystem document whose content no longer earns its place. Pruning means retiring an entire
        obsolete file - never trimming or rewriting unrelated prose (Decisions, Invariants, or other clauses)
        inside a document that remains in scope. Unrelated pre-existing content must survive verbatim unless the
        declared task specifically requires revising it. Prefer the smallest targeted edit over a whole-file
        rewrite: a replace_file call on a file with pre-existing content is a scope violation unless the entire
        previous content is itself the target of the declared task. Never touch code, tests, or any file outside
        .anneal/architecture/ - that is a different pass's job, run later.

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
        "clause in .anneal/architecture/ using system-contracts.md's TODO. planned-obligation form, ahead of any " +
        "implementation. Succeeds when a clause was authored and a non-strict, repository-wide check-contracts " +
        "run exits clean; escalates when DocumentAuthor names a reroute, a protected-path write is refused, or " +
        "the reported changes reach outside .anneal/architecture/; fails when the repository's contract check " +
        "does not pass after staging, the file-count budget is exceeded and a Light-role oracle judges the file " +
        "list disproportionate to the instruction, or no model could be reached.";

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
        var author = new DocumentAuthor(
            _repositoryRoot, DocumentAuthorCharter, scopeDriftCheckInterval: 1, endpointFor: _endpointFor);

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
            var failNote = result.Notes.FirstOrDefault()?.Text;
            var failMessage = string.IsNullOrWhiteSpace(failNote)
                ? "DocumentAuthor did not complete this work."
                : failNote;
            output.WriteLine($"stage-contract: failed - {failMessage}");
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
        // rather than an unqualified success. Checked against DocumentAuthor's reported changed-file list,
        // normalized against the repository root the same way ProtectedPathTripwire normalizes a declared
        // file scope - no ledger of the model's real tool calls exists yet, so this report is the only
        // evidence available, the same evidence maintain's own equivalent check reasons from.
        var outOfScopeFile = change.FilesChanged.FirstOrDefault(file => !IsUnderArchitectureTree(file));
        if (outOfScopeFile is not null)
        {
            output.WriteLine(
                $"stage-contract: escalated - '{outOfScopeFile}' falls outside .anneal/architecture/; a person " +
                "must review this run.");
            return new OperationResult(
                OperationOutcome.Escalated,
                new StageContractReport(change.FilesChanged, change.Summary, outOfScopeFile, null, null));
        }

        // Non-strict: implementation is deliberately not yet complete, so an unfulfilled planned obligation
        // must not be promoted from a warning to an error. This runs against the whole repository, not
        // scoped to this run's own changes, so a non-zero exit here can also mean a pre-existing unrelated
        // failure elsewhere in the tree - a coarser signal than "the clause this run staged is malformed",
        // but the only one available without building check-contracts a change-scoped mode it does not
        // otherwise need.
        var check = await ContractCheckRunner.RunAsync(_repositoryRoot, cancellationToken, strict: false)
            .ConfigureAwait(false);
        if (check.ExitCode != 0)
        {
            output.WriteLine("stage-contract: failed - the repository's contract check did not pass after staging:");
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

    /// <summary>
    ///     Whether <paramref name="file" /> falls under <c>.anneal/architecture/</c> in this run's repository,
    ///     resolved the same way <see cref="ProtectedPathTripwire" /> resolves a declared file scope: normalized
    ///     against the repository root rather than matched as a raw string, so a relative path with <c>./</c> or
    ///     <c>../</c> segments, or an absolute path DocumentAuthor happened to report, cannot slip past a
    ///     literal-prefix check that a real model's own output would defeat.
    /// </summary>
    private bool IsUnderArchitectureTree(string file)
    {
        var candidate = Path.IsPathRooted(file) ? file : Path.Combine(_repositoryRoot, file);
        var fullPath = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(_repositoryRoot, fullPath).Replace('\\', '/');

        return !relative.StartsWith("..", StringComparison.Ordinal) &&
               relative.StartsWith(".anneal/architecture/", StringComparison.OrdinalIgnoreCase);
    }
}
