using DemaConsulting.Anneal.Toolkit.Operations;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the Toolkit contract in <c>docs/architecture/toolkit.md</c>.
/// </summary>
/// <remarks>
///     Everything here goes through the same surface a caller has: the action name is passed to
///     <see cref="AnnealTool.Run(IReadOnlyList{string}, TextWriter)" /> and the assertions are on the exit
///     code and the written output. The operation set is injected where a clause is about the dispatcher
///     rather than about a shipped action, because a rule stated over categories cannot be proven by the one
///     category that happens to ship today.
/// </remarks>
public class ToolkitContractTests
{
    /// <summary>
    ///     TOOLKIT-01 — an unrecognized action exits non-zero and lists the actions that exist, so a caller
    ///     discovers the surface without reading the source.
    /// </summary>
    [Fact]
    public void UnknownActionListsAvailableActions()
    {
        // Arrange: a caller who has named an action this tool does not have
        var output = new StringWriter();

        // Act: the action is named first, as "dotnet anneal <action>"
        var exitCode = AnnealTool.Run(["no-such-action"], output);
        var written = output.ToString();

        // Assert: non-zero, and every shipped action is discoverable from the output alone
        Assert.Multiple(
            () => Assert.NotEqual(0, exitCode),
            () => Assert.Contains("unknown action 'no-such-action'", written, StringComparison.Ordinal),
            () => Assert.NotEmpty(AnnealTool.DefaultOperations),
            () => Assert.All(
                AnnealTool.DefaultOperations,
                operation => Assert.Contains(operation.Name, written, StringComparison.Ordinal)));
    }

    /// <summary>
    ///     TOOLKIT-02 — the declared category alone decides whether a non-zero exit gates a build, and only
    ///     enforcement gates.
    /// </summary>
    [Fact]
    public void OnlyEnforcementOperationsGate()
    {
        // Arrange: the same failure, declared under each category in turn
        var categories = Enum.GetValues<OperationCategory>();

        // Act: run each one, plus a succeeding enforcement operation as the control
        var failingExitCodes = categories.ToDictionary(
            category => category,
            category => RunStub(category, OperationOutcome.Failed));
        var succeedingEnforcement = RunStub(OperationCategory.Enforcement, OperationOutcome.Succeeded);

        // Assert: identical failures gate or not purely by category, and success never gates
        Assert.Multiple(
            () => Assert.Equal(AnnealTool.ExitGatedFailure, failingExitCodes[OperationCategory.Enforcement]),
            () => Assert.All(
                categories.Where(category => category != OperationCategory.Enforcement),
                category => Assert.Equal(AnnealTool.ExitSuccess, failingExitCodes[category])),
            () => Assert.Equal(AnnealTool.ExitSuccess, succeedingEnforcement));
    }

    /// <summary>
    ///     TOOLKIT-03 — verify-evidence reports, for each locator cited in a report, whether the quoted text
    ///     is at the file and line named, reaching no verdict about the report's own conclusion.
    /// </summary>
    [Fact]
    public void EvidenceLocatorsAreCheckedAgainstSource()
    {
        // Arrange: a source file, and a report citing one locator that holds and one that does not
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllLines(
                Path.Combine(root, "subject.txt"),
                ["first line", "the promise this cites", "third line"]);

            var honest = WriteReport(root, "honest.md", "`subject.txt:2` - \"the promise this cites\"");
            var wrong = WriteReport(
                root,
                "wrong.md",
                "`subject.txt:2` - \"the promise this cites\"",
                "`subject.txt:3` - \"the promise this cites\"",
                "`absent.txt:1` - \"never written\"");

            var operations = new[] { (IOperation)new VerifyEvidenceOperation(root) };

            // Act: check both reports through the command surface
            var honestOutput = new StringWriter();
            var honestExit = AnnealTool.Run(["verify-evidence", honest], honestOutput, operations);

            var wrongOutput = new StringWriter();
            var wrongExit = AnnealTool.Run(["verify-evidence", wrong], wrongOutput, operations);
            var wrongWritten = wrongOutput.ToString();

            // Assert: each locator is reported individually, and nothing is said about the report's verdict
            Assert.Multiple(
                () => Assert.Equal(AnnealTool.ExitSuccess, honestExit),
                () => Assert.Contains(
                    "present  subject.txt:2 \"the promise this cites\"",
                    honestOutput.ToString(),
                    StringComparison.Ordinal),
                () => Assert.Contains(
                    "1 locators: 1 present, 0 absent.",
                    honestOutput.ToString(),
                    StringComparison.Ordinal),
                () => Assert.NotEqual(AnnealTool.ExitSuccess, wrongExit),
                () => Assert.Contains(
                    "present  subject.txt:2",
                    wrongWritten,
                    StringComparison.Ordinal),
                () => Assert.Contains(
                    "absent   subject.txt:3 \"the promise this cites\" - line 3 does not contain",
                    wrongWritten,
                    StringComparison.Ordinal),
                () => Assert.Contains(
                    "absent   absent.txt:1 \"never written\" - file not found",
                    wrongWritten,
                    StringComparison.Ordinal),
                () => Assert.Contains("3 locators: 1 present, 2 absent.", wrongWritten, StringComparison.Ordinal),
                () => Assert.DoesNotContain("SUCCEEDED", wrongWritten, StringComparison.Ordinal),
                () => Assert.DoesNotContain("verdict", wrongWritten, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int RunStub(OperationCategory category, OperationOutcome outcome) =>
        AnnealTool.Run(["stub"], new StringWriter(), [new StubOperation(category, outcome)]);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "anneal-toolkit-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <returns>The path of the written report, relative to the root the operation resolves against.</returns>
    private static string WriteReport(string root, string name, params string[] citations)
    {
        File.WriteAllLines(
            Path.Combine(root, name),
            ["**Result**: SUCCEEDED", "", .. citations]);
        return name;
    }

    /// <remarks>
    ///     Stands in for a real operation so that the gating rule can be exercised for every category,
    ///     including the three no shipped operation currently declares.
    /// </remarks>
    private sealed class StubOperation(OperationCategory category, OperationOutcome outcome) : IOperation
    {
        public string Name => "stub";

        public OperationCategory Category => category;

        public string Summary => "Reports a fixed outcome under a fixed category";

        public OperationOutcome Execute(IReadOnlyList<string> arguments, TextWriter output) => outcome;
    }
}
