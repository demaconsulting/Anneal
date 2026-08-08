using System.Diagnostics;
using System.Text;
using DemaConsulting.TestResults;
using DemaConsulting.TestResults.IO;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.Contract;

/// <summary>
///     Boundary tests for the contract check, one per documented failure mode, that spawn the real, installed
///     <c>dotnet anneal check-contracts</c> against a throw-away fixture repository and assert on its exit
///     code and output.
/// </summary>
/// <remarks>
///     This is the compiled successor to <c>test-check-contracts.ps1</c>. Nothing about the boundary being
///     verified moved: both drive the packaged tool as a real subprocess against fixture repositories built
///     outside the repository's own architecture tree. Fixtures are written under this repository's own
///     <c>artifacts/check-contract-fixtures</c> - not the OS temp directory - so that <c>dotnet anneal</c>
///     resolves its tool manifest by walking up from the fixture's working directory to this repository's
///     root, the same technique <c>test-check-contracts.ps1</c> used. Deleted after each case whether it
///     passed or failed; a leftover fixture is litter, not evidence.
///     <para>
///         The in-process coverage under <c>ContractChecking/</c> exercises the same parsing and
///         reconciliation logic directly and is not a substitute for this: only spawning the packaged,
///         installed tool proves that the compiled entry point, its argument parsing, and its exit code
///         actually behave the way the skill documentation promises.
///     </para>
/// </remarks>
public class CheckContractsSubprocessTests
{
    /// <summary>
    ///     The arguments a fixture-case repository needs: its tests are named cases in a flat file at the
    ///     repository root, and its results are not TRX.
    /// </summary>
    private static readonly string[] FixtureCaseArguments =
    [
        "-TestRoots", ".",
        "-TestFilePatterns", "*.suite",
        "-TestDeclarationPattern", """"^\s*Test-Case\s+-Name\s+"(?<name>[^"]+)"""",
        "-ContractTestFolder", "",
        "-TestResults", "results/" + "*.txt",
        "-TestResultFormat", "text"
    ];

    /// <summary>
    ///     A discovery profile record describing a repository holding C# boundary tests recorded in TRX -
    ///     the shape the CONTRACT-CHECK defaults already describe.
    /// </summary>
    private const string CSharpProfile =
        "TestRoots=test;TestFilePatterns=" + "*.cs;ContractTestFolder=Contract;TestResults=artifacts/tests/" + "*.trx;TestResultFormat=trx";

    /// <summary>
    ///     A discovery profile record describing a repository holding a flat, fixture-case suite recording a
    ///     text tally - paired against <see cref="CSharpProfile" /> to prove a single run resolves clauses in
    ///     both languages at once.
    /// </summary>
    private const string ScriptProfile =
        """TestRoots=.;TestFilePatterns=suite.suite;TestDeclarationPattern=^\s*Test-Case\s+-Name\s+"(?<name>[^"]+)";ContractTestFolder=;TestResults=results/""" + "*.txt;TestResultFormat=text";

    /// <summary>
    ///     A conventional system document: two provided clauses and one invariant, plus a Requires subsection
    ///     whose bolded entry deliberately carries no clause ID.
    /// </summary>
    private const string StandardContract =
        """
        ---
        level: system
        covers:
          - src/Ingest.cs
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

        ## Decisions

        Nothing yet.
        """;

    private const string StandardTests =
        """
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

    // ==========================================================================================
    // CONTRACT-CHECK-01
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-01 - a repository whose clauses all name existing, passing tests is not flagged.
    ///     Without this, a checker that failed on everything would score perfectly against every other case
    ///     here.
    /// </summary>
    [Fact]
    public void CleanRepositoryPasses()
    {
        // Arrange: a repository whose contract holds completely
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 0, expect: ["2 clauses, 2 test links checked."], reject: ["error:", "warning:"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-02
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-02 - a level-2 system document with no <c>## Contract</c> section is rejected.
    /// </summary>
    [Fact]
    public void SystemDocumentWithNoContractSectionIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", "# Ingest\n\n## Decisions\n\nNothing.\n");

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["has no '## Contract' section"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-03 / CONTRACT-CHECK-I1
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-03, CONTRACT-CHECK-I1 - an unresolved template placeholder is not a well-formed
    ///     clause ID, and parsing an input it cannot understand is rejected rather than silently skipped.
    /// </summary>
    [Fact]
    public void UnresolvedPlaceholderIsNotAWellFormedId()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            "# Ingest\n\n## Contract\n\n### Provides\n\n- **{SYSTEM}-01** - unresolved placeholder.\n");

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["is not a well-formed clause ID"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-04
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-04 - the same clause identifier appearing in two documents is rejected.
    /// </summary>
    [Fact]
    public void DuplicateClauseIdAcrossTwoDocumentsIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteDocument("store.md", StandardContract.Replace("Ingest", "Store", StringComparison.Ordinal));
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["Duplicate clause ID 'INGEST-01'"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-05
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-05 - a clause that names no verifying test is rejected.
    /// </summary>
    [Fact]
    public void ClauseNamingNoVerifyingTestIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            "# Ingest\n\n## Contract\n\n### Provides\n\n- **INGEST-01** - a promise with no verification.\n");

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["names no verifying test"]);
    }

    /// <summary>
    ///     A clause naming a test that no longer exists is rejected, because it was renamed away from the
    ///     source rather than never having existed - the common case CONTRACT-CHECK-06 also covers.
    /// </summary>
    [Fact]
    public void ClauseNamingATestThatWasRenamedAwayIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(
            StandardTests.Replace("AcceptedRecordIsDurable", "RenamedAwayFromTheClause", StringComparison.Ordinal));

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["which is not declared as a test method"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-06
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-06 - a clause naming a test surviving only inside a comment is rejected: comments
    ///     are stripped before matching, so a commented-out declaration cannot keep a deleted promise alive.
    ///     The commented lines sit directly beneath a real attribute, which is the case that would otherwise
    ///     resolve - a doc comment naming the clause's test is the common form.
    /// </summary>
    [Fact]
    public void TestSurvivingOnlyInACommentDoesNotSatisfyAClause()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(
            """
            namespace Ingest.Tests.Contract;

