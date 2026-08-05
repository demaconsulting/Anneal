using DemaConsulting.Anneal.Toolkit.Architecture;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for reading a contract out of an architecture document.
/// </summary>
/// <remarks>
///     Disposable. These pin the two things a line-based reader got wrong and a structural one does not: a
///     clause body that wraps across lines, and a clause-shaped string inside a fenced example. Both occur in
///     this repository's own tree, and both mis-report a contract silently.
/// </remarks>
public class ArchitectureDocumentTests
{
    /// <summary>
    ///     Validates that a verifier is found even when the clause body wraps and pushes it several lines
    ///     below the identifier.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_WrappedClauseBody_StillFindsTheVerifier()
    {
        // Arrange: a clause whose prose runs to three lines before the verifier
        const string markdown = """
                                ## Contract

                                ### Provides

                                - **INGEST-01** - Accepts records and returns 202 once the record is durably
                                  queued, so a caller that received 202 need not retry, and a caller that
                                  did not receive one may retry without duplicating the record.
                                  *Verified by:* `AcceptedRecordIsDurable`
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        var clause = Assert.Single(document.Clauses);
        Assert.Multiple(
            () => Assert.Equal("INGEST-01", clause.Id),
            () => Assert.Equal(["AcceptedRecordIsDurable"], clause.Verifiers.Select(item => item.Text)));
    }

    /// <summary>
    ///     Validates that a clause-shaped string inside a fenced block is an example rather than a promise.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_ClauseInsideAFence_IsNotAClause()
    {
        // Arrange: a template shown to an author, beside a real clause
        const string markdown = """
                                ## Contract

                                ### Provides

                                - **INGEST-01** - Accepts records.
                                  *Verified by:* `AcceptedRecordIsDurable`

                                ```markdown
                                - **SYSTEM-01** - What the system provides.
                                  *Verified by:* `TestName`
                                ```
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        Assert.Equal(["INGEST-01"], document.Clauses.Select(clause => clause.Id));
    }

    /// <summary>
    ///     Validates that entries under Requires are not clauses, since they name depended-upon behavior
    ///     belonging to another system and legitimately carry no identifier.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_RequiresSubsection_DeclaresNoClauses()
    {
        // Arrange
        const string markdown = """
                                ## Contract

                                ### Requires

                                - **Store** - durable append with at-least-once delivery.

                                ### Provides

                                - **INGEST-01** - Accepts records.
                                  *Verified by:* `AcceptedRecordIsDurable`
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        Assert.Multiple(
            () => Assert.Equal(["INGEST-01"], document.Clauses.Select(clause => clause.Id)),
            () => Assert.Empty(document.MalformedClauses));
    }

    /// <summary>
    ///     Validates that an unresolved placeholder under a clause subsection is reported rather than
    ///     skipped, because a clause nobody parses is a clause nobody checks.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_MalformedIdentifier_IsReported()
    {
        // Arrange: a template placeholder left behind
        const string markdown = """
                                ## Contract

                                ### Provides

                                - **SYSTEM-nn** - What the system provides.
                                  *Verified by:* `TestName`
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        var malformed = Assert.Single(document.MalformedClauses);
        Assert.Multiple(
            () => Assert.Equal("SYSTEM-nn", malformed.Label),
            () => Assert.Equal("Provides", malformed.Section),
            () => Assert.Empty(document.Clauses));
    }

    /// <summary>
    ///     Validates that a document declaring no contract section is visible as such.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_NoContractHeading_DeclaresNoContract()
    {
        // Arrange: prose with a bolded list item that is not a clause
        const string markdown = """
                                # Ingest

                                ## Decisions

                                - **Queue depth** - bounded at ten thousand.
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        Assert.Multiple(
            () => Assert.False(document.DeclaresContract),
            () => Assert.Empty(document.Clauses),
            () => Assert.Empty(document.MalformedClauses));
    }

    /// <summary>
    ///     Validates that a heading after the contract closes it, so a bolded item further down the document
    ///     is not read as a clause.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_SectionAfterTheContract_ContributesNoClauses()
    {
        // Arrange
        const string markdown = """
                                ## Contract

                                ### Provides

                                - **INGEST-01** - Accepts records.
                                  *Verified by:* `AcceptedRecordIsDurable`

                                ## Decisions

                                ### Provides

                                - **INGEST-99** - Not a promise.
                                  *Verified by:* `NotATest`
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        Assert.Equal(["INGEST-01"], document.Clauses.Select(clause => clause.Id));
    }

    /// <summary>
    ///     Validates that inline code appearing after the verifier line is not read as a further test, so a
    ///     file name mentioned in a later sentence cannot become a promise.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_CodeAfterTheVerifierLine_IsNotAVerifier()
    {
        // Arrange
        const string markdown = """
                                ## Contract

                                ### Provides

                                - **INGEST-01** - Accepts records.
                                  *Verified by:* `AcceptedRecordIsDurable`
                                  Configured by `ingest.json`.
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        var clause = Assert.Single(document.Clauses);
        Assert.Equal(["AcceptedRecordIsDurable"], clause.Verifiers.Select(item => item.Text));
    }

    /// <summary>
    ///     Validates that a clause naming several tests, on one line or on two, collects all of them.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_SeveralVerifiers_AreAllCollected()
    {
        // Arrange
        const string markdown = """
                                ## Contract

                                ### Invariants

                                - **INGEST-I1** - Records are queued in arrival order.
                                  *Verified by:* `PreservesPerConnectionOrder`, `PreservesOrderUnderLoad`
                                  *Verified by:* `PreservesOrderAfterRestart`
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        var clause = Assert.Single(document.Clauses);
        Assert.Equal(
            ["PreservesPerConnectionOrder", "PreservesOrderUnderLoad", "PreservesOrderAfterRestart"],
            clause.Verifiers.Select(item => item.Text));
    }

    /// <summary>
    ///     Validates that front matter is read as front matter rather than as document content.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_FrontMatter_ContributesNothing()
    {
        // Arrange: the level and coverage block every system document opens with
        const string markdown = """
                                ---
                                level: system
                                covers:
                                  - src/Ingest/**
                                ---

                                # Ingest

                                ## Contract

                                ### Provides

                                - **INGEST-01** - Accepts records.
                                  *Verified by:* `AcceptedRecordIsDurable`
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        Assert.Multiple(
            () => Assert.True(document.DeclaresContract),
            () => Assert.Equal(["INGEST-01"], document.Clauses.Select(clause => clause.Id)));
    }

    /// <summary>
    ///     Validates that a clause naming no test is read as a clause, so the check can report the promise
    ///     nobody proves rather than losing it.
    /// </summary>
    [Fact]
    public void ArchitectureDocument_Read_ClauseWithNoVerifier_IsStillAClause()
    {
        // Arrange
        const string markdown = """
                                ## Contract

                                ### Provides

                                - **INGEST-01** - Accepts records.
                                """;

        // Act
        var document = ArchitectureDocument.Read("ingest.md", markdown, true);

        // Assert
        var clause = Assert.Single(document.Clauses);
        Assert.Empty(clause.Verifiers);
    }
}
