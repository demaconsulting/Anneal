# Technology

The language, framework, and stack facts about this repository. This is the file an oracle prompt
injects when it needs to know what the codebase is built from, not how to validate a change against
it (see `validation.md`) or what conventions govern its style (see `conventions.md`).

Descriptive, evolvable, but named as a scope tripwire: any change here escalates to at least Contract
Change scope.

- **Languages** — PowerShell and C#, with Markdown and YAML as the primary content.
- **Platform** — PowerShell 7 and the .NET SDK; Node and Python supply the lint tooling. The build
  requires network access, to fetch the Copilot CLI the SDK depends on — build-time only, no
  enforcement operation's runtime determinism is affected.
- **Model access** — `GitHub.Copilot.SDK`, under the ambient account of the calling session. It has
  no response-format facility, so a typed answer rests on a schema described in the prompt and a
  retry on parse failure.
- **On-prem model access is planned, not present** — `OllamaSharp` is the intended second provider
  behind the same model seam; no repository uses it yet.
- **Distribution** — a .NET tool (`dotnet anneal`), packaged and published through NuGet; see
  `../architecture/toolkit.md` for the tool's own composition.
- **Adoption today is .NET and C# only** — the shipped layout defaults to `*.cs` sources, xUnit
  attributes and TRX results, and the template's scripts assume a solution. Another ecosystem would
  need a different template, not a patch to this one.
