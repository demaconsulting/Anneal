using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Tools;

namespace DemaConsulting.Anneal.Toolkit.Operations;

/// <summary>
///     Drives the repository to a clean <c>lint.ps1</c>, or reports why it could not.
/// </summary>
/// <remarks>
///     The first prose agent compiled into a process, and chosen for that because its success is decided by an
///     exit code rather than by a judgement: <c>pwsh ./lint.ps1</c> returns zero or it does not. A pathfinder
///     whose success could not be checked mechanically would prove nothing, because a failure could be the
///     machinery or the judgement with no way to tell which.
///     <para>
///         The state flow is code and the fix guidance is data. Running <c>fix.ps1</c>, looping, counting the
///         budget, reading the exit code and deciding the outcome are all decided here; what to do about a
///         particular lint failure is a block of text composed into the worker's prompt, exactly as the prose
///         agent stated it. Compiling the guidance into branches is the mistake this whole design refuses — a
///         rule in released code is corrected through build, test, publish and restore, where a prompt is
///         corrected in one edit.
///     </para>
///     <para>
///         Four outcomes, not two. A clean lint succeeds and an exhausted budget fails, but a repair that
///         genuinely requires a protected configuration file <em>escalates</em>: the worker is refused the
///         write, the refusal is a recorded fact rather than a self-report, and the operation says so instead of
///         grinding its budget editing sources to work around a misconfigured linter. A contract-check failure
///         is the fourth case and is not a lint issue at all, so it is filtered out of the lint output
///         deterministically and stops the run — renaming a test to satisfy a clause is a semantic change that
///         belongs to a different process entirely.
///     </para>
///     <para>
///         It declares <see cref="OperationCategory.Authoring" />: it edits the repository, and nothing that
///         edits the repository may also decide whether the build passes.
///     </para>
///     <para>
///         Thread safety: instances are immutable and safe to share, but a run edits the working tree, so two
///         concurrent runs over one repository race exactly as two agents would.
///     </para>
/// </remarks>
public sealed class LintFixOperation : IOperation
{
    /// <summary>
    ///     The most worker iterations a run may spend, matching the bound the prose agent was given.
    /// </summary>
    /// <remarks>
    ///     Five, because it is what the prose agent used and this stage is a compilation of that agent rather
    ///     than a redesign of it. A bound at all is the point: without one, a worker that cannot fix a failure
    ///     re-reads the same output forever, and the run's cost is unbounded while its progress is zero.
    /// </remarks>
    public const int MaxIterations = 5;

    /// <summary>
    ///     The line <c>check-contracts</c> writes before its findings, which is what makes them structurally
    ///     identifiable in the lint output rather than a matter of pattern-guessing.
    /// </summary>
    internal const string ContractCheckHeading = "Checking: system contracts...";

    private const string ErrorPrefix = "  error: ";

    /// <remarks>
    ///     States what the worker is, what it may do, and — as importantly — what it must not: it fixes lint
    ///     issues and nothing else. The prohibitions are here rather than only in the tools because a model that
    ///     understands the boundary asks for fewer things it will be refused, while the tools are what make the
    ///     boundary hold when it does not.
    /// </remarks>
    private const string Charter =
        """
        You are fixing lint issues in a software repository, and nothing else.

        You have tools to read the repository and to edit files in it. Use them on the real files rather than
        reasoning from memory: read a file before you edit it, and copy the snippet you replace verbatim from
        what you read.

        Fix only lint issues. Do not refactor, restructure, or make functional changes. Never modify a file
        whose header marks it auto-generated.

        Some files are protected and your edit tools will refuse them. A refusal is a real answer, not an
        obstacle to route around: if the correct repair needs a protected file changed, say so plainly and stop
        rather than editing something else to make the symptom go away.
        """;

