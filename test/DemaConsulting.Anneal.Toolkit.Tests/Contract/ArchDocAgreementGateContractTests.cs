using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-57: after <c>maintain</c> completes, it runs a separate model-backed
///     architecture doc/code agreement check — grounded on the actual git diff, once per matched document.
///     A contract disagreement is durably persisted under <c>.anneal/logs/findings/</c> and reported in the
///     output; the run still exits 0 (Authoring category). When no architecture document covers any changed
///     file, the gate is a no-op.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync" /> and assertions are on the exit code, the written output, and the
///     files present on disk. Nothing here reaches inside <see cref="MaintainOperation" /> or
///     <see cref="ArchDocAgreementGate" />.
/// </remarks>
public class ArchDocAgreementGateContractTests
{
    [Fact]
    public async Task MaintainArchGateSkipsWhenNoArchDocCoversChangedFiles()
    {
        var root = CreateTemporaryDirectory("tk57-skip");
        try
        {
            var endpoint = new QueuedEndpoint(
                "I made the change.",
                CompletedJson(["src/a.cs"], "tidied the helper"),
                PassedVerifierJson());

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
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "tidy the helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task MaintainArchGateRunsOncePerMatchedDocument()
    {
        var root = CreateTemporaryDirectory("tk57-once");
        try
        {
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

            var endpoint = new QueuedEndpoint(
                "I made the change.",
                CompletedJson(["src/a.cs", "src/b.cs"], "updated widget"),
                PassedVerifierJson(),
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

            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "update widget", "src/a.cs", "src/b.cs"],
                TextWriter.Null,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MaintainArchGateCorrectsWordingOnlyMismatchOutsideContract()
    {
        var root = CreateTemporaryDirectory("tk57-wording");
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
                "I updated a.cs.",
                CompletedJson(["src/a.cs"], "renamed helper"),
                PassedVerifierJson(),
                """{"verdict":"WordingOnly","reason":"'OldHelper' should be 'NewHelper' in the description paragraph","hasSufficientEvidence":true}""",
                "I updated the doc.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit/widget.md"],"summary":"corrected stale wording"}""");

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
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "rename helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("corrected stale wording", output.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
                "I updated a.cs.",
                CompletedJson(["src/a.cs"], "renamed helper"),
                PassedVerifierJson(),
                """{"verdict":"WordingOnly","reason":"'OldHelper' should be 'NewHelper' in the description paragraph","hasSufficientEvidence":true}""",
                "I updated the doc.",
                """{"kind":"Authored","why":"","filesChanged":[".anneal/architecture/toolkit/widget.md"],"summary":"corrected stale wording"}""");

            var (runGit, wasCheckoutCalled) = MakeRevertCheckGitStub(
                "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-class OldHelper\n+class NewHelper\n",
                "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-class OldHelper\n+class NewHelper\n",
                "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-class OldHelper\n+class NewHelper\n",
                "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n" +
                "--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n" +
                "@@ -1,4 +1,4 @@\n # Widget\n-Refers to the old helper name OldHelper in a description.\n+Refers to the new helper name NewHelper in a description.\n ## Contract\n ### Provides\n",
                "diff --git a/.anneal/architecture/toolkit/widget.md b/.anneal/architecture/toolkit/widget.md\n" +
                "--- a/.anneal/architecture/toolkit/widget.md\n+++ b/.anneal/architecture/toolkit/widget.md\n" +
                "@@ -1,4 +1,4 @@\n # Widget\n ## Contract\n-- **WIDGET-1** — does something.\n+- **WIDGET-1** — does a new thing.\n");

            var operation = new MaintainOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "rename helper", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.True(wasCheckoutCalled()),
                () => Assert.Contains("touched the ## Contract section and was reverted", output.ToString(), StringComparison.OrdinalIgnoreCase),
                () => Assert.DoesNotContain("corrected stale wording", output.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MaintainArchGatePersistsContractDisagreementFindingWithoutEditing()
    {
        var root = CreateTemporaryDirectory("tk57-contract");
        try
        {
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
                "I updated a.cs.",
                CompletedJson(["src/a.cs"], "changed behavior"),
                PassedVerifierJson(),
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
            var exitCode = await AnnealTool.RunAsync(
                ["maintain", "change behavior", "src/a.cs"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            var written = output.ToString();
            var findingsDir = Path.Combine(root, ".anneal", "logs", "findings");
            var findingFiles = Directory.Exists(findingsDir)
                ? Directory.GetFiles(findingsDir, "arch-disagreement-*.json")
                : [];

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.NotEmpty(findingFiles),
                () => Assert.Contains("arch-doc agreement finding", written, StringComparison.OrdinalIgnoreCase),
                () => Assert.Contains("neither is presumed correct", written, StringComparison.Ordinal),
                () => Assert.Equal(4, endpoint.Calls));

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

    private static (RunGitCommand RunGit, Func<bool> WasCheckoutCalled) MakeRevertCheckGitStub(
        params string[] diffPatches)
    {
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

            var index = Math.Min(diffCallCount, diffPatches.Length - 1);
            var patch = diffPatches[index];
            diffCallCount++;
            return Task.FromResult(new ScriptRun(0, patch));
        };

        return (runGit, () => checkoutCalled);
    }

    private static string PassedVerifierJson() =>
        """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""";

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
