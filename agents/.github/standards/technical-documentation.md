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
  answer.
- **Current state only.** Documentation describes how things are, not how they came to be. History
  lives in git and release notes.
- **Concrete over abstract.** Real names, real commands, real output. Placeholders in prose are a
  defect.
- **Agent-legible.** Consistent heading hierarchy and fenced blocks with language identifiers, so
  documentation can be parsed as reliably as it is read.

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

- **Relative links** for anything inside the repository — they are how a reader descends the
  documentation tree.
- **Absolute URLs** in `README.md`, because it is rendered outside repository context by package
  registries and agents.
- **Verify link targets exist** when creating or moving files. A broken descent path defeats
  progressive disclosure.

# README.md

The README is level 0 of the architecture tree. Keep it to two or three paragraphs of substance
before the practical sections.

- **What it is** — the product, its audience, the problem it solves.
- **Installation** — exact commands, exact version prerequisites.
- **Usage** — one or two concrete examples with real expected output.
- **Architecture** — a single link to `docs/architecture/overview.md`.
- **License** — statement and link.

It does **not** enumerate features, list subsystems, or describe internals. Those are owned by lower
levels of the tree and restating them here creates the coupling this process exists to avoid.

# User Guides

Where a repository ships a user guide under `docs/`, it documents **how to use** the software — task
oriented, from the user's vocabulary. It is not a mirror of the architecture tree, which is
structure-oriented and written in the developer's vocabulary. Keeping the two separate prevents user
documentation from churning every time internals move.

# Quality Gates

- [ ] Markdown passes `pwsh ./lint.ps1`
- [ ] All fenced blocks carry a language identifier
- [ ] Relative links resolve; README uses absolute URLs
- [ ] README stays within three paragraphs before installation and does not describe internals
- [ ] No placeholder text or TODO markers remain
- [ ] Content reflects current state with no changelog voice