            public class IngestContractTests
            {
                [Fact]
                // public void AcceptedRecordIsDurable()
                public void SomethingElseEntirely()
                {
                }

                [Fact]
                /* public void PreservesPerConnectionOrder() */
                public void AlsoSomethingElse()
                {
                }
            }
            """);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(
            result,
            1,
            expect:
            [
                "clause INGEST-01 names test 'AcceptedRecordIsDurable' which is not declared as a test method",
                "clause INGEST-I1 names test 'PreservesPerConnectionOrder' which is not declared as a test method"
            ]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-07
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-07 - a clause verified by an interior test rather than a boundary test is rejected.
    /// </summary>
    [Fact]
    public void ClausePointingAtAnInteriorTestIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(
            StandardTests.Replace("AcceptedRecordIsDurable", "PlaceholderTest", StringComparison.Ordinal));
        fixture.Write(
            "test/Ingest.Tests/InteriorTests.cs",
            """
            namespace Ingest.Tests;

            public class InteriorTests
            {
                [Fact]
                public void AcceptedRecordIsDurable()
                {
                }
            }
            """);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(
            result, 1, expect: ["is not in a 'Contract' folder", "contract tests must be boundary tests"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-08
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-08 - a clause whose named test most recently failed is rejected under -Strict's
    ///     default (already enforced without it, since a fresh failure is never tolerated).
    /// </summary>
    [Fact]
    public void ClauseWhoseTestMostRecentlyFailedIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Failed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["whose most recent result is 'Failed'"]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-08 - a clause whose named test is declared but never ran is rejected: a result that
    ///     is merely absent is a distinct failure from one that failed.
    /// </summary>
    [Fact]
    public void DeclaredTestThatDidNotRunIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx("artifacts/tests/results.trx", ["AcceptedRecordIsDurable=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["which has no result - it did not run"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-09
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-09 - results older than the test sources they describe are rejected, so a stale
    ///     passing run cannot vouch for current code.
    /// </summary>
    [Fact]
    public void StaleResultsAreRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"],
            DateTime.UtcNow.AddDays(-2));

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["Test results are stale"]);
    }

    /// <summary>
    ///     The newest result for a test wins: merging passes across result files would let an old run keep a
    ///     currently failing clause green.
    /// </summary>
    [Fact]
    public void OlderPassingRunCannotVouchForANewerFailure()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/old.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"],
            DateTime.UtcNow.AddMinutes(4));
        fixture.WriteTrx(
            "artifacts/tests/new.trx",
            ["AcceptedRecordIsDurable=Failed", "PreservesPerConnectionOrder=Passed"],
            DateTime.UtcNow.AddMinutes(9));

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["whose most recent result is 'Failed'"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-I2
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-I2 - a single failing case within a data-driven test fails the clause it verifies,
    ///     so a failing case is not masked by a passing sibling.
    /// </summary>
    [Fact]
    public void OneFailingDataDrivenCaseFailsTheClause()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            [
                "AcceptedRecordIsDurable(size: 1)=Failed",
                "AcceptedRecordIsDurable(size: 2)=Passed",
                "PreservesPerConnectionOrder=Passed"
            ]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 1, expect: ["whose most recent result is 'Failed'"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-10
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-10 - a planned obligation (verifier opening with the placeholder form) is a warning
    ///     by default, not an error.
    /// </summary>
    [Fact]
    public void TodoObligationIsAWarningByDefault()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            StandardContract.Replace("`AcceptedRecordIsDurable`", "`TODO_AcceptedRecordIsDurable`", StringComparison.Ordinal));
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx("artifacts/tests/results.trx", ["PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 0, expect: ["warning: ", "unfulfilled test obligation"], reject: ["error:"]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-10 - a planned obligation is an error under -Strict. One repository proves both
    ///     halves at once, because a clause can name only one verifier: the placeholder form IS reported (and
    ///     is an error under -Strict), and a verifier that is near-miss but not the placeholder form is NOT.
    ///     Each of the three near-miss witnesses differs from <c>TODO_</c> at the start in exactly one
    ///     dimension, so each one alone would become an obligation if the detector lost that dimension:
    ///     <c>Todo_ItemsAreReturned</c> (right shape, wrong case), <c>List_TODO_Items</c> (right shape, not at
    ///     the start), and <c>TODOItemsAreReturned</c> (uppercase TODO at the start with no separator). All
    ///     three are genuine, declared, passing tests and must be checked normally, so none of the names may
    ///     appear in the output at all.
    /// </summary>
    [Fact]
    public void PlannedObligationIsAnErrorUnderStrict()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            """
            ---
            level: system
            covers:
              - src/Ingest.cs
            ---

