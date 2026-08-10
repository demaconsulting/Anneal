# Vision

Anneal's long-term strategic destination and the reasoning behind it. See `docs/user-guide/` for detail.

Anneal is a CLI coding agent: it maintains its own understanding of a repository, maintains and
improves the code in it, and works with the owner to add new functionality — from a small fix to a
full re-architecture — and can onboard itself into a repository it has never seen.

Underneath, work is classified and routed by deterministic, compiled control flow. A model does the
actual work — writing the code, editing the files — and separately answers narrow, typed oracle
questions that the control flow uses to decide what happens next; the model never decides the
sequencing itself. This keeps the process cheap per change and usable on weaker models, though a
model too weak to write correct code is still too weak to use.

Anneal does not only react to requests — it can propose its own maintenance and architectural work on
a repository, under the same reversibility guard as anything a person asks for: everything short of a
published release, an install into another repository, or a real-world tool grant reaches `main` only
through the ordinary route of branch, review, and test.

Anneal separates two responsibilities: a front-end that carries the narrative conversation with the
user, and a back-end that runs classified, compiled operations. Both can live in the same Toolkit
executable.
