<!-- TEMPLATE-DIRECTIVE: Durable conditions the architecture must satisfy. One bullet per
     user-admitted constraint. Agents may propose entries, but only the user admits one. Entries
     state the condition, not the mechanism that currently satisfies it. Entries move between the
     two sections; they are removed only when the condition stops being required. A new repository
     usually has nothing to record here yet: leave both sections empty rather than inventing an
     entry, and delete the example bullets. Remove this block. -->

# Constraints

Conditions this architecture must satisfy. `helper` reads this before re-cutting system
boundaries, and `route`'s Structural Change worker reads it before a Structural Change.

A satisfied constraint is not finished business — it is the reason the current shape is the shape it
is, and the guard rail that stops the next re-cut from silently regressing it. Entries are never
deleted for being met. Remove one only when the condition stops being **required**, which is a
decision, not bookkeeping.

An entry belongs here only if it **holds** rather than **completes** — a standing property like
"supports .NET Standard 2.0", not a unit of work like "add a `--version` flag". The Intake admission
test, the rule on who may admit an entry here, and the rule on what an entry may say all live in
`change-classification.md`. Work
that finishes goes in
[BACKLOG.md](BACKLOG.md).

A belief the world could prove wrong is an **assumption** rather than a constraint; those live in the
Assumptions section of [README.md](README.md).

## Satisfied

Conditions the current design meets. Breaking one is a regression, not a trade-off to be made
quietly.

- **TODO: the condition, in one line** — TODO: the durable property this architecture must keep true.

## Not Yet Satisfied

Conditions the current decomposition gets in the way of. These are the pressure that argues for a
re-cut. An entry moves up to **Satisfied** when a change absorbs it.

- **TODO: the condition, in one line** — TODO: why the current decomposition cannot meet it.