            # Ingest

            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records and returns 202 once durably queued.
              *Verified by:* `TODO_AcceptedRecordIsDurable`

            - **INGEST-02** - Returns the outstanding work queue.
              *Verified by:* `Todo_ItemsAreReturned`

            ### Invariants

            - **INGEST-I1** - Records are queued in arrival order.
              *Verified by:* `List_TODO_Items`

            - **INGEST-I2** - The queue is never reordered after acceptance.
              *Verified by:* `TODOItemsAreReturned`

            ## Decisions

            Nothing yet.
            """);
        fixture.WriteContractTests(
            """
            namespace Ingest.Tests.Contract;

            public class IngestContractTests
            {
                [Fact]
                public void Todo_ItemsAreReturned()
                {
                }

                [Fact]
                public void List_TODO_Items()
                {
                }

                [Fact]
                public void TODOItemsAreReturned()
                {
                }
            }
            """);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["Todo_ItemsAreReturned=Passed", "List_TODO_Items=Passed", "TODOItemsAreReturned=Passed"]);

        // Act
        var result = Run(fixture, strict: true);

        // Assert
        AssertResult(
            result,
            1,
            expect: ["4 clauses, 4 test links checked.", "error: ", "unfulfilled test obligation 'TODO_AcceptedRecordIsDurable'"],
            reject: ["Todo_ItemsAreReturned", "List_TODO_Items", "TODOItemsAreReturned"]);
    }

    /// <summary>
    ///     End-to-end shape check, not the discrimination proof: a genuine failing test whose name merely
    ///     contains "Todo" is reported as a failure rather than excused as an obligation. What makes the
    ///     detector case-sensitive, anchored and separator-bearing is proven by the three near-miss witnesses
    ///     in <see cref="PlannedObligationIsAnErrorUnderStrict" />.
    /// </summary>
    [Fact]
    public void GenuineTestNamedTodoIsCheckedNormally()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            StandardContract.Replace("`AcceptedRecordIsDurable`", "`TodoItemsAreReturned`", StringComparison.Ordinal));
        fixture.WriteContractTests(
            StandardTests.Replace("AcceptedRecordIsDurable", "TodoItemsAreReturned", StringComparison.Ordinal));
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["TodoItemsAreReturned=Failed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(
            result, 1, expect: ["whose most recent result is 'Failed'"], reject: ["unfulfilled test obligation"]);
    }

    /// <summary>
    ///     The obligation marker is the placeholder form, not the word: a genuine fixture case actually named
    ///     "TODO obligation is an error", declared and passing, is a real verifier - matching the resolved
    ///     name, or matching the word anywhere, would exempt it from the only enforced check in the process.
    /// </summary>
    [Fact]
    public void GenuineFixtureCaseNamedTodoIsCheckedNormally()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            """
            # Ingest

            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records.
              *Verified by:* `suite.suite: "TODO obligation is an error"`
            """);
        fixture.Write("suite.suite", """Test-Case -Name "TODO obligation is an error" -ExpectExit 1""");
        fixture.WriteTextResults("results/tests.txt", ["TODO obligation is an error=Passed"]);

        // Act
        var result = Run(fixture, FixtureCaseArguments, strict: true);

        // Assert
        AssertResult(
            result,
            0,
            expect: ["1 clauses, 1 test links checked."],
            reject: ["error:", "warning:", "unfulfilled test obligation"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-08 (absent results)
    // ==========================================================================================

    /// <summary>
    ///     Absent test results are a warning by default rather than an error, so a repository whose tests
    ///     have not yet been run is not gated on that alone.
    /// </summary>
    [Fact]
    public void AbsentResultsWarnByDefault()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 0, expect: ["No test results matching"], reject: ["error:"]);
    }

    /// <summary>
    ///     Absent test results are an error under -Strict.
    /// </summary>
    [Fact]
    public void AbsentResultsAreAnErrorUnderStrict()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);

        // Act
        var result = Run(fixture, strict: true);

        // Assert
        AssertResult(result, 1, expect: ["error: ", "No test results matching"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-11
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-11 - clauses inside fenced blocks are examples, not live promises. If the parser
    ///     read them, every repository would inherit the illustrative clauses from system-contracts.md and
    ///     the templates.
    /// </summary>
    [Fact]
    public void FencedExampleClausesAreIgnored()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            """
            # Ingest

            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records.
              *Verified by:* `AcceptedRecordIsDurable`

