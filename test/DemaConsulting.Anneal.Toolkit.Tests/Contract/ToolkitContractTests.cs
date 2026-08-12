using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Providers;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the Toolkit contract in <c>.anneal/architecture/toolkit.md</c>.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> and the
///     assertions are on the exit code and the written output. The operation set is injected where a clause is
///     about the dispatcher rather than about a shipped action, because a rule stated over categories cannot be
///     proven by the one category that happens to ship today.
///     <para>
///         Two clauses are about what a caller receives rather than what a terminal shows — a finding returned
///         as data, and a cancellation that lands — and those go through <see cref="IOperation" /> itself,
///         which is public for exactly that reason. Nothing here reaches inside an operation.
///     </para>
/// </remarks>
public partial class ToolkitContractTests
{
    /// <remarks>
    ///     How long a cancellation test waits before declaring the invocation stuck. Generous, because a slow
    ///     machine is not a defect, and bounded, because the failure this guards against — a signal that never
    ///     reaches the thing it should stop — presents as a wait that never ends.
    /// </remarks>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <remarks>
    ///     The exit code TOOLKIT-16 promises an interrupted run leaves. Stated as a literal rather than read
    ///     from the tool, because the number itself is the promise: a caller reading exit codes has only the
    ///     number, and a test that asked the tool what code it uses would agree with any answer it gave.
    /// </remarks>
    private const int ExitInterrupted = 130;

    /// <remarks>
    ///     How much of the report the interrupted run must be seen working through before the interrupt is
    ///     raised. Small, because the point is only that a step is genuinely under way and not that a
    ///     particular amount of it is done.
    /// </remarks>
    private const int ProgressBeforeInterrupt = 20;

    /// <remarks>
    ///     Citations in the report the interrupt test writes, and lines in the file each of them reads. Far
    ///     more work than the tool can finish in the fraction of a second the test needs to see it start, so
    ///     "it was still running" is a property of the workload rather than of the machine's speed.
    /// </remarks>
    private const int LongWorkloadCitations = 100_000;

    private const int CitedFileLines = 5_000;

    /// <remarks>
    ///     How long the test host stays on the child's console after raising the interrupt. It is a bound and
    ///     not a wait: the child normally exits within a few milliseconds and the attachment ends with it.
    /// </remarks>
    private const int InterruptDeliveryMilliseconds = 5_000;


    /// <summary>
    ///     Writes a synthetic invocation-record corpus in the same shape <see cref="RecordStore" /> writes, so
    ///     stats reads exactly what a real repository would have accumulated.
    /// </summary>
    private static void WriteInvocationRecords(string root, params InvocationRecord[] records)
    {
        var path = RecordStore.InvocationsPathFor(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, records.Select(record => JsonSerializer.Serialize(record)));
    }

    private static InvocationRecord Record(string action, string outcome, DateTimeOffset at) =>
        new(at, "test", action, [], outcome, null, 0, 0, null, 0);

    private static InvocationRecord RecordWithUsage(
        string action, string outcome, DateTimeOffset at,
        int modelInteractions, long inputTokens, long outputTokens, double durationMs) =>
        new(at, "test", action, [], outcome, null, 0, modelInteractions,
            new ModelUsage(inputTokens, outputTokens), durationMs);

