<!-- TEMPLATE-DIRECTIVE: This is level 0 of the architecture tree - the 50,000 foot view. Read
     architecture-documentation.md and technical-documentation.md before writing it. It answers
     four questions: what is this, what does it give me, how does it work, and how do I start.
     Features and approach belong here, pitched at an altitude that does NOT change when a system
     changes - describe the value and the organizing idea, never the inventory. Do NOT list
     systems, restate contracts, describe internals, or write a feature list that mirrors contract
     clauses; those are owned by lower levels and restating them here creates the coupling this
     process exists to avoid. Present tense, no roadmap language - unmet needs belong in
     CONSTRAINTS.md or BACKLOG.md, not here. Use relative links for files inside this repository and
     absolute URLs for external resources. Remove this block. -->

# {ProjectName}

TODO: One paragraph naming what this product is and the problem it solves. Lead with the answer —
assume the reader stops after this paragraph.

TODO: One or two paragraphs on who it is for and what makes it worth using. Concrete, present tense.

## Features

<!-- TEMPLATE-DIRECTIVE: What the user gets, in their words, not yours. Each bullet is an outcome
     that stays true as the implementation changes. Bold lead-in, then one sentence. Six to ten
     bullets. If a bullet would need editing when a system changes, it is pitched too low. Remove
     this block. -->

- **TODO: outcome** — TODO: one sentence on what that means for the person using it.
- **TODO: outcome** — TODO: one sentence on what that means for the person using it.

## How It Works

<!-- TEMPLATE-DIRECTIVE: Two to four paragraphs on the organizing idea - the shape a reader needs
     before anything below makes sense. Name the central mechanism and what follows from it. Do NOT
     enumerate systems; overview.md owns that. Remove this block. -->

TODO: Two to four paragraphs on the organizing idea behind the product.

## Requirements

<!-- TEMPLATE-DIRECTIVE: The invariants of the product contract - properties that must hold for the
     features above to mean anything, written so someone can check this repository against them
     rather than argue about them. Each must say something no feature bullet already says; if it
     restates a feature, delete it. Pitch at the same altitude as Features: a requirement that a
     system change would force you to edit belongs to that system's contract instead. Prose, no
     identifiers and no named tests - level 0 is enforced by review, not by script. Three to six
     bullets. Remove this block. -->

- TODO: a property that must hold, stated so it can be checked.

## Assumptions

<!-- TEMPLATE-DIRECTIVE: What this design takes to be true and cannot itself guarantee - about the
     environment, the platform, the people, or the tools. An assumption is a belief the world could
     falsify; a constraint in CONSTRAINTS.md is a condition you have decided the system must meet.
     Test: could reality prove this wrong without anyone changing their mind? Then it is an
     assumption. Record only load-bearing ones - if it were false, the shape below would be wrong.
     Three to six bullets, or omit the section entirely if the design rests on nothing unusual. An
     assumption that is disproved is a re-cut trigger, not a bug. Remove this block. -->

- **TODO: the belief, in a bold lead-in** — TODO: what rests on it, and what would follow if it
  turned out to be false.

## Installation

```pwsh
TODO: exact installation commands
```

TODO: Exact version prerequisites.

## Usage

```pwsh
TODO: a real command
```

```text
TODO: its real expected output
```

## Architecture

See [Architecture Overview](docs/architecture/overview.md).

## License

TODO: license statement and link.
