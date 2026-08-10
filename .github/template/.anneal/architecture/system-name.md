<!-- TEMPLATE-DIRECTIVE: This is level 2 of the architecture tree — one file per system.
     It carries: ## Contract (Provides, Requires, Invariants), the system's internal decomposition
     and rationale for that decomposition, and decisions local to this system. It does NOT restate
     interactions already in overview.md, and does NOT document individual classes or methods.
     Rename this file from {system-name}.md to the system's actual kebab-case name.
     Remove this block. -->

---
level: system
covers:

- src/{SystemName}/**

---

[← Architecture Overview](./overview.md)

# {SystemName}

TODO: One paragraph on what this system does and why it exists. Present tense.

## Contract

### Provides

- **{SYSTEM}-01** — TODO: observable behavior a consumer may rely on.
  *Verified by:* `TODO.{SystemName}ContractTest`

### Requires

- TODO: what this system depends on, by advertised behavior.

### Invariants

- **{SYSTEM}-I1** — TODO: a property no single call can demonstrate.
  *Verified by:* `TODO.{SystemName}InvariantTest`

## Composition

TODO: The internal parts of this system, each with its path, one-line role, and barest
relationships. Rationale for the decomposition — why these parts, why this boundary.

## Decisions

TODO: Decisions local to this system — choices made, alternatives rejected, and why.
