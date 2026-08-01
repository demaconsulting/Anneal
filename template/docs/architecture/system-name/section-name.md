---
level: section
covers:
  - src/{SystemName}/{RelevantPath}/**
---

<!-- TEMPLATE-DIRECTIVE: This is level 3 - the 2,000 foot view. IT IS EXCEPTIONAL.
     Before writing this file, confirm the subject meets at least one creation test from
     architecture-documentation.md:
       - Hidden invariant a reader would plausibly violate
       - Non-local correctness constraint not visible at the call site
       - Compatibility surface (wire format, file format, schema, protocol)
       - Settled debate with a seriously considered rejected alternative
       - Cross-cutting mechanism many units must participate in correctly
     If none apply, DO NOT CREATE THIS FILE. Documenting class structure, restating the
     public API, describing test coverage, or recording history are all disqualifying.
     This document is DELETED in the same change that obsoletes its subject.
     Budget: 2 pages. Remove this block. -->

[← {System Name}](../{system-name}.md)

# {Section Name}

TODO: One paragraph stating what this document covers and, explicitly, which creation test it meets.
If you cannot name the test, delete the file.

## TODO: The Substance

TODO: The actual content — the invariant, the algorithm and its constraints, the format
specification, or the settled decision. Explain the reasoning a reader cannot recover from the code.

Facts recoverable by reading the source do not belong here. Reasons do.

## Consequences

<!-- TEMPLATE-DIRECTIVE: What a developer must do, or must not do, as a result of the above.
     This is the section that earns the document its place - it is what a reader was going to
     get wrong. Remove this block. -->

TODO: What this obliges a developer touching this code to honor, and what breaks if they do not.
