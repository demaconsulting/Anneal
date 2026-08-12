using DemaConsulting.Anneal.Toolkit.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="Verifier" />'s basic shape and outcome mapping, including that a failing
///     deterministic check decides the verdict without a model ever being consulted.
/// </summary>
public class VerifierTests
{
    [Fact]
    public async Task VerifyAsync_FailingDeterministicEvidence_FailsWithNoModelCall()
    {
        // Arrange: a check that already failed, handed in as staged evidence
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);
            var evidence = new[] { new CheckFinding("build", false, 1, "it broke", ["build.ps1"]) };

            // Act
            var result = await verifier.VerifyAsync(
                VerificationIntent.TemplateAudit, evidence, "is this correct?", TestContext.Current.CancellationToken);

            // Assert: Failed, deterministic-first, no model asked at all
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Equal(VerificationVerdict.RepairRequired, result.Finding?.Verdict),
                () => Assert.Equal(0, endpoint.Calls));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_PassingDeterministicEvidenceAndModelPasses_Succeeds()
    {
        // Arrange: deterministic evidence already passing, and a model that agrees
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"verdict": "Passed", "concerns": [], "advisoryNotes": [], "evidenceSufficient": true}""");
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);
            var evidence = new[] { new CheckFinding("build", true, 0, "all good", ["build.ps1"]) };

            // Act
            var result = await verifier.VerifyAsync(
                VerificationIntent.TemplateAudit, evidence, "is this correct?", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_EvidenceInsufficient_Refuses()
    {
        // Arrange: the model honestly reports it cannot judge from what it was given
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"verdict": "Passed", "concerns": [], "advisoryNotes": [], "evidenceSufficient": false}""");
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await verifier.VerifyAsync(
                VerificationIntent.Other, [], "is this correct?", TestContext.Current.CancellationToken);

            // Assert: insufficiency overrides whatever verdict was decoded
            Assert.Equal(OperationOutcome.Refused, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_RerouteRequired_Escalates()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """
                {
                    "verdict": "RerouteRequired",
                    "concerns": [],
                    "advisoryNotes": ["reclassify this change"],
                    "evidenceSufficient": true
                }
                """);
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await verifier.VerifyAsync(
                VerificationIntent.ContractConformance, [], "is this correct?", TestContext.Current.CancellationToken);

            // Assert: only a person can resolve a classification that was wrong underneath the work
            Assert.Equal(OperationOutcome.Escalated, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_RepairRequiredFromModel_Fails()
    {
        // Arrange: no deterministic evidence at all, but the model itself finds a blocking issue
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """
                {
                    "verdict": "RepairRequired",
                    "concerns": [{"owner": "Code", "fixText": "fix the null check"}],
                    "advisoryNotes": [],
                    "evidenceSufficient": true
                }
                """);
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await verifier.VerifyAsync(
                VerificationIntent.TemplateAudit, [], "is this correct?", TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Failed, result.Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_NoModelAvailable_Fails()
    {
        // Arrange
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);

            // Act
            var result = await verifier.VerifyAsync(
                VerificationIntent.TemplateAudit, [], "is this correct?", TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.Null(result.Finding));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_WithDiffEvidence_IncludesDiffBlockInModelQuestion()
    {
        // Arrange: passing deterministic evidence and a diff with a recognizable patch
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"verdict": "Passed", "concerns": [], "advisoryNotes": [], "evidenceSufficient": true}""");
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);
            var evidence = new[] { new CheckFinding("build", true, 0, "all good", ["build.ps1"]) };
            var diff = new DiffFinding(true, ["src/Foo.cs"], "diff --git a/src/Foo.cs ... +DIFF-MARKER");

            // Act
            var result = await verifier.VerifyAsync(
                VerificationIntent.ContractConformance, evidence, "is this correct?",
                TestContext.Current.CancellationToken, diff);

            // Assert: the question sent to the model includes the diff block
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
            var modelText = string.Join("\n", endpoint.Requests[0].Messages.Select(m => m.Text));
            Assert.Multiple(
                () => Assert.Contains("<changed-files>", modelText, StringComparison.Ordinal),
                () => Assert.Contains("DIFF-MARKER", modelText, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_WithUnavailableDiff_OmitsDiffBlockFromModelQuestion()
    {
        // Arrange: the diff reports unavailable; no diff block must reach the model
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"verdict": "Passed", "concerns": [], "advisoryNotes": [], "evidenceSufficient": true}""");
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);
            var diff = new DiffFinding(false, [], "git failed");

            // Act
            var result = await verifier.VerifyAsync(
                VerificationIntent.ContractConformance, [], "is this correct?",
                TestContext.Current.CancellationToken, diff);

            // Assert: no diff block in the model question when the diff was unavailable
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
            var modelText = string.Join("\n", endpoint.Requests[0].Messages.Select(m => m.Text));
            Assert.DoesNotContain("<changed-files>", modelText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyAsync_NullDiffEvidence_OmitsDiffBlockFromModelQuestion()
    {
        // Arrange: null diffEvidence (the default); no diff block must reach the model
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"verdict": "Passed", "concerns": [], "advisoryNotes": [], "evidenceSufficient": true}""");
            var verifier = new Verifier(root, "a charter", endpointFor: _ => endpoint);

            // Act: omit diffEvidence entirely (uses default null)
            var result = await verifier.VerifyAsync(
                VerificationIntent.ContractConformance, [], "is this correct?",
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(OperationOutcome.Succeeded, result.Outcome);
            var modelText = string.Join("\n", endpoint.Requests[0].Messages.Select(m => m.Text));
            Assert.DoesNotContain("<changed-files>", modelText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-verifier-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
