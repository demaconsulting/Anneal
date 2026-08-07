// TODO: Replace SystemName with the actual system name.
//
// CONTRACT TESTS ARE DURABLE.
//
// Every test in this folder proves one clause from the Contract section of
// docs/architecture/{system-name}.md, and is named in that clause.
//
// Rules:
//   - Exercise the system ONLY through its public boundary. No internal types.
//   - These tests must survive Small Fix changes UNCHANGED. If a refactor
//     breaks one, the change was not a Small Fix - or it is a defect.
//   - Never edit a contract test to make a build pass. That silently narrows a promise.
//   - Prefer real dependencies over mocks; a contract verified only against mocks is
//     not verified.
//
// Interior tests belong in the parent folder, are free to use internals, and may be
// deleted or rewritten without ceremony.

namespace SystemName.Tests.Contract;

/// <summary>
/// Contract tests for the SystemName system.
/// </summary>
public class SystemNameContractTests
{
    /// <summary>
    /// Verifies SYSTEM-01: TODO: restate the clause being proven.
    /// </summary>
    [Fact]
    public void TodoContractTestName()
    {
        // Arrange
        // TODO: set up only what a real consumer would.

        // Act
        // TODO: call through the public boundary.

        // Assert
        // TODO: assert the observable result named in the clause.
        Assert.True(false, "TODO: implement this contract test.");
    }
}
