using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DemaConsulting.Anneal.Toolkit.Model;
using DemaConsulting.Anneal.Toolkit.Model.Providers;
using DemaConsulting.Anneal.Toolkit.Model.Tools;
using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Recording;
using DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;
using DemaConsulting.Anneal.Toolkit.Tests.Primitives;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the probe-rule-owner action, TOOLKIT-04.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{

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
                        .BuildSessionConfig(owned.Reasoning.Requests[0])
                        .ModelCapabilities?.Limits?.MaxOutputTokens));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
