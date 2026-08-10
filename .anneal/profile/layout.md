# Layout

The physical directory layout of this repository — where each kind of content lives on disk. This is
the file an agent or oracle prompt injects to find a path, distinct from `../architecture/` (what
each system promises) or `conventions.md` (how code within a path is written).

Descriptive, evolvable, but named as a scope tripwire: any change here escalates to at least Contract
Change scope.

This repository is laid out exactly as a repository that has installed Anneal, so the process can be
maintained using its own agents.

- **`.github/agents/`, `.github/skills/`, `.github/standards/`** — the payload, live here and shipped
  unchanged.
- **`.github/template/`** — the canonical repository layout and file templates, including the
  pristine `AGENTS.md`.
- **`docs/architecture/`** — Anneal's own architecture tree, maintained with its own agents. (A
  parallel copy lives at `../architecture/`, this folder's Toolkit-native counterpart — see the S19
  stage note in `MIGRATION.md` for why both currently exist.)
- **`docs/user-guide/`** — how to use and maintain this process.
- **`docs/template/`** — shared Pandoc inputs: HTML template and the collection link filter.
- **`docs/build-doc.ps1`** — compiles one document collection into HTML and then PDF.
- **`src/`, `test/`, `Anneal.slnx`** — the Toolkit, a .NET tool hosting operations that combine
  deterministic checks with model-backed judgement.
- **`.anneal/`** — this folder. Repository-local runtime configuration the Toolkit resolves
  (`config.json`: role-to-model mapping, the arguments a self-hosted run's contract check is invoked
  with), `skills/` where `file-skill` writes deliberately curated, committed lessons about this
  repository (see [Skills](../architecture/toolkit/skills.md)), `records/` and `transcripts/`
  (invocation and model-interaction logs), and the `governance/`, `profile/`, `work/`, and
  `architecture/` documentation folders this file belongs to.
- **`test-process-contract.ps1`** — a fixture suite holding the payload itself to its documented
  behavior; `dotnet anneal check-contracts` is held to its own contract by
  `CheckContractsSubprocessTests` under `test/`, a compiled C# suite that spawns the tool as a real
  subprocess.
- **`.agent-logs/`** — agent report corpus (gitignored, local only); `AGENTS.md` already requires
  every agent to write a report here, making the corpus automatic; `agent-metrics.ps1` harvests it
  into a bounded behavioral summary.
