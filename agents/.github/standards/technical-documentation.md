---
name: Technical Documentation
description: Follow these standards when writing README, user guide, or other repository markdown.
globs: ["**/*.md", "!docs/architecture/**"]
---

# Scope

These are the general markdown and README conventions for the repository. The architecture tree has
its own stricter rules — see `architecture-documentation.md`, which takes precedence for anything
under `docs/architecture/`.

# Core Principles

- **Written to be read partially.** Assume the reader stops after the first screen. Front-load the
  answer, and apply the stop test below.
- **Current state only.** Documentation describes how things are, not how they came to be. History
  lives in git and release notes.
- **Concrete over abstract.** Real names, real commands, real output. Placeholders in prose are a
  defect.
- **Agent-legible.** Consistent heading hierarchy and fenced blocks with language identifiers, so
  documentation can be parsed as reliably as it is read.

# Progressive Disclosure Within a Document

The architecture tree discloses progressively *across* files. Every document must do the same
*inside itself*. A reader who stops early is not a failure of the reader — it is the normal case,
and the document is what has to accommodate it.

Both audiences read in chunks. A human reads the first screen and decides whether to scroll; an
agent reads the file in roughly hundred-line reads and decides whether to read on. This is the same
constraint, so it gets one answer. **Treat the first hundred lines as the document's most valuable
space, and the first thirty as the most valuable of all.** Anything that displaces them — badges,
boilerplate, a long preamble — is spending that budget on nothing.

## The Two Tests

**The stop test (MANDATORY).** Cover everything after the first screen and ask: *is what remains
true, useful, and not misleading?* If a reader who stopped there would act incorrectly, describe the
thing wrongly to a colleague, or walk away without the central mechanism, the opening is wrong — not
the reader.

**The chunk test (MANDATORY).** Read only the first hundred lines. From those alone a reader must be
able to answer both *what is this document for* and *does it contain what I came for, and where*.
The first is comprehension; the second is routing, and it is the one documents usually fail. A reader
who has to scan the whole file to discover it holds nothing relevant has been charged the full price
for no answer.

## Rules

- **The first chunk carries a fuzzy but complete picture.** What it is, what it gives, and *how it
  works*, in outline. An opening carrying only benefits produces a reader who can advocate for the
  thing but can neither use it nor evaluate it.
- **The first chunk carries the map.** What lies below must be visible from the top — as headings
  reached within that budget, or as an explicit contents list when they are not. If the map cannot
  fit, the document is too long or too flat: split it, or give it sections.
- **Headings are routing signals, not decoration.** A heading is read out of context, in a contents
  list or a search result, by someone who has not read the section above it. Name the content, not
  the theme.
- **Later sections refine; they never revise.** Reading further sharpens the same picture. Nothing
  below may contradict or reframe what came above. If a later section changes what the opening
  meant, the opening is the defect.
- **Define a term where it is first used.** A word carrying a special meaning is defined at first
  use — not in a later section, and never by implication.
- **Every claim carries its mechanism.** A claim stated without what makes it true reads as
  marketing, and is discounted by exactly the readers worth convincing.
- **Separate what is enforced from what is asked.** Where a document describes both, say plainly
  which is which. Blurring them costs more credibility than the unenforced half was worth.
- **State the cost early.** A document advocating an approach names its cost and its limits near the
  top. A document that only sells is read as a sales document, and discounted accordingly.
- **Sections are entered cold.** Detail lives in named sections a reader can jump straight into.
  Assume each is reached from a link or a search, not from the section above it.

## Anti-Patterns

- **Benefits before mechanism.** Every advantage listed, with the explanation deferred to a later
  section the reader may never reach.
- **A first chunk with no map.** The opening explains the subject well and gives no indication of
  what else the document holds, so the only way to route is to read all of it.
- **Slogans in place of information.** A memorable phrase that carries no content — it survives
  editing because it sounds good, and teaches nothing.
- **Terms of art used before they are defined**, on the assumption the reader shares the author's
  vocabulary.
- **Limitations at the end.** Costs and exclusions placed after the reader has already decided.

# Markdown Format Requirements

Follow `.markdownlint-cli2.yaml`:

- **120-character line limit** — break at punctuation or logical boundaries, never mid-code-span or
  mid-URL.
- **No trailing whitespace.**
- **Blank lines** around headings, lists, and fenced code blocks.
- **ATX headings** (`#`), never underline style.
- **2-space indentation** for nested list items.
- **Language identifiers** on every fenced code block.

# Links

This repository's documentation is read on disk and on the web, not compiled into a single PDF, so
ordinary markdown links work and are encouraged:

- **Relative links** for anything inside the repository — including from `README.md`. They resolve
  both on disk and on the repository host, and they are how a reader descends the documentation tree.
- **Absolute URLs** for external resources, and for anything that must survive being rendered
  outside repository context (a package registry description, for example).
- **Verify link targets exist** when creating or moving files. A broken descent path defeats
  progressive disclosure.

# README.md

The README is level 0 of the architecture tree. `architecture-documentation.md` owns what it may
contain; this section covers only its shape.

- **What it is** — the product, its audience, the problem it solves.
- **Features** — what the user gets, as outcomes that survive implementation change.
- **How it works** — two to four paragraphs on the organizing idea.
- **Installation** — exact commands, exact version prerequisites.
- **Usage** — one or two concrete examples with real expected output.
- **Architecture** — a single link to `docs/architecture/overview.md`.
- **License** — statement and link.

It does **not** list subsystems, restate contracts, or describe internals. Those are owned by lower
levels of the tree and restating them here creates the coupling this process exists to avoid.

# User Guides

Where a repository ships a user guide under `docs/`, it documents **how to use** the software — task
oriented, from the user's vocabulary. It is not a mirror of the architecture tree, which is
structure-oriented and written in the developer's vocabulary. Keeping the two separate prevents user
documentation from churning every time internals move.

# Quality Gates

- [ ] Markdown passes `pwsh ./lint.ps1`
- [ ] All fenced blocks carry a language identifier
- [ ] Relative links resolve; absolute URLs used for external resources
- [ ] README stays within three paragraphs before installation and does not describe internals
- [ ] No placeholder text or TODO markers remain
- [ ] Content reflects current state with no changelog voice
