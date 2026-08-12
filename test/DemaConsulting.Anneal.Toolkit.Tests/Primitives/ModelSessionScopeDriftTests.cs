using DemaConsulting.Anneal.Toolkit.Model;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="ModelSession.CheckScopeAsync" />'s oracle outcomes: aligned, drifted,
///     empty-diff (no evidence), and model-unavailable.
/// </summary>
/// <remarks>
///     Each test constructs a <see cref="ModelSession" /> with a <see cref="QueuedEndpoint" /> supplying a canned
///     oracle reply, then calls <see cref="ModelSession.CheckScopeAsync" /> directly with an explicit changed-file
///     list and patch, mirroring what <c>DiffCheck</c> provides at runtime. No tools are granted because the scope
///     check does not use them.
/// </remarks>
public class ModelSessionScopeDriftTests
{
    [Fact]
    public async Task CheckScopeAsync_OracleReturnsAligned_ReturnsAlignedTrue()
    {
        // Arrange: oracle reports aligned with sufficient evidence; one changed file supplied as evidence.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"aligned": true, "why": "", "hasSufficientEvidence": true}""");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");

            // Act
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work",
                ["src/Foo.cs"],
                "diff --git a/src/Foo.cs b/src/Foo.cs\n+// change",
                TestContext.Current.CancellationToken);

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
        // Arrange: oracle reports drift with a stated reason; one changed file supplied as evidence.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"aligned": false, "why": "the pass touched unrelated files", "hasSufficientEvidence": true}""");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");

            // Act
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work",
                ["src/Unrelated.cs"],
                "diff --git a/src/Unrelated.cs b/src/Unrelated.cs\n+// unrelated",
                TestContext.Current.CancellationToken);

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
    public async Task CheckScopeAsync_EmptyChangedFiles_ReturnsAlignedTrue()
    {
        // Arrange: no changed files means no diff evidence — the check returns aligned conservatively
        // without consulting the oracle at all. No endpoint reply is queued because no model call is made.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint();
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");

            // Act: empty changed-file list → short-circuits before the model call
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work",
                [],
                string.Empty,
                TestContext.Current.CancellationToken);

            // Assert: no diff evidence → treat as aligned so the run is not aborted prematurely
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
    public async Task CheckScopeAsync_OracleHasInsufficientEvidence_ReturnsAlignedTrue()
    {
        // Arrange: oracle cannot judge (hasSufficientEvidence = false) — conservative default: keep going.
        var root = CreateTemporaryDirectory();
        try
        {
            var endpoint = new QueuedEndpoint(
                """{"aligned": false, "why": "cannot tell yet", "hasSufficientEvidence": false}""");
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");

            // Act
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work",
                ["src/Foo.cs"],
                "diff --git a/src/Foo.cs b/src/Foo.cs\n+// change",
                TestContext.Current.CancellationToken);

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
            var endpoint = new QueuedEndpoint();
            var roles = new ModelRoles(root, _ => endpoint);
            var session = new ModelSession(roles, "a charter");

            // Act: no reply queued → ModelUnavailableException → treated as aligned
            var (aligned, reason) = await session.CheckScopeAsync(
                "do the work",
                ["src/Foo.cs"],
                "diff --git a/src/Foo.cs b/src/Foo.cs\n+// change",
                TestContext.Current.CancellationToken);

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
