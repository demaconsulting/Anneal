---
name: C# Testing
description: Follow these standards when developing C# tests.
globs: ["**/test/**/*.cs", "**/tests/**/*.cs", "**/*Tests.cs", "**/*Test.cs"]
---

# Required Standards

Read these standards first before applying this standard:

- **`testing-principles.md`** - Test lifecycles and universal testing principles
- **`csharp-language.md`** - C# language development standards

# File Organization

Tests are split by lifecycle, not by source structure:

```text
test/
└── {SystemName}.Tests/
    ├── Contract/
    │ └── {SystemName}ContractTests.cs # durable - one test per clause
    └── ... # interior tests - disposable
```

Contract tests live under `Contract/` so their durability is visible. Interior
tests sit alongside and may be organized however is convenient — they are deleted
with the code they cover.

# Package Reference

Every xUnit v3 test project requires the following package references for
`dotnet test` to discover and execute tests:

| Package | Purpose |
| ------- | ------- |
| `xunit.v3` | xUnit v3 framework (monolithic - includes assertions and fixtures) |
| `Microsoft.NET.Test.Sdk` | Required by the VSTest/`dotnet test` host for test discovery |
| `xunit.runner.visualstudio` | VSTest adapter that bridges xUnit v3 to `dotnet test` |

Omitting `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio` causes tests
to be silently undiscoverable by `dotnet test`.

If tests require mocking of dependencies, add `NSubstitute` as a package
reference - it is recommended when mocking is needed but is not required for
every test project.

# Test Style

Contract test names are written verbatim into the clause they verify, so they must
be stable and greppable. Follow AAA with labeled comments:

- **Contract tests**: `{SystemName}ContractTests.{ClauseBehavior}`
- **Interior tests**: `{Subject}_{MethodUnderTest}_{Scenario}_{ExpectedBehavior}`

```csharp
/// <summary>
/// Validates that an invalid email format throws an ArgumentException.
/// </summary>
[Fact]
public void UserValidator_ValidateEmail_InvalidFormat_ThrowsArgumentException()
{
    // Arrange: create a validator with default configuration
    var validator = new UserValidator();

    // Act / Assert: email with no domain throws
    Assert.Throws<ArgumentException>(() => validator.ValidateEmail("not-an-email"));
}
```

# xUnit v3 Specifics

These are non-obvious v3 behaviors that differ from v2 or common assumptions:

- **`IAsyncLifetime`**: Both `InitializeAsync` and `DisposeAsync` return `ValueTask`
  in v3, not `Task` - using `Task` compiles but does not satisfy the v3 interface
- **`Assert.Multiple`**: Use to collect all assertion failures in a single test
  rather than stopping at the first
- **`[Collection]` without `[CollectionDefinition]`**: Silently disables parallelism
  without providing any shared fixture - always pair them or remove `[Collection]`

# Live Trial Tests

A live trial is an expensive, real-model, real-boundary interior test: a real temp-folder git
repository, a real in-process invocation of the compiled tool against it, and a real model-backed
grading oracle over the outcome - the pattern this repository's own migration built by hand, over and
over, before `test/DemaConsulting.Anneal.Toolkit.Tests/LiveTrial/LiveTrialFixture.cs` made it reusable.

- Must be gated behind an explicit opt-in environment variable (`ANNEAL_LIVE_TRIALS=1` for this
  repository's own harness) and skip by default, using xUnit v3's runtime `Assert.SkipUnless` - never
  a compile-time `[Fact(Skip = ...)]`, which cannot read an environment variable. A plain
  `dotnet test`, `pwsh ./build.ps1`, or CI run must never make a real model call.
- Are interior tests: disposable, carry no contract clause, and are never linked by
  `check-contracts`.
- See `LiveTrialFixture` for the harness a new live trial builds on rather than re-implementing.

# Quality Checks

- [ ] All tests follow AAA pattern with clear section comments
- [ ] Contract tests live under `Contract/` and use only the system's public surface
- [ ] Contract test names match the clause that names them, exactly
- [ ] Each test verifies single, specific behavior (no shared state between tests)
- [ ] Both success and failure scenarios covered including edge cases
- [ ] External dependencies mocked with NSubstitute in interior tests
- [ ] Test results generated in TRX format (`dotnet test --logger trx`) so
  `dotnet anneal check-contracts` can verify clause-to-test links passed
