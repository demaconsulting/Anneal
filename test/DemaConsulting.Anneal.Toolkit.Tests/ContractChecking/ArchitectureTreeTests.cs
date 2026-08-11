using DemaConsulting.Anneal.Toolkit.Architecture;
using Xunit;

namespace DemaConsulting.Anneal.Toolkit.Tests.ContractChecking;

/// <summary>
///     Interior tests for reading the architecture tree as a whole.
/// </summary>
/// <remarks>
///     Disposable. The behavior worth protecting is that the tree is read recursively: a reader that only
///     looked at the top level would stop checking every clause that moved when a system outgrew one
///     document, which is the failure that makes splitting a large document unsafe.
/// </remarks>
public class ArchitectureTreeTests
{
    private const string Contract = """
                                    ## Contract

                                    ### Provides

                                    - **INGEST-01** - Accepts records.
                                      *Verified by:* `AcceptedRecordIsDurable`
                                    """;

    /// <summary>
    ///     Validates that a clause in a subsystem document below the top level is found.
    /// </summary>
    [Fact]
    public void ArchitectureTree_Read_ClauseInASubdirectory_IsFound()
    {
        // Arrange: a system whose contract has been split into a part document
        using var repository = new TemporaryRepository();
        repository.WriteDocument("ingest.md", Contract);
        repository.Write(
            ".anneal/architecture/ingest/queueing.md",
            """
            ## Contract

            ### Invariants

            - **INGEST-I1** - Records are queued in arrival order.
              *Verified by:* `PreservesPerConnectionOrder`
            """);

        // Act
        var tree = ArchitectureTree.Read(Path.Combine(repository.Root, ".anneal", "architecture"));

        // Assert
        Assert.Equal(
            ["INGEST-01", "INGEST-I1"],
            tree.Clauses.Select(clause => clause.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Validates that a subsystem document is not held to the requirement to declare a contract, since it
    ///     elaborates one system's interior rather than owning a contract of its own.
    /// </summary>
    [Fact]
    public void ArchitectureTree_Read_SectionDocument_IsNotASystemDocument()
    {
        // Arrange
        using var repository = new TemporaryRepository();
        repository.WriteDocument("ingest.md", Contract);
        repository.Write(".anneal/architecture/ingest/notes.md", "# Notes\n\n- **Queue depth** - bounded.\n");

        // Act
        var tree = ArchitectureTree.Read(Path.Combine(repository.Root, ".anneal", "architecture"));

        // Assert
        var section = Assert.Single(tree.Documents, document => document.Name == "notes.md");
        Assert.Multiple(
            () => Assert.False(section.IsSystemDocument),
            () => Assert.False(section.DeclaresContract),
            () => Assert.Empty(section.Clauses));
    }

    /// <summary>
    ///     Validates that the overview is excluded entirely: it introduces the systems and owns no contract.
    /// </summary>
    [Fact]
    public void ArchitectureTree_Read_Overview_IsExcluded()
    {
        // Arrange
        using var repository = new TemporaryRepository();
        repository.WriteDocument("ingest.md", Contract);
        repository.WriteDocument("overview.md", "# Overview\n\nThe systems.\n");

        // Act
        var tree = ArchitectureTree.Read(Path.Combine(repository.Root, ".anneal", "architecture"));

        // Assert
        Assert.Equal(["ingest.md"], tree.Documents.Select(document => document.Name));
    }

    /// <summary>
    ///     Validates that a repository with no architecture tree reads as an empty one, since a repository may
    ///     adopt the check before it has a tree.
    /// </summary>
    [Fact]
    public void ArchitectureTree_Read_MissingRoot_ReadsAsEmpty()
    {
        // Arrange
        using var repository = new TemporaryRepository();

        // Act
        var tree = ArchitectureTree.Read(Path.Combine(repository.Root, "no", "such", "tree"));

        // Assert
        Assert.Multiple(
            () => Assert.Empty(tree.Documents),
            () => Assert.Empty(tree.Clauses));
    }
}
