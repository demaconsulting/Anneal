using DemaConsulting.Anneal.Toolkit.Enforcement;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for the contract check as a whole, one per documented failure.
/// </summary>
/// <remarks>
///     Disposable, and deliberately end to end: each case builds a repository exhibiting one failure and
///     asserts the message a reader is told to look for. The messages are what consumers depend on — the
///     skill documentation quotes them — so asserting on a verdict alone would let them drift.
///     <para>
///         Every fixture stamps its results into the near future, because results must post-date the test
///         sources or the staleness check fires and every case fails for the same wrong reason.
///     </para>
/// </remarks>
public class ContractCheckTests
{
    /// <summary>
    ///     A conventional system document: one provided clause and one invariant, plus a Requires subsection
    ///     whose bolded entries deliberately carry no clause identifier.
    /// </summary>
    private const string StandardContract = """
                                            ---
                                            level: system
                                            covers:
                                              - src/Ingest/**
                                            ---

                                            # Ingest

                                            ## Contract

                                            ### Provides

                                            - **INGEST-01** - Accepts records and returns 202 once durably queued.
                                              *Verified by:* `AcceptedRecordIsDurable`

                                            ### Requires

                                            - **Store** - durable append with at-least-once delivery.

                                            ### Invariants

                                            - **INGEST-I1** - Records are queued in arrival order.
                                              *Verified by:* `PreservesPerConnectionOrder`
                                            """;

    private const string StandardTests = """
                                         namespace Ingest.Tests.Contract;

                                         public class IngestContractTests
                                         {
                                             [Fact]
                                             public void AcceptedRecordIsDurable()
                                             {
                                             }

                                             [Fact]
                                             public void PreservesPerConnectionOrder()
                                             {
                                             }
                                         }
                                         """;

    private static readonly (string Name, string Outcome)[] StandardOutcomes =
    [
        ("AcceptedRecordIsDurable", "Passed"),
        ("PreservesPerConnectionOrder", "Passed")
    ];

    /// <summary>
    ///     Validates that a repository whose contract holds is not flagged. Without this, a check that failed
    ///     on everything would satisfy every other case here.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_CleanRepository_Passes()
    {
        // Arrange
        using var repository = Standard();

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Multiple(
            () => Assert.True(report.Passed),
            () => Assert.Empty(report.Warnings),
            () => Assert.Equal(2, report.ClauseCount),
            () => Assert.Equal(2, report.TestLinkCount));
    }

    /// <summary>
    ///     Validates that a system document declaring no contract is reported.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_SystemDocumentWithNoContract_IsReported()
    {
        // Arrange
        using var repository = Standard();
        repository.WriteDocument("store.md", "# Store\n\nNothing promised yet.\n");

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains("store.md: system document has no '## Contract' section", report.Errors);
    }

    /// <summary>
    ///     Validates that an unresolved template placeholder is reported rather than skipped.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_MalformedClauseIdentifier_IsReported()
    {
        // Arrange
        using var repository = Standard();
        repository.WriteDocument(
            "ingest.md",
            StandardContract.Replace("**INGEST-01**", "**SYSTEM-nn**", StringComparison.Ordinal));

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains(
            "ingest.md: 'SYSTEM-nn' under 'Provides' is not a well-formed clause ID " +
            "(expected {SYSTEM}-nn or {SYSTEM}-In)",
            report.Errors);
    }

    /// <summary>
    ///     Validates that the same clause identifier in two documents is reported, since a change to "that
    ///     clause" would otherwise land on whichever the reader happened to find.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_DuplicateClauseIdentifier_IsReported()
    {
        // Arrange: a second system reusing the first system's identifier
        using var repository = Standard();
        repository.WriteDocument(
            "store.md",
            """
            ## Contract

            ### Provides

            - **INGEST-01** - Appends durably.
              *Verified by:* `AcceptedRecordIsDurable`
            """);

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains("Duplicate clause ID 'INGEST-01' in: ingest.md, store.md", report.Errors);
    }