            ## Decisions

            An example of the shape, which is not a live clause:

            ```markdown
            ### Provides

            - **EXAMPLE-99** - illustrative only.
            ```
            """);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx("artifacts/tests/results.trx", ["AcceptedRecordIsDurable=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 0, expect: ["1 clauses, 1 test links checked."], reject: ["EXAMPLE-99", "error:"]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-11 - entries under Requires carry no clause ID and must not be flagged.
    /// </summary>
    [Fact]
    public void RequiresEntriesAreNotTreatedAsClauses()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 0, reject: ["'Store' under", "error:"]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-11 - overview.md never carries a contract and is exempt from the requirement.
    /// </summary>
    [Fact]
    public void OverviewIsExemptFromTheContractRequirement()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteDocument("overview.md", "# Architecture Overview\n\nNo contract here.\n");
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 0, reject: ["overview.md", "error:"]);
    }

    /// <summary>
    ///     An empty tree is not a failure: a repository with no clauses at all reports nothing to check
    ///     rather than being flagged.
    /// </summary>
    [Fact]
    public void RepositoryWithNoClausesReportsNothingToCheck()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 0, expect: ["nothing to check"], reject: ["error:"]);
    }

    /// <summary>
    ///     A clause naming a prefix of a real test is not satisfied. Matching was once substring-based, which
    ///     let a clause point at a name that merely appeared inside a longer one.
    /// </summary>
    [Fact]
    public void ClauseNamingAPrefixOfARealTestIsNotSatisfied()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            StandardContract.Replace("`AcceptedRecordIsDurable`", "`AcceptedRecord`", StringComparison.Ordinal));
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(
            result, 1, expect: ["clause INGEST-01 names test 'AcceptedRecord' which is not declared as a test method"]);
    }

    /// <summary>
    ///     A result whose name merely ends with the clause's test does not count. Qualified names are matched
    ///     on a dot boundary, so <c>OtherAcceptedRecordIsDurable</c> cannot report a result on behalf of
    ///     <c>AcceptedRecordIsDurable</c>.
    /// </summary>
    [Fact]
    public void ResultMatchingOnlyAsASuffixDoesNotCount()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["Ingest.Tests.Contract.OtherAcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(
            result,
            1,
            expect: ["clause INGEST-01 names test 'AcceptedRecordIsDurable' which has no result - it did not run"]);
    }

    /// <summary>
    ///     A .trx outside the configured location is ignored: the whole glob is honored, not just its leaf,
    ///     so a stray result file elsewhere in the tree cannot satisfy the pass check.
    /// </summary>
    [Fact]
    public void TrxOutsideTheConfiguredLocationIsIgnored()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "other/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(result, 0, expect: ["No test results matching"], reject: ["error:"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-12
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-12 - a repository that is neither C# nor xUnit is checked through discovery
    ///     patterns supplying all four things that vary between test frameworks: the files searched, the
    ///     declaration shape, the absence of an interior/boundary split in the layout, and the result format.
    ///     Nothing in the fixture is C#.
    /// </summary>
    [Fact]
    public void FixtureCaseRepositoryIsCheckedThroughDiscoveryPatterns()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            """
            ---
            level: system
            covers:
              - suite.suite
            ---

            # Ingest

            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records and returns 202 once durably queued.
              *Verified by:* `suite.suite: "accepted record is durable"`

            ### Invariants

            - **INGEST-I1** - Records are queued in arrival order.
              *Verified by:* `suite.suite: "records keep arrival order"`

            ## Decisions

