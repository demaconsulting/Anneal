# Technology

The language, framework, and stack facts about this repository — the descriptive counterpart to
`../governance/tenets.md`'s prescriptive .NET/C# choice. This is the file an oracle prompt injects
when it needs to know what the codebase is built from, not how to validate a change against it (see
`validation.md`) or what conventions govern its style (see `conventions.md`).

Descriptive, evolvable, but named as a scope tripwire: any change here escalates to at least Contract
Change scope.

- **Languages** — PowerShell and C#, with Markdown and YAML as the primary content.
- **Platform** — PowerShell 7 and the .NET SDK; Node and Python supply the lint tooling.
- **Model access** — the GitHub Copilot SDK, under the ambient account of the calling session.
- **Distribution** — a .NET tool (`dotnet anneal`), packaged and published through NuGet; see
  `../architecture/toolkit.md` for the tool's own composition.
