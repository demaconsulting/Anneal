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

**Finding your way around this standard.** `Decomposition and Ownership` carries the rule the rest of
this file elaborates; read it always. Then add only what the task needs: writing or judging a
`README.md` — `What Each Level Owns` and `Writing Guidelines`; adding or deleting a document — the two
section-document rules, `Drift Anchors` and `Publishing`.

# The Four Levels

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` | 50,000 ft | What is this product, what does it give me, and what parts is it built from? |
| 1 | `docs/architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `docs/architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3 | `docs/architecture/{system}/{section}.md` | 2,000 ft | How does this one non-obvious specific work? |

**Levels are created when they are earned, never upfront.** A level exists because the level above
has grown content it cannot hold at its own altitude — not because the table lists it. A repository
whose whole story fits in `README.md` is correctly documented, not under-documented; creating
`overview.md` for it is the speculative structure this process rejects everywhere else.

Level 3 is **optional and exceptional**. Most systems have zero or one section documents. More than
five is a signal that the system should be split, not documented harder.

# Descending the Tree

The tree is written to be **entered part-way and left early**. A reader descends by locating a part,
not by reading levels through: enter at the altitude that matches the question, read until a named
part matches the thing about to change, then open the document that part points to. **Stop at the
level that answers the question.** Descending past it is how a small change acquires a large context,
and the cost of that is paid on every change, not once.

Level 0 confirms which product this is and what it is built from; an agent already working in the
repository rarely needs more of it than the part names. A change confined to one system needs that
system's document, and from its ancestors only the line that named it.

**You have arrived** when the document names the thing you are about to change as one of its *own*
parts. A level that names it only as somewhere to descend into is one level too high. A mechanism
*inside* a part is documented at level 3 only when it independently meets a creation test below;
otherwise it belongs to the system document and you have already arrived.

# Decomposition and Ownership (MANDATORY)

Every level is a **decomposition**. It names the parts at its own altitude, gives each a one-line
role, and states the barest relationships between them — what depends on, contains, or talks to
what. A reader must be able to locate the piece they are about to touch and know which document
holds the next level of detail about it. A narrative — a story of a request moving through the
system — fails this even when every sentence is true: it gives the reader nothing to locate
themselves *on*. Structure therefore appears at more than one level by design, sharpening as it
descends, and that is not duplication — it is the handle a reader grabs in order to descend at all.

A decomposition is this shape, and rarely needs more:

```markdown
- [Ingest](./ingest.md) — accepts and validates inbound records; rejects to Quarantine
- [Store](./store.md) — durable persistence and query; the only writer to the database
- [Report](./report.md) — reads Store, never writes; renders scheduled extracts
```

Each part is named once, given a role, placed against its siblings, and pointed at. What Ingest
accepts, how Store indexes, and why Report may not write belong to those three documents.

**Detail** — how a part works, what it promises, what is inside it — lives at exactly one level, and
a parent MUST NOT restate a child's detail. Restated detail creates N-way coupling: every child edit
dirties its ancestors, and the tree becomes the same inertial mass this process exists to avoid.

**The one-file test**: for any change, ask *"how many documents must I edit to state this detail?"*
More than one means ownership has been violated; fix the duplication rather than editing both files.
Adding, removing or renaming a *part* is the single expected exception — the document that owns it,
plus the one line naming it in its parent. Needing more than that line means the parent is carrying
detail it does not own.

## What Each Level Owns

**`README.md`** — what the product is, what it gives its user, the parts it is built from, how to
install it, and a pointer to `docs/architecture/overview.md`. Its parts are the **kinds** of thing the
product is built from, which need not match level 1's system inventory: adding a system always edits
`overview.md`, and edits `README.md` only if it introduces a new kind of part.

Features and approach belong here at an **altitude that does not change when a system changes**: the
value and the organizing idea, never the inventory of promises. No lower level owns "what the user
gets" — contracts describe what systems promise *each other*, not what the product gives a *person*.

Level 0 is the **product contract**: a system contract one altitude up, with a person as the
consumer instead of other code, and it has the same two parts. **Features** are its clauses — what a
consumer may rely on getting. **Requirements** are its invariants — properties that must hold for
those features to mean anything, written so a repository can be checked against them rather than
argued about. Removing or narrowing either is a **breaking change to users**; `system-contracts.md`
defines what breaking means, and you say so in the change summary exactly as for a clause.

The discipline carries up; the machinery does not. Product promises stay prose — no identifiers, no
named verifying test, no mechanical check. Numbered requirements traceable to acceptance tests are
the regulated-development cost this process declines, and it would be paid on every later change.

Level 0 also carries the product's **gross structure**: the kinds of part it is built from, each
with its path and a one-line role, and a short account of how they meet. This map is required — it
is what lets a reader pick where to descend — and it is cheap, because a part is one line. What it
must **not** do is restate contracts, describe internals, or enumerate capabilities that map
one-to-one onto contract clauses. *"Rearrange the interior without paperwork"* is level 0;
*"supports CSV, JSON, and XML export"* is a **system** contract at the wrong altitude, and it dirties
this file whenever a format is added. The test: would a system changing its *promises or interior*
force an edit here? If so it belongs to that system, not to the product.

`README.md` also owns the product's **assumptions**: what the design takes to be true and cannot
itself guarantee — about its environment, platform, users, or tooling. Record only load-bearing
ones, where the shape of everything below would be wrong if the belief were false. Distinguish them
from `CONSTRAINTS.md` by asking whether reality could prove the statement wrong without anyone
changing their mind: if yes it is an assumption; if it could only change by decision it is a
constraint. They live at level 0 because they underpin the whole decomposition; a belief that
constrains only one system belongs in that system's decomposition rationale.

An assumption that is disproved is a **re-cut trigger**, not a defect to patch. Say so in your
report rather than adjusting prose to keep the assumption looking true — that is the level 0 form of
editing a clause to match the code.

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

This tree is read on disk, on the repository host, and as the compiled architecture PDF, so
**relative markdown links are required** for downward navigation; `collection-links.lua` turns them
into cross-references when the tree is compiled.

- Every level MUST link to each of its direct children.
- A child MUST link back to its parent in a single line at the top.
- Never link sideways across systems in body prose; route through `overview.md` so that
  cross-system coupling stays visible in exactly one place.

Downward links carry a one-line role, never a summary, in the shape shown under `Decomposition and
Ownership`. Paths are relative to the linking document — `overview.md` sits alongside the system
documents it links to.

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
the document did not — can be spotted. Nothing computes it; the `tier-check` agent judges it by
reading, and it is advisory either way: a review flag, never a hard failure. Blocking gates on every
file change are precisely what makes evolution expensive.

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
architecture, and a listed file that no longer exists fails the build.

Place a new system document after the systems it depends on, and a section document immediately
after its parent system, so the compiled document reads top-down.

# Length

A document is the right length when a reader at that altitude can stop there and act; there is no
page count. Length should scale with the number of things at the document's **own** altitude, never
with the volume of code beneath it. The signal is never that a document is long — it is **what made
it long**:

| Document | Re-examine when it grew because |
| --- | --- |
| `README.md` | contract-level capabilities were enumerated, or a part described rather than placed |
| `overview.md` | a system's contract or interior is restated instead of linked |
| `{system}.md` | decomposition detail crept in that a section document should own, or the system is too large |
| `{system}/{section}.md` | it covers more than one non-obvious thing, or its subject no longer meets a creation test |

Each is a reason to move material to the level that owns it, or to delete it — never to trim prose
that earns its place. A short document missing the *why* is the more expensive failure.

# Writing Guidelines

- **State the why.** Facts recoverable by reading the code do not belong here; reasons do.
- **Prefer prose to bullets** for *rationale*; bullets fragment reasoning into assertions. A
  structural map is the exception — parts, roles and relationships read best as a list.
- **Name concrete things.** Real system names, real paths, real formats — never placeholders.
- **Write for a reader who will stop here.** Each level must be coherent alone.
- **Present tense, current state.** No changelog voice, no "we will", no "recently changed".

# Markdown Conventions

`technical-documentation.md` owns these; follow it. Only the link rule is specific to this tree, and
`Navigation` above states it.

# Quality Gates

- [ ] Every level names its parts, their roles, and their barest relationships; no level restates a
      child's detail
- [ ] A reader stopping at a level can locate the part they must change and name the document
      holding the next level of detail about it
- [ ] The one-file test passes for the change just made
- [ ] Every document links to its direct children; children link back to their parent
- [ ] Level 2 and 3 documents carry `level` and `covers` front matter
- [ ] Every section document still satisfies at least one creation test
- [ ] Section documents whose subject was removed were deleted in the same change
- [ ] Every added or deleted document was listed or unlisted in `definition.yaml` in the same change
- [ ] No document is long for a reason that belongs at another level
