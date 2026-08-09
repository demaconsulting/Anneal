using System.Diagnostics;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     Reusable harness for a "live trial": a real <c>git</c>-backed working tree in a throwaway temp folder,
///     a real in-process <c>route</c> invocation against it, and a real model-backed grading oracle over the
///     result.
/// </summary>
/// <remarks>
///     This replaces the hand-built, outside-the-repository fixture repos this migration's stage log
///     (<c>MIGRATION.md</c> S9 through S13) narrates rebuilding by hand for every live trial: create a temp
///     folder, seed it as a git repo, run the compiled tool against it, read <c>git status</c>/<c>git diff</c>
///     by hand, then delete it. This type does the same thing once, in-repo, as something disposable.
///     <para>
///         <b>Every test that uses this fixture must skip by default.</b> Set the
///         <c>ANNEAL_LIVE_TRIALS=1</c> environment variable to opt in; check <see cref="GateEnabled" /> at the
///         top of the test body and call <c>Assert.SkipUnless(LiveTrialFixture.GateEnabled, "...")</c> before
///         doing anything else. A live trial makes real model calls and real process/build activity, so it must
///         never run under a plain <c>dotnet test</c>, <c>pwsh ./build.ps1</c>, or CI.
///     </para>
///     <para>
///         <b>In-process, not a spawned <c>dotnet anneal</c> process:</b> this calls
///         <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, IReadOnlyList{IOperation}, string, CancellationToken)" />
///         directly against a <see cref="RouteOperation" /> bound to the fixture's temp folder. That already
///         exercises every real compiled path a spawned process would — <see cref="AnnealTool" />,
///         <see cref="Router" />, the real workers, and a real <see cref="Model.Providers.CopilotEndpoint" /> —
///         because <see cref="RouteOperation" />'s default endpoint resolution
///         (<see cref="Model.ModelRoles" />'s <c>DefaultEndpointFor</c>) is unchanged. Spawning a separate OS
///         process would additionally require the local tool to be installed or built into a runnable package
///         in the fixture, for no additional coverage: only the process boundary itself would be exercised, and
///         nothing above it reads or relies on the assembly having crossed one.
///     </para>
///     <para>
///         This is an interior, disposable test-support type. It carries no contract clause and is never linked
///         by <c>check-contracts</c>.
///     </para>
/// </remarks>
public sealed class LiveTrialFixture : IAsyncDisposable
{
    /// <summary>
    ///     The environment variable a caller sets to <c>"1"</c> to opt in to running live trials. Unset or any
    ///     other value means "skip".
    /// </summary>
    public const string GateEnvironmentVariable = "ANNEAL_LIVE_TRIALS";

    /// <summary>
    ///     The charter a live trial's grading oracle is bound to: a narrow, honest pass/fail judgement over a
    ///     stated expectation and the outcome actually observed.
    /// </summary>
    private const string GradingCharter =
        """
        You are grading a single live trial of an automated developer tool against a stated expectation. You
        are given what the trial expected to happen and what was actually observed - the tool's own report and
        the real state of the working tree it produced. Decide honestly whether the observed outcome satisfies
        the stated expectation. Answer Passed=true only when the evidence clearly supports it; answer
        Passed=false when it clearly does not; report HasSufficientEvidence=false rather than guess when the
        evidence does not clearly support either answer.
        """;

    private LiveTrialFixture(string repositoryRoot) => RepositoryRoot = repositoryRoot;

    /// <summary>
    ///     Whether the live-trial gate is set for this process. Every live trial test must check this first and
    ///     skip when it is false.
    /// </summary>
    public static bool GateEnabled =>
        Environment.GetEnvironmentVariable(GateEnvironmentVariable) == "1";

    /// <summary>
    ///     The throwaway working tree this fixture owns: a real git repository under the OS temp folder.
    /// </summary>
    public string RepositoryRoot { get; }

    /// <summary>
    ///     Creates a fresh temp folder and seeds it as a real git repository.
    /// </summary>
    /// <param name="cancellationToken">The caller's signal, carried unchanged into every process this creates.</param>
    /// <returns>A fixture bound to the new repository, ready to have files written and a trial run against it.</returns>
    public static async Task<LiveTrialFixture> CreateAsync(CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var fixture = new LiveTrialFixture(root);
        try
        {
            await fixture.RunGitAsync(["init"], cancellationToken).ConfigureAwait(false);
            await fixture.RunGitAsync(["config", "user.name", "Anneal Live Trial"], cancellationToken)
                .ConfigureAwait(false);
            await fixture.RunGitAsync(["config", "user.email", "anneal-live-trial@example.invalid"], cancellationToken)
                .ConfigureAwait(false);
            return fixture;
        }
        catch
        {
            Directory.Delete(root, recursive: true);
            throw;
        }
    }

    /// <summary>
    ///     Writes a seed file into the fixture's working tree, creating any intermediate directories.
    /// </summary>
    /// <param name="relativePath">The file's path, relative to <see cref="RepositoryRoot" />. Must not be null or blank.</param>
    /// <param name="content">The file's content. Must not be null.</param>
    public void WriteFile(string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = Path.Combine(RepositoryRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullPath, content);
    }