    /// <remarks>
    ///     Lifted from the prose <c>lint-fix</c> agent's own guidance, and kept as one block of text so it stays
    ///     data the operation composes into a prompt. It is deliberately not parsed, indexed or branched on:
    ///     the moment code reads a category out of it and decides something, the judgement has moved into the
    ///     Toolkit and correcting it costs a release.
    /// </remarks>
    private const string FixGuidance =
        """
        Guidance by failure type:

        - cspell spelling errors: correct genuine misspellings in the source text. Legitimate technical terms
          belong in the spelling dictionary, which is a protected file - if that is the correct repair, say so
          and stop.

        - markdownlint MD013 (line length): wrap long lines at natural break points, after commas, before
          conjunctions, or at sentence boundaries. Do not break in the middle of a code span or a URL.
          Pipe-tables that cannot be wrapped without breaking their structure are a special case: convert one
          to a bullet list if the data reads naturally that way, or drop a column. Do not get stuck trying to
          squeeze a wide pipe-table into the line limit.

        - markdownlint other rules: apply the specific fix the output indicates - a missing blank line, a
          heading level, a code fence language.

        - yamllint errors: fix indentation, trailing spaces, or missing document markers as indicated.

        Rules: fix only lint issues. Prefer correcting text over rewriting correct technical content. Never
        modify an auto-generated file. Respect the protected files your tools refuse.
        """;

    private readonly string _repositoryRoot;
    private readonly Func<ModelRole, IChatEndpoint>? _endpointFor;
    private readonly RunRepositoryScript _runScript;

    /// <summary>
    ///     Creates an operation over the current working directory, running the repository's own scripts through
    ///     the PowerShell host and consulting the configured models.
    /// </summary>
    /// <remarks>
    ///     The working directory is the repository root when the tool is invoked as a repository's own tool.
    /// </remarks>
    public LintFixOperation()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>
    ///     Creates an operation against an explicit repository root and, optionally, a substituted provider and
    ///     script runner.
    /// </summary>
    /// <param name="repositoryRoot">
    ///     The repository whose lint is driven clean, outside which every tool call is refused, and whose
    ///     configuration names the models behind the capability roles. Must not be null or blank.
    /// </param>
    /// <param name="endpointFor">
    ///     Supplies the endpoint driving a role, or null to drive every role through the GitHub Copilot SDK. It
    ///     substitutes the provider and never the mapping: which model serves a role stays the repository
    ///     configuration's decision on every path.
    /// </param>
    /// <param name="runScript">
    ///     Runs one of the repository's scripts, or null to run them through the PowerShell host. Injected so
    ///     the state flow — the loop, the budget, the escalation and the failure paths — is exercisable without
    ///     rebuilding and re-linting a repository for every case.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="repositoryRoot" /> is null, empty or blank.</exception>
    public LintFixOperation(
        string repositoryRoot,
        Func<ModelRole, IChatEndpoint>? endpointFor = null,
        RunRepositoryScript? runScript = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _endpointFor = endpointFor;
        _runScript = runScript ?? new PowerShellScripts(_repositoryRoot).RunAsync;
    }

    /// <inheritdoc />
    public string Name => "lint-fix";

    /// <inheritdoc />
    public OperationCategory Category => OperationCategory.Authoring;

    /// <inheritdoc />
    public string Summary => "Drive the repository to a clean lint, or report why it could not";

    /// <inheritdoc />
    /// <remarks>
    ///     The capable tier. A worker here writes to the working tree, so its mistakes are committed to disk
    ///     rather than returned for a caller to weigh, and the cheaper tiers' failure mode — a plausible edit in
    ///     the wrong place — costs more to find and undo than the tier costs to run. Which model serves that
    ///     tier is read from the repository's configuration, never named here.
    /// </remarks>
    public ModelRole? RequiredRole => ModelRole.Heavy;

    /// <inheritdoc />
    public string Usage =>
        "usage: dotnet anneal lint-fix - runs fix.ps1, then repeatedly runs lint.ps1 and has a model repair " +
        "what it reports, up to " + MaxIterations + " times. Takes no arguments. Succeeds when lint is clean, " +
        "escalates when a repair needs a protected configuration file or repository script changed, and fails " +
        "when the budget is exhausted or lint reports a contract-check failure.";

