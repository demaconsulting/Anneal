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
///     Boundary tests for the shared invocation runtime every operation runs under - gating, findings, cancellation, interruption, and structured records.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{

    /// <summary>
    ///     TOOLKIT-I3 — an enforcement operation reaches the same verdict every time it is given the same
    ///     repository, which is what makes it safe to gate a build on. Proven by running check-contracts twice
    ///     over one unchanged repository and requiring an identical verdict and identical rendered output both
    ///     times, on a passing repository and on a failing one, and requiring the two verdicts to differ so the
    ///     stability is of a verdict that actually reads its input rather than a constant.
    /// </summary>
    [Fact]
    public async Task EnforcementVerdictsAreStableOnUnchangedInput()
    {
        // Arrange: two repositories that must reach opposite verdicts - a clause whose test passed, and the
        // same clause whose test failed
        using var passing = BuildContractRepository("AcceptedRecordIsDurable", "Passed");
        using var failing = BuildContractRepository("AcceptedRecordIsDurable", "Failed");

        // Act: run the enforcement operation twice over each, without changing anything between the runs
        var (firstPass, firstPassText) = await RunCheckContracts(passing.Root);
        var (secondPass, secondPassText) = await RunCheckContracts(passing.Root);
        var (firstFail, firstFailText) = await RunCheckContracts(failing.Root);
        var (secondFail, secondFailText) = await RunCheckContracts(failing.Root);

        // Assert: each verdict is identical to its own repeat, in both exit code and rendered text, and the
        // pass and fail verdicts differ - so a verdict that stopped reading its input, or that varied between
        // runs, would fail this test
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, firstPass),
            () => Assert.Equal(firstPass, secondPass),
            () => Assert.Equal(firstPassText, secondPassText),
            () => Assert.Equal(AnnealTool.ExitGatedFailure, firstFail),
            () => Assert.Equal(firstFail, secondFail),
            () => Assert.Equal(firstFailText, secondFailText),
            () => Assert.NotEqual(firstPass, firstFail));
    }


    /// <summary>
    ///     TOOLKIT-02 — the declared category alone decides whether a non-zero exit gates a build, and only
    ///     enforcement gates.
    /// </summary>
    [Fact]
    public async Task OnlyEnforcementOperationsGate()
    {
        // Arrange: the same failure, declared under each category in turn
        var categories = Enum.GetValues<OperationCategory>();

        // Act: run each one, plus a succeeding enforcement operation as the control
        var failingExitCodes = new Dictionary<OperationCategory, int>();
        foreach (var category in categories)
            failingExitCodes[category] = await RunStub(category, OperationOutcome.Failed);

        var succeedingEnforcement = await RunStub(OperationCategory.Enforcement, OperationOutcome.Succeeded);

        // Assert: identical failures gate or not purely by category, and success never gates
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitGatedFailure, failingExitCodes[OperationCategory.Enforcement]),
            () => Assert.All(
                categories.Where(category => category != OperationCategory.Enforcement),
                category => Assert.Equal(AnnealTool.ExitSuccess, failingExitCodes[category])),
            () => Assert.Equal(AnnealTool.ExitSuccess, succeedingEnforcement));
    }

    /// <summary>
    ///     TOOLKIT-14 — an operation reports what it found as data carried beside its outcome, so a caller
    ///     consumes the finding without parsing the text rendered for a person, while an operation with nothing
    ///     structured to report carries none and that absence is an answer rather than a failure.
    /// </summary>
    [Fact]
    public async Task OperationFindingsReachCallersAsData()
    {
        // Arrange: a repository, a report the deterministic check can verify, and a model scripted to answer
        // and then to refuse. Every invocation below renders into TextWriter.Null, so nothing this test
        // asserts can have come from the rendered text - there is none to read.
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "owner.md"), "Each rule has exactly one owner.");
            File.WriteAllLines(Path.Combine(root, "subject.txt"), ["first line", "the promise this cites"]);
            var report = WriteReport(root, "report.md", "`subject.txt:2` - \"the promise this cites\"");

            // Act: a probe that answers, invoked through the public operation surface a composing caller holds
            IOperation answering = new ProbeRuleOwnerOperation(
                root,
                Scripted("owner.md states it.", Answer("SingleOwner", "owner.md")));
            var answered = await answering.ExecuteAsync(
                ["each rule has exactly one owner"], TextWriter.Null, TestContext.Current.CancellationToken);

            // Act: the same operation refusing - the outcome is a peer of the finding, not folded into it
            IOperation refusing = new ProbeRuleOwnerOperation(
                root,
                Scripted("Two files state it.", Answer("StatedInSeveralPlaces", "")));
            var refused = await refusing.ExecuteAsync(
                ["each rule has exactly one owner"], TextWriter.Null, TestContext.Current.CancellationToken);

            // Act: an operation whose whole answer is its outcome and its rendered lines
            IOperation deterministic = new VerifyEvidenceOperation(root);
            var verified = await deterministic.ExecuteAsync(
                [report], TextWriter.Null, TestContext.Current.CancellationToken);

            // Assert: the typed value the probe computed survives to the caller intact, beside an outcome that
            // is still its own answer; and the operation with nothing structured carries none, and succeeds
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, answered.Outcome),
                () => Assert.Equal(
                    new RuleOwnerAnswer
                    {
                        Ownership = RuleOwnership.SingleOwner,
                        OwningFile = "owner.md",
                        Evidence = "I read the files."
                    },
                    Assert.IsType<RuleOwnerAnswer>(answered.Finding)),
                () => Assert.Same(answered.Finding, answered.FindingAs<RuleOwnerAnswer>()),

                // A refusal still carries what was found: the outcome says the question was not answerable,
                // the finding says what the probe saw, and neither is recoverable from the other.
                () => Assert.Equal(OperationOutcome.Refused, refused.Outcome),
                () => Assert.Equal(
                    RuleOwnership.StatedInSeveralPlaces,
                    Assert.IsType<RuleOwnerAnswer>(refused.Finding).Ownership),

                // Nothing structured to report is an answer, not a failure and not an invented payload.
                () => Assert.Null(verified.Finding),
                () => Assert.Equal(OperationOutcome.Succeeded, verified.Outcome));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-15 — a caller supplies a cancellation signal with an invocation, and cancelling it stops the
    ///     invocation rather than letting it run to completion.
    /// </summary>
    /// <remarks>
    ///     The stub waits on the signal it was handed and on nothing else, so it can only stop if the signal
    ///     reached it: a dispatcher that substituted a token of its own, or dropped it, leaves the stub waiting
    ///     forever and this test fails on its deadline rather than passing quietly. The completion flag makes
    ///     "stopped" mean stopped rather than merely "finished" — a run that fell through to its end would set
    ///     it.
    /// </remarks>
    [Fact]
    public async Task CancellingAnInvocationStopsIt()
    {
        // Arrange: an invocation that will not finish on its own, under the caller's own signal
        using var cancellation = new CancellationTokenSource();
        var operation = new WaitingOperation();

        // Act: start it, and wait until it is genuinely running rather than merely dispatched
        var run = Task.Run(
            () => AnnealTool.RunAsync(["waiting"], TextWriter.Null, [operation], cancellation.Token),
            TestContext.Current.CancellationToken);

        await operation.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        var runningWhenCancelled = !run.IsCompleted;

        // Act: the caller withdraws the request
        await cancellation.CancelAsync();
        var settled = await Task.WhenAny(run, Task.Delay(Deadline, TestContext.Current.CancellationToken));

        // Assert: it stopped where it was, and never ran to completion
        Assert.Multiple(
            () => Assert.True(runningWhenCancelled, "the invocation had already finished before it was cancelled"),
            () => Assert.Same(run, settled),
            () => Assert.True(operation.ObservedCancellation),
            () => Assert.False(operation.RanToCompletion));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    /// <summary>
    ///     TOOLKIT-16 — an invocation interrupted at the terminal stops where it is rather than being killed,
    ///     and exits with the interrupt code 130, which is distinct from every code an outcome maps to and from
    ///     the caller-error code of TOOLKIT-10.
    /// </summary>
    /// <remarks>
    ///     The only clause here about the process rather than the library, so it is the only test that runs the
    ///     built executable: an interrupt is delivered to a process, and nothing an in-process test can do
    ///     stands in for one without testing a copy of the entry point instead of the entry point.
    ///     <para>
    ///         Vacuity is guarded on both halves of the clause. The interrupt is not sent until the child has
    ///         reported real progress through a step — locator lines it can only write by having started
    ///         checking — so a run that had already finished, or never started, fails here rather than passing
    ///         quietly. "Stopped rather than killed" is then read from three independent observations: the
    ///         graceful line only the interrupt path writes, the tally line the operation writes at its end
    ///         which must be absent, and the exit code itself, since a process killed at the terminal reports
    ///         its killer's code and never 130. Folding the interrupt back into the outcome mapping fails the
    ///         code assertions; removing the interrupt path entirely fails all three.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task InterruptedInvocationStopsAndExitsOutsideTheOutcomeCodes()
    {
        var root = CreateTemporaryDirectory();
        var citations = WriteLongWorkload(root);

        // Arrange: the tool as a caller runs it, checking far more citations than it can finish while the test
        // watches, so the invocation is certain to be mid-step when the interrupt arrives
        using var process = StartTool(root, "verify-evidence", "report.md");
        try
        {
            var rendered = new List<string>();
            var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var draining = Task.Run(
                async () =>
                {
                    while (await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                               .ConfigureAwait(false) is { } line)
                    {
                        lock (rendered)
                        {
                            rendered.Add(line);
                            if (rendered.Count >= ProgressBeforeInterrupt)
                                checking.TrySetResult();
                        }
                    }
                },
                TestContext.Current.CancellationToken);

            // Act: wait until it is genuinely working through the report, then interrupt it at the terminal
            await checking.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            var workingWhenInterrupted = !process.HasExited;

            Interrupt(process);

            var exit = process.WaitForExitAsync(TestContext.Current.CancellationToken);
            var settled = await Task.WhenAny(exit, Task.Delay(Deadline, TestContext.Current.CancellationToken));

            var exitedOnItsOwn = process.HasExited;
            var exitCode = exitedOnItsOwn ? process.ExitCode : int.MinValue;

            // Only once it has exited is the end of its output the end of the story; a run still going has no
            // last line to wait for, and the assertions below say so rather than timing out here.
            if (exitedOnItsOwn)
                await draining.WaitAsync(Deadline, TestContext.Current.CancellationToken);

            string[] written;
            lock (rendered)
                written = [.. rendered];

            // Assert: it was interrupted mid-report, unwound rather than died, and left a code no outcome maps to
            Assert.Multiple(
                () => Assert.True(workingWhenInterrupted, "the invocation had already finished before it was interrupted"),
                () => Assert.Same(exit, settled),
                () => Assert.True(exitedOnItsOwn, "the interrupted invocation never exited"),
                () => Assert.Contains(written, line => line.Contains("present  cited.txt", StringComparison.Ordinal)),
                () => Assert.True(written.Length < citations, "the invocation ran the whole report to completion"),
                () => Assert.DoesNotContain(written, line => line.Contains("locators:", StringComparison.Ordinal)),
                () => Assert.Contains("anneal: interrupted.", written),
                () => Assert.Equal(ExitInterrupted, exitCode),
                () => Assert.NotEqual(AnnealTool.ExitSuccess, exitCode),
                () => Assert.NotEqual(AnnealTool.ExitGatedFailure, exitCode),
                () => Assert.NotEqual(AnnealTool.ExitUsageError, exitCode),
                () => Assert.NotEqual(AnnealTool.ExitRefused, exitCode));
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-I5 — the caller's signal is the only one in effect for the whole of an invocation, so a
    ///     cancellation takes effect while a model call is still waiting for its reply rather than only after
    ///     the reply arrives.
    /// </summary>
    /// <remarks>
    ///     The endpoint never replies, which is what makes this test non-vacuous. Nothing can complete this
    ///     invocation except cancellation reaching the seam, so every way of losing the signal between the entry
    ///     point and the model — blocking on the asynchronous seam, or handing it a token of the operation's own
    ///     — fails here rather than elsewhere: the run never settles and the deadline reports it. That the
    ///     endpoint's token could be cancelled at all is asserted separately, because a signal that can never
    ///     fire is exactly what a hardcoded absent one looks like from below.
    /// </remarks>
    [Fact]
    public async Task CancellationTakesEffectWhileAModelCallIsInFlight()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: a model that accepts the turn and then never answers it
            var endpoint = new NeverRepliesEndpoint();
            using var cancellation = new CancellationTokenSource();

            // Act: run the probe through the command surface under the caller's signal
            var run = Task.Run(
                () => AnnealTool.RunAsync(
                    ["probe-rule-owner", "each rule has exactly one owner"],
                    TextWriter.Null,
                    [new ProbeRuleOwnerOperation(root, _ => endpoint)],
                    root,
                    cancellation.Token),
                TestContext.Current.CancellationToken);

            // Act: wait until a model call is genuinely in flight, then cancel during that wait
            await endpoint.InFlight.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            var waitingWhenCancelled = !run.IsCompleted;

            await cancellation.CancelAsync();
            var settled = await Task.WhenAny(run, Task.Delay(Deadline, TestContext.Current.CancellationToken));

            // Assert: the cancellation landed inside the model call, which never produced a reply
            Assert.Multiple(
                () => Assert.True(waitingWhenCancelled, "the invocation finished without waiting on the model"),
                () => Assert.Same(run, settled),
                () => Assert.True(endpoint.TokenCouldBeCancelled, "the seam was handed a signal that can never fire"),
                () => Assert.True(endpoint.CancelledWhileWaiting, "cancellation did not land during the wait"),
                () => Assert.False(endpoint.Replied),
                () => Assert.Equal(1, endpoint.Calls));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-08 — every invocation appends a structured record of the operation, its inputs, its outcome
    ///     and any model usage, in a form a later query can aggregate without parsing prose, identifying the
    ///     outcome so that its meaning is fixed as new outcomes are added.
    /// </summary>
    /// <remarks>
    ///     Every assertion here reads the recorded file rather than the rendered output, because "without
    ///     parsing prose" is the clause: a record a test had to interpret would be prose with punctuation. The
    ///     four invocations deliberately reach four different outcomes, including one that never entered an
    ///     operation, so the record cannot be a by-product of a successful run.
    ///     <para>
    ///         The outcome is asserted to be the member's name and asserted not to be its position, because the
    ///         two are indistinguishable while the set has its present shape and diverge silently the moment a
    ///         member is inserted mid-set. Records are aggregated across releases — that is what aggregation is
    ///         for here — so a record written today is read by a version that has more outcomes than this one.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task InvocationsAppendStructuredRecords()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "owner.md"), "Each rule has exactly one owner.");
            File.WriteAllLines(Path.Combine(root, "subject.txt"), ["first line", "the promise this cites"]);
            var honest = WriteReport(root, "honest.md", "`subject.txt:2` - \"the promise this cites\"");
            var wrong = WriteReport(root, "wrong.md", "`subject.txt:1` - \"a line that is not there\"");

            IReadOnlyList<IOperation> operations = [new VerifyEvidenceOperation(root)];

            // Act: four invocations reaching four different outcomes, one of which never enters an operation
            var succeeded = await AnnealTool.RunAsync(
                ["verify-evidence", honest], TextWriter.Null, operations, root, TestContext.Current.CancellationToken);
            var failed = await AnnealTool.RunAsync(
                ["verify-evidence", wrong], TextWriter.Null, operations, root, TestContext.Current.CancellationToken);
            var misused = await AnnealTool.RunAsync(
                ["no-such-action"], TextWriter.Null, operations, root, TestContext.Current.CancellationToken);
            var refused = await RunProbe(root, "Two files state it.", Answer("StatedInSeveralPlaces", ""));

            // Assert: one record per invocation, in the order they ran, each of which a query can total without reading prose
            var records = ReadRecords(RecordStore.InvocationsPathFor(root));
            Assert.Equal(4, records.Length);

            var success = records[0];
            var failure = records[1];
            var usageError = records[2];
            var refusal = records[3];

            Assert.Multiple(
                // The operation and its inputs, as the caller gave them.
                () => Assert.Equal("verify-evidence", Text(success, "action")),
                () => Assert.Equal([honest], Strings(success, "arguments")),
                () => Assert.Equal("probe-rule-owner", Text(refusal, "action")),
                () => Assert.Equal(["each rule has exactly one owner"], Strings(refusal, "arguments")),
                () => Assert.Equal("no-such-action", Text(usageError, "action")),
                () => Assert.Empty(Strings(usageError, "arguments")),

                // The outcome, by name, matching what the caller was told through the exit code.
                () => Assert.Equal(nameof(OperationOutcome.Succeeded), Text(success, "outcome")),
                () => Assert.Equal(nameof(OperationOutcome.Failed), Text(failure, "outcome")),
                () => Assert.Equal(nameof(OperationOutcome.UsageError), Text(usageError, "outcome")),
                () => Assert.Equal(nameof(OperationOutcome.Refused), Text(refusal, "outcome")),
                () => Assert.Equal(succeeded, records[0].GetProperty("exitCode").GetInt32()),
                () => Assert.Equal(failed, records[1].GetProperty("exitCode").GetInt32()),
                () => Assert.Equal(misused, records[2].GetProperty("exitCode").GetInt32()),
                () => Assert.Equal(refused.ExitCode, records[3].GetProperty("exitCode").GetInt32()),

                // An outcome identified by name and never by position, so a record outlives the set growing.
                () => Assert.All(
                    records,
                    record => Assert.False(
                        int.TryParse(Text(record, "outcome"), out _),
                        "an outcome recorded as a number changes meaning when a member is inserted mid-set")),
                () => Assert.All(
                    records,
                    record => Assert.Contains(
                        Text(record, "outcome"),
                        Enum.GetNames<OperationOutcome>())),

                // The version that produced the record, so records aggregated across releases stay attributable.
                () => Assert.All(records, record => Assert.Equal(AnnealTool.Version, Text(record, "toolVersion"))),

                // Model usage, totalled over the invocation - and absent where nothing consulted a model.
                () => Assert.Equal(0, success.GetProperty("modelInteractions").GetInt32()),
                () => Assert.False(success.TryGetProperty("usage", out _)),
                () => Assert.Equal(2, refusal.GetProperty("modelInteractions").GetInt32()),
                () => Assert.Equal(
                    2 * ScriptedEndpoint.ReportedInputTokens,
                    refusal.GetProperty("usage").GetProperty("inputTokens").GetInt64()),
                () => Assert.Equal(
                    2 * ScriptedEndpoint.ReportedOutputTokens,
                    refusal.GetProperty("usage").GetProperty("outputTokens").GetInt64()),

                // Enough to order and to cost a run without reading a line of it.
                () => Assert.All(
                    records,
                    record => Assert.True(record.GetProperty("durationMilliseconds").GetDouble() >= 0)),
                () => Assert.All(
                    records,
                    record => Assert.True(record.GetProperty("at").GetDateTimeOffset() > DateTimeOffset.MinValue)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-20 — an operation reports escalation as an outcome distinct from both success and failure,
    ///     carrying its own exit code and rendering distinctly at the terminal.
    /// </summary>
    [Fact]
    public async Task EscalationIsDistinctFromSuccessAndFailure()
    {
        // Arrange: the same escalating operation under a gating category and a non-gating one, because the
        // promise is that no category may turn an escalation into a verdict
        var gating = new StringWriter();
        var advisory = new StringWriter();

        // Act
        var gatingExit = await AnnealTool.RunAsync(
            ["stub"],
            gating,
            [new StubOperation(OperationCategory.Enforcement, OperationOutcome.Escalated)],
            TestContext.Current.CancellationToken);

        var advisoryExit = await AnnealTool.RunAsync(
            ["stub"],
            advisory,
            [new StubOperation(OperationCategory.Advisory, OperationOutcome.Escalated)],
            TestContext.Current.CancellationToken);

        // Assert: its own code, the same one whatever the category, and distinct from every other outcome's
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitEscalated, gatingExit),
            () => Assert.Equal(AnnealTool.ExitEscalated, advisoryExit),
            () => Assert.NotEqual(AnnealTool.ExitSuccess, AnnealTool.ExitEscalated),
            () => Assert.NotEqual(AnnealTool.ExitGatedFailure, AnnealTool.ExitEscalated),
            () => Assert.NotEqual(AnnealTool.ExitUsageError, AnnealTool.ExitEscalated),
            () => Assert.NotEqual(AnnealTool.ExitRefused, AnnealTool.ExitEscalated),

            // And it reads as an escalation rather than as a failure.
            () => Assert.Contains("escalated", gating.ToString(), StringComparison.OrdinalIgnoreCase),
            () => Assert.Contains("escalated", advisory.ToString(), StringComparison.OrdinalIgnoreCase),
            () => Assert.DoesNotContain("failed", gating.ToString(), StringComparison.OrdinalIgnoreCase),
            () => Assert.DoesNotContain("failed", advisory.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}
