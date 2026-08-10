# Tenets

Core, prescriptive facts about what Anneal chooses to always be. Losing any one of these would mean
Anneal stops being what it is for — not a standing architectural condition that can be satisfied or
not (that is a Constraint, in `../work/constraints.md`), and not a belief about the world outside the
project's control (that is an Assumption, in `assumptions.md`).

Owner-authored, owner-approved to change. Anneal may propose a revision — during onboarding, or an
explicit owner-triggered review — but never edits this file unilaterally otherwise. A newly-admitted
Tenet the project doesn't fully embody yet stays a real Tenet from the moment it's admitted; progress
toward it is tracked by a Not-Yet-Satisfied Constraint that cites it, not by the Tenet itself carrying
a state.

- **Anneal is a development process for AI coding agents working in long-lived .NET and C#
  codebases.** The mechanism — documentation work triggered only by contract change — is offered as a
  process, not a general-purpose framework for any language or any style of AI-assisted development.
  Losing the .NET/C# scope would mean building a different product, not a variant of this one; see
  `technology.md` for what that entails mechanically.
- **Documentation work is triggered only by contract change, never by file change.** This is the
  central rule stated at the top of `product.md` and is the whole reason Anneal exists rather than
  regulated development or unstructured prompting. Every other mechanism in this repository —
  progressive disclosure, scoped effort, the tripwire, the clause-to-test link — exists to serve this
  one rule, not the reverse.
- **Anneal ships as a .NET tool, installable and updatable through NuGet.** This is load-bearing to
  purpose, not an incidental implementation detail: the Toolkit's own architecture (see
  `../architecture/toolkit.md`) hardens the .NET/C# tenet above into a distribution mechanism, and a
  repository outside the .NET ecosystem can read the process but not run its compiled operations as a
  direct consequence.
- **Anneal is not a regulated-development process and does not produce IEC 62304 or equivalent
  compliance evidence.** This is a boundary tenet — it names what Anneal deliberately refuses to be. A
  sibling project, [Agents](https://github.com/demaconsulting/Agents), targets that need instead; a
  change that pulled Anneal toward compliance-evidence generation would be building a different
  product, not extending this one.
