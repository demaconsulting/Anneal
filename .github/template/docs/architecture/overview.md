---
level: overview
covers:
  - src/**
---

<!-- TEMPLATE-DIRECTIVE: This is level 1 - the 20,000 foot view. Read
 architecture-documentation.md before writing it. This file owns the system inventory
 and the interactions BETWEEN systems. It does NOT describe what is inside any system.
 Give each system exactly one line of role in the composition - never a summary of its
 contents, because a summary makes every system change dirty this file. Length follows the
 number of systems, never the size of the code inside them. Remove this block. -->

[← Project README](../../README.md)

# Overview

TODO: One paragraph on the shape of the system as a whole — the organizing idea a reader needs
before the inventory below makes sense.

## Systems

<!-- TEMPLATE-DIRECTIVE: One entry per system. One line each, describing its ROLE in this
 composition - not its contents. Link to its level 2 document. Remove this block. -->

- [{System Name}](./{system-name}.md) — TODO: one-line role in the composition
- [{Other System}](./{other-system}.md) — TODO: one-line role in the composition

## Interactions

<!-- TEMPLATE-DIRECTIVE: Data flow and control flow BETWEEN systems. This is the one place
 cross-system coupling is described, so it stays visible in a single file. Prose, or a
 fenced diagram. Name the mechanism (in-process call, HTTP, queue, shared file) and the
 direction. Remove this block. -->

TODO: How the systems above communicate, in what direction, over what mechanism.

## Boundaries

<!-- TEMPLATE-DIRECTIVE: Process, deployment, and trust boundaries. Which systems share a
 process, which are separately deployed, where untrusted input enters. Omit this section
 entirely if everything runs in one process and there is nothing to say. Remove this block. -->

TODO: Process, deployment, and trust boundaries — or delete this section if there are none.

## Repository-Wide Decisions

<!-- TEMPLATE-DIRECTIVE: Only decisions that constrain EVERY system - language, runtime,
 error-handling philosophy, concurrency model, persistence strategy. Decisions local to
 one system belong in that system's document. State the decision and the reason; a
 decision without a reason will be re-litigated. Remove this block. -->

**TODO: Decision name** — TODO: what was decided, and why. Name the alternative that was rejected
and what would have to change for it to be reconsidered.