    /// <summary>
    ///     Validates that a clause naming no test is reported, which is the failure the whole check exists to
    ///     catch.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ClauseNamingNoTest_IsReported()
    {
        // Arrange
        using var repository = Standard();
        repository.WriteDocument(
            "ingest.md",
            StandardContract.Replace(
                "  *Verified by:* `AcceptedRecordIsDurable`", string.Empty, StringComparison.Ordinal));

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains("ingest.md: clause INGEST-01 names no verifying test", report.Errors);
    }

    /// <summary>
    ///     Validates that a clause pointing at a test that has been renamed away is reported.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ClauseNamingARenamedTest_IsReported()
    {
        // Arrange
        using var repository = Standard();
        repository.Write(
            "test/Ingest.Tests/Contract/IngestContractTests.cs",
            StandardTests.Replace("AcceptedRecordIsDurable", "AcceptsRecords", StringComparison.Ordinal));
        repository.WriteTrx("artifacts/tests/results.trx", [("AcceptsRecords", "Passed"), StandardOutcomes[1]]);

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains(
            "ingest.md: clause INGEST-01 names test 'AcceptedRecordIsDurable' which is not declared as a " +
            "test method in test, tests",
            report.Errors);
    }

    /// <summary>
    ///     Validates that a clause proved by an interior test is reported, since an interior test may be
    ///     deleted without ceremony and would take the promise with it.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ClausePointingAtAnInteriorTest_IsReported()
    {
        // Arrange: the same tests, moved out of the contract test location
        using var repository = Standard();
        File.Delete(Path.Combine(repository.Root, "test", "Ingest.Tests", "Contract", "IngestContractTests.cs"));
        repository.Write("test/Ingest.Tests/IngestInteriorTests.cs", StandardTests);

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains(
            "ingest.md: clause INGEST-01 names test 'AcceptedRecordIsDurable' which is not in a 'Contract' " +
            "folder (found in IngestInteriorTests.cs) - contract tests must be boundary tests",
            report.Errors);
    }

    /// <summary>
    ///     Validates that a clause whose test most recently failed is reported with the outcome recorded.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ClauseWhoseTestFailed_IsReported()
    {
        // Arrange
        using var repository = Standard();
        repository.WriteTrx(
            "artifacts/tests/results.trx",
            [("AcceptedRecordIsDurable", "Failed"), StandardOutcomes[1]]);

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains(
            "ingest.md: clause INGEST-01 names test 'AcceptedRecordIsDurable' whose most recent result is " +
            "'Failed'",
            report.Errors);
    }

    /// <summary>
    ///     Validates that a declared test with no result is reported as never having run, rather than passing
    ///     because it exists.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_DeclaredTestThatDidNotRun_IsReported()
    {
        // Arrange
        using var repository = Standard();
        repository.WriteTrx("artifacts/tests/results.trx", [StandardOutcomes[1]]);

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains(
            "ingest.md: clause INGEST-01 names test 'AcceptedRecordIsDurable' which has no result - it did " +
            "not run",
            report.Errors);
    }

