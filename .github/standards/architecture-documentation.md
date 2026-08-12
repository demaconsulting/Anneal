---
name: Architecture Documentation
description: Follow these standards when creating or maintaining the progressive-disclosure architecture tree.
globs: ["README.md", ".anneal/architecture/**/*.md"]
---

# Purpose

The architecture tree is a **progressive-disclosure** map of the repository. A reader — human or
agent — starts at the top and descends only as far as the question requires. Each level answers a
different question at a different altitude.

The tree exists to make change **cheap**, not to make the design **immovable**. Every rule below
exists to keep documentation weight proportional to how slowly a thing changes.

**Finding your way around this standard.** `Decomposition and Ownership` carries the rule the rest of
this file elaborates; read it always. Then add only what the task needs: writing or judging a
`README.md` — `What Each Level Owns` and `Writing Guidelines`; adding or deleting a document — the two
subsystem-document rules and `Drift Anchors`.

# Levels of the Tree

The tree is a **recursive decomposition**. The rows below are **illustrative depths, not a closed
set**: every node may give birth to children, and the tree descends as far as subdivision keeps
earning its place.

| Level | File | Altitude | Answers |
| --- | --- | --- | --- |
| 0 | `README.md` | 50,000 ft | What is this product, what does it give me, and what parts is it built from? |
| 1 | `.anneal/architecture/overview.md` | 20,000 ft | What systems exist and how do they interact? |
| 2 | `.anneal/architecture/{system}.md` | 10,000 ft | What does this system promise, and how is it composed? |
| 3+ | `.anneal/architecture/{system}/{subsystem}.md` | 2,000 ft+ | How does one non-obvious specific work? |

**Levels are created when they are earned, never upfront.** A level exists because the level above
has grown content it cannot hold at its own altitude, or because that growth is already committed in
the repository — a written migration stage that names content moving into the node, or an admitted
backlog or constraint entry that commits to it — not because the table lists it, and not because "we
might need this later" alone. A repository whose whole story fits in `README.md` is correctly
documented, not under-documented; creating `overview.md` for it is the speculative structure this
process rejects everywhere else.

A node gives birth to children only when the organization benefits, and the benefit test is exactly
three triggers: **clarity of structure, conformity, or size.** There is no fixed depth cap and no
child-count cap; a node descends further whenever subdivision keeps earning its place, and a node
with no children is one that has not earned them yet.

# Descending the Tree

The tree is written to be **entered part-way and left early**. A reader descends by locating a part,
not by reading levels through: enter at the altitude that matches the question, read until a named
part matches the subject, then open the document that part points to.

**You have arrived when the document holds the detail you came for.** Two ways to get that wrong.
Stopping early leaves you holding a signpost that reads like knowledge and is not, because a parent
carries only a child's **scope and purpose** — what it covers and why you would descend — never the
child's conclusions; where no document owns the detail, say the tree does not record it rather than
inferring it from a signpost. Descending past the answer costs context on every read.

A node owns its own detail and subdivides into a child only when that child benefits the
organization; until then the node holds the detail itself. Level 0 confirms which product this is and
what it is built from; an agent already working in the repository rarely needs more of it than the
part names.

# Decomposition and Ownership (MANDATORY)

Every level that has parts **is a decomposition**. It names the parts at its own altitude, gives each
a one-line role, and states the barest relationships between them — what depends on, contains, or
talks to what. A reader must be able to locate the piece they came for and know which document
holds the next level of detail about it. A narrative — a story of a request moving through the
system — fails this even when every sentence is true: it gives the reader nothing to locate
themselves *on*. Structure therefore appears at more than one level by design, sharpening as it
descends, and that is not duplication — it is the handle a reader grabs to descend at all.

**A node with no children has no parts to name**, so it carries no decomposition list — not as an
exception, but because a leaf is a node that has not earned children yet. A document covering
one specific in depth — an invariant, a compatibility surface, a settled debate — is such a leaf, and
imposing the shape on it would invent structure that is not there. When it later earns children it
decomposes like any other node; it is exempt from the shape only while it stays a leaf, never from
the ownership rule below.

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
install it, and a pointer to `.anneal/architecture/overview.md`. Its parts are the **kinds** of thing the
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

Assumptions, tenets, and constraints are recorded elsewhere, under `.anneal/governance/` and
`.anneal/work/` respectively, so that an oracle prompt can inject the one it needs without the rest of
`README.md`'s installation and licensing prose; `README.md` links to them rather than restating them.
**Assumptions** (`.anneal/governance/assumptions.md`) are what the design takes to be true and cannot
itself guarantee — about its environment, platform, users, or tooling. Record only load-bearing ones,
where the shape of everything below would be wrong if the belief were false. Distinguish an assumption
from a **constraint** (`.anneal/work/constraints.md`) by asking whether reality could prove the
statement wrong without anyone changing their mind: if yes it is an assumption; if it could only change
by decision it is a constraint. Both sit conceptually at level 0 because they underpin the whole
decomposition; a belief that constrains only one system belongs in that system's decomposition
rationale instead.

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