            Nothing yet.
            """);
        fixture.Write(
            "suite.suite",
            """
            Test-Case -Name "accepted record is durable" -ExpectExit 0
            Test-Case -Name "records keep arrival order" -ExpectExit 0
            """);
        fixture.WriteTextResults(
            "results/tests.txt",
            ["accepted record is durable=Passed", "records keep arrival order=Passed"]);

        // Act
        var result = Run(fixture, FixtureCaseArguments);

        // Assert
        AssertResult(result, 0, expect: ["2 clauses, 2 test links checked."], reject: ["error:", "warning:"]);
    }

    /// <summary>
    ///     A stale non-TRX result is still stale: staleness is a property of the run, not of the result
    ///     format, so a result file written before the suite it describes cannot vouch for it whatever its
    ///     shape.
    /// </summary>
    [Fact]
    public void StaleResultInTheTextFormatIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            """
            # Ingest

            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records.
              *Verified by:* `suite.suite: "accepted record is durable"`
            """);
        fixture.Write("suite.suite", """Test-Case -Name "accepted record is durable" -ExpectExit 0""");
        fixture.WriteTextResults(
            "results/tests.txt", ["accepted record is durable=Passed"], DateTime.UtcNow.AddDays(-2));

        // Act
        var result = Run(fixture, FixtureCaseArguments);

        // Assert
        AssertResult(result, 1, expect: ["Test results are stale"]);
    }

    /// <summary>
    ///     A failing non-TRX result fails its clause.
    /// </summary>
    [Fact]
    public void FailingResultInTheTextFormatFailsItsClause()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument(
            "ingest.md",
            """
            # Ingest

            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records.
              *Verified by:* `suite.suite: "accepted record is durable"`
            """);
        fixture.Write("suite.suite", """Test-Case -Name "accepted record is durable" -ExpectExit 0""");
        fixture.WriteTextResults("results/tests.txt", ["accepted record is durable=Failed"]);

        // Act
        var result = Run(fixture, FixtureCaseArguments);

        // Assert
        AssertResult(result, 1, expect: ["whose most recent result is 'Failed'"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-13
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-13 - discovery that matches nothing is reported once, as itself, rather than as a
    ///     missing test per clause. The repository is entirely well-formed; only the patterns are wrong.
    ///     Reporting a missing test per clause would send a reader off to write tests that already exist.
    /// </summary>
    [Fact]
    public void DiscoveryThatMatchesNothingIsItsOwnFailure()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture, ["-TestRoots", "no-such-directory"]);

        // Assert
        AssertResult(
            result,
            1,
            expect: ["No test declarations found in 'no-such-directory' matching '*.cs'"],
            reject: ["is not declared as a test method"]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-13 - the same holds when the file patterns are the wrong ones rather than the root.
    /// </summary>
    [Fact]
    public void FilePatternsMatchingNoFileAreADiscoveryFailure()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);

        // Act
        var result = Run(fixture, ["-TestFilePatterns", "*.nope"]);

        // Assert
        AssertResult(
            result, 1, expect: ["No test declarations found", "*.nope"], reject: ["is not declared as a test method"]);
    }

    /// <summary>
    ///     Bootstrap escape hatch: a tree of planned clauses with no test sources at all is not a discovery
    ///     failure. A clause naming a TODO obligation is not expected to resolve to anything, so a repository
    ///     with no test sources stays green until it claims otherwise.
    /// </summary>
    [Fact]
    public void TreeOfPlannedClausesWithNoTestSourcesIsNotADiscoveryFailure()
    {
        // Arrange
        using var fixture = new Fixture();
        var planned = StandardContract
            .Replace("`AcceptedRecordIsDurable`", "`TODO_AcceptedRecordIsDurable`", StringComparison.Ordinal)
            .Replace("`PreservesPerConnectionOrder`", "`TODO_PreservesPerConnectionOrder`", StringComparison.Ordinal);
        fixture.WriteDocument("ingest.md", planned);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(
            result, 0, expect: ["unfulfilled test obligation"], reject: ["error:", "No test declarations found"]);
    }

    /// <summary>
    ///     Discovery skips hidden directories, as a plain directory walk without forcing hidden entries would.
    ///     A stale copy under a hidden directory does not keep a clause alive: the live test was deleted, and
    ///     only a copy under a hidden directory still declares it - the fail-open direction this guards.
    /// </summary>
    [Fact]
    public void HiddenDirectoryDoesNotSupplyTestDeclarations()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(
            StandardTests.Replace("AcceptedRecordIsDurable", "SomeOtherTest", StringComparison.Ordinal));
        fixture.Write("test/.old/Contract/OldTests.cs", StandardTests);
        fixture.CreateHiddenDirectory("test/.old");
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture);

        // Assert
        AssertResult(
            result, 1, expect: ["names test 'AcceptedRecordIsDurable' which is not declared as a test method"]);
    }

    /// <summary>
    ///     A wildcard test root is expanded rather than thrown at: undocumented but previously working input
    ///     that reaches every downstream caller, so a glob root must produce a report rather than a raw
    ///     failure.
    /// </summary>
    [Fact]
    public void WildcardTestRootIsExpanded()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.WriteDocument("ingest.md", StandardContract);
        fixture.WriteContractTests(StandardTests);
        fixture.WriteTrx(
            "artifacts/tests/results.trx",
            ["AcceptedRecordIsDurable=Passed", "PreservesPerConnectionOrder=Passed"]);

        // Act
        var result = Run(fixture, ["-TestRoots", "test/" + "*"]);

        // Assert
        AssertResult(result, 0, expect: ["2 clauses, 2 test links checked."], reject: ["error:", "warning:"]);
    }

    // ==========================================================================================
    // CONTRACT-CHECK-14 / CONTRACT-CHECK-15
    // ==========================================================================================

    /// <summary>
    ///     CONTRACT-CHECK-14 - two discovery profiles resolve clauses in two languages in one invocation. The
    ///     case the profile facility exists for: no combination of the single-profile parameters expresses
    ///     this repository, because -ContractTestFolder must be 'Contract' for the C# test and empty for the
    ///     flat suite at the same time.
    /// </summary>
    [Fact]
    public void TwoDiscoveryProfilesResolveClausesInBothLanguages()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);
        fixture.WriteTrx(
            "artifacts/tests/results.trx", ["Ingest.Tests.Contract.IngestContractTests.AcceptedRecordIsDurable=Passed"]);
        fixture.WriteTextResults("results/tests.txt", ["records keep arrival order=Passed"]);

        // Act
        var result = Run(fixture, ProfileArguments(CSharpProfile, ScriptProfile));

        // Assert
        AssertResult(result, 0, expect: ["2 clauses, 2 test links checked."], reject: ["error:", "warning:"]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-15 - a profile matching no test declarations is an error, not a silent skip. The C#
    ///     profile still discovers everything it did before, so a whole-run emptiness check would report
    ///     success while one framework went entirely unchecked.
    /// </summary>
    [Fact]
    public void ProfileMatchingNoTestDeclarationsIsAnError()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);
        fixture.WriteTrx("artifacts/tests/results.trx", ["AcceptedRecordIsDurable=Passed"]);
        fixture.WriteTextResults("results/tests.txt", ["records keep arrival order=Passed"]);
        var brokenScriptProfile = ScriptProfile.Replace(
            "TestFilePatterns=suite.suite", "TestFilePatterns=renamed.suite", StringComparison.Ordinal);

        // Act
        var result = Run(fixture, ProfileArguments(CSharpProfile, brokenScriptProfile));

        // Assert
        AssertResult(result, 1, expect: ["profile 2: No test declarations found in '.' matching 'renamed.suite'"]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-15 - a failing test in the second profile still fails its clause. Results pool
    ///     across profiles, so this proves the pooling did not lose the outcome of the framework that is not
    ///     the first one.
    /// </summary>
    [Fact]
    public void FailingTestInTheSecondProfileFailsItsClause()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);
        fixture.WriteTrx("artifacts/tests/results.trx", ["AcceptedRecordIsDurable=Passed"]);
        fixture.WriteTextResults("results/tests.txt", ["records keep arrival order=Failed"]);

        // Act
        var result = Run(fixture, ProfileArguments(CSharpProfile, ScriptProfile));

        // Assert
        AssertResult(
            result,
            1,
            expect:
            [
                """clause INGEST-I1 names test 'suite.suite: "records keep arrival order"' whose most recent result is 'Failed'"""
            ]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-15 - results missing for one profile are reported against that profile.
    /// </summary>
    [Fact]
    public void ResultsMissingForOneProfileAreReportedAgainstThatProfile()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);
        fixture.WriteTrx("artifacts/tests/results.trx", ["AcceptedRecordIsDurable=Passed"]);

        // Act
        var result = Run(fixture, ProfileArguments(CSharpProfile, ScriptProfile));

        // Assert
        AssertResult(
            result,
            1,
            expect: ["profile 2: No test results matching 'results/" + "*.txt'", "which has no result - it did not run"]);
    }

    /// <summary>
    ///     CONTRACT-CHECK-15 - staleness is judged within a profile, not across them.
    /// </summary>
    [Fact]
    public void StaleResultInOneProfileIsRejectedWhileTheOtherIsFresh()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);
        fixture.WriteTrx("artifacts/tests/results.trx", ["AcceptedRecordIsDurable=Passed"]);
        fixture.WriteTextResults(
            "results/tests.txt", ["records keep arrival order=Passed"], DateTime.UtcNow.AddDays(-2));

        // Act
        var result = Run(fixture, ProfileArguments(CSharpProfile, ScriptProfile));

        // Assert
        AssertResult(result, 1, expect: ["profile 2: Test results are stale", "suite.suite"]);
    }

    /// <summary>
    ///     A misspelled profile field is rejected rather than defaulted. The whole point of a closed field
    ///     set: a field name the operation does not know would otherwise take its default silently, and the
    ///     profile would check something other than what the call site says it checks.
    /// </summary>
    [Fact]
    public void UnknownProfileFieldIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);
        fixture.WriteTrx("artifacts/tests/results.trx", ["AcceptedRecordIsDurable=Passed"]);

        // Act
        var result = Run(fixture, ProfileArguments("TestRoots=test;TestFilePattern=*.cs"));

        // Assert
        AssertResult(result, 1, expect: ["profile 1: unknown field 'TestFilePattern'"]);
    }

    /// <summary>
    ///     A profile field that is not <c>Key=Value</c> is rejected.
    /// </summary>
    [Fact]
    public void ProfileFieldThatIsNotKeyValueIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);

        // Act
        var result = Run(fixture, ProfileArguments("TestRoots=test;*.cs"));

        // Assert
        AssertResult(result, 1, expect: ["profile 1: '*.cs' is not a Key=Value field"]);
    }

    /// <summary>
    ///     A result format no reader implements is rejected. -TestResultFormat is validated on the command
    ///     line itself; a profile field has to make the same rejection or the profile form would be the
    ///     looser one.
    /// </summary>
    [Fact]
    public void UnknownResultFormatInAProfileIsRejected()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);

        // Act
        var result = Run(fixture, ProfileArguments("TestRoots=test;TestResultFormat=junit"));

        // Assert
        AssertResult(result, 1, expect: ["profile 1: TestResultFormat 'junit' is not one of: trx, text"]);
    }

    /// <summary>
    ///     Profiles and the parameters they replace cannot both be supplied - rejected rather than merged,
    ///     since whichever won would be invisible at the call site, and the call site is where a repository's
    ///     layout is meant to be readable.
    /// </summary>
    [Fact]
    public void ProfilesCannotBeCombinedWithTheParametersTheyReplace()
    {
        // Arrange
        using var fixture = new Fixture();
        WriteMixedRepository(fixture);
        var arguments = ProfileArguments(CSharpProfile).Concat(["-TestRoots", "test"]).ToArray();

        // Act
        var result = Run(fixture, arguments);

        // Assert
        AssertResult(result, 1, expect: ["-TestProfiles cannot be combined with -TestRoots"]);
    }

    // ==========================================================================================
    // HELPERS
    // ==========================================================================================

    /// <summary>
    ///     A repository whose contract is verified in two languages at once: one clause by a C# boundary
    ///     test, one by a named case in a flat fixture-case suite.
    /// </summary>
    private static void WriteMixedRepository(Fixture fixture)
    {
        fixture.WriteDocument(
            "ingest.md",
            """
            ---
            level: system
            covers:
              - src/Ingest.cs
            ---

