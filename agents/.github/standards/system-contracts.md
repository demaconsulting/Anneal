---
name: System Contracts
description: Follow these standards when defining or changing what a system promises to its consumers.
globs: ["docs/architecture/*.md"]
---

# Principle

A **contract** is what consumers outside a system may rely on. It is the only requirement-like
artifact in this process, and it lives in exactly one place: the `## Contract` section of
`docs/architecture/{system}.md`.

There are deliberately **no subsystem or unit requirements**. Interior structure must be free to
churn without documentation cost — that freedom is the entire point of this process. Requirements
written against interior structure convert every refactor into a documentation project.

# Placement

The contract is embedded in the system's architecture document, not in a parallel tree. Parallel
artifact trees must be kept in sync, and that synchronization cost is paid on every change forever.
One file, one edit.

# Structure

```markdown
## Contract

### Provides

- **INGEST-01** — Accepts newline-delimited JSON records over HTTP and returns
  `202 Accepted` once the record is durably queued.
  *Verified by:* `IngestContractTests.AcceptedRecordIsDurable`

- **INGEST-02** — Rejects malformed records with `400` and a body naming the byte
  offset of the first parse failure.
  *Verified by:* `IngestContractTests.MalformedRecordReportsOffset`

### Requires

- **Store** — durable append with at-least-once delivery semantics.
- **System.Text.Json** — record deserialization.

### Invariants

- **INGEST-I1** — Records from a single connection are queued in arrival order.
  *Verified by:* `IngestContractTests.PreservesPerConnectionOrder`
```

# Clause Rules

- **Boundary-observable only.** If a clause can only be checked by reading the system's internals,
  it is not a contract clause — delete it. The test: could a consumer with no source access detect a
  violation?
- **WHAT, not HOW.** A clause states observable behavior. It never names an internal class, method,
  algorithm, or data structure. The system's own name is identity, not implementation.
- **Every clause names at least one test.** A clause without verification is an aspiration. Use the
  test's real name so the link is greppable and breaks loudly when the test is renamed.
- **Invariants** capture properties no single call can demonstrate: ordering, idempotency,
  thread-safety, resource bounds, and version-compatibility guarantees.
- **Requires** lists what this system depends on, by advertised behavior — never by internal design.

# Enforcement

The clause-to-test link is the **only** mechanically enforced relationship in this process, and it is
checked deterministically rather than by an agent reading files:

```pwsh
pwsh ./check-contracts.ps1
```

`lint.ps1` runs it, so CI fails when:

- A clause ID is duplicated
- A clause names no verifying test
- A clause names a test that does not exist in the test sources
- A clause names a test that did not pass, when test results are present

This matters because `*Verified by:*` is otherwise unenforced prose: rename the test and the promise
rots silently. Everything else in this process is judgement, deliberately. This one thing is not,
because a script does it faster and more reliably than an agent can.

Clauses whose test name contains `TODO` are reported as **unfulfilled obligations** — a warning, not
an error — so `software-architect` can write a contract before its tests exist. Run with `-Strict` to
fail on them once implementation is complete.

**Never resolve a check failure by editing the clause to match the code.** Fix the test name, or make
the contract change deliberately.

# Identifiers

- Format: `{SYSTEM}-{nn}` for provided behavior, `{SYSTEM}-I{n}` for invariants.
- IDs are **stable for the life of the clause**. Never renumber to close gaps.
- **Never reuse a retired number.** Gaps in the sequence are correct and expected.
- Deleted clauses are simply removed; git holds the history. Do not maintain a graveyard section.

# Sizing

A healthy system contract has roughly **5 to 25 clauses**. Interpret the extremes:

- **Fewer than 3** — the system probably is not a system; it may be interior detail of another.
- **More than 40** — either the decomposition is wrong, or the contract has drifted into restating
  the public API. Method-level enumeration belongs in doc comments, not here.

# Changing a Contract

Changing a clause is the definition of a **Tier 1** change (see `change-tiers.md`) and is the
project's breaking-change signal:

- **Adding** a clause is additive; consumers are unaffected.
- **Narrowing or removing** a clause is breaking; it must be called out in the change summary and in
  release notes.
- **Rewording without semantic change** is free — but confirm it is genuinely free before treating
  it as such.

Update the contract **before** implementing, not after. A contract edited to match code already
written is a description, not a promise, and it provides no design pressure.

# Anti-Patterns

- Writing clauses for subsystems or units.
- One clause per public method — that is API documentation wearing a contract's clothes.
- Clauses describing internal construction: "shall use a `TokenStream`".
- Clauses with no test link, or linked to interior tests rather than boundary tests.
- Duplicating clauses into a separate requirements file "for tooling".
- Back-filling clauses from existing code to raise a coverage number.

# Quality Gates

- [ ] Every clause is observable from outside the system
- [ ] Every clause describes WHAT, never HOW
- [ ] Every clause and invariant names at least one existing, passing test
- [ ] `pwsh ./check-contracts.ps1` exits clean
- [ ] Clause IDs are stable and no retired number has been reused
- [ ] `Requires` entries name advertised behavior, not internal design
- [ ] Contract clause count is within the healthy range, or the deviation is understood
- [ ] The contract was updated before implementation, not after
- [ ] No requirements exist below system level anywhere in the repository
