using System.Diagnostics;
using System.Runtime.InteropServices;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Providers;
using DemaConsulting.Anneal.Toolkit.Operations;
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
    ///     actions that exist, so a caller discovers the surface without reading the source.
    /// </summary>
    [Fact]
    public async Task UnknownActionListsAvailableActions()
    {
        // Arrange: a caller who has named an action this tool does not have
        var output = new StringWriter();

        // Act: the action is named first, as "dotnet anneal <action>"
        var exitCode = await AnnealTool.RunAsync(["no-such-action"], output, TestContext.Current.CancellationToken);
        var written = output.ToString();

        // Assert: the caller-error code, and every shipped action is discoverable from the output alone
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitUsageError, exitCode),
            () => Assert.Contains("unknown action 'no-such-action'", written, StringComparison.Ordinal),
            () => Assert.NotEmpty(AnnealTool.DefaultOperations),
            () => Assert.All(
                AnnealTool.DefaultOperations,
                operation => Assert.Contains(operation.Name, written, StringComparison.Ordinal)));
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
                        .BuildSessionConfig(owned.Reasoning.Requests[0], "a-model")
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

            // Assert: the failure names the cause and claims nothing, and the deterministic check is unaffected
            Assert.Multiple(
                () => Assert.Contains("no judgement was obtained", probe.Output, StringComparison.Ordinal),
                () => Assert.Contains("the Copilot account is not signed in", probe.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("  owner: ", probe.Output, StringComparison.Ordinal),
                () => Assert.DoesNotContain("evidence: ", probe.Output, StringComparison.Ordinal),
                () => Assert.Equal(AnnealTool.ExitSuccess, evidenceExit),
                () => Assert.Contains("1 present, 0 absent.", evidenceOutput.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-I1 — a model is granted read-only repository tools, and the granted set is always an explicit
    ///     allowlist rather than an absent one.
    /// </summary>
    [Fact]
    public void ModelToolGrantsAreReadOnlyAndExplicit()
    {
        // Arrange: the granted tools, and the two turn shapes an operation produces - one with tools, one without
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "present.md"), "a line to read");
            var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray();

            var tools = RepositoryReadTools.CreateAll(root);
            var messages = new[] { new ChatMessage(ChatRole.User, "anything") };

            // Act: build the session configuration for each shape, and exercise every granted tool
            var granting = CopilotEndpoint.BuildSessionConfig(new ChatTurnRequest(messages, tools, 100), "a-model");
            var withheld = CopilotEndpoint.BuildSessionConfig(new ChatTurnRequest(messages, [], 100), "a-model");

            var results = tools.OfType<AIFunction>().ToDictionary(
                tool => tool.Name,
                tool => Invoke(tool));

            var after = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).Order().ToArray();

            // Assert: the allowlist is never absent, holds exactly the read-only tools, and nothing was mutated
            Assert.Multiple(
                () => Assert.NotNull(granting.AvailableTools),
                () => Assert.Equal(RepositoryReadTools.Names, granting.AvailableTools),

                // The dangerous default: a turn granting no tools must still carry an EMPTY allowlist, because a
                // null one imposes no restriction and exposes the provider's built-in mutating tools.
                () => Assert.NotNull(withheld.AvailableTools),
                () => Assert.Empty(withheld.AvailableTools!),

                // A granted tool must be marked as an intentional override or the session rejects the collision.
                () => Assert.All(
                    granting.Tools!,
                    tool => Assert.True(tool.AdditionalProperties["is_override"] is true)),

                // Nothing granted can execute a command or write a file: the surface is these three readers.
                () => Assert.Equal(["list_files", "read_file", "search_files"], results.Keys.Order()),
                () => Assert.Equal(before, after),
                () => Assert.Contains("a line to read", results["read_file"], StringComparison.Ordinal),

                // And a read is confined to the working tree, whatever the model asks for.
                () => Assert.Contains(
                    "refused",
                    Invoke(tools.OfType<AIFunction>().First(tool => tool.Name == "read_file"), "../escape.txt"),
                    StringComparison.Ordinal));
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
                () => Scripted("owner.md states it.", Answer("SingleOwner", "owner.md")));
            var answered = await answering.ExecuteAsync(
                ["each rule has exactly one owner"], TextWriter.Null, TestContext.Current.CancellationToken);

            // Act: the same operation refusing - the outcome is a peer of the finding, not folded into it
            IOperation refusing = new ProbeRuleOwnerOperation(
                root,
                () => Scripted("Two files state it.", Answer("StatedInSeveralPlaces", "")));
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
            var roles = new ModelRoles(endpoint);
            using var cancellation = new CancellationTokenSource();

            // Act: run the probe through the command surface under the caller's signal
            var run = Task.Run(
                () => AnnealTool.RunAsync(
                    ["probe-rule-owner", "each rule has exactly one owner"],
                    TextWriter.Null,
                    [new ProbeRuleOwnerOperation(root, () => roles)],
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

    /// <returns>Endpoints serving every role from one script, for a probe invoked without the dispatcher.</returns>
    private static ModelRoles Scripted(string reasoningReply, string probeReply) =>
        new(new ScriptedEndpoint(probeReply), new ScriptedEndpoint(reasoningReply), new ScriptedEndpoint("unused"));

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
            ["extension"] = string.Empty
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

    private static Task<ProbeRun> RunProbe(string root, string reasoningReply, string probeReply, bool reachable) =>
        RunProbe(root, reasoningReply, [probeReply], reachable);

    private static async Task<ProbeRun> RunProbe(
        string root, string reasoningReply, string[] probeReplies, bool reachable)
    {
        var reasoning = new ScriptedEndpoint(reasoningReply);
        var probing = new ScriptedEndpoint(probeReplies);
        var openEnded = new ScriptedEndpoint("the open-ended tier is not consulted by this operation");

        var roles = reachable
            ? new ModelRoles(probing, reasoning, openEnded)
            : new ModelRoles(new UnreachableEndpoint());

        var output = new StringWriter();
        var exitCode = await AnnealTool.RunAsync(
            ["probe-rule-owner", "each rule has exactly one owner"],
            output,
            [new ProbeRuleOwnerOperation(root, () => roles)],
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
        private readonly Queue<string> _replies = new(replies);

        public List<ChatTurnRequest> Requests { get; } = [];

        public Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken)
        {
            // A real endpoint checks before it spends anything, and so does this one: a turn sent under an
            // already-cancelled signal is a turn that should never have left.
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(request);
            return Task.FromResult(new ChatTurnResult(_replies.Count > 0 ? _replies.Dequeue() : "(script exhausted)"));
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

    private sealed class UnreachableEndpoint : IChatEndpoint
    {
        public Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken) =>
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
    private static Process StartTool(string workingDirectory, params string[] arguments)
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

        return Process.Start(start) ?? throw new InvalidOperationException("the tool did not start");
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
    private static void Interrupt(Process process)
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

        public string Summary => "Reports a fixed outcome under a fixed category";

        public string Usage => usage;

        public Task<OperationResult> ExecuteAsync(
            IReadOnlyList<string> arguments, TextWriter output, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationResult(outcome));
    }
}
