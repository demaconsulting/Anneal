---
level: system
covers:
  - .github/template/**
  - docs/build-doc.ps1
  - docs/template/**
---

[← Architecture Overview](./overview.md)

# Template

Template defines the repository a product gets, as opposed to the process it follows. It owns the
canonical layout — which files exist, where they sit, and what shape each has when empty — together with
the scripts that layout ships with: `lint.ps1`, `fix.ps1`, `build.ps1`, and the
document pipeline that compiles a folder of markdown into HTML and PDF.

It is a system rather than inert content because it has a consumer that never installs anything. The
`template-sync` agent reads `repository-map.md` to audit an existing repository against the canonical
layout, and to patch what is missing. That consumer depends on the map being complete and accurate, which
is a promise independent of whether anyone ever runs `install.ps1`.

The boundary against [Process](./process.md) is worth stating precisely, because the two systems both
touch `AGENTS.pristine.md`: **Process owns what that file says; Template owns that a correct, unmodified
copy of it is present in the layout.** Content versus placement. A wording change edits Process, a layout
change edits Template, and neither dirties the other.

## Contract

### Provides

- **TEMPLATE-01** — Provides a complete repository layout: every file a repository following this
  process requires, in its canonical location.
  *Verified by:* `LayoutIsComplete`

- **TEMPLATE-02** — Provides `repository-map.md` listing every file in the layout with its role, so an
  audit can be performed against the map alone.
  *Verified by:* `RepositoryMapListsEveryFile`

- **TEMPLATE-03** — Ships every file it describes, so no map entry names a file the template does not
  contain and no template file is absent from the map.
  *Verified by:* `MapAndTemplateAgree`

- **TEMPLATE-04** — Ships `AGENTS.pristine.md` containing no project-specific values, so a target
  repository needs no post-install editing and an upgrade may replace it outright.
  *Verified by:* `PristineCarriesNoProjectValues`

- **TEMPLATE-05** — Compiles any folder containing a `definition.yaml` collection into HTML and then PDF,
  resolving relative links between documents in that collection into cross-references.
  *Verified by:* `TODO.CollectionCompilesToPdf`

- **TEMPLATE-06** — Ships template documents whose placeholders and directives are machine-recognizable,
  so an agent filling one in can confirm none remain.
  *Verified by:* `DirectivesAreRecognizable`

- **TEMPLATE-07** — Ships a .NET tool manifest pinning the [Toolkit](./toolkit.md) version, so a
  repository can restore the tool.
  *Verified by:* `ToolManifestIsShipped`

### Requires

- **[Process](./process.md)** — the content of `AGENTS.pristine.md` and of the standards the layout
  expects to be installed alongside it.
- **Pandoc** — markdown to HTML and PDF conversion, with Lua filter support.

### Invariants

- **TEMPLATE-I1** — The layout is valid for a C# product repository regardless of Anneal's own needs; a
  root file in this repository may diverge from its template counterpart, but never the reverse.
  *Verified by:* `TODO.TemplateRemainsProductShaped`

## Composition

The template splits into three parts with different rates of change. The **root files** — configuration,
registers, scripts — change rarely and are the part Anneal keeps a working copy of. The **document
skeletons** under `docs/architecture/` change whenever the standards change, because they encode the
structure `architecture-documentation.md` describes. The **document pipeline** — `build-doc.ps1`, the
HTML template, and `collection-links.lua` — changes least of all and is shared by every collection.

The pipeline is owned here rather than being its own system because what it promises is a property of the
layout: put a `definition.yaml` in a folder and that folder becomes a publishable document. There is no
consumer that wants the pipeline without the layout.

The hazard specific to this system is over-synchronization. Anneal's own root files may legitimately
differ from their template counterparts when this repository needs behavior a downstream C# product
should not inherit. Reconciling them by making the template mirror Anneal would break downstream
repositories while looking like cleanup. The rule is directional and recorded as `TEMPLATE-I1`.

## Decisions

**`TEMPLATE-I1` has no mechanical verifier** — the invariant is a directional property: Anneal's own
root files may diverge from their template counterparts, but the template must remain valid for a C#
product repository. Verifying "valid for a C# product repository" requires compiling a downstream
project, running its tests, and judging that nothing Anneal-specific leaked into the generic
skeleton — a judgement call that depends on what a product repository looks like and is therefore
owned by `architecture-design` review rather than a script. The `TODO.TemplateRemainsProductShaped`
verifier placeholder stands as an obligation reminder, not a gap — if a future constraint makes
the property mechanical, the placeholder resolves then.

**Skeletons carry directives, not prose** — template documents contain explicit `TEMPLATE-DIRECTIVE`
blocks that instruct an agent and are then deleted. Shipping prose examples was rejected because an
example that is merely edited leaves its own assumptions behind, and nothing marks where they were.

**One pipeline, many collections** — `build-doc.ps1` is generic over any folder with a
`definition.yaml`, so the user guide and the architecture tree compile through the same path. A per-
collection script was rejected because it would drift between collections in exactly the way the
template drifts from Anneal when maintenance is applied asymmetrically.
