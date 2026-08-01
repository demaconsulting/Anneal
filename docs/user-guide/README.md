# User Guide

Documentation for using and maintaining the Anneal evolutionary development process.

Read the [project README](../../README.md) first for what this process is and why it exists. This
guide covers how to work with it.

## Contents

- **[Getting Started](getting-started.md)** — installing the agents into a repository, bootstrapping
  the architecture tree, and making a first change. Start here.
- **[Common Tasks](common-tasks.md)** — the day-to-day jobs and the prompt to use for each. The page
  to keep open while working.
- **[Workflow](workflow.md)** — how change tiers and agent routing work in practice, with worked
  examples and the failure modes to watch for. Read this second; it is the part people get wrong.
- **[Authoring](authoring.md)** — how to write the architecture tree and system contracts well.
  Reference this when writing or reviewing documentation.
- **[Reference](reference.md)** — every agent, skill, and standard: what it does, when to invoke it,
  what it produces, and when *not* to use it.
- **[Maintaining](maintaining.md)** — how this repository is put together, the design invariants that
  must be preserved, and how to add or change an agent or standard safely.

## Who Should Read What

| You are | Read |
| --- | --- |
| Setting up a new repository | Getting Started, then Authoring |
| Working in a repository day to day | Common Tasks, then Workflow |
| Reviewing a pull request | Workflow, Authoring |
| Changing the agents or standards themselves | Maintaining |
