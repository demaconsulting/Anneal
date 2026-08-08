<!-- TEMPLATE-DIRECTIVE: This is level 0 of the architecture tree - the 50,000 foot view. It answers
 four questions: what is this, what does it give me, how does it work, and how do I start. The
 sections below are its shape, and each carries its own directive. What may and may not appear
 at this level is owned by `.github/standards/architecture-documentation.md`; read it and
 `technical-documentation.md` before writing. Present tense, no roadmap language - unmet needs
 belong in CONSTRAINTS.md or BACKLOG.md, not here. Use relative links for files inside this
 repository and absolute URLs for external resources. Remove this block. -->

# {ProjectName}

TODO: One paragraph naming what this product is and the problem it solves. Lead with the answer —
assume the reader stops after this paragraph.

TODO: One or two paragraphs on who it is for and what makes it worth using. Concrete, present tense.

## Features

<!-- TEMPLATE-DIRECTIVE: What the user gets, in their words, not yours. Each bullet is an outcome
 that stays true as the implementation changes. Bold lead-in, then one sentence. One bullet per
 outcome a user would name and no padding - usually six to ten, fewer for a small
 product. If a bullet would need editing when a system changes, it is pitched too low. Remove
 this block. -->

- **TODO: outcome** — TODO: one sentence on what that means for the person using it.
- **TODO: outcome** — TODO: one sentence on what that means for the person using it.

## Requirements

<!-- TEMPLATE-DIRECTIVE: The invariants of the product contract - properties that must hold for the
 features above to mean anything, written so someone can check this repository against them
 rather than argue about them. Each must say something no feature bullet already says; if it
 restates a feature, delete it. Pitch at the same altitude as Features: a requirement that a
 system change would force you to edit belongs to that system's contract instead. Prose, no
 identifiers and no named tests - level 0 is enforced by review, not by script. As many as
 hold, usually three to six. Remove this block. -->

- TODO: a property that must hold, stated so it can be checked.

## How It Works

<!-- TEMPLATE-DIRECTIVE: The organizing idea a reader needs before anything below makes sense, then
 the product's gross structure: the kinds of part it is built from, each with its path and a
 one-line role, and a short account of how they meet. One line per part - a part is placed here
 and described by the document that owns it. Remove this block. -->

TODO: One or two paragraphs on the organizing idea behind the product.

TODO: The parts it is built from - path and one-line role each - and how they meet.

## Assumptions

<!-- TEMPLATE-DIRECTIVE: What this design takes to be true and cannot itself guarantee - about the
 environment, the platform, the people, or the tools. An assumption is a belief the world could
 falsify; a constraint in CONSTRAINTS.md is a condition you have decided the system must meet.
 Test: could reality prove this wrong without anyone changing their mind? Then it is an
 assumption. Record only load-bearing ones - if it were false, the shape below would be wrong.
 Usually three to six, or omit the section entirely if the design rests on nothing unusual. An
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

## Technology

<!-- TEMPLATE-DIRECTIVE: The languages and platforms this repository is written in. Agents read this
 to choose which standards to load, so name the language exactly (`C#`, not ` .NET languages`).
 Stable by design - this changes when the product is re-platformed, not when a system changes.
 Do NOT list libraries, packages, or tools; those belong to the systems that use them. One or two
 bullets. Remove this block. -->

- **Languages** — TODO: languages used, e.g. `C#`
- **Platform** — TODO: key platform or framework, e.g. `.NET 8`

## Architecture

See [Architecture Overview](docs/architecture/overview.md).

## License

TODO: license statement and link.
