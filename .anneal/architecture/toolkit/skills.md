---
level: subsystem
covers:
  - src/DemaConsulting.Anneal.Toolkit/Operations/FileSkillOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Operations/SearchSkillsOperation.cs
  - src/DemaConsulting.Anneal.Toolkit/Skills/**
---

[← Toolkit](../toolkit.md)

# Skills

A skill is a curated, atomic lesson that never rises to a contract promise — a pattern that works, a
check that's easy to misread, a gotcha worth remembering — filed deliberately by an agent that decides
it is worth keeping. Neither existing memory shape holds this today: `.anneal/architecture/`'s Decisions
sections carry the reasoning behind a promise, which a lesson is not, and `.anneal/logs/records`/
`transcripts` (`TOOLKIT-08`, `TOOLKIT-11`) carry raw telemetry nobody curated. This system is that third
shape, filed as `.anneal/work/backlog.md` unscheduled future work and built once a genuine boundary — not an
extension of an existing one — was confirmed through `helper`'s boundary-work interview rather
than guessed at by a compiled routing oracle, which is exactly what happened: `dispatch`'s own `route`
escalated a first attempt to build this mechanically, reasoning correctly that it "reshapes the Toolkit
surface, adds new primitives/CLI entry points, and requires contract changes."

Two tiers exist, distinguished by **who authors a skill and how it ships**, not by anything a reader of
one need know about the other:

- **Repository-local skills** are files an agent writes at runtime, under `.anneal/skills/` in the
  repository it is working in, capturing something true about *that* repository. They are never
  scraped from telemetry or a report corpus — the same mistake `agent-metrics.ps1` made before its
  retirement — only filed by deliberate choice.
- **Toolkit-wide skills** are markdown files with the same front matter, embedded as data in the
  Toolkit assembly at build time — the same mechanism a sibling internal project
  (`DemaConsulting.Jeeves`) already uses for its own knowledge-card catalog — and hand-authored in
  Anneal's own source tree exactly like any other file, so a downstream repository receives updated
  general knowledge the moment it bumps the Toolkit version in its `dotnet-tools.json`, with no runtime
  write path and no promotion pipeline from the repository-local tier.

Both tiers share one file shape (YAML front matter — `id`, `tags`, `summary` — over a markdown body) and
are queried through one search surface, so a caller never needs to know which tier answered.

## Contract

### Provides

- **TOOLKIT-38** — `file-skill` takes an id, a summary, at least one tag, and a body, and writes a
  repository-local skill file under `.anneal/skills/` in the front-matter shape this document defines.
  It succeeds once the file is written; fails when the id collides with an existing skill file, or the
  target path falls outside `.anneal/skills/`. A missing id, summary, or body, an empty tag list, or an
  id that is not a single path segment (containing `/` or `\`, or equal to `.` or `..`), is a usage
  error under `TOOLKIT-10`. There is no embedded-tier equivalent: a toolkit-wide skill is authored as
  Toolkit source, never through this action.
  *Verified by:* `FileSkillWritesAWellFormedRepositoryLocalSkill`

- **TOOLKIT-39** — `search-skills` takes a query and performs lexical search — no embeddings — over
  every repository-local skill under `.anneal/skills/` and every embedded toolkit-wide skill compiled
  into the running assembly, matching against each skill's `id`, `tags`, and `summary`, and returns the
  matches ranked by match strength with each match's full body available to the caller. An empty query
  or no skills found is a success with zero matches, never a failure or refusal; a missing query is a
  usage error under `TOOLKIT-10`.
  *Verified by:* `SearchSkillsRanksLexicalMatchesAcrossBothTiers`

- **TOOLKIT-40** — Whatever files and topic text a model-backed operation already assembles for its own
  turn, that same input also drives one automatic `search-skills` lookup, and each match's summary and
  body are added to the assembled context before the model ever asks. A model turn may still issue its
  own `search-skills` query mid-task for anything the automatic pass did not surface; both paths share
  the one ranking `TOOLKIT-39` defines, so a caller never reasons about two different notions of
  "relevant."
  *Verified by:* `ContextAssemblyAutoLoadsSkillsMatchingTheCurrentFileScope`

### Requires

- **[Runtime](./runtime.md)** — the category, outcome, and finding machinery `file-skill` and
  `search-skills` are built from, and the invocation record both are subject to.
- **[Process](../process.md)** — the context-assembly step `TOOLKIT-40` extends, reusing
  `RepositoryFacts`' file/topic signal rather than deriving a second one.

## Decisions

**Lexical search first, vector search deferred, not assumed** — `search-skills` matches on tags,
summary, and id text, not embeddings. A vector index is real added machinery (an embedding model,
a stored index kept in sync as skills are added) that only earns its cost once lexical search is
observed missing relevant entries in practice — matching the standing decision already in
`.anneal/work/backlog.md` before this system existed, and not reopened by this document.

**Two tiers, one file shape, one search surface** — a repository-local skill and a toolkit-wide skill
answer the same question ("what have we learned that isn't a promise") and are indistinguishable to a
reader once matched; only *how each got there* differs. Giving them different formats or separate
search actions would force every caller to know which tier to ask, which is exactly the distinction
this document exists to hide.

**No promotion path from repository-local to toolkit-wide** — a skill learned about one repository is
not automatically, or semi-automatically, proposed for the embedded catalog. Toolkit-wide skills are
authored the same way any other Toolkit source content is authored: reviewed, tested, and released.
Building a promotion pipeline before either tier has real usage would be exactly the speculative
machinery this Toolkit's own `Details` list elsewhere warns against.

**`file-skill` has no embedded-tier counterpart** — the embedded catalog is compiled into the running
assembly; nothing an agent does at runtime, in any repository, can add to it. An action that appeared to
"file" a toolkit-wide skill would either silently write to the repository-local tier under a misleading
name, or attempt to modify Anneal's own source tree from an unrelated repository's session — neither is
offered.

**Automatic injection reuses an existing signal, not a new one, and does not claim to be standards
loading** — `Process` already resolves `RepositoryFacts.ChangedFileHints` and the work item's own text
as routing facts; standards loading itself is a fixed, static list per worker, not derived from either
signal, and this document does not claim otherwise. `TOOLKIT-40` reuses the same file/topic signal
`RepositoryFacts` already resolves, so skill relevance and routing relevance are read from one source,
but a skill match is not a standard, and this clause draws no equivalence between them.

**Automatic injection renders every match, uncapped, for now** — `TOOLKIT-40` renders every match the
shared ranking returns, in ranked order, with no separate top-N cap or body-size limit layered on top.
That is deliberate for now: the corpus each tier holds today is small, and adding a cap before there is
a real case of an oversized prompt would be exactly the kind of speculative machinery this document
elsewhere declines to build ahead of need. If a real repository's skill corpus grows large enough to
make this a problem, bounding the render (not the ranking) is the fix, and belongs in a future revision
of this clause, not a silent implementation change.
