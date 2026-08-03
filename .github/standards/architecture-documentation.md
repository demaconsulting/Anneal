---
name: Architecture Documentation
description: Follow these standards when creating or maintaining the progressive-disclosure architecture tree.
globs: ["README.md", "docs/architecture/**/*.md"]
---

# Purpose

The architecture tree is a **progressive-disclosure** map of the repository. A reader — human or
agent — starts at the top and descends only as far as the task requires. Each level answers a
different question at a different altitude.

The tree exists to make change **cheap**, not to make the design **immovable**. Every rule below
exists to keep documentation weight proportional to how slowly a thing changes.

# The Four Levels

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` | 50,000 ft | What is this product, what does it give me, and how does it work? |
| 1 | `docs/architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `docs/architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3 | `docs/architecture/{system}/{section}.md` | 2,000 ft | How does this one non-obvious specific work? |

**Levels are created when they are earned, never upfront.** A level exists because the level above it
has grown content it cannot hold at its own altitude — not because the table lists it. A small
repository whose whole story fits in `README.md` is correctly documented, not under-documented;
creating `overview.md` for it is the same speculative structure this process rejects everywhere else.

Level 3 is **optional and exceptional**. Most systems have zero or one section documents. A system
with more than five section documents is a signal that the system should be split, not documented
harder.

# Exclusive Ownership (MANDATORY)

Each level owns content that **no other level restates**. A parent may *name* its children and give
each a one-line role in the parent's composition. A parent MUST NOT summarize a child's content.

This is the single most important rule in this standard. Summaries create N-way coupling: every
child edit dirties its ancestors, and the tree becomes the same inertial mass this process exists to
avoid.

**The one-file test**: for any change, ask *"how many documentation files must I edit?"* If the
answer is more than one, ownership has been violated somewhere. Fix the duplication rather than
editing both files.

## What Each Level Owns

**`README.md`** — what the product is, what it gives its user, how it works in broad strokes, how to
install it, and a pointer to `docs/architecture/overview.md`.

Features and approach belong here, stated at an **altitude that does not change when a system
changes**: describe the value and the organizing idea, never the inventory. No lower level owns
"what the user gets" — contracts describe what systems promise *each other*, not what the product
gives a *person* — so there is nothing to duplicate and this is the only place it can live.

Level 0 is the **product contract**: the same idea as a system contract, one altitude up, with a
person as the consumer instead of other code. It has the same two parts. **Features** are its
clauses — what a consumer may rely on getting. **Requirements** are its invariants — properties that
must hold for those features to mean anything, written so a repository can be checked against them
rather than argued about. Removing or narrowing either is a **breaking change to users**, and
`system-contracts.md` defines what breaking means; say so in the change summary exactly as you would
for a clause.

The discipline carries up; the machinery does not. Product promises stay prose — no identifiers, no
named verifying test, and no mechanical check. Numbered requirements traceable to acceptance tests
are the regulated-development cost this process deliberately declines, and that cost would be paid on
every subsequent change.

It does **not** list systems, restate contracts, describe internals, or enumerate capabilities that
map one-to-one onto contract clauses. *"Rearrange the interior without paperwork"* is level 0;
*"supports CSV, JSON, and XML export"* is a **system** contract stated at the wrong altitude, and it
dirties this file every time a format is added. The test is whether a system changing would force an
edit here: if it would, the promise belongs to that system, not to the product.

`README.md` also owns the product's **assumptions**: what the design takes to be true and cannot
itself guarantee — about its environment, platform, users, or tooling. Record only load-bearing
ones, where the shape of everything below would be wrong if the belief were false. Distinguish them
from `CONSTRAINTS.md` by asking whether reality could prove the statement wrong without anyone
changing their mind: if yes it is an assumption, and if it could only change by decision it is a
constraint. Assumptions live at level 0 because they underpin the whole decomposition; a belief that
constrains only one system belongs in that system's decomposition rationale instead.

An assumption that is disproved is a **re-cut trigger**, not a defect to be patched. Say so plainly
in your report rather than adjusting the surrounding prose to keep the assumption looking true —
that is the level 0 form of editing a clause to match the code.

**`overview.md`** — the system inventory and the interactions *between* systems: data flow, control
flow, process and deployment boundaries, and repository-wide decisions that constrain every system
(language, runtime, error-handling philosophy, concurrency model). It does **not** describe what is
inside any system.

**`{system}.md`** — the system's `## Contract` (see `system-contracts.md`), its dependencies, its
internal decomposition and the *rationale* for that decomposition, and the decisions local to this
system. It does **not** restate the interactions already described in `overview.md`, and it does not
document individual classes.

**`{system}/{section}.md`** — exactly one non-obvious specific, in depth. It does **not** repeat the
system's contract or decomposition.

# Navigation

This tree is read three ways — on disk, on the repository host, and as the compiled architecture PDF
— so **relative markdown links are required** for downward navigation. They are how progressive
disclosure actually works, and `collection-links.lua` turns them into cross-references when the tree
is compiled.

