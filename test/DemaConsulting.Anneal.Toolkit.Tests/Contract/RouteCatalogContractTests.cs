using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for route's production catalog surface.
/// </summary>
public class RouteCatalogContractTests
{
    [Fact]
    public async Task RouteCatalogCanSelectGeneralLarge()
    {
        var root = CreateTemporaryDirectory("route-general-large");
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind":"SelectWorker","why":"this needs the capability-complete large path","workerKey":"general-large","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Large","hasSufficientEvidence":true}""",
                "I implemented the code.",
                """{"kind":"Completed","why":"","suggestedWorker":"","filesChanged":["src/Internal.cs"],"summary":"implemented the worker"}""",
                """{"verdict":"Passed","concerns":[],"advisoryNotes":[],"evidenceSufficient":true}""");

            RunGitCommand runGit = (args, _) =>
            {
                var joined = string.Join(" ", args);
                if (!joined.Contains("diff", StringComparison.Ordinal))
                    return Task.FromResult(new ScriptRun(0, string.Empty));

                return Task.FromResult(
                    new ScriptRun(
                        0,
                        "diff --git a/src/Internal.cs b/src/Internal.cs\n--- a/src/Internal.cs\n+++ b/src/Internal.cs\n@@ -1 +1 @@\n-private int value;\n+private int value = 1;\n"));
            };

            var operation = new RouteOperation(
                root,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")),
                runGit: runGit);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "implement the capability-complete worker"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);

            var written = output.ToString();
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("route: completed", written, StringComparison.OrdinalIgnoreCase),
                () => Assert.Contains("route: effort classified as Large", written, StringComparison.Ordinal),
                () => Assert.Equal(4, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory(string stem)
    {
        var root = Path.Combine(Path.GetTempPath(), $"{stem}-{Guid.NewGuid():N}"[..24]);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "build.ps1"), string.Empty);
        return root;
    }
}
