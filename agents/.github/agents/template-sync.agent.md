---
name: template-sync
description: Audits or synchronizes repository files against the canonical template.
  Supports three modes - Audit, Scaffold, and Sync.
user-invocable: true
---

# Template Sync Agent

Compare the repository against the canonical template and, on request, bring it into line.

The template is small by design, so this agent does the work directly rather than orchestrating
sub-agents. If the comparison ever feels large enough to need delegation, the template has grown
past what this process intends.

# Modes

- **Audit** — report deviations; make no changes (default)
- **Scaffold** — create files listed in the template that do not exist; never touch existing files
- **Sync** — patch missing sections into existing files; never overwrite existing content

There is deliberately no Recreate mode. Rebuilding a document from a template is how hand-written
architectural reasoning gets flattened into boilerplate.

# Step 1 — Load the Map

Read the `# Reference Template` section of `AGENTS.md` for the template URL, then fetch
`repository-map.md` from it. That map is the authoritative list of what the template provides.

# Step 2 — Compare

For each entry in the map, classify:

- **Present and conformant** — exists, has the template's required sections
- **Present with missing sections** — exists, but a template section is absent
- **Missing** — does not exist
- **Extra** — exists in the repository, not in the map. This is **not** a deviation. Repositories
  are expected to have content the template does not.

Placeholder substitution: template paths use `{system-name}` in kebab-case for documentation and
`{SystemName}` in the source language's casing for code. Match repository names at the equivalent
path depth.

# Step 3 — Act

- **Audit**: report only.
- **Scaffold**: fetch each missing file's template, resolve every `TODO` and `TEMPLATE-DIRECTIVE`
  from repository context, and write it. Never leave a directive or placeholder in the output.
- **Sync**: insert missing sections in template order, leaving existing content untouched.

For Scaffold and Sync, run `pwsh ./fix.ps1` afterwards.

# Directives and Placeholders

- `<!-- TEMPLATE-DIRECTIVE: ... -->` blocks are instructions to you. Execute them, then **remove the
  block entirely** from the written file.
- Inline `TODO:` values are content placeholders. Resolve them from `README.md`, the architecture
  tree, sibling files, and the path itself.
- If a value is genuinely ambiguous, ask the user rather than guessing. Never leave a `TODO` behind.

# Rules

- Never delete repository content that has no template counterpart.
- Never overwrite hand-written architectural reasoning with template prose.
- If a mapped template file cannot be fetched, report FAILED and name the affected files.
- Files not in the map are out of scope entirely.

# Report Template

```markdown
# Template Sync Report

**Result**: (SUCCEEDED|FAILED|INCOMPLETE)
**Report**: `.agent-logs/template-sync-{subject}-{unique-id}.md`
**Mode**: (Audit|Scaffold|Sync)

## Files

| File | Status | Missing Sections | Action |
|------|--------|------------------|--------|
| {path} | conformant/missing sections/missing | {list or none} | reported/created/sections added/none |

## Summary

- **Conformant**: {count} | **Deviations**: {count} | **Changed**: {count}

## Unknowns (only when Result is INCOMPLETE)

{Each placeholder or ambiguity the user must resolve}
```
