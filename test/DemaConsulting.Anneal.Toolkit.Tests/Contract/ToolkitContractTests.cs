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
///     <see cref="AnnealTool.Run(IReadOnlyList{string}, TextWriter)" /> and the assertions are on the exit
///     code and the written output. The operation set is injected where a clause is about the dispatcher
///     rather than about a shipped action, because a rule stated over categories cannot be proven by the one
///     category that happens to ship today.
/// </remarks>
public class ToolkitContractTests
{
    /// <summary>
    ///     TOOLKIT-01 — an unrecognized action exits with the caller-error code of TOOLKIT-10 and lists the
    ///     actions that exist, so a caller discovers the surface without reading the source.
    /// </summary>
    [Fact]
    public void UnknownActionListsAvailableActions()
    {
        // Arrange: a caller who has named an action this tool does not have
        var output = new StringWriter();

        // Act: the action is named first, as "dotnet anneal <action>"
        var exitCode = AnnealTool.Run(["no-such-action"], output);
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
    public void OnlyEnforcementOperationsGate()
    {
        // Arrange: the same failure, declared under each category in turn
        var categories = Enum.GetValues<OperationCategory>();

        // Act: run each one, plus a succeeding enforcement operation as the control
        var failingExitCodes = categories.ToDictionary(
            category => category,
            category => RunStub(category, OperationOutcome.Failed));
        var succeedingEnforcement = RunStub(OperationCategory.Enforcement, OperationOutcome.Succeeded);

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
    public void UsageErrorExitsAsCallerErrorWhateverTheCategory()
    {
        // Arrange and act: the same usage error under a category that gates and one that does not
        var researchMisuse = RunStub(OperationCategory.Research, OperationOutcome.UsageError);
        var enforcementMisuse = RunStub(OperationCategory.Enforcement, OperationOutcome.UsageError);

        // Act: the same two operations, having actually run and reported an answer
        var researchFailure = RunStub(OperationCategory.Research, OperationOutcome.Failed);
        var enforcementFailure = RunStub(OperationCategory.Enforcement, OperationOutcome.Failed);
        var researchRefusal = RunStub(OperationCategory.Research, OperationOutcome.Refused);
        var enforcementRefusal = RunStub(OperationCategory.Enforcement, OperationOutcome.Refused);

        // Act: a caller who scripted an option the action does not take, as the reported defect did
        var misuseOutput = new StringWriter();
        AnnealTool.Run(
            ["stub", "--rule", "some rule"],
            misuseOutput,
            [new StubOperation(OperationCategory.Research, OperationOutcome.UsageError)]);
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
    public void EvidenceLocatorsAreCheckedAgainstSource()
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
            var honestExit = AnnealTool.Run(["verify-evidence", honest], honestOutput, operations);

            var wrongOutput = new StringWriter();
            var wrongExit = AnnealTool.Run(["verify-evidence", wrong], wrongOutput, operations);
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
    public void RuleOwnerProbeNamesOneFileOrRefuses()
    {
        // Arrange: a repository, and a model scripted to reach each of the three conclusions in turn
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "owner.md"), "Each rule has exactly one owner.");

            // Act: the same question answered three ways
            var owned = RunProbe(root, "owner.md states it and nothing else does.", Answer("SingleOwner", "owner.md"));
            var several = RunProbe(root, "Two files state it.", Answer("StatedInSeveralPlaces", ""));
            var nowhere = RunProbe(root, "Nothing states it.", Answer("StatedNowhere", ""));

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
    public void RefusalIsDistinctFromFailure()
    {
        // Arrange: one operation, driven to each of the three outcomes
        var root = CreateTemporaryDirectory();
        try
        {
            // Act: an answer, a refusal, and a failure that is not a refusal
            var answered = RunProbe(root, "owner.md states it.", Answer("SingleOwner", "owner.md"));
            var refused = RunProbe(root, "Several files state it.", Answer("StatedInSeveralPlaces", ""));
            var failed = RunProbe(root, "unreachable", "unreachable", reachable: false);

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
    public void UnreachableModelFailsLoudly()
    {
        // Arrange: a repository whose model cannot be reached, and a report the deterministic check can verify
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllLines(Path.Combine(root, "subject.txt"), ["first line", "the promise this cites"]);
            var report = WriteReport(root, "report.md", "`subject.txt:2` - \"the promise this cites\"");

            // Act: the probe with no model reachable, and the deterministic operation under the same conditions
            var probe = RunProbe(root, "unused", "unused", reachable: false);

            var evidenceOutput = new StringWriter();
            var evidenceExit = AnnealTool.Run(
                ["verify-evidence", report],
                evidenceOutput,
                [new VerifyEvidenceOperation(root)]);

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
    public void UndecodableProbeResultFailsTheOperation()
    {
        // Arrange: a model that never produces valid JSON, and one that produces it only after being corrected
        var root = CreateTemporaryDirectory();
        try
        {
            var hopeless = RunProbe(
                root,
                "owner.md states it.",
                "I think owner.md owns it.",
                "Still prose, sorry.",
                "{ \"ownership\": ");

            var rescued = RunProbe(
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
    private static ProbeRun RunProbe(string root, string reasoningReply, params string[] probeReplies) =>
        RunProbe(root, reasoningReply, probeReplies, reachable: true);

    private static ProbeRun RunProbe(string root, string reasoningReply, string probeReply, bool reachable) =>
        RunProbe(root, reasoningReply, [probeReply], reachable);

    private static ProbeRun RunProbe(string root, string reasoningReply, string[] probeReplies, bool reachable)
    {
        var reasoning = new ScriptedEndpoint(reasoningReply);
        var probing = new ScriptedEndpoint(probeReplies);
        var openEnded = new ScriptedEndpoint("the open-ended tier is not consulted by this operation");

        var roles = reachable
            ? new ModelRoles(probing, reasoning, openEnded)
            : new ModelRoles(new UnreachableEndpoint());

        var output = new StringWriter();
        var exitCode = AnnealTool.Run(
            ["probe-rule-owner", "each rule has exactly one owner"],
            output,
            [new ProbeRuleOwnerOperation(root, () => roles)]);

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
            Requests.Add(request);
            return Task.FromResult(new ChatTurnResult(_replies.Count > 0 ? _replies.Dequeue() : "(script exhausted)"));
        }
    }

    private sealed class UnreachableEndpoint : IChatEndpoint
    {
        public Task<ChatTurnResult> CompleteAsync(ChatTurnRequest request, CancellationToken cancellationToken) =>
            throw new ModelUnavailableException("the Copilot account is not signed in on this machine");
    }

    private static int RunStub(OperationCategory category, OperationOutcome outcome) =>
        AnnealTool.Run(["stub"], new StringWriter(), [new StubOperation(category, outcome)]);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-toolkit-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
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
    ///     including the three no shipped operation currently declares. It states the argument form it wanted
    ///     when it reports a usage error, as a real operation does, so the dispatcher's own message can be
    ///     read alongside it.
    /// </remarks>
    private sealed class StubOperation(OperationCategory category, OperationOutcome outcome) : IOperation
    {
        public string Name => "stub";

        public OperationCategory Category => category;

        public string Summary => "Reports a fixed outcome under a fixed category";

        public OperationOutcome Execute(IReadOnlyList<string> arguments, TextWriter output)
        {
            if (outcome == OperationOutcome.UsageError)
                output.WriteLine("stub: expected one argument, given positionally.");

            return outcome;
        }
    }
}
