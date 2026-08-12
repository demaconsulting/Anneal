using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the capability-complete Large general worker.
/// </summary>
public class GeneralWorkerContractTests
{
    [Fact]
    public async Task GeneralWorkerCanAuthorContractArchitectureAndCodeInOneRun()
    {
        var root = CreateTemporaryDirectory("gw-capability");
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"update docs and code","planSteps":["update overview","update contract doc","implement code"]}""",
                "I updated the docs.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/overview.md",".anneal/architecture/toolkit/general-worker.md"],"summary":"updated the architecture and contract docs"}""",
                "I implemented the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Worker.cs","test/WorkerTests.cs"],"summary":"implemented the worker"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var buildCalls = 0;
            var triggeredContractCalls = 0;
            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) =>
                {
                    buildCalls++;
                    return Task.FromResult(new ScriptRun(0, "all good"));
                },
                contractCheckRunScript: (_, _) =>
                {
                    triggeredContractCalls++;
                    return Task.FromResult(new ScriptRun(0, "contracts good"));
                },
                runGit: SequencedGitStub(
                    "diff --git a/.anneal/architecture/overview.md b/.anneal/architecture/overview.md\n--- a/.anneal/architecture/overview.md\n+++ b/.anneal/architecture/overview.md\n@@ -1 +1 @@\n-old\n+new\n" +
                    "diff --git a/.anneal/architecture/toolkit/general-worker.md b/.anneal/architecture/toolkit/general-worker.md\n--- a/.anneal/architecture/toolkit/general-worker.md\n+++ b/.anneal/architecture/toolkit/general-worker.md\n@@ -3,4 +3,4 @@\n ## Contract\n-- **TOOLKIT-58** — old\n+- **TOOLKIT-58** — new\n" +
                    "diff --git a/src/Worker.cs b/src/Worker.cs\n--- a/src/Worker.cs\n+++ b/src/Worker.cs\n@@ -1 +1 @@\n-internal sealed class OldWorker {}\n+internal sealed class NewWorker {}"));

            var result = await worker.RunAsync(
                MakeBrief("Split the process worker and update overview.md plus the general-worker contract document."),
                TestContext.Current.CancellationToken);

            var completed = Assert.IsType<WorkerRunResult.Completed>(result.Finding);
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains(".anneal/architecture/overview.md", completed.Summary.FilesChanged),
                () => Assert.Contains(".anneal/architecture/toolkit/general-worker.md", completed.Summary.FilesChanged),
                () => Assert.Contains("src/Worker.cs", completed.Summary.FilesChanged),
                () => Assert.Equal(1, buildCalls),
                () => Assert.Equal(1, triggeredContractCalls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerPreflightRunsPlannerAndDocumentAuthorBeforeDeveloperWhenFramingImpliesStructuralShape()
    {
        var root = CreateTemporaryDirectory("gw-preflight");
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"Plan","why":"","planSummary":"split the boundary","planSteps":["update overview","update contract doc","implement code"]}""",
                "I updated the docs.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/overview.md"],"summary":"updated the docs"}""",
                "I implemented the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Worker.cs"],"summary":"implemented"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: SequencedGitStub(
                    "diff --git a/src/Worker.cs b/src/Worker.cs\n--- a/src/Worker.cs\n+++ b/src/Worker.cs\n@@ -1 +1 @@\n-old\n+new"));

            await worker.RunAsync(
                MakeBrief("This structural change splits the system boundary and updates overview.md."),
                TestContext.Current.CancellationToken);

            var firstRequest = string.Join("\n", endpoint.Requests[0].Messages.Select(message => message.Text));
            var secondRequest = string.Join("\n", endpoint.Requests[1].Messages.Select(message => message.Text));
            var thirdRequest = string.Join("\n", endpoint.Requests[3].Messages.Select(message => message.Text));

            Assert.Multiple(
                () => Assert.Contains("needs an explicit plan", firstRequest, StringComparison.OrdinalIgnoreCase),
                () => Assert.Contains("Author any contract-clause or architecture-document changes", secondRequest, StringComparison.Ordinal),
                () => Assert.Contains("Implement the change in code and tests", thirdRequest, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerPostflightFiresOnlyTriggeredChecks()
    {
        var root = CreateTemporaryDirectory("gw-triggered");
        try
        {
            CreateArchDoc(
                root,
                "toolkit/public-api.md",
                """
                ---
                covers:
                  - src/PublicApi.cs
                ---
                # Public API
                ## Contract
                ### Provides
                - **PUBLIC-01** — exposes a public API.
                  *Verified by:* `SomeBoundaryTest`
                """);

            var endpoint = new QueuedEndpoint(
                "I updated the docs and code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":[".anneal/architecture/toolkit/general-worker.md","src/PublicApi.cs"],"summary":"implemented the change"}""",
                """{"verdict":"Agree","reason":"","hasSufficientEvidence":true}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var buildCalls = 0;
            var triggeredContractCalls = 0;
            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) =>
                {
                    buildCalls++;
                    return Task.FromResult(new ScriptRun(0, "all good"));
                },
                contractCheckRunScript: (_, _) =>
                {
                    triggeredContractCalls++;
                    return Task.FromResult(new ScriptRun(0, "contracts good"));
                },
                runGit: SequencedGitStub(
                    "diff --git a/.anneal/architecture/toolkit/general-worker.md b/.anneal/architecture/toolkit/general-worker.md\n--- a/.anneal/architecture/toolkit/general-worker.md\n+++ b/.anneal/architecture/toolkit/general-worker.md\n@@ -3,4 +3,4 @@\n ## Contract\n-- **TOOLKIT-58** — old\n+- **TOOLKIT-58** — new\n" +
                    "diff --git a/src/PublicApi.cs b/src/PublicApi.cs\n--- a/src/PublicApi.cs\n+++ b/src/PublicApi.cs\n@@ -1 +1 @@\n-public string OldName() => \"old\";\n+public string NewName() => \"new\";\n",
                    "diff --git a/.anneal/architecture/toolkit/general-worker.md b/.anneal/architecture/toolkit/general-worker.md\n--- a/.anneal/architecture/toolkit/general-worker.md\n+++ b/.anneal/architecture/toolkit/general-worker.md\n@@ -3,4 +3,4 @@\n ## Contract\n-- **TOOLKIT-58** — old\n+- **TOOLKIT-58** — new\n" +
                    "diff --git a/src/PublicApi.cs b/src/PublicApi.cs\n--- a/src/PublicApi.cs\n+++ b/src/PublicApi.cs\n@@ -1 +1 @@\n-public string OldName() => \"old\";\n+public string NewName() => \"new\";\n"));

            await worker.RunAsync(
                MakeBrief("Implement the API rename.", tenets: ["Never expose an unstable public API by accident"]),
                TestContext.Current.CancellationToken);

            var verifierText = string.Join("\n", endpoint.Requests[^1].Messages.Select(message => message.Text));
            Assert.Multiple(
                () => Assert.Equal(1, buildCalls),
                () => Assert.Equal(1, triggeredContractCalls),
                () => Assert.Contains("Also judge the diff against these repository tenets", verifierText, StringComparison.Ordinal),
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerPostflightSkipsChecksForUntouchedSurfaces()
    {
        var root = CreateTemporaryDirectory("gw-skip");
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the helper.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Internal.cs"],"summary":"updated the helper"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var triggeredContractCalls = 0;
            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                contractCheckRunScript: (_, _) =>
                {
                    triggeredContractCalls++;
                    return Task.FromResult(new ScriptRun(0, "contracts good"));
                },
                runGit: SequencedGitStub(
                    "diff --git a/src/Internal.cs b/src/Internal.cs\n--- a/src/Internal.cs\n+++ b/src/Internal.cs\n@@ -1 +1 @@\n-private int value;\n+private int value = 1;\n"));

            await worker.RunAsync(
                MakeBrief("Tidy the helper implementation.", tenets: ["Never expose an unstable public API by accident"]),
                TestContext.Current.CancellationToken);

            var verifierText = string.Join("\n", endpoint.Requests[^1].Messages.Select(message => message.Text));
            Assert.Multiple(
                () => Assert.Equal(0, triggeredContractCalls),
                () => Assert.DoesNotContain("Also judge the diff against these repository tenets", verifierText, StringComparison.Ordinal),
                () => Assert.Equal(3, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerAmbiguousDiffAnalysisFailsClosed()
    {
        var root = CreateTemporaryDirectory("gw-ambiguous");
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the helper.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Internal.cs"],"summary":"updated the helper"}""");

            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: SequencedGitStub(
                    "@@ -1 +1 @@\n-old\n+new\n"));

            var result = await worker.RunAsync(
                MakeBrief("Tidy the helper implementation."),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Escalated, result.Outcome),
                () => Assert.Contains("could not be classified honestly", result.Notes.Select(note => note.Text).Single(), StringComparison.Ordinal),
                () => Assert.Equal(2, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerProtectedPathBackstopStillEscalatesDangerousEdit()
    {
        var root = CreateTemporaryDirectory("gw-protected");
        try
        {
            var endpoint = new QueuedEndpoint(
                "I updated the policy.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":[".anneal/governance/tenets.md"],"summary":"updated the policy"}""");

            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: SequencedGitStub(
                    "diff --git a/.anneal/governance/tenets.md b/.anneal/governance/tenets.md\n--- a/.anneal/governance/tenets.md\n+++ b/.anneal/governance/tenets.md\n@@ -1 +1 @@\n-old\n+new\n"));

            var result = await worker.RunAsync(
                MakeBrief("Update the governance tenet wording."),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Escalated, result.Outcome),
                () => Assert.Contains(".anneal/governance/tenets.md", result.Notes.Select(note => note.Text).Single(), StringComparison.Ordinal),
                () => Assert.Equal(2, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerAbsorbedArchGateCorrectsWordingOnlyMismatch()
    {
        var root = CreateTemporaryDirectory("gw-wording");
        try
        {
            CreateArchDoc(
                root,
                "toolkit/widget.md",
                """
                ---
                covers:
                  - src/Widget.cs
                ---
                # Widget
                Refers to OldWidget in an introductory sentence.
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeBoundaryTest`
                """);

            var endpoint = new QueuedEndpoint(
                "I updated Widget.cs.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Widget.cs"],"summary":"renamed the widget"}""",
                """{"verdict":"WordingOnly","reason":"'OldWidget' should now be 'NewWidget' in the introductory sentence","hasSufficientEvidence":true}""",
                "I corrected the wording.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit/widget.md"],"summary":"corrected stale wording"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: SequencedGitStub(
                    "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n",
                    "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n",
                    "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n@@ -1,4 +1,4 @@\n # Widget\n-Refers to OldWidget in an introductory sentence.\n+Refers to NewWidget in an introductory sentence.\n ## Contract\n ### Provides\n",
                    "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n@@ -1,4 +1,4 @@\n # Widget\n-Refers to OldWidget in an introductory sentence.\n+Refers to NewWidget in an introductory sentence.\n ## Contract\n ### Provides\n",
                    "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n"));

            var result = await worker.RunAsync(
                MakeBrief("Rename the widget type."),
                TestContext.Current.CancellationToken);

            var completed = Assert.IsType<WorkerRunResult.Completed>(result.Finding);
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains(".anneal/architecture/toolkit/widget.md", completed.Summary.FilesChanged),
                () => Assert.Contains("corrected stale wording", string.Join("\n", result.Notes.Select(note => note.Text)), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerAbsorbedArchGateRevertsCorrectionThatTouchesContract()
    {
        var root = CreateTemporaryDirectory("gw-revert");
        try
        {
            CreateArchDoc(
                root,
                "toolkit/widget.md",
                """
                ---
                covers:
                  - src/Widget.cs
                ---
                # Widget
                Refers to OldWidget in an introductory sentence.
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeBoundaryTest`
                """);

            var endpoint = new QueuedEndpoint(
                "I updated Widget.cs.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Widget.cs"],"summary":"renamed the widget"}""",
                """{"verdict":"WordingOnly","reason":"'OldWidget' should now be 'NewWidget' in the introductory sentence","hasSufficientEvidence":true}""",
                "I corrected the wording.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit/widget.md"],"summary":"corrected stale wording"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            var checkoutCalled = false;
            var correctionDiffCalls = 0;
            RunGitCommand runGit = (args, _) =>
            {
                var joined = string.Join(" ", args);
                if (joined.Contains("checkout", StringComparison.Ordinal))
                {
                    checkoutCalled = true;
                    return Task.FromResult(new ScriptRun(0, string.Empty));
                }

                if (!joined.Contains("diff", StringComparison.Ordinal))
                    return Task.FromResult(new ScriptRun(0, string.Empty));

                var diff = correctionDiffCalls++ switch
                {
                    0 => "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n",
                    1 => "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n",
                    2 => "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n@@ -1,4 +1,4 @@\n # Widget\n-Refers to OldWidget in an introductory sentence.\n+Refers to NewWidget in an introductory sentence.\n ## Contract\n ### Provides\n",
                    3 => "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n@@ -1,4 +1,4 @@\n # Widget\n ## Contract\n-- **WIDGET-1** — does something.\n+- **WIDGET-1** — does a new thing.\n",
                    _ => "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n"
                };

                return Task.FromResult(new ScriptRun(0, diff));
            };

            var worker = new GeneralWorker(
                root,
                Effort.Large,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var result = await worker.RunAsync(
                MakeBrief("Rename the widget type."),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.True(checkoutCalled),
                () => Assert.Contains(
                    "touched the ## Contract section and was reverted",
                    string.Join("\n", result.Notes.Select(note => note.Text)),
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkerBrief MakeBrief(
        string workItem,
        IReadOnlyList<string>? tenets = null,
        IReadOnlyList<string>? changedFileHints = null) =>
        new("parent-1", workItem, "general-large", [], [], "the route selected the capability-complete large worker", [], tenets ?? [], changedFileHints ?? []);

    private static string CreateTemporaryDirectory(string stem)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{stem}-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), string.Empty);
        return root;
    }

    private static void CreateArchDoc(string root, string relativePath, string markdown)
    {
        var fullPath = Path.Combine(root, ".anneal", "architecture", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, markdown);
    }

    private static RunGitCommand SequencedGitStub(params string[] diffPatches)
    {
        var queue = new Queue<string>(diffPatches);
        var last = diffPatches.LastOrDefault() ?? string.Empty;

        return (args, _) =>
        {
            var joined = string.Join(" ", args);
            if (!joined.Contains("diff", StringComparison.Ordinal))
                return Task.FromResult(new ScriptRun(0, string.Empty));

            var diff = queue.Count > 0 ? queue.Dequeue() : last;
            return Task.FromResult(new ScriptRun(0, diff));
        };
    }
}