            # Ingest

            ## Contract

            ### Provides

            - **INGEST-01** - Accepts records and returns 202 once durably queued.
              *Verified by:* `AcceptedRecordIsDurable`

            ### Invariants

            - **INGEST-I1** - Records are queued in arrival order.
              *Verified by:* `suite.suite: "records keep arrival order"`

            ## Decisions

            Nothing yet.
            """);

        fixture.WriteContractTests(
            """
            namespace Ingest.Tests.Contract;

            public class IngestContractTests
            {
                [Fact]
                public void AcceptedRecordIsDurable()
                {
                }
            }
            """);

        fixture.Write("suite.suite", """Test-Case -Name "records keep arrival order" -ExpectExit 0""");
    }

    /// <returns>Repeated <c>-TestProfiles</c> arguments, one per record.</returns>
    private static string[] ProfileArguments(params string[] records) =>
        [.. records.SelectMany(record => new[] { "-TestProfiles", record })];

    /// <summary>
    ///     Spawns the real, installed <c>dotnet anneal check-contracts</c> against the fixture, with the
    ///     fixture's root as the working directory so it resolves as the repository under check.
    /// </summary>
    private static (int ExitCode, string Output) Run(Fixture fixture, IEnumerable<string>? arguments = null, bool strict = false)
    {
        var allArguments = new List<string> { "anneal", "check-contracts" };
        allArguments.AddRange(arguments ?? []);
        if (strict) allArguments.Add("-Strict");

        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = fixture.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in allArguments) start.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("dotnet anneal did not start");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = new StringBuilder(stdoutTask.GetAwaiter().GetResult());
        output.Append(stderrTask.GetAwaiter().GetResult());
        return (process.ExitCode, output.ToString());
    }

    /// <summary>
    ///     Asserts the checker's exit code and, where supplied, that its output contains every expected
    ///     substring and none of the rejected ones. Absence is asserted alongside presence where a case is
    ///     about something <i>not</i> firing - both must agree for the case to mean anything.
    /// </summary>
    private static void AssertResult(
        (int ExitCode, string Output) result, int expectExit, string[]? expect = null, string[]? reject = null)
    {
        Assert.Multiple(
            () => Assert.True(
                expectExit == result.ExitCode,
                $"expected exit {expectExit}, got {result.ExitCode}. Output was:\n{result.Output}"),
            () =>
            {
                foreach (var text in expect ?? [])
                    Assert.True(
                        result.Output.Contains(text, StringComparison.Ordinal),
                        $"expected output to contain '{text}'. Output was:\n{result.Output}");
            },
            () =>
            {
                foreach (var text in reject ?? [])
                    Assert.False(
                        result.Output.Contains(text, StringComparison.Ordinal),
                        $"expected output NOT to contain '{text}'. Output was:\n{result.Output}");
            });
    }

    /// <summary>
    ///     A throwaway repository built one file at a time under this repository's own
    ///     <c>artifacts/check-contract-fixtures</c> directory (git-ignored), so that <c>dotnet anneal</c>
    ///     resolves this repository's tool manifest by walking up from the fixture's working directory.
    /// </summary>
    /// <remarks>
    ///     A fixture placed under the OS temp directory instead has no <c>.config/dotnet-tools.json</c>
    ///     ancestor, so a plain <c>dotnet anneal</c> invocation run from inside it cannot find the tool at
    ///     all. Rooting fixtures as descendants of this repository sidesteps that resolution problem entirely,
    ///     the same way <c>test-check-contracts.ps1</c> did, rather than reaching past the manifest to invoke
    ///     a resolved tool binary directly - which would stop proving that the manifest-driven "dotnet anneal"
    ///     path itself still works.
    ///     <para>Thread safety: not safe for concurrent use; each test owns one instance.</para>
    /// </remarks>
    private sealed class Fixture : IDisposable
    {
        /// <summary>
        ///     How far into the future a result file is stamped when a test is not exercising staleness.
        ///     Results must post-date the test sources or every fixture would report itself stale.
        /// </summary>
        private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);

        /// <summary>
        ///     This repository's root, found once by walking up from the running test assembly's own
        ///     directory until the solution file is found.
        /// </summary>
        private static readonly string RepositoryRoot = FindRepositoryRoot();

        /// <summary>
        ///     Where fixture repositories are rooted: a git-ignored directory under this repository, cleared
        ///     of nothing here - each fixture cleans up its own directory on disposal.
        /// </summary>
        private static readonly string FixtureRoot = Path.Combine(RepositoryRoot, "artifacts", "check-contract-fixtures");

        public Fixture()
        {
            Root = Path.Combine(FixtureRoot, $"anneal-fixture-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "docs", "architecture"));
        }

        /// <summary>
        ///     The fixture repository's root directory.
        /// </summary>
        public string Root { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
            }
            catch (IOException)
            {
                // A fixture left behind is litter, not a test failure.
            }
            catch (UnauthorizedAccessException)
            {
                // Same tolerance as above, for a hidden or read-only leftover.
            }
        }

        /// <returns>The full path a relative fixture path was written to.</returns>
        public string Write(string relativePath, string contents)
        {
            var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, contents, Encoding.UTF8);
            return full;
        }

        /// <summary>
        ///     Writes a system document into the architecture tree.
        /// </summary>
        public void WriteDocument(string name, string contents) => Write($"docs/architecture/{name}", contents);

        /// <summary>
        ///     Writes the conventional Ingest contract test class.
        /// </summary>
        public void WriteContractTests(string contents) => Write("test/Ingest.Tests/Contract/IngestContractTests.cs", contents);

        /// <summary>
        ///     Writes a well-formed TRX recording the given "Name=Outcome" outcomes, with realistic testId
        ///     GUIDs and TestDefinitions matching the shape <c>dotnet test</c> actually produces.
        /// </summary>
        public void WriteTrx(string relativePath, IEnumerable<string> outcomes, DateTime? written = null)
        {
            var run = new TestResults.TestResults { Name = "Fixture" };

            foreach (var entry in outcomes)
            {
                var split = entry.IndexOf('=', StringComparison.Ordinal);
                run.Results.Add(
                    new TestResults.TestResult
                    {
                        Name = entry[..split],
                        Outcome = Enum.Parse<TestOutcome>(entry[(split + 1)..], true)
                    });
            }

            Stamp(Write(relativePath, TrxSerializer.Serialize(run)), written);
        }

        /// <summary>
        ///     Writes a text tally: one result per line, an outcome token then the test name. Outcomes are
        ///     "Test name=Outcome" strings, split on the last '=' so a case name may itself contain one.
        /// </summary>
        public void WriteTextResults(string relativePath, IEnumerable<string> outcomes, DateTime? written = null)
        {
            var lines = outcomes.Select(
                entry =>
                {
                    var split = entry.LastIndexOf('=');
                    return $"{entry[(split + 1)..]} {entry[..split]}";
                });
            Stamp(Write(relativePath, string.Join("\n", ["# outcome name", .. lines])), written);
        }

        /// <summary>
        ///     Creates a directory the platform treats as hidden: Windows decides by attribute, the Unix-like
        ///     platforms by a dot-prefixed name, so the caller supplies a dot-prefixed leaf and the attribute
        ///     is also set here, letting one fixture assert the same thing everywhere.
        /// </summary>
        public void CreateHiddenDirectory(string relativePath)
        {
            var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(full);

            if (OperatingSystem.IsWindows())
                File.SetAttributes(full, File.GetAttributes(full) | FileAttributes.Hidden);
        }

        private static void Stamp(string path, DateTime? written) =>
            File.SetLastWriteTimeUtc(path, (written ?? DateTime.UtcNow.Add(Fresh)).ToUniversalTime());

        /// <returns>
        ///     This repository's root, located by walking up from the test assembly's own directory until
        ///     the solution file is found.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when no ancestor of the running test assembly holds the solution file - this suite can
        ///     only run from within a build of this repository.
        /// </exception>
        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Anneal.slnx")))
                    return directory.FullName;
            }

            throw new InvalidOperationException(
                $"Could not locate this repository's root (Anneal.slnx) above {AppContext.BaseDirectory}");
        }
    }
}
