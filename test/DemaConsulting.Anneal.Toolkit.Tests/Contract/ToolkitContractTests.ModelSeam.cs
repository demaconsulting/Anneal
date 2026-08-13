using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Providers;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the model seam shared by every model-backed operation - refusal, unreachable and undecodable models, role resolution, transcription, and tool grants.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{

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
    ///     TOOLKIT-53 — the Contract Change worker's final verifier question instructs the verifier to consider
    ///     disproportionate deletions or rewrites inside an otherwise in-scope, already-existing architecture
    ///     document as a Documentation concern, and a targeted clause addition inside an existing document passes
    ///     without raising that concern.
    /// </summary>
    [Fact]
    public async Task VerifierQuestionIncludesDisproportionateDeletionCheck()
    {
        // Arrange: a general-worker route with a targeted clause addition that passes cleanly; the endpoint
        // records every request so the verifier question text is auditable without a network call.
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "build.ps1"), "");

            var endpoint = new QueuedEndpoint(
                // Route oracle selects general at Medium effort
                """{"kind":"SelectWorker","why":"adds a contract clause","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Medium","hasSufficientEvidence":true}""",
                """{"scope":"Docs","conclusion":"Proceed"}""",
                // DocumentAuthor: free-form turn, then structured decision
                "I added the new clause.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"added clause"}""",
                // Developer: free-form turn, then structured decision
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                // Verifier: passes — targeted clause addition does not raise a concern
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")),
                runGit: ContractTouchingDiff(".anneal/architecture/toolkit.md", "src/Foo.cs"));

            // Act: targeted clause addition through the public boundary
            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["add a new contract clause for the widget"], output, TestContext.Current.CancellationToken);

            // Assert: run succeeds, and the verifier question (index 6: route + preflight + 2 doc + 2 dev turns, then verifier)
            // explicitly instructs the verifier to treat disproportionate deletions as a Documentation concern.
            var verifierText = string.Join("\n", endpoint.Requests[6].Messages.Select(m => m.Text));
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains("disproportionate", verifierText, StringComparison.Ordinal),
                () => Assert.Contains("Documentation", verifierText, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-53 — when the verifier reports a Documentation concern for a disproportionate deletion inside
    ///     an otherwise in-scope architecture document, the general worker enters the documentation-repair path
    ///     rather than reporting a clean Succeeded outcome.
    /// </summary>
    [Fact]
    public async Task VerifierCatchesDisproportionateDeletionAsDocumentationConcern()
    {
        // Arrange: the verifier detects a whole-file overwrite that deleted a Decisions section the declared
        // task never asked to revise, and reports it as a Documentation concern. The worker must enter the
        // documentation-repair path; after the repair the verifier passes and the run completes cleanly.
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "build.ps1"), "");

            var endpoint = new QueuedEndpoint(
                // Route oracle selects general at Medium effort
                """{"kind":"SelectWorker","why":"adds a contract clause","workerKey":"general","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Medium","hasSufficientEvidence":true}""",
                """{"scope":"Docs","conclusion":"Proceed"}""",
                // DocumentAuthor: free-form turn, then structured decision
                "I updated the contract document (whole-file overwrite).",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"added clause — used replace_file"}""",
                // Developer: free-form turn, then structured decision
                "I implemented the change.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"implemented"}""",
                // Verifier: finds disproportionate deletion — reports Documentation concern
                """{"verdict":"RepairRequired","concerns":[{"owner":"Documentation","fixText":"the whole-file overwrite deleted the pre-existing Decisions section; restore the unrelated content verbatim"}],"advisoryNotes":[],"evidenceSufficient":true}""",
                // Documentation repair: DocumentAuthor restores the deleted content
                "I restored the Decisions section that was incorrectly deleted.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit.md"],"summary":"restored unrelated Decisions content"}""",
                // Developer re-sync after repair
                "I re-synced the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Foo.cs"],"summary":"re-synced"}""",
                // Verifier: now passes
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "43/43")),
                runGit: ContractTouchingDiff(".anneal/architecture/toolkit.md", "src/Foo.cs"));

            // Act: run a contract change where the DocumentAuthor used a whole-file overwrite
            var output = new StringWriter();
            var result = await operation.ExecuteAsync(
                ["add a new contract clause for the widget"], output, TestContext.Current.CancellationToken);

            // Assert: the worker caught the deletion via the verifier and completed only after the repair —
            // 12 model calls total: route + preflight + 2 doc + 2 dev + verifier + 2 doc-repair + 2 dev-resync + verifier.
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(12, endpoint.Calls));
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

    private static RunGitCommand ContractTouchingDiff(params string[] files) =>
        (_, _) => Task.FromResult(new ScriptRun(0, BuildDiff(files)));

    private static string BuildDiff(IReadOnlyList<string> files) =>
        string.Join(
            "\n",
            files.Select(file =>
            {
                var body = file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && file.Contains(".anneal")
                    ? "@@ -1,5 +1,5 @@\n ## Contract\n \n ### Provides\n \n-- old\n++ new"
                    : "@@ -1 +1 @@\n-old\n+new";
                return $"diff --git a/{file} b/{file}\n--- a/{file}\n+++ b/{file}\n{body}";
            }));
}
