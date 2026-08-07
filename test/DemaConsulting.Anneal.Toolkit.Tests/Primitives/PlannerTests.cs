using DemaConsulting.Anneal.Toolkit.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="Planner" />'s basic shape and outcome mapping, including the single-shot
///     property and the disabled/enabled control knob.
/// </summary>
public class PlannerTests
{
    [Fact]
    public async Task PlanAsync_Disabled_RefusesWithoutConsultingAModel()
    {
        // Arrange: a planner explicitly disabled
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var planner = new Planner(root, "a charter", enabled: false, endpointFor: _ => endpoint);

            // Act
            var result = await planner.PlanAsync("plan this", [], TestContext.Current.CancellationToken);

            // Assert: refused, and no model was ever asked
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Refused, result.Outcome),
                () => Assert.Equal(0, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_PlanWithinBudget_SucceedsWithThePlan()
    {
        // Arrange: a reply naming two steps, well within the default budget
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """
                {
                    "kind": "Plan",
                    "why": "",
                    "planSummary": "do the two things",
                    "planSteps": ["do the first thing", "do the second thing"]
                }
                """);
            var planner = new Planner(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await planner.PlanAsync("plan this", [], TestContext.Current.CancellationToken);

            // Assert: succeeded with a Plan carrying both steps - single call, single shot
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<PlanningDecision.Plan>(result.Finding),
                () => Assert.Equal(2, ((PlanningDecision.Plan)result.Finding!).Steps.Steps.Count),
                () => Assert.Equal(1, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_PlanOverBudget_Refuses()
    {
        // Arrange: a reply naming more steps than a one-step budget allows
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """
                {
                    "kind": "Plan",
                    "why": "",
                    "planSummary": "do several things",
                    "planSteps": ["one", "two", "three"]
                }
                """);
            var planner = new Planner(root, "a charter", maxPlanSteps: 1, endpointFor: _ => endpoint);

            // Act
            var result = await planner.PlanAsync("plan this", [], TestContext.Current.CancellationToken);

            // Assert: refused - the answer given was not one this planner may hand back
            Assert.Equal(OperationOutcome.Refused, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_DirectExecutionIsBetter_SucceedsWithTheJudgement()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind": "DirectExecutionIsBetter", "why": "it is one line", "planSummary": "", "planSteps": []}""");
            var planner = new Planner(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await planner.PlanAsync("plan this", [], TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<PlanningDecision.DirectExecutionIsBetter>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_Reroute_SucceedsWithTheJudgement()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"kind": "Reroute", "why": "wrong worker", "planSummary": "", "planSteps": []}""");
            var planner = new Planner(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await planner.PlanAsync("plan this", [], TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.IsType<PlanningDecision.Reroute>(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PlanAsync_NoModelAvailable_Fails()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var planner = new Planner(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await planner.PlanAsync("plan this", [], TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Failed, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-planner-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
