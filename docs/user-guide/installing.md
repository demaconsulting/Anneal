# Installing

Everything `install.ps1` does: install the payload, scaffold the repository layout, and upgrade.

## Install

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product
```

This lays down the payload and vendors the template into `.github/template/`. It refuses to overwrite
existing files unless you pass `-Force`, so re-running it after an upgrade will not silently discard
local edits.

There is nothing to fill in afterwards. What your product is and what it is written in belong in
`README.md`, which the agents read as level 0 of the architecture tree.

## Scaffold the Layout

For a repository that does not yet have the layout:

```text
@helper scaffold the repository structure from the template
```

This creates files listed in the template that do not already exist. It never overwrites anything.

## Upgrade

```pwsh
pwsh ./install.ps1 -TargetRepository ../my-product -Force -Prune
```

- `-Force` overwrites **every** file the payload owns. There is no backup and no diff. Commit first,
  and expect to restore any locally edited standard from the diff afterwards.
- `-Prune` finds files in the payload directories that this payload does not provide — agents
  renamed or removed upstream. It lists what it finds, split into ones Anneal retired and ones it
  does not recognize, and deletes only what you confirm. Without `-Prune` the installer still counts
  those files and says so.

Then run:

```text
@helper scaffold the template files I am missing
@helper patch in template sections I am missing
```

Scaffold creates template files added since you installed; Patch inserts new sections into files you
already have.

## Next

Then ask `@helper` for the first thing you want built; if this repository has no system boundaries
yet it will stay in the conversation and interview you first.
