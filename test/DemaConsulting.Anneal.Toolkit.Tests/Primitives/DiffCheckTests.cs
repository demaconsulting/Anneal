using DemaConsulting.Anneal.Toolkit.Operations;
using DemaConsulting.Anneal.Toolkit.Primitives;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Primitives;

/// <summary>
///     Interior tests for <see cref="DiffCheck" />'s outcome mapping and file-header parsing. The one primitive
///     besides <see cref="DeterministicCheck" /> with no model call, so these tests inject only a substituted
///     <see cref="RunGitCommand" />.
/// </summary>
public class DiffCheckTests
{
    [Fact]
    public async Task RunAsync_NoBaseRef_DiffsUncommittedWorkAgainstHead()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            IReadOnlyList<string>? seenArguments = null;
            var check = new DiffCheck(
                root,
                runGit: (arguments, _) =>
                {
                    seenArguments = arguments;
                    return Task.FromResult(new ScriptRun(0, ""));
                });

            await check.RunAsync(null, TestContext.Current.CancellationToken);

            Assert.Equal(["diff", "HEAD"], seenArguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_BaseRefGiven_DiffsThreeDotRangeAgainstHead()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            IReadOnlyList<string>? seenArguments = null;
            var check = new DiffCheck(
                root,
                runGit: (arguments, _) =>
                {
                    seenArguments = arguments;
                    return Task.FromResult(new ScriptRun(0, ""));
                });

            await check.RunAsync("main", TestContext.Current.CancellationToken);

            Assert.Equal(["diff", "main...HEAD"], seenArguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_GitExitsZero_ReportsAvailableWithParsedFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            const string patch =
                """
                diff --git a/src/Foo.cs b/src/Foo.cs
                index 1111111..2222222 100644
                --- a/src/Foo.cs
                +++ b/src/Foo.cs
                @@ -1 +1 @@
                -old
                +new
                diff --git a/.anneal/architecture/toolkit.md b/.anneal/architecture/toolkit.md
                index 3333333..4444444 100644
                --- a/.anneal/architecture/toolkit.md
                +++ b/.anneal/architecture/toolkit.md
                @@ -1 +1 @@
                -old doc
                +new doc
                """;

            var check = new DiffCheck(root, runGit: (_, _) => Task.FromResult(new ScriptRun(0, patch)));

            var result = await check.RunAsync(null, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.True(result.Finding?.Available),
                () => Assert.Equal(
                    ["src/Foo.cs", ".anneal/architecture/toolkit.md"], result.Finding?.ChangedFiles),
                () => Assert.Contains("new doc", result.Finding?.Patch));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_GitExitsZeroWithNoOutput_ReportsAvailableWithNoChangedFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var check = new DiffCheck(root, runGit: (_, _) => Task.FromResult(new ScriptRun(0, "")));

            var result = await check.RunAsync(null, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Succeeded, result.Outcome),
                () => Assert.True(result.Finding?.Available),
                () => Assert.Empty(result.Finding?.ChangedFiles ?? []));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_GitExitsNonZero_ReportsUnavailable()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var check = new DiffCheck(
                root, runGit: (_, _) => Task.FromResult(new ScriptRun(128, "fatal: not a git repository")));

            var result = await check.RunAsync(null, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.False(result.Finding?.Available),
                () => Assert.Empty(result.Finding?.ChangedFiles ?? []));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_GitOutlivesItsTimeout_ReportsUnavailable()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var check = new DiffCheck(
                root,
                timeout: TimeSpan.FromMilliseconds(20),
                runGit: async (_, cancellationToken) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    return new ScriptRun(0, "should never get here");
                });

            var result = await check.RunAsync(null, TestContext.Current.CancellationToken);

            Assert.Multiple(
                () => Assert.Equal(OperationOutcome.Failed, result.Outcome),
                () => Assert.False(result.Finding?.Available));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-diff-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
