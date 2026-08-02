-- Rewrites links between documents in a collection into internal cross-references,
-- so the same link works both on disk and in the compiled PDF.
--
-- WHY: a collection is concatenated into a single output file, so a link to
-- ./ingest.md points at a file the PDF reader does not have. The target content
-- is present -- it just moved into this document. Rewriting the link to an anchor
-- preserves the navigation that architecture-documentation.md requires.
--
-- HOW: headings are collected first, then a link is rewritten only when its
-- target anchor actually exists in the compiled document. This matters because
-- not every .md link points inside the collection -- overview.md links up to
-- README.md, which is level 0 and is never compiled into the architecture
-- document. Those links are left exactly as written, rather than rewritten to an
-- anchor that resolves nowhere.
--
-- The anchor is the file name: ingest.md carries the heading "# Ingest", which
-- Pandoc slugs as #ingest. Where a link already carries a fragment, the fragment
-- wins, because it names a section rather than a document.

local anchors = {}

local function collect(el)
  if el.identifier and el.identifier ~= "" then
    anchors[el.identifier] = true
  end
end

local function resolve(target)
  -- Leave external links, absolute URLs and in-page anchors alone.
  if target:match("^%a[%w+.-]*:") or target:match("^//") or target:match("^#") then
    return nil
  end

  local path, fragment = target:match("^([^#]*)#(.*)$")
  if not path then
    path = target
    fragment = nil
  end

  if not path:match("%.md$") then
    return nil
  end

  local anchor
  if fragment and fragment ~= "" then
    anchor = fragment
  else
    -- Strip directories and the extension: docs/system/ingest.md -> ingest
    local name = path:match("([^/\\]+)%.md$")
    if not name then
      return nil
    end
    anchor = name:lower()
  end

  -- Only rewrite when the target is genuinely inside this document.
  if not anchors[anchor] then
    return nil
  end

  return "#" .. anchor
end

return {
  {
    Header = collect
  },
  {
    Link = function(el)
      local rewritten = resolve(el.target)
      if rewritten then
        el.target = rewritten
      end
      return el
    end
  }
}