    /// <summary>
    ///     Validates that results recorded before the tests they claim to describe are rejected, since a stale
    ///     result is worse than an absent one.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_StaleResults_AreRejected()
    {
        // Arrange: results stamped a year before the sources
        using var repository = Standard();
        repository.WriteTrx(
            "artifacts/tests/results.trx",
            StandardOutcomes,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains(
            "Test results are stale: 'IngestContractTests.cs' changed after the newest result matching " +
            "'artifacts/**/*.trx'. Re-run the tests.",
            report.Errors);
    }

    /// <summary>
    ///     Validates that an unfulfilled obligation is a warning while a tree is being bootstrapped, and an
    ///     error once the repository claims to be finished.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_PlannedObligation_WarnsUnlessStrict()
    {
        // Arrange: one clause still carrying a placeholder
        using var repository = Standard();
        repository.WriteDocument(
            "ingest.md",
            StandardContract.Replace(
                "`AcceptedRecordIsDurable`", "`TODO.AcceptedRecordIsDurable`", StringComparison.Ordinal));
        repository.WriteTrx("artifacts/tests/results.trx", [StandardOutcomes[1]]);

        const string message =
            "ingest.md: clause INGEST-01 has an unfulfilled test obligation 'TODO.AcceptedRecordIsDurable'";

        // Act
        var relaxed = ContractCheck.Run(Options(repository));
        var strict = ContractCheck.Run(Options(repository) with { Strict = true });

        // Assert
        Assert.Multiple(
            () => Assert.True(relaxed.Passed),
            () => Assert.Contains(message, relaxed.Warnings),
            () => Assert.False(strict.Passed),
            () => Assert.Contains(message, strict.Errors));
    }

    /// <summary>
    ///     Validates that absent results warn by default and fail under strict checking, so a tree that has
    ///     not been built is not silently reported as verified.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_AbsentResults_WarnUnlessStrict()
    {
        // Arrange: a repository whose tests have never been run
        using var repository = Standard();
        File.Delete(Path.Combine(repository.Root, "artifacts", "tests", "results.trx"));

        const string message =
            "No test results matching 'artifacts/**/*.trx' - run build.ps1 first; pass verification was skipped";

        // Act
        var relaxed = ContractCheck.Run(Options(repository));
        var strict = ContractCheck.Run(Options(repository) with { Strict = true });

        // Assert
        Assert.Multiple(
            () => Assert.True(relaxed.Passed),
            () => Assert.Contains(message, relaxed.Warnings),
            () => Assert.False(strict.Passed),
            () => Assert.Contains(message, strict.Errors));
    }

    /// <summary>
    ///     Validates that a repository with no clauses reports that there is nothing to check, rather than
    ///     failing a repository that has not adopted contracts yet.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_NoClauses_ReportsNothingToCheck()
    {
        // Arrange
        using var repository = new TemporaryRepository();

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Multiple(
            () => Assert.Equal(ContractCheckStage.NothingToCheck, report.Stage),
            () => Assert.True(report.Passed));
    }

    /// <summary>
    ///     Validates that discovery matching nothing is reported once, naming the patterns rather than every
    ///     clause, because the tests exist and the patterns point elsewhere.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_DiscoveryMatchingNothing_IsItsOwnFailure()
    {
        // Arrange: a file pattern that reaches no source
        using var repository = Standard();

        // Act
        var report = ContractCheck.Run(Options(repository, "TestRoots=test;TestFilePatterns=*.fs"));

        // Assert
        Assert.Multiple(
            () => Assert.Contains(
                "No test declarations found in 'test' matching '*.fs' - 2 verifiers need one, so fix the " +
                "discovery patterns rather than the tests. Declaration shape: attribute-marked methods " +
                "(Fact, Theory)",
                report.Errors),
            () => Assert.DoesNotContain(
                report.Errors, error => error.Contains("is not declared", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Validates that discovery pointed at an unknown test root is reported as its own failure, naming
    ///     the root rather than every clause, because the root itself is the thing that is wrong.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_DiscoveryFailsOnAnUnknownTestRoot_IsItsOwnFailure()
    {
        // Arrange: a test root that does not exist
        using var repository = Standard();

        // Act
        var report = ContractCheck.Run(Options(repository, "TestRoots=no-such-directory"));

        // Assert
        Assert.Multiple(
            () => Assert.Contains(
                "No test declarations found in 'no-such-directory' matching '*.cs' - 2 verifiers need one, " +
                "so fix the discovery patterns rather than the tests. Declaration shape: attribute-marked " +
                "methods (Fact, Theory)",
                report.Errors),
            () => Assert.DoesNotContain(
                report.Errors, error => error.Contains("is not declared", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Validates that a tree of planned clauses with no test sources at all is not a discovery failure,
    ///     since a tree being bootstrapped is not expected to resolve anything.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_PlannedClausesWithNoTestSources_IsNotADiscoveryFailure()
    {
        // Arrange: every clause still an obligation, and no tests written yet
        using var repository = new TemporaryRepository();
        repository.WriteDocument(
            "ingest.md",
            """
            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records.
              *Verified by:* `TODO.AcceptedRecordIsDurable`
            """);

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Multiple(
            () => Assert.True(report.Passed),
            () => Assert.DoesNotContain(
                report.Warnings,
                warning => warning.Contains("No test declarations found", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Validates that a clause declared in a section document below the top level is checked, which is
    ///     what lets a system's contract be split across several documents.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ClauseInASectionDocument_IsChecked()
    {
        // Arrange: the invariant moved into a part document
        using var repository = Standard();
        repository.Write(
            ".anneal/architecture/ingest/queueing.md",
            """
            ## Contract

            ### Invariants

            - **INGEST-I2** - Records survive a restart.
              *Verified by:* `SurvivesRestart`
            """);

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Multiple(
            () => Assert.Equal(3, report.ClauseCount),
            () => Assert.Contains(
                "queueing.md: clause INGEST-I2 names test 'SurvivesRestart' which is not declared as a test " +
                "method in test, tests",
                report.Errors));
    }

    /// <summary>
    ///     Validates that two frameworks pool their declarations and results, so a clause is satisfied by
    ///     whichever framework declares its test.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_TwoProfiles_ResolveClausesInBothFrameworks()
    {
        // Arrange: the invariant proved by a named case in a script suite instead
        using var repository = Standard();
        repository.WriteDocument(
            "ingest.md",
            StandardContract.Replace(
                "`PreservesPerConnectionOrder`",
                """`suite.ps1: "preserves per connection order"`""",
                StringComparison.Ordinal));
        repository.Write(
            "test/Ingest.Tests/Contract/IngestContractTests.cs",
            StandardTests.Replace("PreservesPerConnectionOrder", "AcceptsRecords", StringComparison.Ordinal));
        repository.Write("suite.ps1", "Test-Case -Name \"preserves per connection order\"\n");
        repository.WriteTrx("artifacts/tests/results.trx", [StandardOutcomes[0]]);
        repository.WriteTextResults("results/tests.txt", [("preserves per connection order", "Passed")]);

        // Act
        var report = ContractCheck.Run(Options(repository, CSharpProfile, ScriptProfile));

        // Assert
        Assert.Multiple(
            () => Assert.True(report.Passed),
            () => Assert.Empty(report.Warnings));
    }

    /// <summary>
    ///     Validates that a profile matching no declaration is an error even when another profile matched
    ///     plenty, so a mistyped pattern is not covered up by the framework that still works.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ProfileMatchingNoDeclarations_IsReportedAgainstThatProfile()
    {
        // Arrange: the script profile points at a suite this repository does not have
        using var repository = Standard();
        repository.WriteTextResults("results/tests.txt", [("a case", "Passed")]);

        // Act
        var report = ContractCheck.Run(Options(repository, CSharpProfile, ScriptProfile));

        // Assert
        Assert.Contains(
            report.Errors,
            error => error.StartsWith("profile 2: No test declarations found in '.'", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Validates that results missing for one profile are reported against that profile, and say that
    ///     only its own tests are unverified.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ResultsMissingForOneProfile_NameThatProfile()
    {
        // Arrange: the script suite exists but has not been run
        using var repository = Standard();
        repository.Write("suite.ps1", "Test-Case -Name \"a case\"\n");

        // Act
        var report = ContractCheck.Run(Options(repository, CSharpProfile, ScriptProfile));

        // Assert
        Assert.Contains(
            "profile 2: No test results matching 'results/*.txt' - run build.ps1 first; tests recorded by " +
            "this profile cannot be verified",
            report.Warnings);
    }

    /// <summary>
    ///     Validates that profiles combined with the options they replace are rejected outright, and that
    ///     nothing is checked with a partial set.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ProfilesCombinedWithTheOptionsTheyReplace_AreRejected()
    {
        // Arrange
        using var repository = Standard();
        var options = Options(repository, CSharpProfile) with
        {
            SuppliedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TestRoots"] = "test"
            }
        };

        // Act
        var report = ContractCheck.Run(options);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(ContractCheckStage.ProfilesRejected, report.Stage),
            () => Assert.Contains(
                "-TestProfiles cannot be combined with -TestRoots - move those values into a profile record",
                report.Errors),
            () => Assert.Equal(0, report.ClauseCount));
    }

    /// <summary>
    ///     Validates that a clause naming a prefix of a real test's name is not satisfied, since matching was
    ///     once substring-based and let a clause point at a name that merely appeared inside a longer one.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_ClauseNamingAPrefixOfARealTest_IsReported()
    {
        // Arrange: the clause names a prefix of the declared test
        using var repository = Standard();
        repository.WriteDocument(
            "ingest.md",
            StandardContract.Replace("`AcceptedRecordIsDurable`", "`AcceptedRecord`", StringComparison.Ordinal));

        // Act
        var report = ContractCheck.Run(Options(repository));

        // Assert
        Assert.Contains(
            "ingest.md: clause INGEST-01 names test 'AcceptedRecord' which is not declared as a test method in " +
            "test, tests",
            report.Errors);
    }

    /// <summary>
    ///     Validates that a repository which is neither C# nor xUnit is checked through discovery patterns
    ///     alone: named cases in a script suite, discovered by a caller-supplied pattern, with results in the
    ///     text format.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_FixtureCaseRepository_IsCheckedThroughDiscoveryPatterns()
    {
        // Arrange: a suite whose cases are named by a quoted argument, not by an attribute-marked method
        using var repository = new TemporaryRepository();
        repository.WriteDocument(
            "ingest.md",
            """
            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records and returns 202 once durably queued.
              *Verified by:* `suite.ps1: "accepted record is durable"`

            ### Invariants

            - **INGEST-I1** - Records are queued in arrival order.
              *Verified by:* `suite.ps1: "records keep arrival order"`
            """);
        repository.Write(
            "suite.ps1",
            "Test-Case -Name \"accepted record is durable\" -ExpectExit 0\n" +
            "Test-Case -Name \"records keep arrival order\" -ExpectExit 0\n");
        repository.WriteTextResults(
            "results/tests.txt",
            [("accepted record is durable", "Passed"), ("records keep arrival order", "Passed")]);

        // Act
        var report = ContractCheck.Run(Options(repository, ScriptProfile));

        // Assert
        Assert.Multiple(
            () => Assert.True(report.Passed),
            () => Assert.Empty(report.Warnings),
            () => Assert.Equal(2, report.ClauseCount),
            () => Assert.Equal(2, report.TestLinkCount));
    }

    /// <summary>
    ///     Validates that a stale result is rejected in the text format too, since staleness is a property of
    ///     the run rather than of the result file's shape.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_StaleResultInTextFormat_IsRejected()
    {
        // Arrange: a result stamped a year before the suite it describes
        using var repository = new TemporaryRepository();
        repository.WriteDocument(
            "ingest.md",
            """
            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records.
              *Verified by:* `suite.ps1: "accepted record is durable"`
            """);
        repository.Write("suite.ps1", "Test-Case -Name \"accepted record is durable\" -ExpectExit 0\n");
        repository.WriteTextResults(
            "results/tests.txt",
            [("accepted record is durable", "Passed")],
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var report = ContractCheck.Run(Options(repository, ScriptProfile));

        // Assert
        Assert.Contains(
            "Test results are stale: 'suite.ps1' changed after the newest result matching 'results/*.txt'. " +
            "Re-run the tests.",
            report.Errors);
    }

    /// <summary>
    ///     Validates that a failing result in the text format fails its clause, just as a failing TRX result
    ///     does.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_FailingResultInTextFormat_FailsItsClause()
    {
        // Arrange
        using var repository = new TemporaryRepository();
        repository.WriteDocument(
            "ingest.md",
            """
            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records.
              *Verified by:* `suite.ps1: "accepted record is durable"`
            """);
        repository.Write("suite.ps1", "Test-Case -Name \"accepted record is durable\" -ExpectExit 0\n");
        repository.WriteTextResults("results/tests.txt", [("accepted record is durable", "Failed")]);

        // Act
        var report = ContractCheck.Run(Options(repository, ScriptProfile));

        // Assert
        Assert.Contains(
            "ingest.md: clause INGEST-01 names test 'suite.ps1: \"accepted record is durable\"' whose most " +
            "recent result is 'Failed'",
            report.Errors);
    }

    /// <summary>
    ///     Validates that a failing test in the second profile still fails its clause, proving that pooling
    ///     results across profiles did not lose the outcome of the framework that is not the first one.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_FailingTestInTheSecondProfile_FailsItsClause()
    {
        // Arrange: the invariant proved by a named case in a script suite, which failed
        using var repository = Standard();
        repository.WriteDocument(
            "ingest.md",
            StandardContract.Replace(
                "`PreservesPerConnectionOrder`",
                """`suite.ps1: "preserves per connection order"`""",
                StringComparison.Ordinal));
        repository.Write(
            "test/Ingest.Tests/Contract/IngestContractTests.cs",
            StandardTests.Replace("PreservesPerConnectionOrder", "AcceptsRecords", StringComparison.Ordinal));
        repository.Write("suite.ps1", "Test-Case -Name \"preserves per connection order\"\n");
        repository.WriteTrx("artifacts/tests/results.trx", [StandardOutcomes[0]]);
        repository.WriteTextResults("results/tests.txt", [("preserves per connection order", "Failed")]);

        // Act
        var report = ContractCheck.Run(Options(repository, CSharpProfile, ScriptProfile));

        // Assert
        Assert.Contains(
            "ingest.md: clause INGEST-I1 names test 'suite.ps1: \"preserves per connection order\"' whose most " +
            "recent result is 'Failed'",
            report.Errors);
    }

    /// <summary>
    ///     Validates that staleness is judged within a profile rather than across them, so a stale script tally
    ///     is rejected even while the C# profile's TRX is fresh.
    /// </summary>
    [Fact]
    public void ContractCheck_Run_StaleResultInOneProfile_IsRejectedWhileTheOtherIsFresh()
    {
        // Arrange: the script profile's result predates the suite it describes; the C# profile's does not
        using var repository = Standard();
        repository.WriteDocument(
            "ingest.md",
            StandardContract.Replace(
                "`PreservesPerConnectionOrder`",
                """`suite.ps1: "preserves per connection order"`""",
                StringComparison.Ordinal));
        repository.Write(
            "test/Ingest.Tests/Contract/IngestContractTests.cs",
            StandardTests.Replace("PreservesPerConnectionOrder", "AcceptsRecords", StringComparison.Ordinal));
        repository.Write("suite.ps1", "Test-Case -Name \"preserves per connection order\"\n");
        repository.WriteTrx("artifacts/tests/results.trx", [StandardOutcomes[0]]);
        repository.WriteTextResults(
            "results/tests.txt",
            [("preserves per connection order", "Passed")],
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var report = ContractCheck.Run(Options(repository, CSharpProfile, ScriptProfile));

        // Assert: profile 2 (the stale script tally) is rejected, while profile 1 (the fresh TRX) is not also
        // reported stale - proving staleness is judged per profile rather than across the whole repository
        Assert.Multiple(
            () => Assert.Contains(
                "profile 2: Test results are stale: 'suite.ps1' changed after the newest result matching " +
                "'results/*.txt'. Re-run the tests.",
                report.Errors),
            () => Assert.DoesNotContain(
                report.Errors,
                error => error.StartsWith("profile 1: Test results are stale", StringComparison.Ordinal)));
    }

    private const string CSharpProfile =
        "TestRoots=test;TestFilePatterns=*.cs;ContractTestFolder=Contract;" +
        "TestResults=artifacts/tests/*.trx;TestResultFormat=trx";

    private const string ScriptProfile =
        """TestRoots=.;TestFilePatterns=*.ps1;TestDeclarationPattern=^\s*Test-Case\s+-Name\s+"(?<name>[^"]+)";""" +
        "ContractTestFolder=;TestResults=results/*.txt;TestResultFormat=text";

    /// <remarks>
    ///     The conventional repository every case starts from: a contract, the boundary tests proving it, and
    ///     a fresh result recording that both passed.
    /// </remarks>
    private static TemporaryRepository Standard()
    {
        var repository = new TemporaryRepository();
        repository.WriteDocument("ingest.md", StandardContract);
        repository.Write("test/Ingest.Tests/Contract/IngestContractTests.cs", StandardTests);
        repository.WriteTrx("artifacts/tests/results.trx", StandardOutcomes);
        return repository;
    }

    private static ContractCheckOptions Options(TemporaryRepository repository, params string[] records) =>
        new() { RepositoryRoot = repository.Root, ProfileRecords = records };
}
