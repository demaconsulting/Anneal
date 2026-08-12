using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-56 (route small-fix gate) and TOOLKIT-57 (maintain gate): after
///     <c>SmallFixWorker</c> completes, both <c>route</c> and <c>maintain</c> run a separate model-backed
///     architecture doc/code agreement check — grounded on the actual git diff, once per matched document.
///     A contract disagreement is durably persisted under <c>.anneal/logs/findings/</c> and reported in the
///     output; the run still exits 0 (Authoring category). When no architecture document covers any changed
///     file, the gate is a no-op.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync" /> and assertions are on the exit code, the written output, and the
///     files present on disk. Nothing here reaches inside <see cref="RouteOperation" />,
///     <see cref="MaintainOperation" />, or <see cref="ArchDocAgreementGate" />.
/// </remarks>
public class ArchDocAgreementGateContractTests
{
    // ── TOOLKIT-56 / route ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     TOOLKIT-56 — when the actual git diff touches no file covered by any architecture document,
    ///     the gate is a complete no-op: no oracle call is made beyond what SmallFixWorker itself needed,
    ///     and no finding file is written. Verified by
    ///     <c>RouteSmallFixGateSkipsWhenNoArchDocCoversChangedFiles</c>.
    /// </summary>
    [Fact]
    public async Task RouteSmallFixGateSkipsWhenNoArchDocCoversChangedFiles()
    {
        var root = CreateTemporaryDirectory("tk56-skip");
        try
        {
            // Arrange: SmallFixWorker's two replies only. The gate's oracle would need a third reply if it ran.
            // The runGit stub returns a diff touching src/Internal.cs — no arch doc covers that path.
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"simple fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                "I made the change.",
                CompletedJson(["src/Internal.cs"], "tidied the helper"));

            RunGitCommand runGit = (args, _) =>
            {
                // 'git add -N .' or 'git diff HEAD' — return a diff touching src/Internal.cs, which no arch doc covers.
                var diff = string.Join(" ", args).Contains("diff")
                    ? "diff --git a/src/Internal.cs b/src/Internal.cs\n--- a/src/Internal.cs\n+++ b/src/Internal.cs\n@@ -1 +1 @@\n-old\n+new"
                    : string.Empty;
                return Task.FromResult(new ScriptRun(0, diff));
            };

            // No arch doc under .anneal/architecture/ covers src/Internal.cs — the dir may not even exist.
            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["route", "tidy the helper"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            // Assert: the run completes, no finding file written, endpoint consumed exactly 3 replies.
            var findingsDir = Path.Combine(root, ".anneal", "logs", "findings");
            var findingFiles = Directory.Exists(findingsDir)
                ? Directory.GetFiles(findingsDir, "arch-disagreement-*.json")
                : [];

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Empty(findingFiles),
                () => Assert.Equal(3, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-56 — the gate runs once per matched architecture document, not once per changed file or
    ///     per hunk: two changed files covered by the same document produce one oracle call, not two.
    ///     Verified by <c>RouteSmallFixGateRunsOncePerMatchedDocument</c>.
    /// </summary>
    [Fact]
    public async Task RouteSmallFixGateRunsOncePerMatchedDocument()
    {
        var root = CreateTemporaryDirectory("tk56-once");
        try
        {
            // Arrange: one arch doc covering src/A.cs and src/B.cs. The diff touches both files.
            // SmallFixWorker needs 3 replies (route oracle + author + completed); the gate oracle needs 1.
            // Total: 4 calls.
            CreateArchDoc(root, "toolkit/widget.md",
                """
                ---
                covers:
                  - src/A.cs
                  - src/B.cs
                ---
                # Widget
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeTest`
                """);

            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"simple fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                "I made the change.",
                CompletedJson(["src/A.cs", "src/B.cs"], "updated widget"),
                // Gate oracle reply — "Agree" for the one matched document.
                """{"verdict":"Agree","reason":"","hasSufficientEvidence":true}""");

            RunGitCommand runGit = (args, _) =>
            {
                var diff = string.Join(" ", args).Contains("diff")
                    ? "diff --git a/src/A.cs b/src/A.cs\n--- a/src/A.cs\n+++ b/src/A.cs\n@@ -1 +1 @@\n-old\n+new\n" +
                      "diff --git a/src/B.cs b/src/B.cs\n--- a/src/B.cs\n+++ b/src/B.cs\n@@ -1 +1 @@\n-old\n+new"
                    : string.Empty;
                return Task.FromResult(new ScriptRun(0, diff));
            };

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["route", "update widget"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            // Assert: run succeeded, gate made exactly 1 oracle call (the 4th total call).
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-56 — when the oracle classifies a mismatch as wording-only outside the Contract section,
    ///     <c>route</c> attempts a narrow inline correction via <see cref="DocumentAuthor" />.
    ///     Verified by <c>RouteSmallFixGateCorrectsWordingOnlyMismatch</c>.
    /// </summary>
    [Fact]
    public async Task RouteSmallFixGateCorrectsWordingOnlyMismatch()
    {
        var root = CreateTemporaryDirectory("tk56-wording");
        try
        {
            // Arrange: one arch doc covering src/Widget.cs, oracle says WordingOnly.
            // After the wording-only verdict, DocumentAuthor needs 2 replies (RunAsync + ProbeAsync for result).
            CreateArchDoc(root, "toolkit/widget.md",
                """
                ---
                covers:
                  - src/Widget.cs
                ---
                # Widget
                The widget uses the old class name OldWidget in its description.
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeTest`
                """);

            var endpoint = new QueuedEndpoint(
                // Route oracle
                """{"kind":"SelectWorker","why":"simple fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                // SmallFixWorker author
                "I updated Widget.cs.",
                // SmallFixWorker completed structured decision
                CompletedJson(["src/Widget.cs"], "renamed Widget"),
                // Gate oracle — WordingOnly
                """{"verdict":"WordingOnly","reason":"'OldWidget' should now be 'NewWidget' in the introductory paragraph","hasSufficientEvidence":true}""",
                // DocumentAuthor free-text turn
                "I corrected OldWidget to NewWidget.",
                // DocumentAuthor.ProbeAsync structured result
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit/widget.md"],"summary":"corrected stale name"}""");

            RunGitCommand runGit = (args, _) =>
            {
                var diff = string.Join(" ", args).Contains("diff")
                    ? "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget\n+class NewWidget"
                    : string.Empty;
                return Task.FromResult(new ScriptRun(0, diff));
            };

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["route", "rename Widget"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            var written = output.ToString();

            // Assert: run succeeded and the output announces the wording correction.
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("corrected stale wording", written, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-56 — a wording-only correction is never trusted on the correcting model's own
    ///     good behavior: after <see cref="DocumentAuthor" /> completes, <c>route</c> mechanically
    ///     re-reads the actual diff it produced and reverts it (rather than letting an unverified
    ///     Contract-touching edit stand) if it touched <c>## Contract</c> despite being told not to.
    ///     Verified by <c>RouteSmallFixGateRevertsCorrectionThatTouchesContract</c>.
    /// </summary>
    [Fact]
    public async Task RouteSmallFixGateRevertsCorrectionThatTouchesContract()
    {
        var root = CreateTemporaryDirectory("tk56-revert");
        try
        {
            CreateArchDoc(root, "toolkit/widget.md",
                """
                ---
                covers:
                  - src/Widget.cs
                ---
                # Widget
                The widget uses the old class name OldWidget in its description.
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeTest`
                """);

            var endpoint = new QueuedEndpoint(
                // Route oracle
                """{"kind":"SelectWorker","why":"simple fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                // SmallFixWorker author
                "I updated Widget.cs.",
                // SmallFixWorker completed structured decision
                CompletedJson(["src/Widget.cs"], "renamed Widget"),
                // Gate oracle — WordingOnly
                """{"verdict":"WordingOnly","reason":"'OldWidget' should now be 'NewWidget' in the introductory paragraph","hasSufficientEvidence":true}""",
                // DocumentAuthor free-text turn
                "I corrected OldWidget to NewWidget.",
                // DocumentAuthor.ProbeAsync structured result
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit/widget.md"],"summary":"corrected stale name"}""");

            var diffCallCount = 0;
            var checkoutCalled = false;
            RunGitCommand runGit = (args, _) =>
            {
                var joined = string.Join(" ", args);
                if (joined.Contains("checkout"))
                {
                    checkoutCalled = true;
                    return Task.FromResult(new ScriptRun(0, string.Empty));
                }

                if (!joined.Contains("diff"))
                    return Task.FromResult(new ScriptRun(0, string.Empty));

                diffCallCount++;

                // First diff (pre-correction) is the ordinary wording-only change to src/Widget.cs.
                // Second diff (post-correction, the mechanical re-check) shows the correction itself
                // — despite being told not to — edited inside widget.md's ## Contract section.
                var patch = diffCallCount == 1
                    ? "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-class OldWidget\n+class NewWidget"
                    : "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n" +
                      "--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n" +
                      "@@ -1,3 +1,3 @@\n # Widget\n ## Contract\n-- **WIDGET-1** — does something.\n+- **WIDGET-1** — does a new thing.";
                return Task.FromResult(new ScriptRun(0, patch));
            };

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["route", "rename Widget"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            var written = output.ToString();

            // Assert: run still succeeds (Authoring category), but the output reports the revert
            // rather than claiming a clean correction, and 'git checkout' was actually invoked.
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.True(checkoutCalled),
                () => Assert.Contains("touched the ## Contract section and was reverted", written, StringComparison.OrdinalIgnoreCase),
                () => Assert.DoesNotContain("corrected stale wording", written, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-56 — when the oracle classifies a disagreement as touching the Contract section substance,
    ///     <c>route</c> makes no edit, persists a neutral finding under <c>.anneal/logs/findings/</c>,
    ///     and reports it in the output. The run still exits 0. Verified by
    ///     <c>RouteSmallFixGatePersistsContractDisagreementFinding</c>.
    /// </summary>
    [Fact]
    public async Task RouteSmallFixGatePersistsContractDisagreementFinding()
    {
        var root = CreateTemporaryDirectory("tk56-contract");
        try
        {
            // Arrange: one arch doc covering src/Widget.cs, oracle says ContractDisagreement.
            CreateArchDoc(root, "toolkit/widget.md",
                """
                ---
                covers:
                  - src/Widget.cs
                ---
                # Widget
                ## Contract
                ### Provides
                - **WIDGET-1** — does something specific.
                  *Verified by:* `SomeTest`
                """);

            var endpoint = new QueuedEndpoint(
                // Route oracle
                """{"kind":"SelectWorker","why":"simple fix","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}""",
                // SmallFixWorker author
                "I updated Widget.cs.",
                // SmallFixWorker completed
                CompletedJson(["src/Widget.cs"], "changed widget contract"),
                // Gate oracle — ContractDisagreement
                """{"verdict":"ContractDisagreement","reason":"WIDGET-1 no longer matches the implementation","hasSufficientEvidence":true}""");

            RunGitCommand runGit = (args, _) =>
            {
                var diff = string.Join(" ", args).Contains("diff")
                    ? "diff --git a/src/Widget.cs b/src/Widget.cs\n--- a/src/Widget.cs\n+++ b/src/Widget.cs\n@@ -1 +1 @@\n-old contract\n+new contract"
                    : string.Empty;
                return Task.FromResult(new ScriptRun(0, diff));
            };

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["route", "change widget contract"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            var written = output.ToString();

            // Assert: exit is 0 (Authoring category never gates), finding file exists, output names it.
            var findingsDir = Path.Combine(root, ".anneal", "logs", "findings");
            var findingFiles = Directory.Exists(findingsDir)
                ? Directory.GetFiles(findingsDir, "arch-disagreement-*.json")
                : [];

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.NotEmpty(findingFiles),
                () => Assert.Contains("arch-doc agreement finding", written, StringComparison.OrdinalIgnoreCase),
                () => Assert.Contains("neither is presumed correct", written, StringComparison.Ordinal));

            if (findingFiles.Length > 0)
            {
                var json = File.ReadAllText(findingFiles[0]);
                Assert.Contains("ContractDisagreement", json, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── TOOLKIT-57 / maintain ──────────────────────────────────────────────────────────

    /// <summary>
    ///     TOOLKIT-57 — when the actual git diff touches no file covered by any architecture document,
    ///     the gate is a no-op for <c>maintain</c> too: no extra oracle call is made and no finding file is
    ///     written. Verified by <c>MaintainArchGateSkipsWhenNoArchDocCoversChangedFiles</c>.
    /// </summary>
    [Fact]
    public async Task MaintainArchGateSkipsWhenNoArchDocCoversChangedFiles()
    {
        var root = CreateTemporaryDirectory("tk57-skip");
        try
        {
            // Arrange: SmallFixWorker needs 2 replies; gate needs none (no matching arch doc).
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                CompletedJson(["src/a.cs"], "tidied the helper"));

            RunGitCommand runGit = (args, _) =>
            {
                var diff = string.Join(" ", args).Contains("diff")
                    ? "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-old\n+new"
                    : string.Empty;
                return Task.FromResult(new ScriptRun(0, diff));
            };

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "tidy the helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            // Assert: success, no finding written, exactly 2 endpoint calls.
            var findingsDir = Path.Combine(root, ".anneal", "logs", "findings");
            var findingFiles = Directory.Exists(findingsDir)
                ? Directory.GetFiles(findingsDir, "arch-disagreement-*.json")
                : [];

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Empty(findingFiles),
                () => Assert.Equal(2, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-57 — for <c>maintain</c>, the gate runs once per matched architecture document, not once
    ///     per changed file. Verified by <c>MaintainArchGateRunsOncePerMatchedDocument</c>.
    /// </summary>
    [Fact]
    public async Task MaintainArchGateRunsOncePerMatchedDocument()
    {
        var root = CreateTemporaryDirectory("tk57-once");
        try
        {
            // Arrange: one arch doc covering both src/a.cs and src/b.cs; diff touches both — one gate call.
            CreateArchDoc(root, "toolkit/widget.md",
                """
                ---
                covers:
                  - src/a.cs
                  - src/b.cs
                ---
                # Widget
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeTest`
                """);

            // SmallFixWorker: 2 replies; gate oracle: 1 reply. Total 3.
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                CompletedJson(["src/a.cs", "src/b.cs"], "updated widget"),
                // Gate oracle reply — Agree.
                """{"verdict":"Agree","reason":"","hasSufficientEvidence":true}""");

            RunGitCommand runGit = (args, _) =>
            {
                var diff = string.Join(" ", args).Contains("diff")
                    ? "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-old\n+new\n" +
                      "diff --git a/src/b.cs b/src/b.cs\n--- a/src/b.cs\n+++ b/src/b.cs\n@@ -1 +1 @@\n-old\n+new"
                    : string.Empty;
                return Task.FromResult(new ScriptRun(0, diff));
            };

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "update widget", "src/a.cs", "src/b.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            // Assert: run succeeded, gate made exactly 1 oracle call (3rd total).
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Equal(3, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-57 — for <c>maintain</c>, a wording-only mismatch outside the Contract section triggers a
    ///     narrow inline correction (the wording-only correction is permitted for maintain). Verified by
    ///     <c>MaintainArchGateCorrectsWordingOnlyMismatchOutsideContract</c>.
    /// </summary>
    [Fact]
    public async Task MaintainArchGateCorrectsWordingOnlyMismatchOutsideContract()
    {
        var root = CreateTemporaryDirectory("tk57-wording");
        try
        {
            // Arrange: one arch doc covering src/a.cs; oracle says WordingOnly.
            // DocumentAuthor correction needs 2 further replies.
            CreateArchDoc(root, "toolkit/widget.md",
                """
                ---
                covers:
                  - src/a.cs
                ---
                # Widget
                Refers to the old helper name OldHelper in a description.
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeTest`
                """);

            var endpoint = new QueuedEndpoint(
                // SmallFixWorker author
                "I updated a.cs.",
                // SmallFixWorker completed
                CompletedJson(["src/a.cs"], "renamed helper"),
                // Gate oracle — WordingOnly
                """{"verdict":"WordingOnly","reason":"'OldHelper' should be 'NewHelper' in the description paragraph","hasSufficientEvidence":true}""",
                // DocumentAuthor free-text turn
                "I updated the doc.",
                // DocumentAuthor structured result
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit/widget.md"],"summary":"corrected stale name"}""");

            RunGitCommand runGit = (args, _) =>
            {
                var diff = string.Join(" ", args).Contains("diff")
                    ? "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-class OldHelper\n+class NewHelper"
                    : string.Empty;
                return Task.FromResult(new ScriptRun(0, diff));
            };

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "rename helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            var written = output.ToString();

            // Assert: run succeeded and the output announces the correction.
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("corrected stale wording", written, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-57 — for <c>maintain</c> too, a wording-only correction is mechanically re-checked
    ///     against the actual diff it produced and reverted rather than trusted, if it touched
    ///     <c>## Contract</c> despite being told not to. Verified by
    ///     <c>MaintainArchGateRevertsCorrectionThatTouchesContract</c>.
    /// </summary>
    [Fact]
    public async Task MaintainArchGateRevertsCorrectionThatTouchesContract()
    {
        var root = CreateTemporaryDirectory("tk57-revert");
        try
        {
            CreateArchDoc(root, "toolkit/widget.md",
                """
                ---
                covers:
                  - src/a.cs
                ---
                # Widget
                Refers to the old helper name OldHelper in a description.
                ## Contract
                ### Provides
                - **WIDGET-1** — does something.
                  *Verified by:* `SomeTest`
                """);

            var endpoint = new QueuedEndpoint(
                // SmallFixWorker author
                "I updated a.cs.",
                // SmallFixWorker completed
                CompletedJson(["src/a.cs"], "renamed helper"),
                // Gate oracle — WordingOnly
                """{"verdict":"WordingOnly","reason":"'OldHelper' should be 'NewHelper' in the description paragraph","hasSufficientEvidence":true}""",
                // DocumentAuthor free-text turn
                "I updated the doc.",
                // DocumentAuthor structured result
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit/widget.md"],"summary":"corrected stale name"}""");

            var diffCallCount = 0;
            var checkoutCalled = false;
            RunGitCommand runGit = (args, _) =>
            {
                var joined = string.Join(" ", args);
                if (joined.Contains("checkout"))
                {
                    checkoutCalled = true;
                    return Task.FromResult(new ScriptRun(0, string.Empty));
                }

                if (!joined.Contains("diff"))
                    return Task.FromResult(new ScriptRun(0, string.Empty));

                diffCallCount++;

                var patch = diffCallCount == 1
                    ? "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-class OldHelper\n+class NewHelper"
                    : "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n" +
                      "--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n" +
                      "@@ -1,3 +1,3 @@\n # Widget\n ## Contract\n-- **WIDGET-1** — does something.\n+- **WIDGET-1** — does a new thing.";
                return Task.FromResult(new ScriptRun(0, patch));
            };

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "rename helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            var written = output.ToString();

            // Assert: run still succeeds, but the revert is reported and 'git checkout' was invoked.
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.True(checkoutCalled),
                () => Assert.Contains("touched the ## Contract section and was reverted", written, StringComparison.OrdinalIgnoreCase),
                () => Assert.DoesNotContain("corrected stale wording", written, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-57 — for <c>maintain</c>, a contract-level disagreement is persisted as a neutral finding
    ///     under <c>.anneal/logs/findings/</c> with no edit made to either the document or the code.
    ///     The run still exits 0. Verified by
    ///     <c>MaintainArchGatePersistsContractDisagreementFindingWithoutEditing</c>.
    /// </summary>
    [Fact]
    public async Task MaintainArchGatePersistsContractDisagreementFindingWithoutEditing()
    {
        var root = CreateTemporaryDirectory("tk57-contract");
        try
        {
            // Arrange: one arch doc covering src/a.cs; oracle says ContractDisagreement.
            // No DocumentAuthor replies are queued — the gate must not attempt any edit.
            CreateArchDoc(root, "toolkit/widget.md",
                """
                ---
                covers:
                  - src/a.cs
                ---
                # Widget
                ## Contract
                ### Provides
                - **WIDGET-1** — does a specific thing.
                  *Verified by:* `SomeTest`
                """);

            var endpoint = new QueuedEndpoint(
                // SmallFixWorker author
                "I updated a.cs.",
                // SmallFixWorker completed
                CompletedJson(["src/a.cs"], "changed behavior"),
                // Gate oracle — ContractDisagreement
                """{"verdict":"ContractDisagreement","reason":"WIDGET-1 no longer does the specific thing","hasSufficientEvidence":true}""");

            RunGitCommand runGit = (args, _) =>
            {
                var diff = string.Join(" ", args).Contains("diff")
                    ? "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-old\n+new"
                    : string.Empty;
                return Task.FromResult(new ScriptRun(0, diff));
            };

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();

            // Act
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "change behavior", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            var written = output.ToString();

            // Assert: exit 0, finding persisted, output names it, no architecture doc was edited.
            var findingsDir = Path.Combine(root, ".anneal", "logs", "findings");
            var findingFiles = Directory.Exists(findingsDir)
                ? Directory.GetFiles(findingsDir, "arch-disagreement-*.json")
                : [];

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.NotEmpty(findingFiles),
                () => Assert.Contains("arch-doc agreement finding", written, StringComparison.OrdinalIgnoreCase),
                () => Assert.Contains("neither is presumed correct", written, StringComparison.Ordinal),
                // Exactly 3 endpoint calls: author + completed + gate oracle — no DocumentAuthor call.
                () => Assert.Equal(3, endpoint.Calls));

            if (findingFiles.Length > 0)
            {
                var json = File.ReadAllText(findingFiles[0]);
                Assert.Contains("ContractDisagreement", json, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CompletedJson(IReadOnlyList<string> filesChanged, string summary) =>
        $$"""
          {"kind":"Completed","why":"","suggestedWorker":"","filesChanged":[{{string.Join(",", filesChanged.Select(file => $"\"{file}\""))}}],"summary":"{{summary}}"}
          """;

    private static string CreateTemporaryDirectory(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), $"anneal-{prefix}-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), "");
        return root;
    }

    private static void CreateArchDoc(string root, string relativePath, string content)
    {
        var archDir = Path.Combine(root, ".anneal", "architecture");
        var fullPath = Path.Combine(archDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