- Every level MUST link to each of its direct children.
- A child SHOULD link back to its parent in a single line at the top.
- Never link sideways across systems in body prose; route through `overview.md` so that
  cross-system coupling stays visible in exactly one place.

Downward links carry a one-line role, never a summary. Paths are relative to the linking document —
`overview.md` sits alongside the system documents it links to:

```markdown
- [Ingest](./ingest.md) — accepts and validates inbound records
- [Store](./store.md) — durable persistence and query
```

# Drift Anchors (MANDATORY)

Every level 2 and level 3 document begins with front matter naming the source it describes:

```yaml
---
level: system
covers:
  - src/Ingest/**
---
```

`covers` names the source a document describes, so **drift** — source under `covers` changed while
the document did not — can be spotted. Nothing computes this today; the `tier-check` agent judges
it by reading. Drift is advisory either way: it raises a review flag, never a hard failure. Blocking
gates on every file change are precisely what makes evolution expensive.

# When to Create a Section Document

Create a level 3 document only when the subject meets at least one of these tests:

- **Hidden invariant** — something a reader would plausibly violate, that the code cannot express.
- **Non-local correctness** — an algorithm whose correctness depends on constraints not visible at
  the call site.
- **Compatibility surface** — a wire format, file format, schema, or protocol that must remain
  compatible across versions.
- **Settled debate** — a decision with a seriously considered rejected alternative, documented so it
  is not re-litigated.
- **Cross-cutting mechanism** — a pattern that many units must participate in correctly.

Do **not** create a section document to:

- Describe class or module structure — the code and the decomposition section already do this.
- Restate the public API — that belongs in doc comments on the members themselves.
- Describe what the tests cover — the tests do this.
- Record history or migration narrative — history belongs in git, and a migration in flight is
  described by `MIGRATION.md`.
- Satisfy a sense that a system "ought to have" documentation.

# When to Delete a Section Document (MANDATORY)

Deletion is a first-class action and is never deferred. Delete in the **same change** that triggers
it:

- The subject was removed or replaced.
- The content became derivable by reading a single file of source.
- The document has decayed into restating names and signatures.
- The rejected alternative it preserved is no longer a plausible option.

The `architecture-update` agent MUST perform a prune check on every Tier 1 (Contract) and Tier 2
(Structural) change: list the section documents under the affected system and confirm each still
meets a creation test. Undeleted documentation is the mechanism by which a tree silently becomes an
anchor.

# Publishing (MANDATORY)

The tree is a document collection: `docs/architecture/definition.yaml` lists its files in reading
order, and `docs/architecture/build.bat` compiles them into one PDF. **Adding or deleting a document
means editing that list in the same change.** An unlisted document is absent from the published
architecture no matter how good it is, and a listed file that no longer exists fails the build.

Place a new system document after the systems it depends on, and a section document immediately
after its parent system, so the compiled document reads top-down.

# Length

A document is the right length when a reader at that altitude can stop there and act. There is no
page count: a hello-world repository and a product of eleven systems both have a correct `README.md`,
and they are not the same size.

Length should scale with the number of things at the document's **own** altitude — features for the
README, systems for `overview.md`, promises and parts for a system document — and never with the
volume of code beneath it. So the signal is never that a document is long. It is **what made it
long**:

| Document | Re-examine when it grew because |
| --- | --- |
| `README.md` | capabilities are enumerated rather than summarized, or a system's internals surfaced |
| `overview.md` | a system's contract or interior is restated instead of linked |
| `{system}.md` | decomposition detail crept in that a section document should own, or the system is too large |
| `{system}/{section}.md` | it covers more than one non-obvious thing, or its subject no longer meets a creation test |

Each of those is a reason to move material to the level that owns it, or to delete it. None is a
reason to trim prose that earns its place: a short document missing the *why* is the more expensive
failure, because the reader then has to descend.

# Writing Guidelines

- **State the why.** Facts recoverable by reading the code do not belong here; reasons do.
- **Prefer prose to bullets** for rationale. Bullets fragment reasoning into assertions.
- **Name concrete things.** Real system names, real paths, real formats — never placeholders.
- **Write for a reader who will stop here.** Each level must be coherent alone.
- **Present tense, current state.** No changelog voice, no "we will", no "recently changed".

# Markdown Conventions

- 120-character line limit; break at punctuation or logical boundaries.
- ATX headings, blank lines around headings, lists, and fenced blocks.
- Language identifiers on all fenced code blocks.
- Relative links for intra-repository navigation; absolute URLs for external resources.

# Quality Gates

- [ ] Every level owns distinct content; no level summarizes a level below it
- [ ] The one-file test passes for the change just made
- [ ] Every document links to its direct children; children link back to their parent
- [ ] Level 2 and 3 documents carry `level` and `covers` front matter
- [ ] Every section document still satisfies at least one creation test
- [ ] Section documents whose subject was removed were deleted in the same change
- [ ] Every added or deleted document was listed or unlisted in `definition.yaml` in the same change
- [ ] No document is long for a reason that belongs at another level
