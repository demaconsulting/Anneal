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
///     Boundary tests for the verify-evidence action, TOOLKIT-03.
/// </summary>
/// <remarks>
///     Split out of <see cref="ToolkitContractTests" /> by topic; shared fields and helpers live there.
/// </remarks>
public partial class ToolkitContractTests
{

    /// <summary>
    ///     TOOLKIT-03 — verify-evidence reports, for each locator cited in a report, whether the quoted text
    ///     is at the file and line named, reaching no verdict about the report's own conclusion.
    /// </summary>
    [Fact]
    public async Task EvidenceLocatorsAreCheckedAgainstSource()
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
            var honestExit = await AnnealTool.RunAsync(
                ["verify-evidence", honest], honestOutput, operations, TestContext.Current.CancellationToken);

            var wrongOutput = new StringWriter();
            var wrongExit = await AnnealTool.RunAsync(
                ["verify-evidence", wrong], wrongOutput, operations, TestContext.Current.CancellationToken);
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
}