    /// <summary>
    ///     Stages and commits everything currently in the working tree, so a later <c>git status</c>/<c>git
    ///     diff</c> reads only what a trial itself changed.
    /// </summary>
    /// <param name="message">The commit message. Must not be null or blank.</param>
    /// <param name="cancellationToken">The caller's signal.</param>
    public async Task CommitAllAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        await RunGitAsync(["add", "-A"], cancellationToken).ConfigureAwait(false);
        await RunGitAsync(["commit", "-m", message], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs a real, in-process <c>route</c> invocation against this fixture's working tree.
    /// </summary>
    /// <param name="workItem">The work item text to route. Must not be null or blank.</param>
    /// <param name="changedFileHints">Changed-file hints to fold into the routing facts. Must not be null.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged into the run.</param>
    /// <returns>The exit code <see cref="AnnealTool" /> reached, and everything it wrote to its output.</returns>
    /// <remarks>
    ///     The worker's deterministic build and contract checks are stubbed to report a trivial pass
    ///     (<see cref="StubScriptRunner" />): the fixture's throwaway repository ships no <c>build.ps1</c> and no
    ///     lint tooling of its own, and standing one up would only prove <c>pwsh</c> can run a script - not
    ///     anything about routing, the workers, or the real model endpoint this harness exists to exercise. Every
    ///     other path - the route oracle, the selected worker's real reasoning, and the grading oracle - runs
    ///     for real, against a real endpoint.
    /// </remarks>
    public async Task<(int ExitCode, string Output)> RunRouteAsync(
        string workItem, IReadOnlyList<string> changedFileHints, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem);
        ArgumentNullException.ThrowIfNull(changedFileHints);

        var operation = new RouteOperation(
            RepositoryRoot, buildRunScript: StubScriptRunner, contractCheckRunScript: StubScriptRunner);
        var writer = new StringWriter();

        var exitCode = await AnnealTool
            .RunAsync(
                [operation.Name, workItem, .. changedFileHints], writer, [operation], RepositoryRoot,
                cancellationToken)
            .ConfigureAwait(false);

        return (exitCode, writer.ToString());
    }

    /// <summary>
    ///     Runs a real, in-process <c>maintain</c> invocation against this fixture's working tree.
    /// </summary>
    /// <param name="workItem">The Maintenance work item text. Must not be null or blank.</param>
    /// <param name="declaredBound">
    ///     The file-scope bound entries to declare, positionally after the work item. Must not be null; must not
    ///     be empty, since <c>maintain</c> reports a usage error rather than running with no declared bound.
    /// </param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged into the run.</param>
    /// <returns>The exit code <see cref="AnnealTool" /> reached, and everything it wrote to its output.</returns>
    /// <remarks>
    ///     Same rationale as <see cref="RunRouteAsync" />: the worker's deterministic build check is stubbed
    ///     (<see cref="StubScriptRunner" />) since this throwaway fixture ships no real <c>build.ps1</c>, while
    ///     every other path — <see cref="Operations.MaintainOperation" />, <c>SmallFixWorker</c>'s real reasoning,
    ///     and the grading oracle — runs for real, against a real endpoint.
    /// </remarks>
    public async Task<(int ExitCode, string Output)> RunMaintainAsync(
        string workItem, IReadOnlyList<string> declaredBound, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem);
        ArgumentNullException.ThrowIfNull(declaredBound);

        var operation = new MaintainOperation(RepositoryRoot, buildRunScript: StubScriptRunner);
        var writer = new StringWriter();

        var exitCode = await AnnealTool
            .RunAsync(
                [operation.Name, workItem, .. declaredBound], writer, [operation], RepositoryRoot,
                cancellationToken)
            .ConfigureAwait(false);

