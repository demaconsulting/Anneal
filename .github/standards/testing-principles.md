---
name: Testing Principles
description: Follow these standards when developing any software tests.
---

# Two Kinds of Test

Tests fall into two categories with **opposite lifecycles**. Confusing them is a major source of
change resistance: if every test is treated as permanent, interior refactoring drags a test rewrite
behind it, and the code stops moving.

## Contract Tests — Durable

Contract tests prove a clause in a system's `## Contract` (see `system-contracts.md`).

- Exercise the system **only through its public boundary**. No internal types, no reaching past the
  entry points a real consumer would use.
- Named in the clause they verify, so the link is greppable in both directions.
- Live in a dedicated location — `test/{System}.Tests/Contract/` — so their special status is
  visible.
- **Survive refactoring untouched.** A Small Fix change that breaks a contract test was misclassified:
  either the change is actually a Contract Change, or it is a defect.
- Changing one requires a corresponding contract change. Never edit a contract test to make a build
  pass.

## Interior Tests — Disposable

Everything else: unit tests, focused integration tests, corner cases, defensive checks, regression
tests for fixed bugs.

- Free to use internal types and to be as fine-grained as useful.
- **Deleted or rewritten without ceremony** when the code they cover is restructured. They are
  scaffolding for the developer who wrote the code, not compliance evidence.
- Need no contract clause and no justification for existing.
- A deleted interior test needs no approval; deleting a contract test does.

# AAA Pattern (MANDATORY)

All tests follow Arrange-Act-Assert with comments marking each phase. A reader must be able to see
what is set up, what is exercised, and what is asserted without reconstructing it from the code.

# Coverage Expectations

- **Every contract clause and invariant has at least one passing contract test.** This is the only
  mandatory coverage rule in this process.
- Interior coverage is a judgement call made by the developer. Chase behavior worth protecting, not
  a percentage.
- Both success and failure paths are covered for contract clauses; error behavior is usually the
  part consumers actually depend on.
- External dependencies are mocked or stubbed in interior tests. Contract tests SHOULD use real
  dependencies where practical — a contract verified only against mocks is not verified.

# Anti-Patterns

- **Contract tests that touch internals** — they become interior tests wearing a durable label, and
  they will block refactoring.
- **Interior tests preserved out of sentiment** after their subject is gone.
- **Editing a contract test to make a build pass** — that is silently narrowing a promise.
- **Mirroring the class structure** with one test class per production class as an obligation. Test
  what is worth protecting.
- **Asserting on internal call sequences** rather than observable results, which welds the test to
  one implementation.

# Language-Specific Implementation

Load `{language}-testing.md` from `.github/standards/` for framework, layout, and tooling detail.

# Quality Gates

- [ ] Every contract clause and invariant has at least one passing contract test
- [ ] Contract tests use only the public boundary of their system
- [ ] Contract tests live in the designated contract test location
- [ ] Contract tests passed unchanged across every Small Fix change
- [ ] No contract test was weakened to make a build pass
- [ ] Interior tests are not preserved after their subject is removed
- [ ] All tests follow AAA with phase comments
- [ ] Both success and failure paths covered for every contract clause
