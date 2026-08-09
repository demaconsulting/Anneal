---
level: system
covers:
  - install.ps1
  - retired-payload.txt
---

[← Architecture Overview](./overview.md)

# Installer

Installer is the only component that writes into a repository other than its own. It copies the payload
and vendors the template into a target repository, and it is the first thing every adopter runs — so its
observable surface is small, heavily depended upon, and unusually unforgiving. A consumer would notice a
rewrite through its parameters, what appears on disk afterwards, and above all through what it refuses to
do without asking.

It carries more recorded pressure than any other system here, because it writes into a target
repository: the damage-guarding conditions in [CONSTRAINTS.md](../../CONSTRAINTS.md) attach here.
That concentration is the reason Installer is a system rather than a script filed under
supporting machinery: the promises are real, they are contested, and they are where the process is most
likely to damage somebody's repository.

## Contract

### Provides

- **INSTALLER-01** — Installs the payload into a target repository by file copy alone, adding no build
  step, package manager, or runtime dependency to that repository.
  *Verified by:* `InstallSubprocessTests.InstalledLayoutMatchesRepository`

- **INSTALLER-02** — Vendors the template to `.github/template/` in the target repository, so the
  template resolves locally and is pinned to the agents installed beside it.
  *Verified by:* `InstallSubprocessTests.TemplateIsVendoredLocally`

- **INSTALLER-03** — Installs `AGENTS.pristine.md` as `AGENTS.md`, so the target receives a file
  carrying no project-specific values.
  *Verified by:* `InstallSubprocessTests.PristineIsInstalledAsAgentsFile`

- **INSTALLER-04** — Detects collisions with existing files and reports them before writing anything, so
  a partial install cannot leave a repository half-converted.
  *Verified by:* `InstallSubprocessTests.CollisionsAreDetectedBeforeAnyWrite`

- **INSTALLER-05** — Replaces payload-owned files when `-Force` is given, and refuses to overwrite
  without it.
  *Verified by:* `InstallSubprocessTests.ForceIsRequiredToOverwrite`

- **INSTALLER-06** — Lists payload-directory files the payload no longer provides when `-Prune` is
  given, separates those `retired-payload.txt` names as formerly ours from those the repository added
  itself, and deletes only what the user confirms.
  *Verified by:* `InstallSubprocessTests.PruneListsRetiredPayloadFiles`

### Requires

- **[Template](./template.md)** — a layout definition that is complete and internally consistent at the
  moment of the copy.
- **PowerShell 7** — recursive copy, path handling, and interactive confirmation.

### Invariants

- **INSTALLER-I1** — No file outside the payload directories and the vendored template is created,
  modified, or deleted in the target repository.
  *Verified by:* `InstallSubprocessTests.WritesAreConfinedToPayloadPaths`

- **INSTALLER-I2** — A run that fails partway leaves the target repository in a state the same command
  can be re-run against.
  *Verified by:* `InstallSubprocessTests.InterruptedInstallIsRecoverable`

## Composition

The script is a payload table plus three phases: resolve what will be written, check every destination
for collisions, then copy. The table is data rather than logic, which is why renaming an agent needs no
code change — a property worth stating because it is easy to lose by adding a special case.

`-Prune` is deliberately a separate phase with its own confirmation rather than part of the copy. Removal
is the irreversible half of an upgrade, and the classification it performs — payload files we retired
versus files the repository added itself — is a judgement the user must be shown before it is acted on.
`retired-payload.txt` exists because a file the payload no longer provides is indistinguishable from a
file the repository authored, unless something remembers. **Renaming or deleting any payload file — an
agent, a standard, or a skill — requires appending its installed path to `retired-payload.txt` in the
same change.** A line never removed from it is what lets a repository upgrade from any earlier version;
skipping it leaves a superseded file installed and selectable, which is worse than not shipping the
rename at all.

The contract is verified by a compiled fixture suite, `InstallSubprocessTests`
(`test/DemaConsulting.Anneal.Toolkit.Tests/Contract/`), modeled on `CheckContractsSubprocessTests`.
It builds throw-away target-repository fixtures and spawns the real `install.ps1` as a subprocess to
verify the payload table (including the `AGENTS.pristine.md` to `AGENTS.md` rename), collision
detection before any write, `-Prune` behavior against `retired-payload.txt`, and installed-layout
parity against this repository.

## Decisions

**Copy, never generate** — installation performs no substitution and leaves nothing to fill in.
Template expansion with project values was rejected because it makes every upgrade a merge; the
alternative chosen instead was to remove project-specific content from `AGENTS.md` entirely, so replacing
it outright is safe.

**Refuse rather than merge on collision** — the installer stops and reports instead of attempting to
reconcile. Merging was rejected because a bad merge into an agent prompt is silent and its damage surfaces
much later, in an agent's behavior.

**Deletion always asks** — `-Prune` never removes without confirmation, even for files it is confident
it owns. The cost of an unnecessary prompt is a keystroke; the cost of deleting a repository's own file
is trust.
