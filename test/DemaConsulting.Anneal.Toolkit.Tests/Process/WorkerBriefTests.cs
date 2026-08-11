using DemaConsulting.Anneal.Toolkit.Primitives;
using DemaConsulting.Anneal.Toolkit.Process.Decomposition;
using DemaConsulting.Anneal.Toolkit.Process.Routing;
using DemaConsulting.Anneal.Toolkit.Process.Workers;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Process;

/// <summary>
///     Interior tests for <see cref="WorkerBrief.FromLedger" />'s deterministic projection: no oracle call, no
///     model call, a pure read of whatever the <see cref="RoutingLedger" /> already holds.
/// </summary>
public class WorkerBriefTests
{
    [Fact]
    public void FromLedger_ProjectsEveryFieldFromTheLedgerUnchanged()
    {
        // Arrange
        var facts = new RepositoryFacts(
            VisionFacts: ["Anneal becomes its own agent CLI"],
            TenetFacts: ["no persistent state outside .anneal/"],
            MigrationPresent: true,
            MigrationCurrentStage: "S8 — the primitive library",
            RelevantArchitectureNodes: ["toolkit.md"],
            ChangedFileHints: ["src/Foo.cs"],
            Implication: RequestImplication.Writing);

        var ledger = new RoutingLedger
        {
            OriginalWorkItem = "fix the flaky test",
            Facts = facts,
            InitialContextArtifacts = []
        };
        ledger.ClassificationHypothesis = "small fix";

        var finding = new ResearchFinding
        {
            Question = "what changed?",
            Answer = "one test flakes under load",
            EvidenceRefs = ["test/FooTests.cs:10"],
            Implications = "needs a small fix",
            SufficientForNextDecision = true
        };
        ledger.ResearchHistory.Add(finding);

        var reroute = new WorkerReroute("contract-change", "needs a contract clause", ["toolkit.md"], "contract-change");
        ledger.WorkerReroutes.Add(reroute);

        // Act
        var brief = WorkerBrief.FromLedger(ledger, "parent-123", "this looks like a small fix");

        // Assert: every field reads back exactly what the ledger held, with no oracle call made
        Assert.Multiple(
            () => Assert.Equal("parent-123", brief.ParentInvocationId),
            () => Assert.Equal("fix the flaky test", brief.OriginalWorkItem),
            () => Assert.Equal("small fix", brief.ClassificationHypothesis),
            () => Assert.Equal([finding], brief.RelevantResearchFindings),
            () => Assert.Equal([reroute], brief.PriorReroutes),
            () => Assert.Equal("this looks like a small fix", brief.ScopeHint),
            () => Assert.Equal(["toolkit.md"], brief.ConstraintRefs),
            () => Assert.Equal(["no persistent state outside .anneal/"], brief.TenetFacts),
            () => Assert.Equal(["src/Foo.cs"], brief.ChangedFileHints));
    }

    [Fact]
    public void FromLedger_NoClassificationHypothesisYet_ProjectsNull()
    {
        // Arrange
        var ledger = new RoutingLedger
        {
            OriginalWorkItem = "look into something",
            Facts = new RepositoryFacts([], [], false, null, [], [], RequestImplication.Unknown),
            InitialContextArtifacts = []
        };

        // Act
        var brief = WorkerBrief.FromLedger(ledger, "parent-456", "unclear yet");

        // Assert
        Assert.Multiple(
            () => Assert.Null(brief.ClassificationHypothesis),
            () => Assert.Empty(brief.RelevantResearchFindings),
            () => Assert.Empty(brief.PriorReroutes));
    }

    [Fact]
    public void FromLedger_NullLedger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WorkerBrief.FromLedger(null!, "parent", "hint"));
    }

    [Fact]
    public void FromLedger_BlankParentInvocationId_Throws()
    {
        var ledger = new RoutingLedger
        {
            OriginalWorkItem = "x",
            Facts = new RepositoryFacts([], [], false, null, [], [], RequestImplication.Unknown),
            InitialContextArtifacts = []
        };

        Assert.Throws<ArgumentException>(() => WorkerBrief.FromLedger(ledger, "  ", "hint"));
    }
}
