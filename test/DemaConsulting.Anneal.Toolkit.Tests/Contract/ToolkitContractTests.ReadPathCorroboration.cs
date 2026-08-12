using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the read-path corroboration mechanism: TOOLKIT-50, TOOLKIT-51, TOOLKIT-52.
/// </summary>
/// <remarks>
///     Split from <see cref="ToolkitContractTests" /> by topic. Shared helpers and private nested types
///     live in <c>ToolkitContractTests.cs</c>.
/// </remarks>
public partial class ToolkitContractTests
{
    /// <summary>
    ///     TOOLKIT-50 - every file path a model's read-tool call succeeds against is recorded by the session,
    ///     normalized to forward-slash separators with any leading ./ stripped, compared case-insensitively,
    ///     and deduplicated, so casing differences and separator differences do not create phantom gaps.
    /// </summary>
    /// <remarks>
    ///     The endpoint reads the same file twice with different casing and once with a leading ./, then
    ///     attempts a read outside the repository root. The assertions confirm all three spellings collapse to
    ///     one path entry, and the refused outside-root call contributes nothing.
    /// </remarks>
    [Fact]
    public async Task SuccessfulReadPathsAreRecordedNormalizedAndDeduplicated()
    {
        // Arrange: a repository with one file, and an endpoint that reads it with a leading ./,
        // then reads it again with a different capitalization, then attempts a read outside the root.
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "readme.md"), "hello");

            var endpoint = new ToolCallingEndpoint(
                ("read_file", new Dictionary<string, object?> { ["path"] = "./readme.md", ["start"] = 0, ["max"] = 0 }),
                ("read_file", new Dictionary<string, object?> { ["path"] = "README.MD", ["start"] = 0, ["max"] = 0 }),
                ("read_file", new Dictionary<string, object?> { ["path"] = "../escape.txt", ["start"] = 0, ["max"] = 0 }));

            var session = new ModelSession(
                new ModelRoles(root, _ => endpoint),
                "a charter",
                new ToolGroups(root).SelectTools([ToolGroups.Read]));

            // Act: one turn in which the provider makes all three read calls
            await session.RunAsync("investigate the file", ModelRole.Heavy, TestContext.Current.CancellationToken);

            var paths = session.SuccessfulReadPaths;

            // Assert: deduplicated to one entry, normalized (no leading ./), and the refused
            // outside-root read did not contribute a path.
            Assert.Multiple(
                () => Assert.Single(paths),
                () => Assert.Contains("readme.md", paths, StringComparer.OrdinalIgnoreCase),
                () => Assert.DoesNotContain(paths, p => p.StartsWith("./", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-51 - when a Research session's finding carries evidence references, only those whose paths
    ///     appear in the session's successful-read set are retained; a reference the model cited but never
    ///     actually read is dropped.
    /// </summary>
    /// <remarks>
    ///     The endpoint makes a real read-tool call for one file; the probe answers with both that file and a
    ///     hallucinated path as evidence. Only the file that was actually read survives corroboration, so the
    ///     caller receives no false grounding.
    /// </remarks>
    [Fact]
    public async Task ResearchCorroboratesEvidenceRefsAgainstSuccessfulReads()
    {
        // Arrange: a repository with one readable file; a ToolCallingEndpoint reads it during the run turn
        // then a QueuedEndpoint answers the probe with both the real file and an invented one as evidence.
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "owner.md"), "Each rule has exactly one owner.");

            var readEndpoint = new ToolCallingEndpoint(
                ("read_file", new Dictionary<string, object?> { ["path"] = "owner.md", ["start"] = 0, ["max"] = 0 }));

            var probeEndpoint = new QueuedEndpoint(
                """
                {
                    "question": "who owns the rule?",
                    "answer": "owner.md states it",
                    "evidenceRefs": ["owner.md", "hallucinated.md"],
                    "implications": "nothing else needs to change",
                    "sufficientForNextDecision": true
                }
                """);

            // endpointFor supplies the tool-calling endpoint for run turns (Medium role, the Research default)
            // and the queued endpoint for the schema-last probe turn (Light role, the Probe default).
            var research = new Research(
                root,
                "find who owns this rule",
                endpointFor: role => role == ModelRole.Medium
                    ? (IChatEndpoint)readEndpoint
                    : probeEndpoint);

            // Act
            var result = await research.InvestigateAsync("who owns the rule?", TestContext.Current.CancellationToken);

            // Assert: the corroborated finding retains only the ref for the file that was actually read.
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Single(result.Finding?.EvidenceRefs ?? []),
                () => Assert.Contains("owner.md", result.Finding?.EvidenceRefs ?? [], StringComparer.OrdinalIgnoreCase),
                () => Assert.DoesNotContain(
                    "hallucinated.md",
                    result.Finding?.EvidenceRefs ?? [],
                    StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-52 - when Developer or DocumentAuthor completes a pass, the self-reported list of changed
    ///     files is filtered against the actual working-tree diff produced by git; files the model claimed to
    ///     have changed but that the diff does not show are dropped. When git is unavailable the self-reported
    ///     list is used unchanged.
    /// </summary>
    /// <remarks>
    ///     The runGit injection point makes this exercisable without a real repository. The diff is manufactured
    ///     to contain only one of the two files the model self-reports, confirming the second is dropped. A
    ///     second scenario with a non-zero git exit confirms the fallback path leaves the full self-reported list
    ///     unchanged, so the corroboration is a strengthening check and not a hard gate.
    /// </remarks>
    [Fact]
    public async Task DeveloperAndDocumentAuthorCorroborateChangedFilesAgainstGitDiff()
    {
        // Arrange: an endpoint that immediately reports completion with two self-reported files; a fake git
        // that produces a diff containing only the first of those two files.
        var root = CreateTemporaryDirectory();
        try
        {
            const string realFile = "real.cs";
            const string hallucinated = "hallucinated.cs";
            const string diffPatch =
                "diff --git a/real.cs b/real.cs\n--- a/real.cs\n+++ b/real.cs\n@@ -1 +1 @@\n-old\n+new\n";

            RunGitCommand fakeGit = (arguments, _) => Task.FromResult(
                new ScriptRun(0, arguments.Count > 0 && arguments[0] == "diff" ? diffPatch : string.Empty));

            RunGitCommand unavailableGit = (_, _) =>
                Task.FromResult(new ScriptRun(128, "fatal: not a git repository"));

            var completedReply =
                $$"""{"kind": "Completed", "why": "", "suggestedWorker": "", "filesChanged": ["{{realFile}}", "{{hallucinated}}"], "summary": "done"}""";

            // A shared endpoint so both the RunAsync turn (Heavy) and the ProbeAsync turn (Light)
            // drain the same queue in order: first the prose reply, then the structured completion.
            var sharedEndpoint = new QueuedEndpoint("I made the changes.", completedReply);

            // Act: Developer with a diff containing only realFile - hallucinated is dropped
            var corroborated = await new Developer(
                root, "a charter",
                endpointFor: _ => sharedEndpoint,
                runGit: fakeGit)
                .DevelopAsync("change a method", TestContext.Current.CancellationToken);

            var sharedEndpoint2 = new QueuedEndpoint("I made the changes.", completedReply);

            // Act: Developer with git unavailable - self-reported list is used unchanged
            var uncorroborated = await new Developer(
                root, "a charter",
                endpointFor: _ => sharedEndpoint2,
                runGit: unavailableGit)
                .DevelopAsync("change a method", TestContext.Current.CancellationToken);

            var corroboratedFiles =
                (corroborated.Finding as DevelopmentResult.Completed)?.Summary.FilesChanged ?? [];
            var uncorroboratedFiles =
                (uncorroborated.Finding as DevelopmentResult.Completed)?.Summary.FilesChanged ?? [];

            // Assert: with git available only the diffed file survives; without git the full self-report is kept.
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, corroborated.Outcome),
                () => Assert.Single(corroboratedFiles),
                () => Assert.Contains(realFile, corroboratedFiles, StringComparer.OrdinalIgnoreCase),
                () => Assert.DoesNotContain(hallucinated, corroboratedFiles, StringComparer.OrdinalIgnoreCase),
                () => Assert.Equal(OperationOutcome.Succeeded, uncorroborated.Outcome),
                () => Assert.Equal(2, uncorroboratedFiles.Count));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
