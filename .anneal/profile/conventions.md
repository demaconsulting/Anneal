# Conventions

Ecosystem-specific technical conventions — the concrete style and idiom rules for the languages
named in `technology.md` (C# and xUnit, for this repository). Deliberately narrower than
`.github/standards/coding-principles.md` and `testing-principles.md`, which state language-agnostic
principles and stay put; this file is where the ecosystem-specific rules those principles specialize
into would live.

Descriptive, evolvable, but named as a scope tripwire: any change here escalates to at least Contract
Change scope.

**This stage does not relocate that content.** `.github/standards/csharp-language.md` and
`csharp-testing.md` are loaded today through a file-discovery mechanism keyed on `globs:` front
matter, wired into code (the standards-loading path agents and compiled operations both use).
Moving their bodies here now, without also retargeting that loader, would either duplicate the
content in two places or require a code change — and this stage is a parallel, non-authoritative
copy with no code changes. The real content stays at:

- [`csharp-language.md`](../../.github/standards/csharp-language.md)
- [`csharp-testing.md`](../../.github/standards/csharp-testing.md)

A later stage, landed together with the loader retargeting, folds their bodies into this file (or
leaves them in place and makes this file the pointer permanently — an open question for that stage,
not this one) and updates this note accordingly.
