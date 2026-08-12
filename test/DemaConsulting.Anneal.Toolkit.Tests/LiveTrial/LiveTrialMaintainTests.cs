using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.LiveTrial;

/// <summary>
///     One real, end-to-end live trial of <c>maintain</c> proving the new operation actually works against a
///     real model: a tiny fixture repo, an obvious in-bound Small-Fix-shaped work item, a real run, and a real
///     grading oracle.
/// </summary>
/// <remarks>
///     Interior test, not a contract test: it lives beside the harness rather than under <c>Contract/</c>, names
///     no clause, and is never linked by <c>check-contracts</c>. It is gated behind
///     <see cref="LiveTrialFixture.GateEnvironmentVariable" /> and skips by default - a plain <c>dotnet test</c>
///     or <c>pwsh ./build.ps1</c> never reaches a real model or a real process here.
/// </remarks>
public sealed class LiveTrialMaintainTests
{
    [Fact]
    public async Task LiveTrial_MaintainHandlesAnObviousInBoundSmallFix_Succeeds()
    {
        // Arrange: skip unless the live-trial gate is explicitly set - this test makes real model calls
        Assert.SkipUnless(
            LiveTrialFixture.GateEnabled,
            $"live trial skipped: set {LiveTrialFixture.GateEnvironmentVariable}=1 to run it against a real model");

        var cancellationToken = TestContext.Current.CancellationToken;

        var passed = false;
        var diagnostic = string.Empty;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await using var fixture = await LiveTrialFixture.CreateAsync(cancellationToken);

            fixture.WriteFile(
                "Arithmetic.txt",
                """
                This file states a simple arithmetic fact.

                Two plus two equals 5.
                """);
            await fixture.CommitAllAsync("seed: add Arithmetic.txt with a wrong sum", cancellationToken);

            var (exitCode, output) = await fixture.RunMaintainAsync(
                "Fix the arithmetic in Arithmetic.txt - \"Two plus two equals 5\" should read " +
                "\"Two plus two equals 4\". This is bounded Maintenance work: touch only Arithmetic.txt.",
                ["Arithmetic.txt"],
                cancellationToken);

            var status = await fixture.GitStatusAsync(cancellationToken);
            var diff = await fixture.GitDiffAsync(cancellationToken);

            var statusLines = status
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Length > 3 ? line[3..] : line)
                .ToArray();

            passed =
                exitCode == AnnealTool.ExitSuccess &&
                output.Contains("maintain: completed", StringComparison.OrdinalIgnoreCase) &&
                diff.Contains("+Two plus two equals 4.", StringComparison.Ordinal) &&
                statusLines.All(path =>
                    string.Equals(path, "Arithmetic.txt", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(".anneal/logs/", StringComparison.OrdinalIgnoreCase));

            diagnostic =
                $"attempt: {attempt}\nmaintain exit code: {exitCode}\nmaintain output:\n{output}\ngit status:\n{status}\ngit diff:\n{diff}";

            if (passed)
                break;
        }

        Assert.True(passed, diagnostic);
    }
}