    /// <summary>
    ///     Runs lint-fix through the dispatcher with the repository's scripts and its worker substituted.
    /// </summary>
    private static async Task<LintFixRun> RunLintFix(
        string repositoryRoot, RunRepositoryScript runScript, IChatEndpoint worker)
    {
        var operation = new LintFixOperation(repositoryRoot, _ => worker, runScript);
        var output = new StringWriter();

        var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);
        return new LintFixRun(result, output.ToString());
    }

    /// <param name="Result">What the operation concluded, outcome and finding together.</param>
    /// <param name="Output">Everything the operation rendered.</param>
    private sealed record LintFixRun(OperationResult Result, string Output);

    /// <remarks>
    ///     Invokes the tools it was handed rather than describing calls it would like made, which is what a
    ///     provider running the tool loop natively does. It is the only stand-in that can exercise a guarantee
    ///     about tool calls the code above the seam never sees.
    /// </remarks>
    private sealed class ToolCallingEndpoint(
        params (string Tool, Dictionary<string, object?> Arguments)[] calls) : IChatEndpoint
    {
        public async Task<ChatTurnResult> CompleteAsync(
            ChatTurnRequest request, CancellationToken cancellationToken)
        {
            foreach (var (tool, arguments) in calls)
            {
                var function = request.Tools.OfType<AIFunction>().FirstOrDefault(candidate => candidate.Name == tool);
                if (function is not null)
                    await function.InvokeAsync(new AIFunctionArguments(arguments), cancellationToken);
            }

            return new ChatTurnResult("I did what I could.", new ModelUsage(1, 1));
        }

        public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    /// <summary>
    ///     Writes the repository's role-to-candidates configuration, as a repository substituting a model does.
    /// </summary>
    /// <remarks>
    ///     Each role names an ordered list, because that is the only form the file has: a role resolves to the
    ///     first candidate the account is offered, so a test that wrote a bare name would be writing a format
    ///     nothing reads.
    /// </remarks>
    private static void WriteModelConfiguration(
        string root, string[] light, string[] medium, string[] heavy)
    {
        var path = Path.Combine(root, ModelConfiguration.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
              {"models": {"light": {{List(light)}}, "medium": {{List(medium)}}, "heavy": {{List(heavy)}} } }
              """);
    }

    /// <summary>
    ///     Dispatches check-contracts against a repository through the command surface a caller uses, returning
    ///     the exit code and everything the run rendered.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunCheckContracts(string repositoryRoot)
    {
        var output = new StringWriter();
        var exitCode = await AnnealTool.RunAsync(
            ["check-contracts"],
            output,
            [new CheckContractsOperation(repositoryRoot)],
            repositoryRoot,
            TestContext.Current.CancellationToken);
        return (exitCode, output.ToString());
    }

    /// <summary>
    ///     Builds a throw-away repository carrying one clause, one boundary test declaration, and one recorded
    ///     result, so a dispatched check-contracts run has a whole contract to check.
    /// </summary>
    /// <param name="clauseVerifier">The test name the clause names - the same as the declared test to link, anything else to break the link.</param>
    /// <param name="resultOutcome">The recorded outcome of the declared test, e.g. "Passed" or "Failed".</param>
    private static TemporaryRepository BuildContractRepository(string clauseVerifier, string resultOutcome)
    {
        var repository = new TemporaryRepository();
        repository.WriteDocument(
            "ingest.md",
            $"""
             ## Contract

             ### Provides

             - **INGEST-01** - Accepts records.
               *Verified by:* `{clauseVerifier}`
             """);
        repository.Write(
            "test/Ingest.Tests/Contract/IngestContractTests.cs",
            """
            public class IngestContractTests
            {
                [Fact]
                public void AcceptedRecordIsDurable()
                {
                }
            }
            """);
        repository.WriteTrx("artifacts/tests/results.trx", [("AcceptedRecordIsDurable", resultOutcome)]);
        return repository;
    }

    /// <returns>A JSON array of the given names, as the configuration file states a role's candidates.</returns>
    private static string List(params string[] names) =>
        "[" + string.Join(", ", names.Select(name => $"\"{name}\"")) + "]";

    /// <summary>
    ///     Reads an appended record stream back as structured data, which is the only way these clauses may be
    ///     read: a test that pattern-matched the file as text would pass on prose that merely looked structured.
    /// </summary>
    private static JsonElement[] ReadRecords(string path)
    {
        Assert.True(File.Exists(path), $"nothing was recorded at {path}");

        return
        [
            .. File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonDocument.Parse(line).RootElement.Clone())
        ];
    }

    /// <returns>A recorded string field, or the empty string when the record omits it.</returns>
    private static string Text(JsonElement record, string field) =>
        record.TryGetProperty(field, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    /// <returns>A recorded array of strings.</returns>
    private static string[] Strings(JsonElement record, string field) =>
        [.. record.GetProperty(field).EnumerateArray().Select(entry => entry.GetString() ?? string.Empty)];

    /// <returns>
    ///     A per-role endpoint selector serving every role from one script, for a probe invoked without the
    ///     dispatcher. Substituting the provider rather than the resolution is deliberate: the operation still
    ///     resolves its role through the repository's configuration, so the seam under test is the real one.
    /// </returns>
    private static Func<ModelRole, IChatEndpoint> Scripted(string reasoningReply, string probeReply) =>
        Serving(new ScriptedEndpoint(probeReply), new ScriptedEndpoint(reasoningReply), new ScriptedEndpoint("unused"));

    /// <returns>A selector handing each role its own endpoint.</returns>
    private static Func<ModelRole, IChatEndpoint> Serving(
        IChatEndpoint light, IChatEndpoint medium, IChatEndpoint heavy) =>
        role => role switch
        {
            ModelRole.Light => light,
            ModelRole.Medium => medium,
            _ => heavy
        };

    /// <returns>A scripted reply carrying a complete answer, as the model would emit it.</returns>
    private static string Answer(string ownership, string owningFile) =>
        $$"""
          {"ownership": "{{ownership}}", "owningFile": "{{owningFile}}", "evidence": "I read the files."}
          """;

    private static string LastMessage(ChatTurnRequest request) => request.Messages[^1].Text;

    /// <returns>The tool's reply, invoked with whatever arguments it declares that the test can supply.</returns>
    private static string Invoke(AIFunction tool, string path = ".")
    {
        var arguments = new AIFunctionArguments
        {
            ["path"] = tool.Name == "read_file" && path == "." ? "present.md" : path,
            ["start"] = 1,
            ["max"] = 10,
            ["depth"] = 1,
            ["pattern"] = "line",
            ["extension"] = string.Empty,

            // The write tools' parameters, so one helper can invoke every granted tool. A tool that does not
            // declare one of these ignores it.
            ["content"] = "written by a model",
            ["oldStr"] = "a line",
            ["newStr"] = "another line"
        };

        return tool.InvokeAsync(arguments, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult()
            ?.ToString() ?? string.Empty;
    }

    /// <summary>
    ///     Runs the probe against substituted endpoints, one per role, so that role resolution, the two-pass
    ///     ordering and the retry path are all exercised without a network call.
    /// </summary>
    private static Task<ProbeRun> RunProbe(string root, string reasoningReply, params string[] probeReplies) =>
        RunProbe(root, reasoningReply, probeReplies, reachable: true);

    /// <summary>
    ///     Runs the probe against an account that is offered exactly the named models, so a test can retire a
    ///     candidate and watch the role land on the next one.
    /// </summary>
    private static Task<ProbeRun> RunProbe(
        string root, string reasoningReply, string probeReply, string[] offered) =>
        RunProbe(root, reasoningReply, [probeReply], reachable: true, offered);

    private static Task<ProbeRun> RunProbe(string root, string reasoningReply, string probeReply, bool reachable) =>
        RunProbe(root, reasoningReply, [probeReply], reachable);

    private static async Task<ProbeRun> RunProbe(
        string root,
        string reasoningReply,
        string[] probeReplies,
        bool reachable,
        IReadOnlyCollection<string>? offered = null)
    {
        // Empty is "the provider stated nothing", which leaves every role on its first candidate - the shape
        // every test that is not about availability wants.
        var offers = offered ?? [];

        var reasoning = new ScriptedEndpoint(reasoningReply) { Offers = offers };
        var probing = new ScriptedEndpoint(probeReplies) { Offers = offers };
        var openEnded = new ScriptedEndpoint("the open-ended tier is not consulted by this operation")
        {
            Offers = offers
        };

        var unreachable = new UnreachableEndpoint();
        var endpointFor = reachable
            ? Serving(probing, reasoning, openEnded)
            : _ => unreachable;

        var output = new StringWriter();
        var exitCode = await AnnealTool.RunAsync(
            ["probe-rule-owner", "each rule has exactly one owner"],
            output,
            [new ProbeRuleOwnerOperation(root, endpointFor)],
            root,
            TestContext.Current.CancellationToken);

        return new ProbeRun(exitCode, output.ToString(), reasoning, probing, openEnded);
    }

    /// <param name="Reasoning">The endpoint serving the middle tier, which the free-form pass resolves to.</param>
    /// <param name="Probing">The endpoint serving the cheapest tier, which the schema-bearing pass resolves to.</param>
    /// <param name="OpenEnded">The endpoint serving the capable tier, which this operation should never reach.</param>
    private sealed record ProbeRun(
        int ExitCode,
        string Output,
        ScriptedEndpoint Reasoning,
        ScriptedEndpoint Probing,
        ScriptedEndpoint OpenEnded);

    /// <remarks>
    ///     Replays a fixed script and records every request, so a test can assert on what was actually sent —
    ///     the tools in scope, where the schema appeared, and the ceiling carried — rather than on a live model's
    ///     cooperation.
    /// </remarks>
    private sealed class ScriptedEndpoint(params string[] replies) : IChatEndpoint
    {
        /// <summary>
        ///     What every scripted reply reports having consumed. Distinctive figures, so a total that appears
        ///     in an invocation record can only have come from adding these up.
        /// </summary>
        public const long ReportedInputTokens = 1100;

        /// <inheritdoc cref="ReportedInputTokens" />
        public const long ReportedOutputTokens = 7;

        private readonly Queue<string> _replies = new(replies);

        public List<ChatTurnRequest> Requests { get; } = [];

        /// <summary>
        ///     The models this endpoint's account is offered, or empty to state nothing — which is what a test
        ///     about something other than availability says, and which leaves a role on its first candidate.
        /// </summary>
        public IReadOnlyCollection<string> Offers { get; init; } = [];

        /// <summary>
        ///     The intermediate progress text reported alongside every reply this endpoint gives, or empty for
        ///     a test about something other than progress.
        /// </summary>
        public IReadOnlyList<string> Progress { get; init; } = [];

        /// <summary>
        ///     How many times this endpoint was asked what it offers, so a test can assert that a run which
        ///     consulted no model asked nothing.
        /// </summary>
        public int Enumerations { get; private set; }

        public Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
        {
            // A real endpoint checks before it spends anything, and so does this one: a turn sent under an
            // already-cancelled signal is a turn that should never have left.
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(request);
            return Task.FromResult(new ChatTurnResult(
                _replies.Count > 0 ? _replies.Dequeue() : "(script exhausted)",
                new ModelUsage(ReportedInputTokens, ReportedOutputTokens),
                Progress));
        }

        public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Enumerations++;
            return Task.FromResult(Offers);
        }
    }

    /// <remarks>
    ///     Accepts a turn and then never answers it, so the only thing that can end the call is the caller's
    ///     cancellation arriving while it waits. It records enough to tell a cancellation that landed mid-flight
    ///     from one that landed after a reply, and to catch a signal that was handed over but could never fire.
    /// </remarks>
    private sealed class NeverRepliesEndpoint : IChatEndpoint
    {
        public TaskCompletionSource InFlight { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TokenCouldBeCancelled { get; private set; }

        public bool CancelledWhileWaiting { get; private set; }

        public bool Replied { get; private set; }

        public int Calls { get; private set; }

        public async Task<ChatTurnResult> CompleteAsync(
            ChatTurnRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            TokenCouldBeCancelled = cancellationToken.CanBeCanceled;
            InFlight.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancelledWhileWaiting = true;
                throw;
            }

            Replied = true;
            return new ChatTurnResult("a reply this endpoint never produces");
        }

        /// <remarks>
        ///     States nothing, so the role under test resolves on its first candidate and the turn this endpoint
        ///     exists to leave hanging is reached.
        /// </remarks>
        public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    /// <remarks>
    ///     Runs until the signal it was handed says stop, and records both that it saw the cancellation and
    ///     that it never reached its own end. Without the second flag "it stopped" and "it finished" would be
    ///     the same observation.
    /// </remarks>
    private sealed class WaitingOperation : IOperation
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ObservedCancellation { get; private set; }

        public bool RanToCompletion { get; private set; }

        public string Name => "waiting";

        public OperationCategory Category => OperationCategory.Research;

        public ModelRole? RequiredRole => null;

        public string Summary => "Waits until its caller withdraws the request";

        public string Usage => "usage: dotnet anneal waiting - waits until cancelled, taking no arguments";

        public async Task<OperationResult> ExecuteAsync(
            IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken)
        {
            Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            RanToCompletion = true;
            return new OperationResult(OperationOutcome.Succeeded);
        }
    }

    /// <remarks>
    ///     An account that cannot be reached at all: the availability enquiry fails as the turn would, which is
    ///     the case that must not become a gate — the run proceeds on its first candidate and fails on the turn,
    ///     naming the real cause rather than an availability verdict.
    /// </remarks>
    private sealed class UnreachableEndpoint : IChatEndpoint
    {
        public Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken) =>
            throw new ModelUnavailableException("the Copilot account is not signed in on this machine");

        public Task<IReadOnlyCollection<string>> AvailableModelsAsync(CancellationToken cancellationToken) =>
            throw new ModelUnavailableException("the Copilot account is not signed in on this machine");
    }

    private static Task<int> RunStub(OperationCategory category, OperationOutcome outcome) =>
        AnnealTool.RunAsync(
            ["stub"],
            new StringWriter(),
            [new StubOperation(category, outcome)],
            TestContext.Current.CancellationToken);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-toolkit-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    ///     Writes a report citing far more evidence than the tool can check while a test watches, so an
    ///     invocation of <c>verify-evidence</c> against it is certain to still be working when interrupted.
    /// </summary>
    /// <returns>The number of citations the report makes.</returns>
    private static int WriteLongWorkload(string root)
    {
        File.WriteAllLines(
            Path.Combine(root, "cited.txt"),
            Enumerable.Range(1, CitedFileLines).Select(line => $"line {line} of the cited file"));

        var citation = "`cited.txt:1` - \"line 1 of the cited file\"" + Environment.NewLine;
        File.WriteAllText(
            Path.Combine(root, "report.md"),
            string.Concat(Enumerable.Repeat(citation, LongWorkloadCitations)));

        return LongWorkloadCitations;
    }

    /// <summary>
    ///     Starts the built tool as a caller does, on a console of its own so that an interrupt can be raised
    ///     on it without disturbing whatever console this test host was started from.
    /// </summary>
    /// <remarks>
    ///     The tool is built beside the tests, because they reference it. Its launcher is preferred and the
    ///     framework-dependent assembly is the fallback, so a platform that builds no launcher still runs the
    ///     same entry point in a process of its own — which is the part this clause is about.
    /// </remarks>
    private static System.Diagnostics.Process StartTool(string workingDirectory, params string[] arguments)
    {
        var launcher = Path.Combine(
            AppContext.BaseDirectory,
            "DemaConsulting.Anneal.Toolkit" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

        var assembly = Path.Combine(AppContext.BaseDirectory, "DemaConsulting.Anneal.Toolkit.dll");

        var start = new ProcessStartInfo
        {
            FileName = File.Exists(launcher) ? launcher : "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!File.Exists(launcher))
        {
            Assert.True(File.Exists(assembly), $"the built tool is not beside the tests, at {assembly}");
            start.ArgumentList.Add(assembly);
        }

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        return System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("the tool did not start");
    }

    /// <summary>
    ///     Delivers the interrupt a terminal delivers — Ctrl+C on Windows, SIGINT elsewhere — to a running
    ///     invocation, without killing it.
    /// </summary>
    /// <remarks>
    ///     On Unix the signal names the process, so there is nothing else to arrange. On Windows it names a
    ///     console rather than a process, so the test host leaves its own console for the moment it takes to
    ///     raise the event on the child's, and ignores the event itself for exactly as long as it is attached
    ///     to that console — otherwise the run that raises the interrupt is one of the processes interrupted
    ///     by it.
    /// </remarks>
    private static void Interrupt(System.Diagnostics.Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (Interop.Kill(process.Id, Interop.Sigint) != 0)
                throw new InvalidOperationException("the interrupt signal could not be delivered");

            return;
        }

        var ownsConsole = !Console.IsOutputRedirected;

        Interop.FreeConsole();
        if (!Interop.AttachConsole((uint)process.Id))
            throw new InvalidOperationException("the interrupted process's console could not be attached");

        try
        {
            Interop.SetConsoleCtrlHandler(IntPtr.Zero, true);
            if (!Interop.GenerateConsoleCtrlEvent(Interop.CtrlCEvent, 0))
                throw new InvalidOperationException("the interrupt could not be raised on that console");

            // Stay attached until the child has acted on the event, so it is never raised on a console this
            // process is already walking away from.
            process.WaitForExit(InterruptDeliveryMilliseconds);
        }
        finally
        {
            Interop.FreeConsole();
            Interop.SetConsoleCtrlHandler(IntPtr.Zero, false);
            Interop.AttachConsole(Interop.AttachParentProcess);

            // Leaving a console invalidates the handles a writer opened on it. A test host writing to a pipe
            // never had any, which is the case under "dotnet test"; one really attached to a terminal is given
            // fresh writers over the console it has just rejoined.
            if (ownsConsole)
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
        }
    }

    /// <remarks>
    ///     The platform calls that deliver a terminal interrupt to another process, which no .NET API exposes.
    ///     Each is called on the one platform it belongs to; the other is never reached there.
    /// </remarks>
    private static class Interop
    {
        /// <summary>CTRL_C_EVENT: the event a terminal raises for Ctrl+C.</summary>
        internal const uint CtrlCEvent = 0;

        /// <summary>ATTACH_PARENT_PROCESS: rejoin the console of whatever started this process.</summary>
        internal const uint AttachParentProcess = 0xFFFFFFFF;

        /// <summary>SIGINT: the signal a terminal sends for Ctrl+C.</summary>
        internal const int Sigint = 2;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachConsole(uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GenerateConsoleCtrlEvent(uint controlEvent, uint processGroupId);

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static extern int Kill(int processId, int signal);
    }

    /// <returns>The path of the written report, relative to the root the operation resolves against.</returns>
    private static string WriteReport(string root, string name, params string[] citations)
    {
        File.WriteAllLines(
            Path.Combine(root, name),
            ["**Result**: SUCCEEDED", "", .. citations]);
        return name;
    }

    /// <remarks>
    ///     Stands in for a real operation so that the gating rule can be exercised for every category,
    ///     including the three no shipped operation currently declares. It declares a usage literal — its own
    ///     by default, or a distinctive one a caller supplies — as its single source, so both the dispatcher's
    ///     usage-error rendering and <c>help stub</c> can be read against the same text and proven not to drift.
    /// </remarks>
    private sealed class StubOperation(
        OperationCategory category,
        OperationOutcome outcome,
        string usage = "usage: dotnet anneal stub <arg> - expects one argument, given positionally") : IOperation
    {
        public string Name => "stub";

        public OperationCategory Category => category;

        public ModelRole? RequiredRole => null;

        public string Summary => "Reports a fixed outcome under a fixed category";

        public string Usage => usage;

        public Task<OperationResult> ExecuteAsync(
            IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationResult(outcome));
    }
}
