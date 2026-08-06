using DemaConsulting.Anneal.Toolkit.Model.Tools;
using DemaConsulting.Anneal.Toolkit.Operations;
using Microsoft.Extensions.AI;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Tools;

/// <summary>
///     Interior tests for the tool surface: the behavior that is real but is not what a contract clause
///     promises.
/// </summary>
/// <remarks>
///     <c>TOOLKIT-I6</c> promises containment, group scoping and the protected-path refusal, and its contract
///     test proves exactly that and no more. What is here is everything else these types actually do — that a
///     create refuses an existing file, that an ambiguous targeted edit is refused rather than resolved to the
///     first match, that the containment primitive never throws — which is worth testing and would be a
///     widening of the clause if it were tested beside it.
/// </remarks>
public class RepositoryToolsTests
{
    [Theory]
    [InlineData("docs/architecture/toolkit.md")]
    [InlineData("./docs/toolkit.md")]
    [InlineData(@"docs\toolkit.md")]
    [InlineData("docs/sub/../toolkit.md")]
    [InlineData(".")]
    public void ContainedPathsResolve(string path)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Assert.True(RepositoryPath.TryResolve(root, path, out var full));
            Assert.NotNull(full);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("..")]
    [InlineData("../escape.txt")]
    [InlineData("a/../../escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\?\C:\file.txt")]
    [InlineData("bad\0name.txt")]
    [InlineData("fix.ps1::$DATA")]
    [InlineData(@"docs\toolkit.md:hidden")]
    public void PathsThatEscapeAreRefusedWithoutThrowing(string? path)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Assert.False(RepositoryPath.TryResolve(root, path, out var full));
            Assert.Null(full);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>
    ///     The root is a contained path and is not a file, so the two resolvers answer differently about it.
    ///     A write that took the root would surface as an opaque I/O error rather than as something a model can
    ///     act on.
    /// </remarks>
    [Fact]
    public void TheRootItselfResolvesButIsNotAFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Assert.Multiple(
                () => Assert.True(RepositoryPath.TryResolve(root, string.Empty, out _)),
                () => Assert.True(RepositoryPath.TryResolve(root, ".", out _)),
                () => Assert.False(RepositoryPath.TryResolveFile(root, string.Empty, out _)),
                () => Assert.False(RepositoryPath.TryResolveFile(root, ".", out _)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>
    ///     The defect the lexical check exists to avoid: a directory beside the repository whose name starts
    ///     with the repository's own is not inside it, however much its text suggests otherwise.
    /// </remarks>
    [Fact]
    public void ASiblingSharingATextualPrefixIsNotInsideTheRoot()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sibling = "../" + Path.GetFileName(root) + "-sibling/file.txt";
            Assert.False(RepositoryPath.TryResolve(root, sibling, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ANestedFileSharingAProtectedNameIsOrdinaryContent() =>
        Assert.Multiple(
            () => Assert.True(ProtectedPaths.IsProtected("fix.ps1")),
            () => Assert.True(ProtectedPaths.IsProtected("./fix.ps1")),
            () => Assert.True(ProtectedPaths.IsProtected("FIX.PS1")),
            () => Assert.False(ProtectedPaths.IsProtected("samples/fix.ps1")),
            () => Assert.False(ProtectedPaths.IsProtected("docs/.editorconfig")),
            () => Assert.False(ProtectedPaths.IsProtected(null)));

    [Fact]
    public void CreateRefusesAnExistingFileAndReplaceRefusesAMissingOne()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "present.md"), "original");

            Assert.Multiple(
                () => Assert.Contains("already exists", Create(root, "present.md", "new"), StringComparison.Ordinal),
                () => Assert.Contains("does not exist", Replace(root, "absent.md", "new"), StringComparison.Ordinal),
                () => Assert.Equal("original", File.ReadAllText(Path.Combine(root, "present.md"))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <remarks>
    ///     A model that supplied too little context is asking to edit a place it has not identified. Picking the
    ///     first match for it is how a targeted edit silently lands somewhere else.
    /// </remarks>
    [Fact]
    public void AnAmbiguousTargetedEditIsRefusedRatherThanResolved()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "twice.md");
            File.WriteAllText(path, "a line\nand a line\n");

            var reply = Edit(root, "twice.md", "a line", "one line");

            Assert.Multiple(
                () => Assert.Contains("exactly once", reply, StringComparison.Ordinal),
                () => Assert.Equal("a line\nand a line\n", File.ReadAllText(path)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ContractFailuresAreFoundStructurallyInLintOutput()
    {
        var output = string.Join(
            Environment.NewLine,
            "Checking: markdown...",
            "  README.md:12 MD013 line too long",
            "Checking: system contracts...",
            "  error: TOOLKIT-19 names a test that does not exist",
            "  error: TOOLKIT-20 names a test that did not run",
            "Checking: spelling...",
            "  error: this one belongs to spelling, not to contracts");

        var failures = LintFixOperation.ContractFailuresIn(output);

        Assert.Multiple(
            () => Assert.Equal(2, failures.Count),
            () => Assert.All(failures, failure => Assert.StartsWith("TOOLKIT-", failure, StringComparison.Ordinal)));
    }

    [Fact]
    public void LintOutputWithNoContractBlockYieldsNoFailures() =>
        Assert.Empty(LintFixOperation.ContractFailuresIn("Checking: markdown...\n  README.md:12 MD013"));

    private static string Create(string root, string path, string content) =>
        InvokeTool(root, "create_file", new Dictionary<string, object?> { ["path"] = path, ["content"] = content });

    private static string Replace(string root, string path, string content) =>
        InvokeTool(root, "replace_file", new Dictionary<string, object?> { ["path"] = path, ["content"] = content });

    private static string Edit(string root, string path, string oldStr, string newStr) =>
        InvokeTool(
            root,
            "edit_file",
            new Dictionary<string, object?> { ["path"] = path, ["oldStr"] = oldStr, ["newStr"] = newStr });

    private static string InvokeTool(string root, string name, Dictionary<string, object?> arguments)
    {
        var tool = RepositoryEditTools.CreateAll(root)
            .OfType<AIFunction>()
            .First(candidate => candidate.Name == name);

        return tool
            .InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult()
            ?.ToString() ?? string.Empty;
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-tools-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }
}
