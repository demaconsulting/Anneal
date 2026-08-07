---
name: System Contracts
description: Follow these standards when defining or changing what a system promises to its consumers.
globs: ["docs/architecture/**/*.md"]
---

# Principle

A **contract** is what consumers outside a system boundary may rely on. Below level 0 it is the only
requirement-like artifact in this process, and each clause lives at the **node that owns the
promise** — the `## Contract` section of that node's architecture document. A child node owns the
promises specific to it; a cross-cutting promise every sibling obeys is owned by the node those
siblings share. That shared node is the system root when the promise belongs to no narrower layer, or
an intermediate machinery node the siblings depend on through `Requires` when holding it there lets a
reader working on one consumer load only the machinery that consumer is built from. The system root
always keeps its own system-identity contract. Every clause has a single owning node, at any depth.
Level 0 carries the product's own promises to a person, which `architecture-documentation.md` owns.

There are deliberately **no subsystem or unit requirements**. Interior structure must be free to
churn without documentation cost — that freedom is the entire point of this process. Requirements
written against interior structure convert every refactor into a documentation project.

# Placement

A contract is embedded in the architecture document of the node that owns it, not in a parallel tree.
Parallel artifact trees must be kept in sync, and that synchronization cost is paid on every change
forever. One clause, one owning node: a clause is edited where it lives, which may be a child node
rather than the system root.

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
- **Stated once.** The clause is the only place its claim is asserted. Architecture prose and code
  comments may cite the clause by ID, explain why it exists, or describe how it is met — they must not
  restate it as a general assertion. A copy carries no authority of its own and can only drift; when the
  clause is later narrowed, every copy becomes a defect somebody has to find.
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

- A system document has no `## Contract` section
- A bolded item under `Provides` or `Invariants` is not a well-formed clause ID
- A clause ID is duplicated
- A clause names no verifying test
- Discovery finds no test declaration anywhere, while some clause names a verifier that is not a
  planned obligation
- A clause names a test that is not declared as a test in a contract test location — by default a
  test method under `test/**/Contract/`
- A clause names a test whose most recent result is not `Passed`
- The test results are older than the test sources they describe

Parsing **fails closed**. A clause the script cannot understand is an error, never a silent skip: a
renamed heading or a malformed ID would otherwise remove the clause from the check while the run
still reported success, which is worse than having no check at all.

Because the pass verification reads recorded test results — TRX by default, and whatever form the
caller selects otherwise — `build.ps1` must run **before** `lint.ps1`. The template CI workflow
orders them that way; running lint alone leaves no results and downgrades the pass check to a
warning.

This matters because `*Verified by:*` is otherwise unenforced prose: rename the test and the promise
rots silently. Everything else in this process is judgement, deliberately. This one thing is not,
because a script does it faster and more reliably than an agent can.

A clause may be **planned**: a promise the repository intends to keep but has not yet built, so that
`architecture-design` can write a contract before its tests exist. This section is the single owner of
that authoring rule; everywhere else cites it rather than restating the form.

Write the planned verifier in the **placeholder form** — an uppercase `TODO.` or `TODO_` opening the
verifier string, followed by the name the test will take:

```markdown
*Verified by:* `TODO.AcceptedRecordIsDurable`
```

`check-contracts.ps1` reports exactly that form as an **unfulfilled obligation** — a warning, not an
error. The match is case-sensitive and anchored at the start of the verifier as written, so nothing else
is exempt: a real test named `TodoItemsAreReturned`, a fixture case whose title mentions TODO, and a
suite file named `TODO-suite.ps1` are all checked normally. Any verifier that is not in the placeholder
form and names no existing test is an **error**, so a bare `TODO` dropped into the middle of a name
breaks the build rather than deferring the obligation.

The `scope-check` agent runs with `-Strict` on Contract Change and Structural Change changes, which
promotes obligations to errors once implementation is complete; that agent owns closing them. During
a Migration the planned clauses are closed stage by stage, and `MIGRATION.md` holds the exit
condition for each.

**Never resolve a check failure by editing the clause to match the code.** Fix the test name, or make
the contract change deliberately.

# Identifiers

- Format: `{SYSTEM}-{nn}` for provided behavior, `{SYSTEM}-I{n}` for invariants.
- The `{SYSTEM}` prefix is alphanumeric, and may be hyphenated for a multi-word system
  (`DATA-STORE-01`). It must not contain spaces, underscores, or a trailing placeholder such as
  `{SYSTEM}` — the check rejects anything it cannot parse rather than skipping it.
- IDs are **stable for the life of the clause**. Never renumber to close gaps.
- **Never reuse a retired number.** Gaps in the sequence are correct and expected, so a new clause
  takes the next number **above the highest ever used** in that system rather than filling a gap.
- When a system is renamed, split, or merged, a surviving clause keeps its wording but takes the new
  owning system's prefix and the next unused number there. Its verifying test is renamed to match,
  and the old identifier is recorded in the changing agent's report. A promise does not lapse because
  the system holding it was renamed, and the numbers it vacates are not reused.
- Deleted clauses are simply removed; git holds the history. Do not maintain a graveyard section.

# Sizing

A healthy contract at a single node has roughly **5 to 25 clauses**. Count per node, not per system:
a node whose promises have grown distributes them down to child nodes, each carrying the clauses
specific to it, and a cross-cutting clause moving to whichever node its consumers share — the parent,
or an intermediate machinery node they reach through `Requires`. Interpret the extremes:

- **Fewer than 3** — the node probably is not a boundary; it may be interior detail of another.
- **More than 40 at one node** — either the decomposition is wrong, or the contract has drifted into
  restating the public API. Distributing clauses down to child nodes is the relief valve, not a
  smell; method-level enumeration belongs in doc comments, not here.

# Changing a Contract

Changing a clause is the definition of a **Contract Change** (see `change-classification.md`) and is
the project's breaking-change signal:

- **Adding** a clause is additive; consumers are unaffected.
- **Narrowing or removing** a clause is breaking; it must be called out in the change summary, which
  is the durable record of it and what any release note is drawn from.
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
