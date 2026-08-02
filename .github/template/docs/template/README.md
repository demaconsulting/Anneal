# Document Templates

Shared build inputs for every document collection under `docs/`. Nothing here is a document itself —
these are the files each `definition.yaml` pulls in through its `resource-path`.

| File | Purpose |
| --- | --- |
| `template.html` | Pandoc HTML template: title page, running headers, page numbering, print styles |
| `collection-links.lua` | Turns links between documents in a collection into cross-references |

## Why `collection-links.lua` Exists

A collection is concatenated into one output file, so a link to `./ingest.md` points at a file the
PDF reader does not have. The content it names is present — it just moved into this document. The
filter rewrites that link to `#ingest`, so the same markdown works two ways: as a file link on disk
and on the repository host, and as a cross-reference in the PDF.

This is what lets `architecture-documentation.md` require relative links for downward navigation
without those links breaking the moment the tree is published.

The anchor is the file name, because `ingest.md` carries the heading `# Ingest`, which Pandoc slugs
as `#ingest`. Two cases are deliberately left alone:

- **A link whose target is not in this collection** — `overview.md` links up to `README.md`, which is
  level 0 and never compiled into the architecture document. Rewriting it would produce an anchor
  that resolves nowhere, so the link is left exactly as written.
- **A link that already carries a fragment** — `./store.md#retention` becomes `#retention`, because
  the fragment names a section and is more specific than the file.

The filter only rewrites a link when the anchor genuinely exists in the compiled document, so a
mistyped or outbound link is passed through untouched rather than silently redirected.
