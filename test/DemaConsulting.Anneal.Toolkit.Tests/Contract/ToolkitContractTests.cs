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
///     Boundary tests for the Toolkit contract in <c>docs/architecture/toolkit.md</c>.
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
public class ToolkitContractTests
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
    ///     TOOLKIT-01 — an unrecognized action exits with the caller-error code of TOOLKIT-10 and lists the
    ///     actions that exist, so a caller discovers the surface without reading the source. The set it lists
    ///     is every action the tool ships, and each is a reachable action rather than only a name in the list.
    /// </summary>
    [Fact]
    public async Task UnknownActionListsAvailableActions()
    {
        // Arrange: a caller who has named an action this tool does not have
        var output = new StringWriter();

        // Act: the action is named first, as "dotnet anneal <action>"
        var exitCode = await AnnealTool.RunAsync(["no-such-action"], output, TestContext.Current.CancellationToken);
        var written = output.ToString();

        // Act: check-contracts, one of the listed actions, dispatched against a repository whose contract holds
        using var repository = BuildContractRepository("AcceptedRecordIsDurable", "Passed");
        var reachableOutput = new StringWriter();
        var reachableExit = await AnnealTool.RunAsync(
            ["check-contracts"],
            reachableOutput,
            [new CheckContractsOperation(repository.Root)],
            repository.Root,
            TestContext.Current.CancellationToken);

        // Assert: the caller-error code, and the shipped set is exactly the five actions, each discoverable
        // from the output and each actually reachable rather than merely advertised
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitUsageError, exitCode),
            () => Assert.Contains("unknown action 'no-such-action'", written, StringComparison.Ordinal),
            () => Assert.NotEmpty(AnnealTool.DefaultOperations),
            () => Assert.Equal(
                new[] { "check-contracts", "lint-fix", "probe-rule-owner", "route", "stats", "verify-evidence" },
                AnnealTool.DefaultOperations.Select(operation => operation.Name).OrderBy(name => name).ToArray()),
            () => Assert.All(
                AnnealTool.DefaultOperations,
                operation => Assert.Contains(operation.Name, written, StringComparison.Ordinal)),
            // check-contracts was reached and reported a verdict, not turned away as an unknown action
            () => Assert.Equal(AnnealTool.ExitSuccess, reachableExit),
            () => Assert.DoesNotContain("unknown action", reachableOutput.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    ///     TOOLKIT-17 — the registered check-contracts action verifies that every contract clause names a
    ///     boundary test that exists and passed: it succeeds when the clause-to-test link holds, and gates the
    ///     build when the link is broken.
    /// </summary>
    /// <remarks>
    ///     Driven through the same command surface a caller has — the action named first, dispatched against a
    ///     throw-away repository — so what is proven is the registered action rather than the check underneath
    ///     it. The verdict is read from the exit code alone, because that is all the enforcement gate that runs
    ///     it in <c>lint.ps1</c> has.
    /// </remarks>
    [Fact]
    public async Task CheckContractsVerifiesTheClauseToTestLink()
    {
        // Arrange: one repository whose clause names an existing passing test, and one whose clause names a
        // test nothing declares - the same clause, the link intact in the first and broken in the second
        using var linked = BuildContractRepository("AcceptedRecordIsDurable", "Passed");
        using var broken = BuildContractRepository("NoSuchBoundaryTest", "Passed");

        // Act: dispatch check-contracts against each, as a real caller does
        var linkedOutput = new StringWriter();
        var linkedExit = await AnnealTool.RunAsync(
            ["check-contracts"],
            linkedOutput,
            [new CheckContractsOperation(linked.Root)],
            linked.Root,
            TestContext.Current.CancellationToken);

        var brokenOutput = new StringWriter();
        var brokenExit = await AnnealTool.RunAsync(
            ["check-contracts"],
            brokenOutput,
            [new CheckContractsOperation(broken.Root)],
            broken.Root,
            TestContext.Current.CancellationToken);

        // Assert: the intact link reports success and says what it checked; the broken link gates the build
        // and names the clause whose test it could not find
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, linkedExit),
            () => Assert.Contains("clauses, 1 test links checked.", linkedOutput.ToString(), StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitGatedFailure, brokenExit),
            () => Assert.Contains("INGEST-01", brokenOutput.ToString(), StringComparison.Ordinal),
            () => Assert.Contains("NoSuchBoundaryTest", brokenOutput.ToString(), StringComparison.Ordinal));
    }

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
    ///     TOOLKIT-10 — an invocation whose arguments the named action cannot use exits with the caller-error
    ///     code whatever category that action declares, while the outcomes of actions that actually ran keep
    ///     the mapping TOOLKIT-02 and TOOLKIT-06 describe.
    /// </summary>
    [Fact]
    public async Task UsageErrorExitsAsCallerErrorWhateverTheCategory()
    {
        // Arrange and act: the same usage error under a category that gates and one that does not
        var researchMisuse = await RunStub(OperationCategory.Research, OperationOutcome.UsageError);
        var enforcementMisuse = await RunStub(OperationCategory.Enforcement, OperationOutcome.UsageError);

        // Act: the same two operations, having actually run and reported an answer
        var researchFailure = await RunStub(OperationCategory.Research, OperationOutcome.Failed);
        var enforcementFailure = await RunStub(OperationCategory.Enforcement, OperationOutcome.Failed);
        var researchRefusal = await RunStub(OperationCategory.Research, OperationOutcome.Refused);
        var enforcementRefusal = await RunStub(OperationCategory.Enforcement, OperationOutcome.Refused);

        // Act: a caller who scripted an option the action does not take, as the reported defect did
        var misuseOutput = new StringWriter();
        await AnnealTool.RunAsync(
            ["stub", "--rule", "some rule"],
            misuseOutput,
            [new StubOperation(OperationCategory.Research, OperationOutcome.UsageError)],
            TestContext.Current.CancellationToken);
        var written = misuseOutput.ToString();

        // Assert: the caller's own mistake never reads as a check that ran, in either direction, and the
        // outcomes of operations that did run are exactly where TOOLKIT-02 and TOOLKIT-06 left them
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitUsageError, researchMisuse),
            () => Assert.NotEqual(AnnealTool.ExitSuccess, researchMisuse),
            () => Assert.Equal(AnnealTool.ExitUsageError, enforcementMisuse),
            () => Assert.NotEqual(AnnealTool.ExitGatedFailure, enforcementMisuse),
            () => Assert.Equal(researchMisuse, enforcementMisuse),
            () => Assert.Contains("'stub'", written, StringComparison.Ordinal),
            () => Assert.Contains("dotnet anneal stub", written, StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitSuccess, researchFailure),
            () => Assert.Equal(AnnealTool.ExitGatedFailure, enforcementFailure),
            () => Assert.Equal(AnnealTool.ExitRefused, researchRefusal),
            () => Assert.Equal(AnnealTool.ExitRefused, enforcementRefusal));
    }

    /// <summary>
    ///     TOOLKIT-03 — verify-evidence reports, for each locator cited in a report, whether the quoted text
    ///     is at the file and line named, reaching no verdict about the report's own conclusion.
    /// </summary>
    [Fact]
    public async Task EvidenceLocatorsAreCheckedAgainstSource()
    {
        // Arrange: a source file, and a report citing one locator that holds and one that does not
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllLines(
                Path.Combine(root, "subject.txt"),
                ["first line", "the promise this cites", "third line"]);

            var honest = WriteReport(root, "honest.md", "`subject.txt:2` - \"the promise this cites\"");
            var wrong = WriteReport(
                root,
                "wrong.md",
                "`subject.txt:2` - \"the promise this cites\"",
                "`subject.txt:3` - \"the promise this cites\"",
                "`absent.txt:1` - \"never written\"");

            var operations = new[] { (IOperation)new VerifyEvidenceOperation(root) };

            // Act: check both reports through the command surface
            var honestOutput = new StringWriter();
            var honestExit = await AnnealTool.RunAsync(
                ["verify-evidence", honest], honestOutput, operations, TestContext.Current.CancellationToken);

            var wrongOutput = new StringWriter();
            var wrongExit = await AnnealTool.RunAsync(
                ["verify-evidence", wrong], wrongOutput, operations, TestContext.Current.CancellationToken);
            var wrongWritten = wrongOutput.ToString();

            // Assert: each locator is reported individually, and nothing is said about the report's verdict
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, honestExit),
                () => Assert.Contains(
                    "present  subject.txt:2 \"the promise this cites\"",
                    honestOutput.ToString(),
                    StringComparison.Ordinal),
                () => Assert.Contains(
                    "1 locators: 1 present, 0 absent.",
                    honestOutput.ToString(),
                    StringComparison.Ordinal),
                () => Assert.NotEqual(AnnealTool.ExitSuccess, wrongExit),
                () => Assert.Contains(
                    "present  subject.txt:2",
                    wrongWritten,
                    StringComparison.Ordinal),
                () => Assert.Contains(
                    "absent   subject.txt:3 \"the promise this cites\" - line 3 does not contain",
                    wrongWritten,
                    StringComparison.Ordinal),
                () => Assert.Contains(
                    "absent   absent.txt:1 \"never written\" - file not found",
                    wrongWritten,
                    StringComparison.Ordinal),
                () => Assert.Contains("3 locators: 1 present, 2 absent.", wrongWritten, StringComparison.Ordinal),
                () => Assert.DoesNotContain("SUCCEEDED", wrongWritten, StringComparison.Ordinal),
                () => Assert.DoesNotContain("verdict", wrongWritten, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-04 — probe-rule-owner names the single file that owns a rule, or refuses when the rule is
    ///     stated in more than one place or in none.
    /// </summary>
    [Fact]
    public async Task RuleOwnerProbeNamesOneFileOrRefuses()
    {
        // Arrange: a repository, and a model scripted to reach each of the three conclusions in turn
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "owner.md"), "Each rule has exactly one owner.");

            // Act: the same question answered three ways
            var owned = await RunProbe(root, "owner.md states it and nothing else does.", Answer("SingleOwner", "owner.md"));
            var several = await RunProbe(root, "Two files state it.", Answer("StatedInSeveralPlaces", ""));
            var nowhere = await RunProbe(root, "Nothing states it.", Answer("StatedNowhere", ""));

            // Assert: one file is named on success, and neither of the two unanswerable cases reports one
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, owned.ExitCode),
                () => Assert.Contains("  owner: owner.md", owned.Output, StringComparison.Ordinal),
                () => Assert.Equal(AnnealTool.ExitRefused, several.ExitCode),
                () => Assert.Contains("more than one place", several.Output, StringComparison.Ordinal),
                () => Assert.Equal(AnnealTool.ExitRefused, nowhere.ExitCode),
                () => Assert.Contains("stated nowhere", nowhere.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("  owner: ", several.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("  owner: ", nowhere.Output, StringComparison.Ordinal),

                // The reasoning pass is served by the middle tier with tools and no schema; the probe by the
                // cheapest tier with the schema last and no tools. The open-ended tier is not consulted at all.
                () => Assert.Single(owned.Reasoning.Requests),
                () => Assert.Single(owned.Probing.Requests),
                () => Assert.Empty(owned.OpenEnded.Requests),
                () => Assert.NotEmpty(owned.Reasoning.Requests[0].Tools),
                () => Assert.DoesNotContain("<schema>", LastMessage(owned.Reasoning.Requests[0]), StringComparison.Ordinal),
                () => Assert.Empty(owned.Probing.Requests[0].Tools),
                () => Assert.Contains("<schema>", LastMessage(owned.Probing.Requests[0]), StringComparison.Ordinal),

                // The schema is presented after the question, and spells out the closed vocabulary.
                () => Assert.True(
                    LastMessage(owned.Probing.Requests[0]).IndexOf("<schema>", StringComparison.Ordinal) >
                    LastMessage(owned.Probing.Requests[0]).IndexOf("which single file owns", StringComparison.Ordinal)),
                () => Assert.Contains("\"StatedInSeveralPlaces\"", LastMessage(owned.Probing.Requests[0]), StringComparison.Ordinal),

                // Every turn carries an output ceiling, so no turn can generate until the window is exhausted.
                () => Assert.All(
                    owned.Reasoning.Requests.Concat(owned.Probing.Requests),
                    request => Assert.True(request.MaxOutputTokens > 0)),

                // And the ceiling is a real transport limit, not just a number the seam carries: it reaches
                // the provider's session configuration. A reasoning model given an open question and no
                // ceiling generates until it exhausts the context window.
                () => Assert.Equal(
                    ModelSession.DefaultMaxOutputTokens,
                    CopilotEndpoint
                        .BuildSessionConfig(owned.Reasoning.Requests[0])
                        .ModelCapabilities?.Limits?.MaxOutputTokens));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-06 — refusal is reported as an outcome distinct from both success and failure, so a caller
    ///     can tell "the question could not be answered" from "the answer is no".
    /// </summary>
    [Fact]
    public async Task RefusalIsDistinctFromFailure()
    {
        // Arrange: one operation, driven to each of the three outcomes
        var root = CreateTemporaryDirectory();
        try
        {
            // Act: an answer, a refusal, and a failure that is not a refusal
            var answered = await RunProbe(root, "owner.md states it.", Answer("SingleOwner", "owner.md"));
            var refused = await RunProbe(root, "Several files state it.", Answer("StatedInSeveralPlaces", ""));
            var failed = await RunProbe(root, "unreachable", "unreachable", reachable: false);

            // Assert: three distinct exit codes, and a refusal that reads as neither of the other two
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, answered.ExitCode),
                () => Assert.Equal(AnnealTool.ExitRefused, refused.ExitCode),
                () => Assert.NotEqual(answered.ExitCode, refused.ExitCode),
                () => Assert.NotEqual(AnnealTool.ExitGatedFailure, refused.ExitCode),
                () => Assert.Contains("refused", refused.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("refused", answered.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("refused", failed.Output, StringComparison.Ordinal),
                () => Assert.Equal(3, new[] { OperationOutcome.Succeeded, OperationOutcome.Failed, OperationOutcome.Refused }.Distinct().Count()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-07 — a model-backed operation that cannot reach a model fails with a message naming the
    ///     cause, never falling back to a deterministic approximation, while the deterministic operation keeps
    ///     working with no model reachable.
    /// </summary>
    /// <remarks>
    ///     Two ways of having no model are asserted, because they are different causes and a caller can only act
    ///     on the one they are told. An account that cannot be reached at all is named as that. An account that
    ///     answers, and offers none of the models the role's candidates name, is a retirement rather than an
    ///     outage — so that message names the role, every candidate tried in order, and the configuration file
    ///     to change them in, which is what makes a retired-everything role a one-line fix instead of a mystery.
    /// </remarks>
    [Fact]
    public async Task UnreachableModelFailsLoudly()
    {
        // Arrange: a repository whose model cannot be reached, and a report the deterministic check can verify
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllLines(Path.Combine(root, "subject.txt"), ["first line", "the promise this cites"]);
            var report = WriteReport(root, "report.md", "`subject.txt:2` - \"the promise this cites\"");

            // Act: the probe with no model reachable, and the deterministic operation under the same conditions
            var probe = await RunProbe(root, "unused", "unused", reachable: false);

            var evidenceOutput = new StringWriter();
            var evidenceExit = await AnnealTool.RunAsync(
                ["verify-evidence", report],
                evidenceOutput,
                [new VerifyEvidenceOperation(root)],
                TestContext.Current.CancellationToken);

            // Act: and again against an account that answers but offers none of the role's candidates
            WriteModelConfiguration(
                root,
                ["a-retired-light-model", "another-retired-light-model"],
                ["a-retired-medium-model"],
                ["a-retired-heavy-model"]);
            var retired = await RunProbe(
                root, "unused", "unused", ["a-model-this-repository-never-named"]);

            // Assert: the failure names the cause and claims nothing, and the deterministic check is unaffected
            Assert.Multiple(
                () => Assert.Contains("no judgement was obtained", probe.Output, StringComparison.Ordinal),
                () => Assert.Contains("the Copilot account is not signed in", probe.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("  owner: ", probe.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("evidence: ", probe.Output, StringComparison.Ordinal),
                () => Assert.Equal(AnnealTool.ExitSuccess, evidenceExit),
                () => Assert.Contains("1 present, 0 absent.", evidenceOutput.ToString(), StringComparison.Ordinal),

                // A role whose every candidate has been retired fails, and says which role, what was tried,
                // and where to change it.
                () => Assert.Contains("no judgement was obtained", retired.Output, StringComparison.Ordinal),
                () => Assert.Contains("Medium", retired.Output, StringComparison.Ordinal),
                () => Assert.Contains("a-retired-medium-model", retired.Output, StringComparison.Ordinal),
                () => Assert.Contains(".anneal/config.json", retired.Output, StringComparison.Ordinal),
                () => Assert.Empty(retired.Reasoning.Requests),
                () => Assert.DoesNotContain("  owner: ", retired.Output, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-I2 — a probe result reaches a caller only as a fully decoded typed value; a reply that cannot
    ///     be decoded within the retry budget fails the operation and returns nothing partial.
    /// </summary>
    [Fact]
    public async Task UndecodableProbeResultFailsTheOperation()
    {
        // Arrange: a model that never produces valid JSON, and one that produces it only after being corrected
        var root = CreateTemporaryDirectory();
        try
        {
            var hopeless = await RunProbe(
                root,
                "owner.md states it.",
                "I think owner.md owns it.",
                "Still prose, sorry.",
                "{ \"ownership\": ");

            var rescued = await RunProbe(
                root,
                "owner.md states it.",
                "{ \"ownership\": \"SingleOwner\" }",
                Answer("SingleOwner", "owner.md"));

            var corrective = rescued.Probing.Requests[1].Messages[^1].Text;

            // Assert: the exhausted budget fails and yields nothing, while the retry that saw its own mistake works
            Assert.Multiple(
                () => Assert.Contains("no judgement was obtained", hopeless.Output, StringComparison.Ordinal),
                () => Assert.Contains("within 3 attempts", hopeless.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("  owner: ", hopeless.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("evidence: ", hopeless.Output, StringComparison.Ordinal),
                () => Assert.Equal(3, hopeless.Probing.Requests.Count),

                // The model is shown its own reply and the parse error, because it cannot correct a mistake it
                // cannot see.
                () => Assert.Equal(AnnealTool.ExitSuccess, rescued.ExitCode),
                () => Assert.Contains("  owner: owner.md", rescued.Output, StringComparison.Ordinal),
                () => Assert.Contains(
                    "{ \"ownership\": \"SingleOwner\" }",
                    rescued.Probing.Requests[1].Messages[^2].Text,
                    StringComparison.Ordinal),
                () => Assert.Contains("could not be parsed", corrective, StringComparison.Ordinal),
                () => Assert.Contains("required", corrective, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-12 — <c>dotnet anneal help</c>, given no further argument, lists every shipped action with
    ///     its one-line summary and exits with the success code, so the surface is discoverable without
    ///     provoking an error.
    /// </summary>
    [Fact]
    public async Task HelpListsEveryActionAndSucceeds()
    {
        // Arrange: a caller who wants to learn the surface deliberately, not by making a mistake
        var output = new StringWriter();

        // Act: "dotnet anneal help", with no action to describe
        var exitCode = await AnnealTool.RunAsync(["help"], output, TestContext.Current.CancellationToken);
        var written = output.ToString();

        // Assert: the success code, and every shipped action with its summary is present in the listing
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
            () => Assert.NotEmpty(AnnealTool.DefaultOperations),
            () => Assert.All(
                AnnealTool.DefaultOperations,
                operation => Assert.Multiple(
                    () => Assert.Contains(operation.Name, written, StringComparison.Ordinal),
                    () => Assert.Contains(operation.Summary, written, StringComparison.Ordinal))));
    }

    /// <summary>
    ///     TOOLKIT-13 — <c>dotnet anneal help &lt;action&gt;</c> prints the named action's detailed usage and
    ///     exits with the success code, while an action that does not ship is the usage error TOOLKIT-10
    ///     defines, reported with the same list of existing actions an unknown action already produces.
    /// </summary>
    [Fact]
    public async Task HelpForActionPrintsItsUsageAndRejectsUnknown()
    {
        // Arrange: a shipped action to describe, and a name that ships nowhere
        var known = AnnealTool.DefaultOperations[0];

        // Act: "help <known>" describes it
        var knownOutput = new StringWriter();
        var knownExit = await AnnealTool.RunAsync(
            ["help", known.Name], knownOutput, TestContext.Current.CancellationToken);
        var knownWritten = knownOutput.ToString();

        // Act: "help <unknown>" is a usage error listing what does exist
        var unknownOutput = new StringWriter();
        var unknownExit = await AnnealTool.RunAsync(
            ["help", "no-such-action"], unknownOutput, TestContext.Current.CancellationToken);
        var unknownWritten = unknownOutput.ToString();

        // Assert: the known action's detailed usage is printed and succeeds; the unknown one is the
        // caller-error code with every real action still discoverable, so help fabricates no guidance
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, knownExit),
            () => Assert.Contains(known.Usage, knownWritten, StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitUsageError, unknownExit),
            () => Assert.Contains("no-such-action", unknownWritten, StringComparison.Ordinal),
            () => Assert.All(
                AnnealTool.DefaultOperations,
                operation => Assert.Contains(operation.Name, unknownWritten, StringComparison.Ordinal)));
    }

    /// <summary>
    ///     TOOLKIT-I4 — the detailed usage an action presents through <c>help &lt;action&gt;</c> and the usage
    ///     it presents when invoked with arguments it cannot use are one and the same text, drawn from a single
    ///     declared source, so the two renderings cannot state the invocation differently or drift apart.
    /// </summary>
    [Fact]
    public async Task HelpAndUsageErrorShareOneUsageSource()
    {
        // Arrange: a stub whose usage is a distinctive literal declared exactly once. If the two renderings
        // ever drew from separate strings, only one of them could contain this literal, and this test fails.
        const string distinctiveUsage = "usage: dotnet anneal stub <sigil-7f3a9c> - one positional argument";
        IReadOnlyList<IOperation> operations =
            [new StubOperation(OperationCategory.Research, OperationOutcome.UsageError, distinctiveUsage)];

        // Act: the discovery rendering, "help <action>"
        var helpOutput = new StringWriter();
        var helpExit = await AnnealTool.RunAsync(
            ["help", "stub"], helpOutput, operations, TestContext.Current.CancellationToken);
        var helpWritten = helpOutput.ToString();

        // Act: the usage-error rendering, the action given arguments it cannot use
        var misuseOutput = new StringWriter();
        var misuseExit = await AnnealTool.RunAsync(
            ["stub", "--flag", "value"], misuseOutput, operations, TestContext.Current.CancellationToken);
        var misuseWritten = misuseOutput.ToString();

        // Assert: both renderings carry the one declared literal verbatim, and each takes the exit its path owns
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitSuccess, helpExit),
            () => Assert.Contains(distinctiveUsage, helpWritten, StringComparison.Ordinal),
            () => Assert.Equal(AnnealTool.ExitUsageError, misuseExit),
            () => Assert.Contains(distinctiveUsage, misuseWritten, StringComparison.Ordinal));
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
    ///     TOOLKIT-05 — every operation that consults a model declares the capability role it requires, and
    ///     roles resolve to concrete models through repository configuration rather than through the operation.
    /// </summary>
    /// <remarks>
    ///     The declaration and the resolution are asserted separately because the clause is two promises. The
    ///     first is that the requirement is visible on the operation: a caller composing operations can see which
    ///     of them will spend model tokens without running them. The second is that the operation does not get
    ///     to answer which model serves its role — the repository does — which is what makes substituting a
    ///     model an edit a downstream repository makes rather than a Toolkit release.
    ///     <para>
    ///         Resolution is proven by changing the configuration file and nothing else, and observing a
    ///         different model reach the seam. An operation that had resolved its own role would be unmoved by
    ///         that edit, and an assertion that only read the configured value back would pass whether or not
    ///         anything used it.
    ///     </para>
    ///     <para>
    ///         A role names an ordered list, so "resolves through configuration" now also means resolving to the
    ///         first candidate the account is offered. That is asserted the same way: the leading candidate is
    ///         retired from the offered set and the next one has to answer. The negative half matters at least
    ///         as much — every model that reached the seam on every run is checked to be one the file named, so
    ///         availability cannot have introduced a model the repository never asked for.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task OperationRolesResolveThroughConfiguration()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "owner.md"), "Each rule has exactly one owner.");

            // Assert: the declaration is on the operation, and distinguishes the model-backed from the not
            var probing = new ProbeRuleOwnerOperation(root);
            var deterministic = new VerifyEvidenceOperation(root);

            // Arrange: a repository that names its own candidates, none of which the Toolkit ships as a default
            WriteModelConfiguration(
                root,
                ["a-named-light-model", "a-spare-light-model"],
                ["a-named-medium-model", "a-spare-medium-model"],
                ["a-named-heavy-model"]);

            // Act: the same operation, run once against that configuration and once against an edited one
            var configured = await RunProbe(root, "owner.md states it.", Answer("SingleOwner", "owner.md"));

            WriteModelConfiguration(
                root,
                ["a-replacement-light-model"],
                ["a-replacement-medium-model"],
                ["unused"]);
            var reconfigured = await RunProbe(root, "owner.md states it.", Answer("SingleOwner", "owner.md"));

            // Act: and once where the account no longer offers each role's leading candidate, so the rearguard
            // behind it is what has to answer
            WriteModelConfiguration(
                root,
                ["a-retired-light-model", "a-surviving-light-model"],
                ["a-retired-medium-model", "a-surviving-medium-model"],
                ["a-retired-heavy-model", "a-surviving-heavy-model"]);
            var retired = await RunProbe(
                root,
                "owner.md states it.",
                Answer("SingleOwner", "owner.md"),
                ["a-surviving-light-model", "a-surviving-medium-model", "a-surviving-heavy-model", "a-bystander"]);

            // Act: and once against a repository that configures nothing at all
            File.Delete(Path.Combine(root, ModelConfiguration.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var defaulted = await RunProbe(root, "owner.md states it.", Answer("SingleOwner", "owner.md"));

            Assert.Multiple(
                // The requirement is declared, and declared as absent by the operation that consults no model.
                () => Assert.NotNull(probing.RequiredRole),
                () => Assert.Null(deterministic.RequiredRole),
                () => Assert.All(
                    AnnealTool.DefaultOperations,
                    operation => Assert.True(
                        operation.RequiredRole is null || Enum.IsDefined(operation.RequiredRole.Value),
                        $"'{operation.Name}' declares a role that is not one of the capability tiers")),

                // Each turn carries the model its role resolved to, and the resolution came from the file.
                () => Assert.All(
                    configured.Reasoning.Requests,
                    request => Assert.Equal("a-named-medium-model", request.Model)),
                () => Assert.All(
                    configured.Probing.Requests,
                    request => Assert.Equal("a-named-light-model", request.Model)),

                // Editing only the repository's configuration moves which model answers.
                () => Assert.All(
                    reconfigured.Reasoning.Requests,
                    request => Assert.Equal("a-replacement-medium-model", request.Model)),
                () => Assert.All(
                    reconfigured.Probing.Requests,
                    request => Assert.Equal("a-replacement-light-model", request.Model)),

                // A leading candidate the account is not offered is skipped, and the next one serves the role.
                () => Assert.All(
                    retired.Reasoning.Requests,
                    request => Assert.Equal("a-surviving-medium-model", request.Model)),
                () => Assert.All(
                    retired.Probing.Requests,
                    request => Assert.Equal("a-surviving-light-model", request.Model)),

                // And nothing the account merely offers can be selected: only what the file named.
                () => Assert.All(
                    retired.Reasoning.Requests.Concat(retired.Probing.Requests),
                    request => Assert.Contains(
                        request.Model,
                        new[]
                        {
                            "a-retired-light-model", "a-surviving-light-model",
                            "a-retired-medium-model", "a-surviving-medium-model",
                            "a-retired-heavy-model", "a-surviving-heavy-model"
                        })),

                // A repository that configures nothing still resolves, to the shipped defaults.
                () => Assert.All(
                    defaulted.Reasoning.Requests,
                    request => Assert.Equal(ModelConfiguration.Default.Medium[0], request.Model)),
                () => Assert.All(
                    defaulted.Probing.Requests,
                    request => Assert.Equal(ModelConfiguration.Default.Light[0], request.Model)),

                // And the roles were genuinely distinct, so "resolution" is not one model reached three ways.
                () => Assert.NotEmpty(configured.Reasoning.Requests),
                () => Assert.NotEmpty(configured.Probing.Requests),
                () => Assert.Empty(configured.OpenEnded.Requests));
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
    ///     TOOLKIT-09 — the tool reports the Anneal version it was built from, so an installed payload can be
    ///     identified by version rather than inferred from its contents.
    /// </summary>
    /// <remarks>
    ///     The reported version is compared against the version stamped into the built assembly, not against a
    ///     literal. A test that asserted a particular number would have to be edited at every release and would
    ///     agree with a tool that reported a version it was not built from — which is the failure the clause
    ///     names, since a payload whose self-report and whose contents disagree is worse than one that reports
    ///     nothing.
    ///     <para>
    ///         The report is taken from the installed payload as a caller takes it, by running the built tool in
    ///         a process of its own, because "an installed payload can be identified" is a claim about the thing
    ///         on disk rather than about a property an in-process test can read.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ToolReportsPayloadVersion()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Arrange: the version stamped into the payload beside these tests, read from the file itself
            var built = FileVersionInfo
                .GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "DemaConsulting.Anneal.Toolkit.dll"))
                .ProductVersion;

            // Act: ask the installed payload, as a caller does, in a process of its own
            using var process = StartTool(root, "version");
            var reported = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            // Act: and through the command surface, so the record written below is from a known invocation
            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["version"], output, AnnealTool.DefaultOperations, root, TestContext.Current.CancellationToken);

            var records = ReadRecords(RecordStore.InvocationsPathFor(root));

            Assert.Multiple(
                // Reporting a version is something the tool does, not something it fails at.
                () => Assert.Equal(0, process.ExitCode),
                () => Assert.Equal(0, exitCode),

                // One line, so a caller reads it without parsing.
                () => Assert.Single(reported.Split('\n', StringSplitOptions.RemoveEmptyEntries)),
                () => Assert.Equal(AnnealTool.Version, reported.Trim()),
                () => Assert.Equal(AnnealTool.Version, output.ToString().Trim()),

                // It is a version, and it is the one the payload was built from.
                () => Assert.Matches(@"^\d+\.\d+\.\d+", AnnealTool.Version),
                () => Assert.Equal(built, AnnealTool.Version),

                // And every record the payload writes carries it, so a run can be attributed to a version later.
                () => Assert.All(records, record => Assert.Equal(AnnealTool.Version, Text(record, "toolVersion"))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-11 — every model interaction records a transcript of it: the prompt sent, the reply received,
    ///     the model consulted and the token usage, for every interaction rather than only for those that failed
    ///     or refused, and with no opt-in that could leave it off.
    /// </summary>
    /// <remarks>
    ///     Three runs, chosen so that the set of transcripts cannot be explained by capture that happens only on
    ///     one kind of ending: one that answered, one that refused, and one that never reached a model at all.
    ///     The third is the important one — an interaction that produced no reply still produced a transcript,
    ///     naming what went wrong, because the evidence a later audit needs is densest exactly where the
    ///     interaction did not go as expected.
    ///     <para>
    ///         "No opt-in" is asserted structurally as well as by observation. Every run here transcribes, but a
    ///         run that transcribed by default and could be told not to would pass an assertion about what it did;
    ///         the public surface is checked to have no construction path that omits the destination, which is
    ///         what makes leaving capture off impossible to state rather than merely unusual.
    ///     </para>
    ///     <para>
    ///         The model recorded is the candidate that answered. Each role here leads with a candidate the
    ///         account is not offered, so a transcript naming the configured first choice would be recording
    ///         what was asked for rather than what served the judgement — which is the one thing this evidence
    ///         exists to settle.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ModelInteractionsAreTranscribed()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "owner.md"), "Each rule has exactly one owner.");

            // A retired candidate leads each role, so the model in the transcript can only be the one that
            // actually answered - not the one the configuration happens to name first.
            WriteModelConfiguration(
                root,
                ["a-retired-light-model", "a-transcribed-light-model"],
                ["a-retired-medium-model", "a-transcribed-medium-model"],
                ["unused"]);
            string[] offered = ["a-transcribed-light-model", "a-transcribed-medium-model"];

            // Act: an interaction that answered, one that refused, and one that never reached a model
            await RunProbe(root, "owner.md states it.", Answer("SingleOwner", "owner.md"), offered);
            await RunProbe(root, "Two files state it.", Answer("StatedInSeveralPlaces", ""), offered);
            await RunProbe(root, "unreachable", Answer("SingleOwner", "owner.md"), reachable: false);

            var transcripts = ReadRecords(RecordStore.TranscriptsPathFor(root));
            var replied = transcripts.Where(entry => Text(entry, "result") == ModelTranscript.Replied).ToArray();
            var unanswered = transcripts.Where(entry => Text(entry, "result") == ModelTranscript.Failed).ToArray();

            Assert.Multiple(
                // Every interaction, not only the ones that went wrong: two passes each for the two that ran.
                () => Assert.Equal(4, replied.Length),
                () => Assert.NotEmpty(unanswered),

                // The prompt as it was sent, verbatim and in order.
                () => Assert.All(
                    transcripts,
                    entry => Assert.NotEmpty(entry.GetProperty("prompt").EnumerateArray())),
                () => Assert.All(
                    transcripts,
                    entry => Assert.All(
                        entry.GetProperty("prompt").EnumerateArray(),
                        message => Assert.False(string.IsNullOrEmpty(Text(message, "text"))))),
                () => Assert.Contains(
                    replied,
                    entry => entry.GetProperty("prompt").EnumerateArray()
                        .Any(message => Text(message, "text").Contains(
                            "each rule has exactly one owner", StringComparison.OrdinalIgnoreCase))),

                // The reply that came back, on the interactions that produced one.
                () => Assert.All(replied, entry => Assert.False(string.IsNullOrEmpty(Text(entry, "reply")))),
                () => Assert.Contains(replied, entry => Text(entry, "reply").Contains("SingleOwner", StringComparison.Ordinal)),

                // The model consulted, named concretely rather than by the role that resolved to it, and named
                // as the candidate that actually answered rather than the one listed first.
                () => Assert.All(transcripts, entry => Assert.False(string.IsNullOrEmpty(Text(entry, "model")))),
                () => Assert.Contains(replied, entry => Text(entry, "model") == "a-transcribed-medium-model"),
                () => Assert.Contains(replied, entry => Text(entry, "model") == "a-transcribed-light-model"),
                () => Assert.All(
                    replied,
                    entry => Assert.DoesNotContain(
                        "a-retired-", Text(entry, "model"), StringComparison.Ordinal)),
                () => Assert.All(
                    transcripts,
                    entry => Assert.Contains(Text(entry, "role"), Enum.GetNames<ModelRole>())),

                // The token usage, where the provider reported any.
                () => Assert.All(
                    replied,
                    entry => Assert.Equal(
                        ScriptedEndpoint.ReportedInputTokens,
                        entry.GetProperty("usage").GetProperty("inputTokens").GetInt64())),
                () => Assert.All(
                    replied,
                    entry => Assert.Equal(
                        ScriptedEndpoint.ReportedOutputTokens,
                        entry.GetProperty("usage").GetProperty("outputTokens").GetInt64())),

                // An interaction that produced no reply is transcribed too, and says why.
                () => Assert.All(unanswered, entry => Assert.False(entry.TryGetProperty("reply", out _))),
                () => Assert.All(unanswered, entry => Assert.False(string.IsNullOrEmpty(Text(entry, "failure")))),

                // Nothing was switched on to obtain any of this, and nothing could switch it off: every way of
                // building the seam demands the repository whose transcripts are being kept.
                () => Assert.All(
                    typeof(ModelRoles).GetConstructors(),
                    constructor => Assert.Equal(
                        "repositoryRoot",
                        constructor.GetParameters().FirstOrDefault()?.Name)),
                () => Assert.All(
                    typeof(ModelRoles).GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                    parameter => Assert.NotEqual(typeof(bool), parameter.ParameterType)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-18 — every tool invocation a model makes is transcribed with its arguments and its outcome,
    ///     including one that was refused.
    /// </summary>
    /// <remarks>
    ///     The endpoint invokes the tools it was handed, which is what a provider that runs the tool loop
    ///     natively does inside its own SDK. That is the case the clause exists for: nothing above the seam sees
    ///     those calls, so a transcript written at a call site would be empty exactly where a writing worker's
    ///     behavior is.
    /// </remarks>
    [Fact]
    public async Task ToolInvocationsAreTranscribed()
    {
        // Arrange: a repository, and a worker that reads one file, is refused a protected write, and is refused
        // a path outside the repository
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "present.md"), "a line to read");

            var endpoint = new ToolCallingEndpoint(
                ("read_file", new Dictionary<string, object?>
                {
                    ["path"] = "present.md",
                    ["start"] = 1,
                    ["max"] = 10
                }),
                ("create_file", new Dictionary<string, object?>
                {
                    ["path"] = "lint.ps1",
                    ["content"] = "exit 0"
                }),
                ("read_file", new Dictionary<string, object?>
                {
                    ["path"] = "../escape.txt",
                    ["start"] = 1,
                    ["max"] = 10
                }));

            var session = new ModelSession(
                new ModelRoles(root, _ => endpoint),
                "a charter",
                new ToolGroups(root).SelectTools([ToolGroups.Read, ToolGroups.Edit]));

            // Act: one turn, during which the provider makes all three calls
            await session.RunAsync("repair what lint reported", ModelRole.Heavy, TestContext.Current.CancellationToken);

            var calls = ReadRecords(RecordStore.ToolCallsPathFor(root));
            var refused = calls.Where(call => ToolReply.IsRefusal(Text(call, "result"))).ToArray();

            // Assert: every invocation is there, each with the arguments it was given and the outcome it reached
            Assert.Multiple(
                () => Assert.Equal(3, calls.Length),
                () => Assert.All(calls, call => Assert.False(string.IsNullOrEmpty(Text(call, "tool")))),
                () => Assert.All(calls, call => Assert.False(string.IsNullOrEmpty(Text(call, "arguments")))),
                () => Assert.All(calls, call => Assert.False(string.IsNullOrEmpty(Text(call, "outcome")))),

                // The arguments as the model supplied them, so a reader can tell which file was touched.
                () => Assert.Contains(
                    calls,
                    call => Text(call, "arguments").Contains("present.md", StringComparison.Ordinal)),

                // A refused call is transcribed exactly as a returning one is - the case a self-report would miss.
                () => Assert.Equal(2, refused.Length),
                () => Assert.Contains(
                    refused,
                    call => Text(call, "arguments").Contains("lint.ps1", StringComparison.Ordinal)),
                () => Assert.Contains(
                    refused,
                    call => Text(call, "arguments").Contains("escape.txt", StringComparison.Ordinal)),
                () => Assert.All(
                    refused,
                    call => Assert.Contains("refused", Text(call, "outcome"), StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-22 — when a provider surfaces intermediate reasoning or progress text during a turn, distinct
    ///     from the turn's final reply, that text is captured as part of the same durable per-turn evidence
    ///     TOOLKIT-11 already transcribes, for every turn a provider offers it, always.
    /// </summary>
    /// <remarks>
    ///     The scripted endpoint stands in for a provider that surfaces such text alongside its final reply — the
    ///     shape <c>CopilotEndpoint</c> takes when its session reports intermediate reasoning distinct from the
    ///     accumulated assistant message. The assertion is on the persisted transcript, not on what
    ///     <see cref="ModelSession.RunAsync" /> hands back, because TOOLKIT-22 is a promise about the transcript
    ///     and says nothing about widening the caller-facing result: the reply a caller receives stays exactly
    ///     the final prose.
    /// </remarks>
    [Fact]
    public async Task IntermediateProgressIsTranscribed()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            string[] progress = ["thinking about the owner rule...", "checking owner.md..."];
            var endpoint = new ScriptedEndpoint("SingleOwner: owner.md") { Progress = progress };

            var session = new ModelSession(new ModelRoles(root, _ => endpoint), "a charter");

            var reply = await session.RunAsync(
                "who owns the rule?", ModelRole.Heavy, TestContext.Current.CancellationToken);

            var transcripts = ReadRecords(RecordStore.TranscriptsPathFor(root));
            var replied = transcripts.Single(entry => Text(entry, "result") == ModelTranscript.Replied);

            Assert.Multiple(
                // The caller-facing reply stays exactly the final prose - TOOLKIT-22 widens the transcript, not
                // what RunAsync hands back.
                () => Assert.Equal("SingleOwner: owner.md", reply),

                // Every progress entry the provider surfaced landed in the transcript, in order.
                () => Assert.Equal(progress, Strings(replied, "progress")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-22 — a provider that surfaces no intermediate progress for a turn is recorded as having none;
    ///     the absence is silence, not a defect.
    /// </summary>
    [Fact]
    public async Task NoIntermediateProgressIsRecordedAsNone()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new ScriptedEndpoint("a plain reply");
            var session = new ModelSession(new ModelRoles(root, _ => endpoint), "a charter");

            await session.RunAsync("anything", ModelRole.Heavy, TestContext.Current.CancellationToken);

            var transcripts = ReadRecords(RecordStore.TranscriptsPathFor(root));
            var replied = transcripts.Single(entry => Text(entry, "result") == ModelTranscript.Replied);

            Assert.Empty(Strings(replied, "progress"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-I6 — a model is granted tools only by group selection, every filesystem path resolves inside
    ///     the repository root, and a write to a protected configuration file or repository script is refused.
    /// </summary>
    [Fact]
    public void ToolGrantsAreScopedContainedAndProtected()
    {
        // Arrange: the groups over a repository holding one readable file
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "present.md"), "a line to read");
            var groups = new ToolGroups(root);

            var readOnly = groups.SelectTools([ToolGroups.Read]);
            var both = groups.SelectTools([ToolGroups.Read, ToolGroups.Edit]);

            var messages = new[] { new ChatMessage(ChatRole.User, "anything") };

            // Act: build the session configuration for a granting and a withholding turn, and attempt every
            // write the deny-list names, plus a set of paths that each try to leave the repository a different way
            var granting = CopilotEndpoint.BuildSessionConfig(new ChatTurnRequest(messages, both, 100, "a-model"));
            var withheld = CopilotEndpoint.BuildSessionConfig(new ChatTurnRequest(messages, [], 100, "a-model"));

            var createFile = both.OfType<AIFunction>().First(tool => tool.Name == "create_file");
            var protectedRefusals = ProtectedPaths.Names.ToDictionary(
                name => name, name => Invoke(createFile, name));
            var noneCreated = ProtectedPaths.Names.All(name => !File.Exists(Path.Combine(root, name)));

            // The same protected files named through the default-data-stream alias Windows resolves back to the
            // file's own contents, which would otherwise walk straight past a deny-list matching text. Each one
            // is given known content first, so a write that got through would be visible.
            const string sentinel = "the content the user approved";
            var replaceFile = both.OfType<AIFunction>().First(tool => tool.Name == "replace_file");
            foreach (var name in ProtectedPaths.Names)
                File.WriteAllText(Path.Combine(root, name), sentinel);

            var aliasRefusals = ProtectedPaths.Names.ToDictionary(
                name => name, name => Invoke(replaceFile, name + "::$DATA"));
            var aliasSurvivors = ProtectedPaths.Names.ToDictionary(
                name => name, name => File.ReadAllText(Path.Combine(root, name)));

            string[] escapes =
            [
                "../escape.txt",
                "sub/../../escape.txt",
                "/etc/passwd",
                @"C:\Windows\System32\drivers\etc\hosts",
                @"\\server\share\escape.txt",
                @"\\?\C:\escape.txt",
                "../" + Path.GetFileName(root) + "-sibling/escape.txt",
                "bad\0name.txt"
            ];

            var escapeRefusals = escapes.ToDictionary(
                path => path,
                path => both.OfType<AIFunction>().Select(tool => Invoke(tool, path)).ToArray());

            Assert.Multiple(
                // Granted by group selection: a group not granted contributes nothing to the set the model sees.
                () => Assert.Equal(
                    RepositoryReadTools.Names,
                    readOnly.Select(tool => tool.Name).ToArray()),
                () => Assert.Equal(
                    RepositoryReadTools.Names.Concat(RepositoryEditTools.Names).ToArray(),
                    both.Select(tool => tool.Name).ToArray()),
                () => Assert.Empty(groups.SelectTools([])),
                () => Assert.Empty(groups.SelectTools(["a-group-that-does-not-exist"])),

                // And the allowlist crossing to the provider is always explicit: a null one imposes no
                // restriction at all, which would grant by absence rather than by selection.
                () => Assert.NotNull(granting.AvailableTools),
                () => Assert.Equal(both.Select(tool => tool.Name).ToArray(), granting.AvailableTools),
                () => Assert.NotNull(withheld.AvailableTools),
                () => Assert.Empty(withheld.AvailableTools!),

                // Every protected configuration file and repository script is refused, and none was created.
                () => Assert.All(
                    protectedRefusals,
                    refusal => Assert.Contains("refused", refusal.Value, StringComparison.Ordinal)),
                () => Assert.All(
                    protectedRefusals,
                    refusal => Assert.Contains(refusal.Key, refusal.Value, StringComparison.Ordinal)),
                () => Assert.All(
                    protectedRefusals,
                    refusal => Assert.Contains("approval", refusal.Value, StringComparison.Ordinal)),
                () => Assert.True(noneCreated),

                // And naming one of them through a stream alias is refused as well, leaving the content the user
                // approved in place - the deny-list cannot be walked past by a spelling that resolves back to the
                // same file.
                () => Assert.All(
                    aliasRefusals,
                    refusal => Assert.Contains("refused", refusal.Value, StringComparison.Ordinal)),
                () => Assert.All(
                    aliasSurvivors,
                    survivor => Assert.Equal(sentinel, survivor.Value)),

                // Every way out of the repository is refused, by every granted tool, and nothing landed outside.
                () => Assert.All(
                    escapeRefusals,
                    refusal => Assert.All(
                        refusal.Value,
                        reply => Assert.Contains("refused", reply, StringComparison.Ordinal))),
                () => Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escape.txt"))),
                () => Assert.False(Directory.Exists(root + "-sibling")));
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

    /// <summary>
    ///     TOOLKIT-19 — `lint-fix` drives the repository to a clean lint or reports why it could not: succeeding
    ///     when lint exits zero, escalating when a repair needs a protected file, and failing when its budget is
    ///     exhausted.
    /// </summary>
    /// <remarks>
    ///     The repository's scripts are substituted so all three state flows are reachable in a test, because the
    ///     alternative — rebuilding and re-linting a real repository per case — would exercise PowerShell rather
    ///     than the operation.
    /// </remarks>
    [Fact]
    public async Task LintFixDrivesTheRepositoryCleanOrReportsWhyNot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "present.md"), "a line to read");

            // Arrange: lint fails once and then passes, and a worker that edits ordinary files
            var attempts = 0;
            var repaired = await RunLintFix(
                root,
                (script, _) => Task.FromResult(
                    script == "lint.ps1" && attempts++ > 0
                        ? new ScriptRun(0, string.Empty)
                        : new ScriptRun(script == "lint.ps1" ? 1 : 0, "present.md:1 MD013 line too long")),
                new ScriptedEndpoint("I wrapped the line."));

            // Arrange: lint keeps reporting the same thing, and the only repair the worker attempts is refused
            var escalated = await RunLintFix(
                root,
                (script, _) => Task.FromResult(
                    new ScriptRun(script == "lint.ps1" ? 1 : 0, "build artifacts are being linted")),
                new ToolCallingEndpoint(
                    ("create_file", new Dictionary<string, object?>
                    {
                        ["path"] = ".cspell.yaml",
                        ["content"] = "words: []"
                    })));

            // Arrange: lint keeps failing and nothing is ever refused, so the budget runs out
            var exhausted = await RunLintFix(
                root,
                (script, _) => Task.FromResult(
                    new ScriptRun(script == "lint.ps1" ? 1 : 0, "present.md:1 MD013 line too long")),
                new ScriptedEndpoint("I could not fix that."));

            // Arrange: the same, except the worker's one refusal is a path outside the repository - its own
            // mistake to correct, and nothing the user has to decide
            var outOfBounds = await RunLintFix(
                root,
                (script, _) => Task.FromResult(
                    new ScriptRun(script == "lint.ps1" ? 1 : 0, "build artifacts are being linted")),
                new ToolCallingEndpoint(
                    ("read_file", new Dictionary<string, object?>
                    {
                        ["path"] = "../something",
                        ["start"] = 1,
                        ["max"] = 10
                    })));

            Assert.Multiple(
                // Clean lint is success.
                () => Assert.Equal(OperationOutcome.Succeeded, repaired.Result.Outcome),

                // A repair that needs a protected file is escalation, naming what was refused - not failure.
                () => Assert.Equal(OperationOutcome.Escalated, escalated.Result.Outcome),
                () => Assert.Contains(
                    ".cspell.yaml", escalated.Output, StringComparison.Ordinal),
                () => Assert.NotEmpty(escalated.Result.FindingAs<LintFixReport>()!.RefusedWrites),

                // An exhausted budget is failure, and it is bounded rather than open-ended.
                () => Assert.Equal(OperationOutcome.Failed, exhausted.Result.Outcome),
                () => Assert.Equal(
                    LintFixOperation.MaxIterations,
                    exhausted.Result.FindingAs<LintFixReport>()!.Iterations),
                () => Assert.Contains(
                    "MD013", exhausted.Result.FindingAs<LintFixReport>()!.RemainingOutput, StringComparison.Ordinal),

                // A refusal that was never about a protected file is not escalation: it exhausts the budget like
                // any other repair that did not work, because telling the user a protected file needs their
                // approval when none does would be false.
                () => Assert.Equal(OperationOutcome.Failed, outOfBounds.Result.Outcome),
                () => Assert.Equal(
                    LintFixOperation.MaxIterations,
                    outOfBounds.Result.FindingAs<LintFixReport>()!.Iterations),
                () => Assert.Empty(outOfBounds.Result.FindingAs<LintFixReport>()!.RefusedWrites));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-21 — stats reports, for each action found in a repository's invocation records, its pass rate
    ///     across five cumulative time windows, with the raw counts behind every percentage, excluding
    ///     UsageError from both sides.
    /// </summary>
    [Fact]
    public async Task StatsReportsPerActionPassRatesAcrossWindows()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var now = DateTimeOffset.UtcNow;

            WriteInvocationRecords(
                root,
                // "verify-evidence": one success today, one failure 10 days ago (last 30 days and all-time
                // only), and a usage error today that must not enter either side of the rate.
                Record("verify-evidence", nameof(OperationOutcome.Succeeded), now),
                Record("verify-evidence", nameof(OperationOutcome.Failed), now - TimeSpan.FromDays(10)),
                Record("verify-evidence", nameof(OperationOutcome.UsageError), now),
                // "probe-rule-owner": nothing at all today, so "today" has no data for it, but one refusal
                // inside the last 3 days keeps every wider window non-empty.
                Record("probe-rule-owner", nameof(OperationOutcome.Refused), now - TimeSpan.FromDays(2)));

            var output = new StringWriter();
            var operation = new StatsOperation(root);
            var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),

                // verify-evidence: today is 1/1 (the usage error excluded entirely); the 10-day-old failure only
                // enters once the window reaches back that far.
                () => Assert.Contains("verify-evidence", written, StringComparison.Ordinal),
                () => Assert.Contains("today", written, StringComparison.Ordinal),
                () => Assert.Contains("100% (1/1)", written, StringComparison.Ordinal),
                () => Assert.Contains("50% (1/2)", written, StringComparison.Ordinal),

                // probe-rule-owner: today has nothing recorded for it at all - a zero denominator - so it must
                // say so rather than print a rate.
                () => Assert.Contains("probe-rule-owner", written, StringComparison.Ordinal),
                () => Assert.Contains("no data", written, StringComparison.Ordinal),

                // Cumulative: the 2-day-old refusal enters "last 3 days" onward but not "today".
                () => Assert.Contains("0% (0/1)", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     `stats` takes no arguments, so given any at all it is a usage error - the arguments were never used
    ///     rather than answered against.
    /// </summary>
    [Fact]
    public async Task StatsRejectsAnyArgument()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var operation = new StatsOperation(root);
            var result = await operation.ExecuteAsync(
                ["unexpected"], TextWriter.Null, TestContext.Current.CancellationToken);

            Assert.Equal(OperationOutcome.UsageError, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     A repository with no invocation record file at all has recorded nothing, which is a successful and
    ///     honest answer for an advisory operation, not a failure to find something.
    /// </summary>
    [Fact]
    public async Task StatsReportsNothingRecordedWhenCorpusIsMissing()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var operation = new StatsOperation(root);
            var output = new StringWriter();
            var result = await operation.ExecuteAsync([], output, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains("no invocations", output.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-23 — `route`, dispatched through the same command surface a caller has, drives a real
    ///     `Process.Router` over the production three-worker catalog and runs whichever compiled worker the
    ///     routing oracle selects, reporting the completed change as data.
    /// </summary>
    /// <remarks>
    ///     Driven through <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, IReadOnlyList{IOperation}, string, CancellationToken)" />
    ///     itself, exactly as every other action's own boundary test is, so what this proves is the registered
    ///     action reaching a real worker rather than <see cref="RouteOperation" /> in isolation - the latter is
    ///     already covered in depth by <c>RouteOperationTests</c>.
    /// </remarks>
    [Fact]
    public async Task RouteRunsTheSelectedCompiledWorker()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"this is a small, interior fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","hasSufficientEvidence":true}""",
                "I made the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"fixed the bug"}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "fix the off-by-one bug"], output, [operation], root, TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("src/Foo.cs", written, StringComparison.Ordinal),
                () => Assert.DoesNotContain("unknown action", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
