# First Run — Architecture Design

`@helper` will send you to `@architecture-design` the first time your repository needs system
boundaries — or when the ones it has need redrawing. This page describes what that conversation is
like so you know what to expect.

## What It Is For

`@architecture-design` is an interactive interview that establishes or re-cuts the system boundaries
in your repository. A **system boundary** is where a contract lives, and a contract is what makes
everything inside it free to change without process overhead. Getting the boundaries wrong is the one
mistake that is expensive to correct later.

## What to Expect

The interview asks one question at a time, shows you the system tree as it develops, and writes
`docs/architecture/` when you confirm you are done.

**Be ready to answer**: which parts of the codebase could be replaced wholesale without the rest
noticing. Answer concretely — names of directories or modules, not abstract categories.

On an existing repository it reads the current tree first and refines it rather than starting from a
blank sheet. Decisions and still-valid contract clauses carry across.

It will write contract clauses naming tests that do not exist yet and list them as implementation
obligations. That is intended — the contract is written before the code, and the obligations are
what `@helper` will route you through next.

## After the Interview

Once you confirm, the tree is written and `@helper` resumes handling your requests. You do not need
to return to `@architecture-design` unless the system boundaries themselves need to change — and
`@helper` will tell you when that is the case.
