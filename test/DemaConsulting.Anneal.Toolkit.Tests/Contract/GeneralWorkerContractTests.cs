using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the capability-complete Effort-parameterized general worker.
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
                """{"scope":"Docs","conclusion":"Proceed"}""",
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
                """{"scope":"Docs","conclusion":"Proceed"}""",
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

            var firstRequest = string.Join("\n", endpoint.Requests[1].Messages.Select(message => message.Text));
            var secondRequest = string.Join("\n", endpoint.Requests[2].Messages.Select(message => message.Text));
            var thirdRequest = string.Join("\n", endpoint.Requests[4].Messages.Select(message => message.Text));

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
    public async Task GeneralWorkerContractClausePreflightRunsPlannerForThreeOrMoreChangedFileHints()
    {
        var root = CreateTemporaryDirectory("gw-contract-wide");
        try
        {
            // Arrange: contract framing with three distinct hint files represents a wide contract edit.
            var endpoint = new QueuedEndpoint(
                """{"scope":"Docs","conclusion":"Proceed"}""",
                """{"kind":"Plan","why":"","planSummary":"coordinate the contract edit","planSteps":["update docs","update code","update tests"]}""",
                "I updated the docs.",
                AuthoredJson(["docs/guide.md"], "updated the docs"),
                "I updated the code.",
                CompletedJson([], "left the code unchanged"));
            var recordStore = new RecordStore(root);
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
                    "diff --git a/docs/guide.md b/docs/guide.md\n--- a/docs/guide.md\n+++ b/docs/guide.md\n@@ -1 +1 @@\n-old\n+new\n"),
                recordStore: recordStore);

            // Act: run a contract-clause-worded request with three changed-file hints.
            var result = await worker.RunAsync(
                MakeBrief(
                    "Update the contract clause wording for the feature.",
                    changedFileHints: [".anneal/architecture/toolkit/feature.md", "src/Feature.cs", "test/FeatureTests.cs"]),
                TestContext.Current.CancellationToken);

            // Assert: Planner and DocumentAuthor both ran before Developer.
            var steps = ReadRecordedSteps(root);
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains(steps, line => line.Contains("Preflight:PlanAndDocument", StringComparison.Ordinal)),
                () => Assert.Contains(steps, line => line.Contains("\"step\":\"Planner\"", StringComparison.Ordinal)),
                () => Assert.Contains(steps, line => line.Contains("\"step\":\"DocumentAuthor\"", StringComparison.Ordinal)),
                () => Assert.Contains(steps, line => line.Contains("\"step\":\"Developer\"", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerContractClausePreflightSkipsPlannerForOneOrTwoChangedFileHints()
    {
        foreach (var hints in new[]
        {
            new[] { ".anneal/architecture/toolkit/feature.md" },
            new[] { ".anneal/architecture/toolkit/feature.md", "src/Feature.cs" }
        })
        {
            var root = CreateTemporaryDirectory($"gw-contract-narrow-{hints.Length}");
            try
            {
                // Arrange: one-file and two-file contract edits should stay on the document-only preflight path.
                var endpoint = new QueuedEndpoint(
                    """{"scope":"Docs","conclusion":"Proceed"}""",
                    "I updated the docs.",
                    AuthoredJson(["docs/guide.md"], "updated the docs"),
                    "I updated the code.",
                    CompletedJson([], "left the code unchanged"));
                var recordStore = new RecordStore(root);
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
                        "diff --git a/docs/guide.md b/docs/guide.md\n--- a/docs/guide.md\n+++ b/docs/guide.md\n@@ -1 +1 @@\n-old\n+new\n"),
                    recordStore: recordStore);

                // Act: run a contract-clause-worded request below the wide-scope threshold.
                var result = await worker.RunAsync(
                    MakeBrief("Update the contract clause wording for the feature.", changedFileHints: hints),
                    TestContext.Current.CancellationToken);

                // Assert: DocumentAuthor ran, but Planner did not.
                var steps = ReadRecordedSteps(root);
                Assert.Multiple(
                    () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                    () => Assert.Contains(steps, line => line.Contains("Preflight:Document", StringComparison.Ordinal)),
                    () => Assert.Contains(steps, line => line.Contains("\"step\":\"DocumentAuthor\"", StringComparison.Ordinal)),
                    () => Assert.Contains(steps, line => line.Contains("\"step\":\"Developer\"", StringComparison.Ordinal)),
                    () => Assert.DoesNotContain(steps, line => line.Contains("\"step\":\"Planner\"", StringComparison.Ordinal)));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
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
                """{"scope":"Code","conclusion":"Proceed"}""",
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
                () => Assert.Equal(5, endpoint.Calls));
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
                """{"scope":"Code","conclusion":"Proceed"}""",
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
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerPostflightIgnoresPublicTestMethodAdditionForTenetCheck()
    {
        var root = CreateTemporaryDirectory("gw-public-test-method");
        try
        {
            // Arrange: a public test method is executable test surface, not production API surface.
            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I added the test.",
                CompletedJson(["test/PublicApiTests.cs"], "added the test"),
                PassedVerifierJson());
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
                    "diff --git a/test/PublicApiTests.cs b/test/PublicApiTests.cs\n--- a/test/PublicApiTests.cs\n+++ b/test/PublicApiTests.cs\n@@ -0,0 +1 @@\n+public void SomeTestMethod()\n"));

            // Act: run with a tenet that would be included only if public API surface was detected.
            var result = await worker.RunAsync(
                MakeBrief("Add a public test method.", tenets: ["Never expose an unstable public API by accident"]),
                TestContext.Current.CancellationToken);

            // Assert: verifier still runs for the test diff, but without the tenet expansion.
            var verifierText = string.Join("\n", endpoint.Requests[^1].Messages.Select(message => message.Text));
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.DoesNotContain("Also judge the diff against these repository tenets", verifierText, StringComparison.Ordinal),
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerPostflightRunsTenetCheckForProductionPublicMemberOrTypeDeclaration()
    {
        foreach (var diff in new[]
        {
            "diff --git a/src/PublicApi.cs b/src/PublicApi.cs\n--- a/src/PublicApi.cs\n+++ b/src/PublicApi.cs\n@@ -0,0 +1 @@\n+public string NewName() => \"new\";\n",
            "diff --git a/src/PublicType.cs b/src/PublicType.cs\n--- a/src/PublicType.cs\n+++ b/src/PublicType.cs\n@@ -0,0 +1 @@\n+public class PublicType\n"
        })
        {
            var root = CreateTemporaryDirectory("gw-public-api");
            try
            {
                // Arrange: production public member and type declarations are public API surface.
                var endpoint = new QueuedEndpoint(
                    """{"scope":"Code","conclusion":"Proceed"}""",
                    "I added public API.",
                    CompletedJson(["src/PublicApi.cs"], "added public API"),
                    PassedVerifierJson());
                var worker = new GeneralWorker(
                    root,
                    Effort.Large,
                    "planner charter",
                    "document charter",
                    "developer charter",
                    "verifier charter",
                    endpointFor: _ => endpoint,
                    buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                    runGit: SequencedGitStub(diff));

                // Act: run with a tenet that should be included for public production API changes.
                var result = await worker.RunAsync(
                    MakeBrief("Add public API.", tenets: ["Never expose an unstable public API by accident"]),
                    TestContext.Current.CancellationToken);

                // Assert: the verifier prompt includes the tenet expansion.
                var verifierText = string.Join("\n", endpoint.Requests[^1].Messages.Select(message => message.Text));
                Assert.Multiple(
                    () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                    () => Assert.Contains("Also judge the diff against these repository tenets", verifierText, StringComparison.Ordinal),
                    () => Assert.Equal(4, endpoint.Calls));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GeneralWorkerDocsOnlyMarkdownWithoutContractTouchSkipsVerifier()
    {
        var root = CreateTemporaryDirectory("gw-docs-only");
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I fixed the typo.",
                CompletedJson(["docs/guide.md"], "fixed the typo"));

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
                    "diff --git a/docs/guide.md b/docs/guide.md\n--- a/docs/guide.md\n+++ b/docs/guide.md\n@@ -1 +1 @@\n-Teh guide explains the worker.\n+The guide explains the worker.\n" +
                    "diff --git a/.anneal/logs/records/process-steps.jsonl b/.anneal/logs/records/process-steps.jsonl\n--- a/.anneal/logs/records/process-steps.jsonl\n+++ b/.anneal/logs/records/process-steps.jsonl\n@@ -0,0 +1 @@\n+{\"step\":\"Developer\"}\n"));

            var result = await worker.RunAsync(
                MakeBrief("Fix the typo in docs/guide.md.", changedFileHints: ["docs/guide.md"]),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(3, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerDocsOnlyContractTouchStillRunsVerifier()
    {
        var root = CreateTemporaryDirectory("gw-doc-touch");
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"scope":"Docs","conclusion":"Proceed"}""",
                "I updated the contract wording.",
                AuthoredJson([".anneal/architecture/toolkit/general-worker.md"], "updated the contract wording"),
                "I left code unchanged.",
                CompletedJson([], "left the code unchanged"),
                PassedVerifierJson());

            var contractChecks = 0;
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
                    contractChecks++;
                    return Task.FromResult(new ScriptRun(0, "contracts good"));
                },
                runGit: SequencedGitStub(
                    "diff --git a/.anneal/architecture/toolkit/general-worker.md b/.anneal/architecture/toolkit/general-worker.md\n--- a/.anneal/architecture/toolkit/general-worker.md\n+++ b/.anneal/architecture/toolkit/general-worker.md\n@@ -20,4 +20,4 @@\n ## Contract\n ### Provides\n-- **TOOLKIT-58** — old wording\n+- **TOOLKIT-58** — new wording\n"));

            var result = await worker.RunAsync(
                MakeBrief(
                    "Update the GeneralWorker contract clause wording in .anneal/architecture/toolkit/general-worker.md.",
                    changedFileHints: [".anneal/architecture/toolkit/general-worker.md"]),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(1, contractChecks),
                () => Assert.Equal(6, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerCodeOrTestDiffAlwaysRunsVerifier()
    {
        var root = CreateTemporaryDirectory("gw-test-diff");
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I tightened the test assertion.",
                CompletedJson(["test/InternalTests.cs"], "tightened the test assertion"),
                PassedVerifierJson());

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
                    "diff --git a/test/InternalTests.cs b/test/InternalTests.cs\n--- a/test/InternalTests.cs\n+++ b/test/InternalTests.cs\n@@ -1 +1 @@\n-Assert.Equal(1, value);\n+Assert.Equal(2, value);\n"));

            var result = await worker.RunAsync(
                MakeBrief("Tighten the test assertion.", changedFileHints: ["test/InternalTests.cs"]),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerMixedOrAmbiguousSurfaceStillRunsVerifier()
    {
        var root = CreateTemporaryDirectory("gw-mixed");
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
                "I fixed the docs and note.",
                CompletedJson(["docs/guide.md", "notes.txt"], "fixed the docs and note"),
                PassedVerifierJson());

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
                    "diff --git a/docs/guide.md b/docs/guide.md\n--- a/docs/guide.md\n+++ b/docs/guide.md\n@@ -1 +1 @@\n-old\n+new\n" +
                    "diff --git a/notes.txt b/notes.txt\n--- a/notes.txt\n+++ b/notes.txt\n@@ -1 +1 @@\n-old\n+new\n"));

            var result = await worker.RunAsync(
                MakeBrief("Fix the guide and note.", changedFileHints: ["docs/guide.md", "notes.txt"]),
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerVerifierRoleNeverDowngradesAcrossEfforts()
    {
        foreach (var effort in new[] { Effort.Small, Effort.Medium, Effort.Large })
        {
            var root = CreateTemporaryDirectory($"gw-verifier-role-{effort.ToString().ToLowerInvariant()}");
            try
            {
                var light = new QueuedEndpoint(
                    """{"scope":"Code","conclusion":"Proceed"}""",
                    CompletedJson(["src/Internal.cs"], "updated the helper"),
                    PassedVerifierJson());
                var medium = new QueuedEndpoint("I updated the helper.");
                var heavy = new QueuedEndpoint("I updated the helper.");

                var worker = new GeneralWorker(
                    root,
                    effort,
                    "planner charter",
                    "document charter",
                    "developer charter",
                    "verifier charter",
                    endpointFor: Serving(light, medium, heavy),
                    buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                    runGit: SequencedGitStub(
                        "diff --git a/src/Internal.cs b/src/Internal.cs\n--- a/src/Internal.cs\n+++ b/src/Internal.cs\n@@ -1 +1 @@\n-private int value;\n+private int value = 1;\n"));

                var result = await worker.RunAsync(
                    MakeBrief("Tidy the helper implementation.", changedFileHints: ["src/Internal.cs"]),
                    TestContext.Current.CancellationToken);

                var verifierText = string.Join("\n", light.Requests[^1].Messages.Select(message => message.Text));
                Assert.Multiple(
                    () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                    () => Assert.Equal(3, light.Calls),
                    () => Assert.Contains("Judge whether this change satisfies the requested work", verifierText, StringComparison.Ordinal),
                    () => Assert.Equal(effort == Effort.Large ? 0 : 1, medium.Calls),
                    () => Assert.Equal(effort == Effort.Large ? 1 : 0, heavy.Calls));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GeneralWorkerRepairEscalatesProducingRoleOnlyAfterRepairRequired()
    {
        var root = CreateTemporaryDirectory("gw-escalate");
        try
        {
            var light = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
                CompletedJson(["src/Internal.cs"], "updated the helper"),
                RepairRequiredVerifierJson(VerificationOwner.Code, "tighten the helper implementation"),
                CompletedJson(["src/Internal.cs"], "tightened the helper"),
                PassedVerifierJson());
            var medium = new QueuedEndpoint("I updated the helper.");
            var heavy = new QueuedEndpoint("I tightened the helper.");

            var worker = new GeneralWorker(
                root,
                Effort.Small,
                "planner charter",
                "document charter",
                "developer charter",
                "verifier charter",
                endpointFor: Serving(light, medium, heavy),
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: SequencedGitStub(
                    "diff --git a/src/Internal.cs b/src/Internal.cs\n--- a/src/Internal.cs\n+++ b/src/Internal.cs\n@@ -1 +1 @@\n-private int value;\n+private int value = 1;\n",
                    "diff --git a/src/Internal.cs b/src/Internal.cs\n--- a/src/Internal.cs\n+++ b/src/Internal.cs\n@@ -1 +1 @@\n-private int value;\n+private int value = 2;\n"));

            var result = await worker.RunAsync(
                MakeBrief("Tidy the helper implementation.", changedFileHints: ["src/Internal.cs"]),
                TestContext.Current.CancellationToken);

            var initialDeveloperText = string.Join("\n", medium.Requests[0].Messages.Select(message => message.Text));
            var repairDeveloperText = string.Join("\n", heavy.Requests[0].Messages.Select(message => message.Text));

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Equal(1, medium.Calls),
                () => Assert.Equal(1, heavy.Calls),
                () => Assert.Equal(5, light.Calls),
                () => Assert.DoesNotContain("tighten the helper implementation", initialDeveloperText, StringComparison.Ordinal),
                () => Assert.Contains("tighten the helper implementation", repairDeveloperText, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GeneralWorkerRunsSamePipelineAcrossAllSupportedEfforts()
    {
        foreach (var effort in new[] { Effort.Small, Effort.Medium, Effort.Large })
        {
            var root = CreateTemporaryDirectory($"gw-effort-{effort.ToString().ToLowerInvariant()}");
            try
            {
                var endpoint = new QueuedEndpoint(
                    """{"scope":"Code","conclusion":"Proceed"}""",
                    "I fixed the typo.",
                    CompletedJson(["docs/guide.md"], "fixed the typo"));
                var recordStore = new RecordStore(root);

                var worker = new GeneralWorker(
                    root,
                    effort,
                    "planner charter",
                    "document charter",
                    "developer charter",
                    "verifier charter",
                    endpointFor: _ => endpoint,
                    buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                    runGit: SequencedGitStub(
                        "diff --git a/docs/guide.md b/docs/guide.md\n--- a/docs/guide.md\n+++ b/docs/guide.md\n@@ -1 +1 @@\n-Teh guide explains the worker.\n+The guide explains the worker.\n"),
                    recordStore: recordStore);

                var result = await worker.RunAsync(
                    MakeBrief("Fix the typo in docs/guide.md.", changedFileHints: ["docs/guide.md"]),
                    TestContext.Current.CancellationToken);

                var steps = ReadRecordedSteps(root);
                Assert.Multiple(
                    () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                    () => Assert.Contains(steps, line => line.Contains("Preflight:CodeOnly", StringComparison.Ordinal)),
                    () => Assert.Contains(steps, line => line.Contains("\"step\":\"Developer\"", StringComparison.Ordinal)),
                    () => Assert.Contains(steps, line => line.Contains("DeterministicCheck:build.ps1", StringComparison.Ordinal)),
                    () => Assert.Contains(steps, line => line.Contains("\"step\":\"DiffCheck\"", StringComparison.Ordinal)),
                    () => Assert.Contains(steps, line => line.Contains("Verifier:skipped", StringComparison.Ordinal)),
                    () => Assert.DoesNotContain(steps, line => line.Contains("\"step\":\"Planner\"", StringComparison.Ordinal)),
                    () => Assert.DoesNotContain(steps, line => line.Contains("DocumentAuthor", StringComparison.Ordinal)));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GeneralWorkerAmbiguousDiffAnalysisFailsClosed()
    {
        var root = CreateTemporaryDirectory("gw-ambiguous");
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
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
                () => Assert.Equal(3, endpoint.Calls));
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
                """{"scope":"Code","conclusion":"Proceed"}""",
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
                () => Assert.Equal(3, endpoint.Calls));
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
                  - .anneal/architecture/toolkit/widget.md
                ---
                # Widget
                Refers to OldWidget in an introductory sentence.
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeBoundaryTest`
                """);

            var endpoint = new QueuedEndpoint(
                """{"scope":"Code","conclusion":"Proceed"}""",
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
                    "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n" +
                    "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n@@ -1,4 +1,4 @@\n # Widget\n-Refers to OldWidget in an introductory sentence.\n+Refers to NewWidget in an introductory sentence.\n ## Contract\n ### Provides\n",
                    "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n" +
                    "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n@@ -1,4 +1,4 @@\n # Widget\n-Refers to OldWidget in an introductory sentence.\n+Refers to NewWidget in an introductory sentence.\n ## Contract\n ### Provides\n",
                    "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n@@ -1,4 +1,4 @@\n # Widget\n-Refers to OldWidget in an introductory sentence.\n+Refers to NewWidget in an introductory sentence.\n ## Contract\n ### Provides\n",
                    "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n@@ -1,4 +1,4 @@\n # Widget\n-Refers to OldWidget in an introductory sentence.\n+Refers to NewWidget in an introductory sentence.\n ## Contract\n ### Provides\n",
                    "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget {}\n+class NewWidget {}\n"));

            var result = await worker.RunAsync(
                MakeBrief("Rename the widget type."),
                TestContext.Current.CancellationToken);

            var notes = string.Join("\n", result.Notes.Select(note => note.Text));
            var completed = Assert.IsType<WorkerRunResult.Completed>(result.Finding);
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.Contains(".anneal/architecture/toolkit/widget.md", completed.Summary.FilesChanged),
                () => Assert.Contains("corrected stale wording", notes, StringComparison.OrdinalIgnoreCase));
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
        new(
            "parent-1",
            workItem,
            Effort.Large,
            "general",
            [],
            [],
            "the route selected the capability-complete general worker",
            [],
            tenets ?? [],
            changedFileHints ?? []);

    private static string PassedVerifierJson() =>
        """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""";

    private static string RepairRequiredVerifierJson(VerificationOwner owner, string fixText) =>
        $$"""{"verdict":"RepairRequired","concerns":[{"owner":"{{owner}}","fixText":"{{fixText}}"}],"advisoryNotes":[],"evidenceSufficient":true}""";

    private static string CompletedJson(IReadOnlyList<string> filesChanged, string summary) =>
        $$"""{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":[{{RenderJsonArray(filesChanged)}}],"summary":"{{summary}}"}""";

    private static string AuthoredJson(IReadOnlyList<string> filesChanged, string summary) =>
        $$"""{"kind":"Authored","why":"","filesChanged":[{{RenderJsonArray(filesChanged)}}],"summary":"{{summary}}"}""";

    private static string RenderJsonArray(IReadOnlyList<string> values) =>
        string.Join(",", values.Select(value => $"\"{value}\""));

    private static Func<ModelRole, IChatEndpoint> Serving(
        IChatEndpoint light,
        IChatEndpoint medium,
        IChatEndpoint heavy) =>
        role => role switch
        {
            ModelRole.Light => light,
            ModelRole.Medium => medium,
            _ => heavy
        };

    private static IReadOnlyList<string> ReadRecordedSteps(string root)
    {
        var path = RecordStore.ProcessStepsPathFor(root);
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

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
