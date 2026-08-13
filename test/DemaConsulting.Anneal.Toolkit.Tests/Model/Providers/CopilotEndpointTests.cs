using GitHub.Copilot;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Model.Providers;

/// <summary>
///     Interior tests for CopilotEndpoint assumptions that mirror GitHub Copilot SDK behavior not exposed as
///     public constants.
/// </summary>
public class CopilotEndpointTests
{
    [Fact]
    public void OverridesBuiltInToolKeyMatchesCopilotSdk()
    {
        // Arrange / Act: exercise the SDK's public override option instead of its internal key constant.
        var probe = CopilotTool.DefineTool(
            (Action)(() => { }),
            new CopilotToolOptions { OverridesBuiltInTool = true });

        // Assert
        Assert.True(
            probe.AdditionalProperties.TryGetValue("is_override", out var value),
            "The GitHub Copilot SDK no longer writes an 'is_override' key when " +
            "CopilotToolOptions.OverridesBuiltInTool is true. Update the OverridesBuiltInToolKey " +
            "constant in CopilotEndpoint.BuiltInToolOverride to match the SDK's new key name.");
        Assert.Equal(true, value);
    }
}