    /// <summary>
    ///     Finds the contract-check errors in a lint run's output.
    /// </summary>
    /// <remarks>
    ///     Structural rather than a search for likely-looking text: the contract check announces itself with a
    ///     heading and writes each finding as an indented <c>error:</c> line, so the block is identified by
    ///     where it starts and where the indentation stops. That matters because the whole point of separating
    ///     them is that they are <em>not</em> lint issues — a clause naming a test that does not exist is a
    ///     semantic disagreement between a contract and a test suite, and a worker told to fix lint would
    ///     resolve it by renaming one of them.
    /// </remarks>
    /// <param name="lintOutput">Everything <c>lint.ps1</c> wrote. Must not be null.</param>
    /// <returns>The contract-check errors, in the order reported. Empty when there are none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lintOutput" /> is null.</exception>
    public static IReadOnlyList<string> ContractFailuresIn(string lintOutput)
    {
        ArgumentNullException.ThrowIfNull(lintOutput);

        var failures = new List<string>();
        var inside = false;

        foreach (var line in lintOutput.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.Trim() == ContractCheckHeading)
            {
                inside = true;
                continue;
            }

            if (!inside)
                continue;

            // The check's own block is entirely indented, so the first line that is not ends it. Anything
            // after that belongs to a different check and is an ordinary lint finding.
            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                inside = false;
                continue;
            }