        return (exitCode, writer.ToString());
    }

    /// <summary>
    ///     Runs a real, in-process <c>stage-contract</c> invocation against this fixture's working tree.
    /// </summary>
    /// <param name="workItem">The work item text describing the clause to stage. Must not be null or blank.</param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged into the run.</param>
    /// <returns>The exit code <see cref="AnnealTool" /> reached, and everything it wrote to its output.</returns>
    /// <remarks>
    ///     Unlike <see cref="RunRouteAsync" /> and <see cref="RunMaintainAsync" />, no script stub is needed:
    ///     <see cref="Operations.StageContractOperation" /> takes no build-script override, and its own
    ///     post-run <c>check-contracts</c> pass already runs in process
    ///     (<see cref="Process.ContractCheckRunner" />), never shelling out to a repository script this
    ///     throwaway fixture does not ship.
    /// </remarks>
    public async Task<(int ExitCode, string Output)> RunStageContractAsync(
        string workItem, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItem);

        var operation = new StageContractOperation(RepositoryRoot);
        var writer = new StringWriter();

        var exitCode = await AnnealTool
            .RunAsync([operation.Name, workItem], writer, [operation], RepositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        return (exitCode, writer.ToString());
    }

    /// <summary>
    ///     Runs a real, in-process <c>verify-change</c> invocation against this fixture's working tree.
    /// </summary>
    /// <param name="baseRef">
    ///     The base reference to diff against, or null to diff uncommitted work against <c>HEAD</c>, carried
    ///     unchanged into the run.
    /// </param>
    /// <param name="cancellationToken">The caller's signal, carried unchanged into the run.</param>
    /// <returns>The exit code <see cref="AnnealTool" /> reached, and everything it wrote to its output.</returns>
    /// <remarks>
    ///     Same build/contract-check stubbing as <see cref="RunRouteAsync" /> and <see cref="RunMaintainAsync" />
    ///     — this throwaway fixture ships no real <c>build.ps1</c> or lint tooling — but no <c>runGit</c>
    ///     override: <see cref="Primitives.DiffCheck" /> is the one part of this operation that reads the
    ///     repository directly rather than composing an existing worker primitive, so a live trial is the first
    ///     place it runs against a real <c>git</c> process over a real working tree rather than a substituted one.
    /// </remarks>
    public async Task<(int ExitCode, string Output)> RunVerifyChangeAsync(
        string? baseRef, CancellationToken cancellationToken)
    {
        var operation = new VerifyChangeOperation(
            RepositoryRoot, buildRunScript: StubScriptRunner, contractCheckRunScript: StubScriptRunner);
        var writer = new StringWriter();

        IReadOnlyList<string> arguments = baseRef is null ? [operation.Name] : [operation.Name, baseRef];

        var exitCode = await AnnealTool
            .RunAsync(arguments, writer, [operation], RepositoryRoot, cancellationToken)
            .ConfigureAwait(false);

        return (exitCode, writer.ToString());
    }

    private static Task<ScriptRun> StubScriptRunner(string script, CancellationToken cancellationToken) =>
        Task.FromResult(new ScriptRun(0, $"live trial stub: '{script}' was not run against this throwaway fixture"));

    /// <summary>
    ///     Reads the working tree's current changes, as <c>git status --porcelain</c> reports them - the
    ///     deterministic evidence a grading oracle is handed alongside the tool's own report.
    /// </summary>
    /// <param name="cancellationToken">The caller's signal.</param>
    /// <returns>The porcelain status output, one line per changed path.</returns>
    public async Task<string> GitStatusAsync(CancellationToken cancellationToken) =>
        await RunGitAsync(["status", "--porcelain"], cancellationToken).ConfigureAwait(false);

    /// <summary>
    ///     Reads the working tree's uncommitted changes, as <c>git diff HEAD</c> reports them.
    /// </summary>
    /// <param name="cancellationToken">The caller's signal.</param>
    /// <returns>The unified diff of every uncommitted change against <c>HEAD</c>.</returns>
    public async Task<string> GitDiffAsync(CancellationToken cancellationToken) =>
        await RunGitAsync(["diff", "HEAD"], cancellationToken).ConfigureAwait(false);

    /// <summary>
    ///     Asks a real, model-backed grading oracle whether an observed outcome satisfies a stated expectation
    ///     - the same narrow typed-question shape <see cref="Router" />'s own oracle passes use, over
    ///     <see cref="LiveTrialVerdict" /> rather than a routing decision.
    /// </summary>
    /// <param name="expectation">What the trial expected to happen, in plain text. Must not be null or blank.</param>
    /// <param name="observedOutcome">
    ///     What was actually observed - the tool's report, the git status, and the diff, folded together by the
    ///     caller. Must not be null or blank.
    /// </param>
    /// <param name="cancellationToken">The caller's signal.</param>
    /// <returns>
    ///     The real endpoint's typed verdict: whether it had enough evidence to grade at all, and if so, whether
    ///     the outcome passed.
    /// </returns>
    public async Task<LiveTrialVerdict> GradeAsync(
        string expectation, string observedOutcome, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observedOutcome);

        // No endpointFor is supplied, so the oracle resolves its endpoint the exact way RouteOperation's own
        // default constructor does: through ModelRoles' default, a real CopilotEndpoint over the ambient
        // Copilot account - never a substitute.
        var oracle = new Oracle<LiveTrialVerdict>(RepositoryRoot, GradingCharter);

        var result = await oracle
            .AskAsync(
                "Grade this live trial.",
                [$"Expectation:\n{expectation}", $"Observed outcome:\n{observedOutcome}"],
                cancellationToken)
            .ConfigureAwait(false);

        return result.Finding ?? new LiveTrialVerdict { HasSufficientEvidence = false, Passed = false, Reasoning = "no verdict was decoded" };
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // Best-effort cleanup: a live trial that failed mid-run must still leave no litter in the OS temp
        // folder, so a delete failure here (a file transiently locked by a process still exiting) is swallowed
        // rather than allowed to mask the trial's own result.
        try
        {
            if (Directory.Exists(RepositoryRoot))
                Directory.Delete(RepositoryRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }

    private async Task<string> RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} exited {process.ExitCode}: {error}");
        }

        return output;
    }
}
