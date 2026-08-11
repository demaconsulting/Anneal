# User Guide

How to install and use the Anneal evolutionary development process in your repository.

Read the [project README](../../README.md) first for what this process is and why it exists. This
guide covers how to work with it day to day.

> **Stability invariant.** The user guide changes only when `install.ps1`'s command-line interface
> changes, or when what a user sees from `@helper` changes, including its boundary-work
> interview. No interior change to Anneal — a new agent, a reworded standard, a new
> contract-check failure, a change to how routing or repairs work — may require an edit here.

## Contents

- **[Installing](installing.md)** — everything `install.ps1` does: install, scaffold the layout, and
  upgrade.
- **[First Run](first-run.md)** — what `@helper`'s first boundary interview is for and what it
  is like.
- **[Using Helper](using-helper.md)** — how to ask `@helper` for what you want, example prompts, a
  worked example of two changes end to end, and what to do when something comes back unfinished.
- **[Repository Scripts](repository-scripts.md)** — the fix/build/lint scripts your repository must
  supply, what each is for, and how to configure non-default names via `.anneal/config.json`.
