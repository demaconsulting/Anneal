---
level: system
covers:
  - src/{SystemName}/**
---

<!-- TEMPLATE-DIRECTIVE: This is level 2 - the 10,000 foot view, one file per system.
     Read architecture-documentation.md and system-contracts.md before writing it.
     This file owns the system's CONTRACT, its dependencies, its internal decomposition
     and the RATIONALE for it, and decisions local to this system. It does NOT restate
     the interactions described in overview.md, and it does NOT document individual
     classes - that intent lives in doc comments on the members themselves.
     Budget: 3 pages. Remove this block. -->

[← Architecture Overview](./overview.md)

# {System Name}

TODO: One paragraph on what this system is responsible for and why it exists as a separate system.
The second half matters most: what would a consumer notice if this were rewritten?

## Contract

<!-- TEMPLATE-DIRECTIVE: Below the README this is the ONLY requirement-like artifact in this
     repository. Every clause must be observable from OUTSIDE the system - if it can only be checked
     by reading internals, delete it. Every clause names at least one contract test that
     exists and passes. IDs are stable forever and retired numbers are never reused.
     Healthy range is 5-25 clauses; over 40 means the decomposition is wrong or this has
     drifted into API documentation. Remove this block. -->

### Provides

- **{SYSTEM}-01** — TODO: observable behavior a consumer may rely on, in WHAT terms.
  *Verified by:* `TODO.ContractTestName`

- **{SYSTEM}-02** — TODO: observable behavior a consumer may rely on.
  *Verified by:* `TODO.ContractTestName`

### Requires

<!-- TEMPLATE-DIRECTIVE: What this system depends on, named by ADVERTISED BEHAVIOR, never
     by internal design. Other systems in this repository, and third-party dependencies
     whose behavior is relied upon. Remove this block. -->

- **{Other System}** — TODO: the behavior relied upon.
- **{Third-Party Package}** — TODO: the behavior relied upon.

### Invariants

<!-- TEMPLATE-DIRECTIVE: Properties no single call can demonstrate - ordering, idempotency,
     thread safety, resource bounds, version compatibility. Delete this section if the
     system genuinely has none. Remove this block. -->

- **{SYSTEM}-I1** — TODO: the property that must always hold.
  *Verified by:* `TODO.ContractTestName`

## Composition

<!-- TEMPLATE-DIRECTIVE: How the system is put together internally and - more importantly -
     WHY it is cut that way. Name the internal parts and the seam between them. Do not
     enumerate classes; if the list starts reading like a directory listing, cut it back to
     the seams that carry the reasoning. Remove this block. -->

TODO: The internal decomposition and the reasoning behind those seams. What was grouped together
and what was deliberately kept apart, and what would break if the seams moved.

## Decisions

<!-- TEMPLATE-DIRECTIVE: Decisions local to this system. State the decision, the reason, and
     the rejected alternative. Omit anything already covered by the repository-wide decisions
     in overview.md. Remove this block. -->

**TODO: Decision name** — TODO: what was decided, why, and what was rejected.

## Details

<!-- TEMPLATE-DIRECTIVE: Links to level 3 section documents, if any exist. MOST SYSTEMS HAVE
     NONE - delete this whole section rather than inventing entries for it. Create a section
     document only when the subject meets a creation test in architecture-documentation.md.
     Remove this block. -->

- [{Section Name}](./{system-name}/{section-name}.md) — TODO: one-line subject