            if (line.StartsWith(ErrorPrefix, StringComparison.Ordinal))
                failures.Add(line[ErrorPrefix.Length..].Trim());
        }

        return failures;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Takes no arguments, so anything given is a usage error. Reports
    ///     <see cref="OperationOutcome.Succeeded" /> when <c>lint.ps1</c> exits zero,
    ///     <see cref="OperationOutcome.Escalated" /> when a worker was refused a protected path and the
    ///     iteration that followed changed nothing, and <see cref="OperationOutcome.Failed" /> when the budget
    ///     is exhausted, when lint reports a contract-check failure, or when no model could be reached.
    /// </remarks>
    public async Task<OperationResult> ExecuteAsync(
        IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        cancellationToken.ThrowIfCancellationRequested();

        // No usage line is written here: the dispatcher renders Usage - the single declared source - on the
        // usage-error path, so what a caller sees after a misuse cannot drift from what help prints.
        if (arguments.Count > 0)
            return new OperationResult(OperationOutcome.UsageError);

        try
        {
            return await DriveAsync(output, cancellationToken).ConfigureAwait(false);
        }
        catch (ModelUnavailableException exception)
        {
            // Named, never softened: an operation that carries on without the worker it needed would report a
            // budget it never spent.
            output.WriteLine($"lint-fix: no worker was available - {exception.Message}");
            return new OperationResult(
                OperationOutcome.Failed, new LintFixReport(0, string.Empty, [], []));
        }
    }

    /// <remarks>
    ///     The state flow itself, kept apart from argument handling so that every path out of it reaches one of
    ///     the four outcomes rather than falling off the end. Lint runs once more than the worker does — before
    ///     the first iteration and after the last — because the budget bounds repairs attempted, not checks
    ///     made, and a run that spent its last iteration usefully must still be able to notice it succeeded.
    /// </remarks>
    private async Task<OperationResult> DriveAsync(TextWriter output, CancellationToken cancellationToken)
    {
        // The auto-fixers first, as process control flow. They are never a tool the model may call: a worker
        // that can run commands can do anything and then report plausibly that it did not.
        output.WriteLine("lint-fix: applying automatic fixes...");
        await _runScript("fix.ps1", cancellationToken).ConfigureAwait(false);

        var session = new ModelSession(
            new ModelRoles(_repositoryRoot, _endpointFor),
            Charter,
            new ToolGroups(_repositoryRoot).SelectTools([ToolGroups.Read, ToolGroups.Edit]));

        var previousOutput = string.Empty;
        var refusedProtectedLastIteration = false;

        for (var iteration = 0; ; iteration++)
        {
            var lint = await _runScript("lint.ps1", cancellationToken).ConfigureAwait(false);

            if (lint.ExitCode == 0)
                return Clean(output, iteration);

            var contractFailures = ContractFailuresIn(lint.Output);
            if (contractFailures.Count > 0)
                return ContractFailure(output, iteration, lint.Output, contractFailures);

            // A refusal on its own is not escalation: a worker may be denied one path and repair the issue
            // another way. What settles it is the iteration after the refusal changing nothing, which is the
            // observable form of "the correct repair genuinely requires the protected file".
            //
            // Only a protected-write refusal counts. A worker denied a path outside the repository asked for
            // the wrong thing and can correct it; reporting that to the user as a protected file needing their
            // approval would be false, and a false escalation is exactly as bad as a false success.
            if (refusedProtectedLastIteration && lint.Output == previousOutput)
                return Escalate(output, iteration, lint.Output, session.RefusedProtectedWrites);

            if (iteration == MaxIterations)
                return Exhausted(output, iteration, lint.Output);

            previousOutput = lint.Output;
            var refusalsBefore = session.RefusedProtectedWrites.Count;

            output.WriteLine($"lint-fix: iteration {iteration + 1} of {MaxIterations} - repairing...");
            await session.RunAsync(Compose(lint.Output), RequiredRole, cancellationToken).ConfigureAwait(false);

            refusedProtectedLastIteration = session.RefusedProtectedWrites.Count > refusalsBefore;
        }
    }

    /// <remarks>
    ///     Lint output first, guidance last. The worker reads the guidance immediately before it acts, which is
    ///     the same ordering the schema-last probe rests on and for the same reason: an instruction given first
    ///     is far behind in the context window by the time it is needed.
    /// </remarks>
    private static string Compose(string lintOutput) =>
        $"""
         pwsh ./lint.ps1 failed. This is everything it reported:

         <lint-output>
         {lintOutput}
         </lint-output>

         Repair every issue above by editing the files it names. Read each file before you edit it.

         {FixGuidance}
         """;

    private static OperationResult Clean(TextWriter output, int iterations)
    {
        output.WriteLine($"lint-fix: lint is clean after {iterations} repair iteration(s).");
        return new OperationResult(
            OperationOutcome.Succeeded, new LintFixReport(iterations, string.Empty, [], []));
    }

    private static OperationResult ContractFailure(
        TextWriter output, int iterations, string lintOutput, IReadOnlyList<string> failures)
    {
        output.WriteLine("lint-fix: lint reports contract-check failures, which are not lint issues:");
        foreach (var failure in failures)
            output.WriteLine($"  {failure}");

        output.WriteLine(
            "lint-fix: stopping. A clause naming a test that does not exist is a contract change, not a lint " +
            "fix.");

        return new OperationResult(
            OperationOutcome.Failed, new LintFixReport(iterations, lintOutput, [], failures));
    }

    private static OperationResult Escalate(
        TextWriter output,
        int iterations,
        string lintOutput,
        IReadOnlyList<Recording.ToolCallTranscript> refusals)
    {
        var refused = refusals.Select(refusal => $"{refusal.Tool} {refusal.Arguments}").ToList();

        output.WriteLine(
            "lint-fix: the remaining repair needs a protected configuration file or repository script " +
            "changed, which needs your approval. The writes that were refused were:");
        foreach (var refusal in refusals)
            output.WriteLine($"  {refusal.Tool} {refusal.Arguments}");

        output.WriteLine("lint-fix: lint still reports:");
        output.WriteLine(lintOutput);

        return new OperationResult(
            OperationOutcome.Escalated, new LintFixReport(iterations, lintOutput, refused, []));
    }

    private static OperationResult Exhausted(TextWriter output, int iterations, string lintOutput)
    {
        output.WriteLine($"lint-fix: {iterations} iterations spent and lint is still failing. It reports:");
        output.WriteLine(lintOutput);

        return new OperationResult(
            OperationOutcome.Failed, new LintFixReport(iterations, lintOutput, [], []));
    }
}
