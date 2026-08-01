# Authoring

Practical guidance for writing the architecture tree and system contracts. The rules live in
`architecture-documentation.md` and `system-contracts.md`; this page is about applying them well.

## The One-File Test

After any documentation change, ask: **how many files did I have to edit?**

More than one means content is duplicated across levels. Do not edit both — find the duplication and
remove it from the level that should not own it.

This single test catches most authoring mistakes, and it is worth applying deliberately until it
becomes reflex.

## Writing the Product Contract

`README.md` is a contract like any system document, one altitude up, with a person as the consumer.
**Features** are its clauses — what someone may rely on getting. **Requirements** are its invariants
— properties that must hold for those features to mean anything. `architecture-documentation.md`
holds the rules.

The altitude test does the work: if a system changing would force you to edit the bullet, the promise
belongs to that system's contract, not to the product. *"Rearrange the interior without paperwork"*
survives any decomposition. *"Supports CSV, JSON, and XML export"* is a system contract that has
wandered upward, and it will need editing every time a format is added.

A requirement that restates a feature is noise — delete it. Each one should say something no feature
bullet says, usually a property the features quietly depend on.

Removing or narrowing either is breaking, exactly as it is for a clause, and belongs in the change
summary. Unlike a clause, neither carries an identifier or a named test: level 0 is held by review
rather than by script, deliberately.

## Writing Assumptions

`README.md` owns the design's assumptions — what it takes to be true and cannot itself guarantee.
`architecture-documentation.md` holds the rules; the practical part is telling them apart from
constraints and knowing when to write one.

Ask whether reality could prove the statement wrong without anyone changing their mind. "Our users
are on a corporate network with no outbound access" is an assumption — someone can discover it is
false. "The tool must run offline" is a constraint in `CONSTRAINTS.md`; it changes only by decision.
The two often pair up, the constraint being what you chose *because* of the assumption.

Write one only if it is load-bearing: if the belief were false, the shape below would be wrong. Most
designs have three to six, and a design resting on nothing unusual has none — an empty section beats
an invented one.

The payoff comes later. When an assumption is disproved, that is a re-cut trigger rather than a bug,
and having written it down is what lets you notice. An architecture whose assumptions were never
recorded fails in a way nobody can attribute.

## Writing `overview.md`

This file owns the system inventory and what happens *between* systems. It does not describe what is
inside any of them.

The trap is the system list. Write this:

```markdown
- [Ingest](./ingest.md) — accepts and validates inbound records
```

Not this:

```markdown
- [Ingest](./ingest.md) — accepts inbound records over HTTP, validates them against
  the schema registry, batches them, and forwards to Store. Contains the validation pipeline,
  the batching buffer, and the retry handler.
```

The second version is a summary of the system document. Every change to Ingest now dirties
`overview.md` too, and the two will drift within a month. One line, describing the system's **role in
the composition** — that is content `overview.md` genuinely owns.

## Writing `{system}.md`

Four things belong here: the contract, what the system requires, how it is composed and **why**, and
decisions local to it.

The composition section is the one that needs judgement. Write the seams and the reasoning, not a
directory listing:

> Parsing is kept separate from validation because the parser must accept anything the wire format
> permits so it can report precise byte offsets, while validation rejects. Merging them would force
> the parser to fail early and lose the offset information the error messages depend on.

If your composition section reads like `ls -R`, cut it back to the seams that carry reasoning. A
reader can see the file layout; what they cannot see is why it is cut that way.

## Writing a Contract

Clauses are what a consumer may rely on, in terms a consumer could check.

Good:

```markdown
- **INGEST-02** — Rejects malformed records with `400` and a body naming the byte
  offset of the first parse failure.
  *Verified by:* `IngestContractTests.MalformedRecordReportsOffset`
```

Not a clause:

```markdown
- **INGEST-07** — Uses a `TokenStream` to tokenize input.
```

The test is simple: could a consumer with no source access detect a violation? `TokenStream` is
invisible from outside, so it is a design decision, not a promise. It belongs in the composition
section if it belongs anywhere.

### Pitching the Contract at the Right Level

Too low is the common failure, and it is expensive because it converts refactoring into Tier 1 work.

| Too low | Right |
| --- | --- |
| "Exposes a `Validate(record)` method" | "Rejects records failing schema validation, with the failing field named" |
| "Logs at Information level on startup" | "Reports startup completion on stdout before accepting requests" |
| "Retries three times with 100 ms backoff" | "Retries transient failures before reporting an error" |

The right-hand column leaves room to change the implementation. The left-hand column does not.

Note the third row: if the retry count is genuinely part of the promise — because a consumer's own
timeout depends on it — then the specific numbers belong in the clause. Judge by what consumers
actually depend on, not by what is easy to state.

### Invariants

Invariants capture what no single call can demonstrate: ordering, idempotency, thread safety,
resource bounds, compatibility. They are easy to forget and expensive to discover the hard way.

```markdown
- **INGEST-I1** — Records from a single connection are queued in arrival order.
  *Verified by:* `IngestContractTests.PreservesPerConnectionOrder`
```

### Identifiers

IDs are stable for the life of the clause. When a clause is retired, delete it and **never reuse the
number**. Gaps are correct. Renumbering to close them breaks every external reference to the old ID.

## Section Documents: Default to Not Writing One

Level 3 is exceptional. Before creating one, name which creation test it meets:

- Hidden invariant a reader would plausibly violate
- Non-local correctness constraint not visible at the call site
- Compatibility surface — wire format, file format, schema, protocol
- Settled debate with a seriously considered rejected alternative
- Cross-cutting mechanism many units must participate in correctly

**If you cannot name one, do not create the file.** Symmetry is not a reason. "This system is
important" is not a reason. "Someone might want to know" is not a reason.

Worth writing:

> The offset counter is measured in bytes, not characters, because error messages must be reproducible
> against the raw file. Any change to multi-byte handling must preserve this, and the golden-file
> tests will not catch a regression here because they use ASCII fixtures.

Not worth writing:

> The `Parser` class contains `Parse`, `ParseHeader`, and `ParseBody`. `Parse` calls the other two in
> order.

The second is recoverable in ten seconds by opening the file, and it is wrong the moment a method is
renamed.

## Deleting

Delete in the **same change** that obsoletes a document. Deferred pruning is how a tree becomes an
anchor — nobody ever schedules it, and each stale document makes the next reader trust the tree a
little less.

Delete when the subject is gone, when the content became derivable from one source file, when the
document has decayed into restating names, or when the rejected alternative it preserved is no longer
plausible.

When in doubt, delete. Git has the history, and a deleted document that turns out to be needed is
cheaper to restore than a stale one is to discover.

## Length

There is no page count. A document is long enough when a reader at that altitude can stop there and
act, and too long when it grew for a reason that belongs at another level — capabilities enumerated
in the README, a contract restated in `overview.md`, decomposition detail in a system document.
`architecture-documentation.md` holds the table of causes.

The remedy is to move the material to the level that owns it, or delete it. Never to trim prose that
earns its place: a short document missing the *why* costs the reader a descent.