**`{system}/{subsystem}.md`** — a child node covering one non-obvious specific in depth. It may carry
its own `## Contract` for promises specific to it (see `system-contracts.md`), and it does **not**
restate its parent's contract or decomposition.

# Navigation

This tree is read on disk and on the repository host, so **relative markdown links are required** for
downward navigation.

- Every level MUST link to each of its direct children.
- A child MUST link back to its parent in a single line at the top.
- Never link sideways across systems in body prose; route through `overview.md` so that
  cross-system coupling stays visible in exactly one place.

Downward links carry a brief **scope-and-purpose signpost** — what the child covers and why you would
descend, never the child's conclusions — in the shape shown under `Decomposition and
Ownership`. Paths are relative to the linking document — `overview.md` sits alongside the system
documents it links to.

# Drift Anchors (MANDATORY)

Every system document and every subsystem document beneath it, at any depth, begins with front matter
naming the source it describes:

```yaml
---
covers:
  - src/Ingest/**
---
```

`covers` names the source a document describes, so **drift** — source under `covers` changed while
the document did not — can be spotted. For routed Change work, this is `GeneralWorker`'s job as part
of the change itself (a `Documentation` concern), gating on completion. For Maintenance — whose front
door still keeps a wording-only exception outside the general route — a separate finish-time gate
(`TOOLKIT-57`) checks every architecture document covering an actually-changed file: a wording-only mismatch outside `## Contract` is corrected
inline and mechanically re-checked against the diff it produced; a mismatch touching `## Contract`
substance, or one that cannot be confidently told apart from touching it, is recorded as a neutral
finding under `.anneal/logs/findings/` rather than presumed in favor of either the document or the
code. `verify-change`'s own standalone drift review of already-completed work remains
advisory — a review flag, never a hard failure, because that path exists to review, not to redo,
the change.

# When to Create a Subsystem Document

A subsystem document is a child node, so the test is the benefit test that governs every node: create
one when subdividing benefits the organization through **clarity of structure, conformity, or size.**
A child earns its place when it sharpens structure a reader must navigate, gathers a rule that many
units must conform to, or lifts material the parent has grown too large to hold at its own altitude —
or material a migration stage or an admitted backlog/constraint entry has already committed to moving
there, distinct from speculative "we might need this later" structure.

Do **not** create a subsystem document to:

- Describe class or module structure — the code and the decomposition section already do this.
- Restate the public API — that belongs in doc comments on the members themselves.
- Describe what the tests cover — the tests do this.
- Record history or migration narrative — history belongs in git, and a migration in flight is
  described by `.anneal/work/active-plan.md`.
- Satisfy a sense that a system "ought to have" documentation.

# When to Delete a Subsystem Document (MANDATORY)

Deletion is a first-class action and is never deferred. Delete in the **same change** that triggers
it:

- The subject was removed or replaced.
- The content became derivable by reading a single file of source.
- The document has decayed into restating names and signatures.
- The rejected alternative it preserved is no longer a plausible option.

Whichever pass authors the change — routed `GeneralWorker`, or boundary work that stages a planned
obligation — MUST perform a prune check on every Contract Change and Structural Change: list the subsystem documents under the affected system and confirm each still
earns its place under the benefit test. Undeleted documentation is the mechanism by which a tree
silently becomes an anchor.

# Length

A document is the right length when a reader at that altitude finds what they came for and can stop
there; there is no
page count. Length should scale with the number of things at the document's **own** altitude, never
with the volume of code beneath it. The signal is never that a document is long — it is **what made
it long**:

| Document | Re-examine when it grew because |
| --- | --- |
| `README.md` | contract-level capabilities were enumerated, or a part described rather than placed |
| `overview.md` | a system's contract or interior is restated instead of linked |
| `{system}.md` | decomposition detail crept in that a subsystem document should own, or the system is too large |
| `{system}/{subsystem}.md` | it covers more than one thing child nodes should hold, or no longer earns its place |

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

- [ ] Every level with parts names them, their roles, and their barest relationships; no level
  restates a child's detail
- [ ] A reader stopping at a level can locate the part they came for and name the document
  holding the next level of detail about it
- [ ] The one-file test passes for the change just made
- [ ] Every document links to its direct children; children link back to their parent
- [ ] Every system document and subsystem document, at any depth, carries `covers` front matter
- [ ] Every subsystem document still earns its place under the benefit test
- [ ] Subsystem documents whose subject was removed were deleted in the same change
- [ ] No document is long for a reason that belongs at another level
