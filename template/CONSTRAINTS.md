<!-- TEMPLATE-DIRECTIVE: Durable conditions the architecture must satisfy. One bullet per
     constraint - keep it that cheap, or constraints stop being filed. Entries move between the
     two sections; they are removed only when the condition stops being required. A new repository
     usually has nothing to record here yet: leave both sections empty rather than inventing an
     entry, and delete the example bullets. Remove this block. -->

# Constraints

Conditions this architecture must satisfy. `architecture-design` reads this before re-cutting system
boundaries, and `architecture-update` reads it before a Tier 2 change.

A satisfied constraint is not finished business — it is the reason the current shape is the shape it
is, and the guard rail that stops the next re-cut from silently regressing it. Entries are never
deleted for being met. Remove one only when the condition stops being **required**, which is a
decision, not bookkeeping.

An entry belongs here only if it **holds** rather than **completes** — a standing property like
"supports .NET Standard 2.0", not a unit of work like "add a `--version` flag". See the Intake
admission test in `change-classification.md`. Work that finishes goes in
[BACKLOG.md](BACKLOG.md).

A belief the world could prove wrong is an **assumption** rather than a constraint; those live in the
Assumptions section of [README.md](README.md).

## Satisfied

Conditions the current design meets. Breaking one is a regression, not a trade-off to be made
quietly.

- **TODO: the condition, in one line** — TODO: what in the current shape upholds it.

## Not Yet Satisfied

Conditions the current decomposition gets in the way of. These are the pressure that argues for a
re-cut. An entry moves up to **Satisfied** when a change absorbs it.

- **TODO: the condition, in one line** — TODO: why the current decomposition cannot meet it.
