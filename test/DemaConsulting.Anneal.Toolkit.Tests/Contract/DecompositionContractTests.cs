using DemaConsulting.Anneal.Toolkit;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for TOOLKIT-26, TOOLKIT-27, and TOOLKIT-28: how <c>route</c> decomposes a Massive-Effort
///     work item into phases, clears the mandatory cumulative check before routing any of them, forces
///     escalation unconditionally on a phase touching a protected path, and caps decomposition recursion at
///     depth two.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.RunAsync(IReadOnlyList{string}, TextWriter, CancellationToken)" /> and assertions
///     are on the exit code and the written output. Nothing here reaches inside <see cref="RouteOperation" />
///     or <c>Process.Router</c>.
/// </remarks>
public class DecompositionContractTests
{
    /// <summary>
    ///     TOOLKIT-26 — the mandatory cumulative check runs, and must clear, before any phase of a decomposed
    ///     Massive item is routed: an Escalate verdict stops the run before a single phase is routed, while a
    ///     Clear verdict lets every proposed phase run. Verified by <c>CumulativeCheckClearsBeforeAnyPhaseIsRouted</c>.
    /// </summary>
    [Fact]
    public async Task CumulativeCheckClearsBeforeAnyPhaseIsRouted()
    {
        // Scenario 1: the cumulative check does not clear - no phase is routed at all, proving the check gates
        // routing rather than merely accompanying it.
        var escalateRoot = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                MassiveSelectWorkerJson(),
                DecomposedJson(["do part A", "do part B"], ["src/a.cs", "src/b.cs"], ["Code", "Code"]),
                EscalateJson("the union quietly moves a system boundary", "a person should review this decomposition"));

            var operation = new RouteOperation(escalateRoot, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "a massive item"],
                output,
                [operation],
                escalateRoot,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
                () => Assert.Contains("route: escalated", written, StringComparison.Ordinal),
                () => Assert.DoesNotContain("route: decomposed into", written, StringComparison.Ordinal),
                () => Assert.DoesNotContain("do part A", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(escalateRoot, recursive: true);
        }

