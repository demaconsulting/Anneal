---
name: Coding Principles
description: Follow these standards when developing any software code.
---

# Core Principles

## Literate Coding

Doc comments are where interior design intent lives. This process has no requirements, design, or
verification artifacts below system level, so a doc comment is the **only** place a reason that
cannot be recovered from the code is recorded. That is what they are for — and it is also the test
for whether one is needed.

All code MUST follow literate programming principles:

- **Intent Documentation**: Function and method documentation (XmlDoc, Doxygen, JSDoc, etc.) MUST
  explain WHY the function exists and its design purpose — not just restate what it does
- **Logical Separation**: Complex functions use block comments to separate and describe logical steps
  within the implementation
- **Boundary Documentation (MANDATORY)**: Every publicly visible symbol is fully documented, to the
  API Documentation standard below. Consumers cannot recover any of it from a signature, and in C#
  this is a build error rather than a matter of discipline.
- **Interior Documentation (BY REASON)**: A private or internal symbol is documented when its intent
  is **not recoverable from the code** — a non-obvious constraint, an ordering requirement, a
  rejected simpler approach, a reason the obvious implementation is wrong. Apply the same test the
  architecture tree uses: *facts recoverable by reading the code do not belong here; reasons do.*
- **Clarity Over Cleverness**: Code should be immediately understandable by team members

**A doc comment that restates the signature is a defect, not compliance.** It cannot be told apart
from real intent without reading the code it was meant to save you reading, nothing checks it, and
the next agent copies the local convention — so filler propagates and the signal that a comment
means "stop, there is intent here" is lost. Where a private member's name and body already say
everything, the correct amount of documentation is none.

Interior doc comments follow the same lifecycle as interior tests: written where they protect
something worth protecting, and deleted without ceremony when their subject changes.

## API Documentation

This checklist applies to the **publicly visible surface** — the members a consumer outside the
system can reach. None of it is recoverable from a signature, which is why all of it is mandatory
here and none of it is mandatory on a private helper:

- **Self-Contained**: Each member's documentation must be fully understandable in isolation —
  consumers must not need to read the implementation to call it correctly
- **Intent-Focused**: Explain WHY the member exists and WHAT problem it solves, not just restate the
  name — this lets reviewers verify the implementation matches design intent
- **Parameter and Return Contracts**: Document valid ranges, null handling, and boundary cases —
  agents and consumers rely on these contracts to call the API correctly
- **Error Conditions**: Document every exception or error code, the condition that triggers it, and
  how the caller should respond — undocumented errors cannot be handled correctly
- **Side Effects**: Document I/O, state mutation, resource allocation, or network calls — hidden side
  effects cause integration bugs that are hard to diagnose
- **Thread Safety**: State whether the API is safe for concurrent use — missing this forces consumers
  to read the implementation or risk data races

## Design

- **Single Responsibility**: Functions with focused, testable purposes.
- **Dependency Injection**: External dependencies injected, so a consumer can substitute them —
  hidden construction is what makes code untestable without also making it obviously wrong.
- **Repository Structure Adherence**: Analyze existing directory conventions before creating files;
  place new files consistent with established patterns.
- **Error Handling**: Every error case handled deliberately. An error a caller cannot distinguish is
  an error they cannot respond to.
- **Resource Management**: Deterministic cleanup using language-appropriate patterns.
- **Minimalism**: The smallest code that reliably and correctly does the job is preferred. When
  fixing or extending, look first for code to delete — a defect removed is worth more than a check
  added.

# Quality Gates

## Code Quality Standards

- [ ] Zero compiler warnings (use language-specific warning-as-error flags)
- [ ] All code follows literate programming style
- [ ] Every publicly visible member is documented to the API Documentation standard
- [ ] Every interior member whose intent is not recoverable from the code is documented
- [ ] No doc comment merely restates the name or signature of what it documents
- [ ] Passes static analysis (language-specific tools)

## Universal Anti-Patterns

- **Skip Literate Coding**: Don't skip literate programming comments
- **Ignore Compiler Warnings**: Don't ignore compiler warnings
- **Hidden Dependencies**: Don't create untestable code with hidden dependencies
- **Undeclared Boundary Behavior**: Don't add consumer-observable behavior at a system boundary
  without a matching contract clause — undeclared behavior gets depended on and then cannot be changed
- **Monolithic Functions**: Don't write monolithic functions with multiple responsibilities
- **Overcomplicated Solutions**: Don't make solutions more complex than necessary
- **Premature Optimization**: Don't optimize for performance before establishing correctness
- **Copy-Paste Programming**: Don't duplicate logic — extract common functionality into a shared unit
- **Magic Numbers**: Don't use unexplained constants - either name them or add clear comments

# Language-Specific Implementation

For each detected language, read `{language}-language.md` from `.github/standards/` and apply its
standards, tooling, and conventions.
