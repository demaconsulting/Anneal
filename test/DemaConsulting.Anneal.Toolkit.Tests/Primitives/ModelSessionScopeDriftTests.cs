using DemaConsulting.Anneal.Toolkit.Model;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="ModelSession.CheckScopeAsync" />'s four oracle outcomes: aligned, drifted,
///     insufficient-evidence, and model-unavailable.
/// </summary>
/// <remarks>
///     Each test constructs a <see cref="ModelSession" /> with a <see cref="QueuedEndpoint" /> supplying a canned
///     oracle reply, then calls <see cref="ModelSession.CheckScopeAsync" /> directly. No tools are granted because
///     the scope check does not use them; the session's main conversation has one run turn already in it so the
///     oracle has something to read.
/// </remarks>
public class ModelSessionScopeDriftTests
{
    [Fact]
    public async Task CheckScopeAsync_OracleReturnsAligned_ReturnsAlignedTrue()
    {
        // Arrange: one run turn already in the conversation; oracle reports aligned with sufficient evidence
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I did the work.",
                """{"aligned": true, "why": "", "hasSufficientEvidence": true}""");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");
            await session.RunAsync("do the work", role: null, TestContext.Current.CancellationToken);

            // Act
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work", TestContext.Current.CancellationToken);

            // Assert
            Assert.Multiple(
                () => Assert.True(aligned),
                () => Assert.Null(reason));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckScopeAsync_OracleReturnsDrifted_ReturnsAlignedFalseWithReason()
    {
        // Arrange: oracle reports drift with a stated reason
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I did the work.",
                """{"aligned": false, "why": "the pass touched unrelated files", "hasSufficientEvidence": true}""");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");
            await session.RunAsync("do the work", role: null, TestContext.Current.CancellationToken);

            // Act
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work", TestContext.Current.CancellationToken);

            // Assert: drifted, and the oracle's stated reason is surfaced
            Assert.Multiple(
                () => Assert.False(aligned),
                () => Assert.Equal("the pass touched unrelated files", reason));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckScopeAsync_OracleHasInsufficientEvidence_ReturnsAlignedTrue()
    {
        // Arrange: oracle cannot judge because not enough has happened yet (conservative default: keep going)
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                "I did the work.",
                """{"aligned": false, "why": "cannot tell yet", "hasSufficientEvidence": false}""");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");
            await session.RunAsync("do the work", role: null, TestContext.Current.CancellationToken);

            // Act
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work", TestContext.Current.CancellationToken);

            // Assert: insufficient evidence → treat as aligned so the run is not aborted prematurely
            Assert.Multiple(
                () => Assert.True(aligned),
                () => Assert.Null(reason));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckScopeAsync_ModelUnavailable_ReturnsAlignedTrue()
    {
        // Arrange: endpoint exhausted before the scope-check call — model unavailable for the oracle probe.
        // The scope check must not abort the run when it cannot complete; the oracle is a guard, not a gate.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint("I did the work.");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");
            await session.RunAsync("do the work", role: null, TestContext.Current.CancellationToken);

            // Act: no reply queued for the scope-check call
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work", TestContext.Current.CancellationToken);

            // Assert: unavailable oracle is treated as aligned
            Assert.Multiple(
                () => Assert.True(aligned),
                () => Assert.Null(reason));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-scope-drift-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