        // Scenario 2: the cumulative check clears - every proposed phase is then routed and completes.
        var clearRoot = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                MassiveSelectWorkerJson(),
                DecomposedJson(["do part A", "do part B"], ["src/a.cs", "src/b.cs"], ["Code", "Code"]),
                ClearJson("no boundary crossed"),
                SmallSelectWorkerJson("do part A"),
                "I made change A.",
                CompletedJson(["src/a.cs"], "added part A"),
                SmallSelectWorkerJson("do part B"),
                "I made change B.",
                CompletedJson(["src/b.cs"], "added part B"));

            var operation = new RouteOperation(
                clearRoot,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "a massive item"],
                output,
                [operation],
                clearRoot,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("route: completed", written, StringComparison.Ordinal),
                () => Assert.Contains("route: decomposed into 2 phase(s)", written, StringComparison.Ordinal),
                () => Assert.Contains("do part A: Succeeded - added part A", written, StringComparison.Ordinal),
                () => Assert.Contains("do part B: Succeeded - added part B", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(clearRoot, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-26 — every generated phase's declared file scope is a strict subset of the file scope already
    ///     cleared for the original item, never equal to it or larger, enforced by a mechanical containment
    ///     check: a phase set that stays inside the cleared scope is allowed to route, and a phase set where one
    ///     phase's scope is not a strict subset of the cleared scope fails closed instead of routing anything.
    ///     Verified by <c>GeneratedPhaseScopeIsStrictSubsetOfClearedScope</c>.
    /// </summary>
    [Fact]
    public async Task GeneratedPhaseScopeIsStrictSubsetOfClearedScope()
    {
        // Scenario 1: every phase's file scope is a strict, proper subset of the cleared scope - the run
        // completes normally.
        var validRoot = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                MassiveSelectWorkerJson(),
                DecomposedJson(["do part A", "do part B"], ["src/a.cs", "src/b.cs"], ["Code", "Code"]),
                ClearJson("no boundary crossed"),
                SmallSelectWorkerJson("do part A"),
                "I made change A.",
                CompletedJson(["src/a.cs"], "added part A"),
                SmallSelectWorkerJson("do part B"),
                "I made change B.",
                CompletedJson(["src/b.cs"], "added part B"));

            var operation = new RouteOperation(
                validRoot,
                endpointFor: _ => endpoint,
                buildRunScript: (_, _) => Task.FromResult(new ScriptRun(0, "all good")));

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                // Changed-file hints declare the cleared scope: exactly the three files below.
                ["route", "a massive item", "src/a.cs", "src/b.cs", "src/c.cs"],
                output,
                [operation],
                validRoot,
                TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("route: completed", output.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(validRoot, recursive: true);
        }

        // Scenario 2: one phase's declared file scope is not a strict subset of the cleared scope (it repeats
        // the whole cleared scope back rather than narrowing it) - the mechanical containment check rejects the
        // phase set before any phase is routed.
        var invalidRoot = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                MassiveSelectWorkerJson(),
                DecomposedJson(
                    ["do everything at once"], ["src/a.cs;src/b.cs;src/c.cs"], ["Code"]),
                ClearJson("no boundary crossed"));

            var operation = new RouteOperation(invalidRoot, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "a massive item", "src/a.cs", "src/b.cs", "src/c.cs"],
                output,
                [operation],
                invalidRoot,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, exitCode),
                () => Assert.Contains("route: failed", written, StringComparison.Ordinal),
                () => Assert.Contains("not a strict subset", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(invalidRoot, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-27 — a phase whose declared file scope touches a protected path forces the same escalation
    ///     outcome TOOLKIT-23 defines, with a recommended next step naming the file, without the run ever asking
    ///     the cumulative check what it concludes for the phase set as a whole. Verified by
    ///     <c>PhaseTouchingProtectedFileForcesEscalation</c>.
    /// </summary>
    [Fact]
    public async Task PhaseTouchingProtectedFileForcesEscalation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            // Only two replies are queued: the route decision and the decomposition. If the run asked the
            // cumulative check oracle before escalating, QueuedEndpoint would report itself unavailable and the
            // run would fail for the wrong reason instead of escalating for this one.
            var endpoint = new QueuedEndpoint(
                MassiveSelectWorkerJson(),
                DecomposedJson(
                    ["do a clean part", "update the direction notes"],
                    ["src/a.cs", "README.md"],
                    ["Code", "Documentation"]));

            var operation = new RouteOperation(root, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "a massive item"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
                () => Assert.Contains("route: escalated", written, StringComparison.Ordinal),
                () => Assert.Contains("README.md", written, StringComparison.Ordinal),
                () => Assert.Contains("update the direction notes", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    ///     TOOLKIT-28 — decomposition recurses through the same Router at most once beyond a Massive item's own
    ///     first decomposition: a depth-two phase that itself classifies Massive escalates instead of decomposing
    ///     further. Verified by <c>SecondLevelMassivePhaseEscalatesInsteadOfDecomposing</c>.
    /// </summary>
    [Fact]
    public async Task SecondLevelMassivePhaseEscalatesInsteadOfDecomposing()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                // Depth 0: the original item is Massive and decomposes into one phase, "phase A".
                MassiveSelectWorkerJson(),
                DecomposedJson(["phase A"], ["src/a.cs;src/a2.cs"], ["Code"]),
                ClearJson("no boundary crossed"),
                // Depth 1: phase A is itself routed, and is itself classified Massive - one decomposition
                // beyond the item's own first is still allowed, so it decomposes into "phase B".
                MassiveSelectWorkerJson(),
                DecomposedJson(["phase B"], ["src/a.cs"], ["Code"]),
                ClearJson("no boundary crossed"),
                // Depth 2: phase B is routed and is also classified Massive - the depth cap of two forbids a
                // third decomposition, so this must escalate rather than ask the decomposition oracle again.
                MassiveSelectWorkerJson());

            var operation = new RouteOperation(root, endpointFor: _ => endpoint);

            var output = new StringWriter();
            var exitCode = await AnnealTool.RunAsync(
                ["route", "a massive item"],
                output,
                [operation],
                root,
                TestContext.Current.CancellationToken);
            var written = output.ToString();

            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitEscalated, exitCode),
                () => Assert.Contains("route: escalated", written, StringComparison.Ordinal),
                () => Assert.Contains("depth cap", written, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string MassiveSelectWorkerJson() =>
        """
        {"kind":"SelectWorker","why":"too large for one unit","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Massive","hasSufficientEvidence":true}
        """;

    private static string SmallSelectWorkerJson(string why) =>
        $$"""
          {"kind":"SelectWorker","why":"{{why}}","workerKey":"small-fix","question":"","researchScope":"Narrow","humanOnlyNextStep":"","effort":"Small","hasSufficientEvidence":true}
          """;

    private static string DecomposedJson(
        IReadOnlyList<string> workItems, IReadOnlyList<string> fileScopes, IReadOnlyList<string> editCategories) =>
        $$"""
          {"kind":"Decomposed","why":"split into narrower phases","phaseWorkItems":[{{string.Join(",", workItems.Select(item => $"\"{item}\""))}}],"phaseFileScopes":[{{string.Join(",", fileScopes.Select(scope => $"\"{scope}\""))}}],"phaseEditCategories":[{{string.Join(",", editCategories.Select(category => $"\"{category}\""))}}],"hasSufficientEvidence":true}
          """;

    private static string ClearJson(string why) =>
        $$"""
          {"kind":"Clear","why":"{{why}}","humanOnlyNextStep":"","hasSufficientEvidence":true}
          """;

    private static string EscalateJson(string why, string humanOnlyNextStep) =>
        $$"""
          {"kind":"Escalate","why":"{{why}}","humanOnlyNextStep":"{{humanOnlyNextStep}}","hasSufficientEvidence":true}
          """;

    private static string CompletedJson(IReadOnlyList<string> filesChanged, string summary) =>
        $$"""
          {"kind":"Completed","why":"","suggestedWorker":"","filesChanged":[{{string.Join(",", filesChanged.Select(file => $"\"{file}\""))}}],"summary":"{{summary}}"}
          """;

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-tk262728-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
