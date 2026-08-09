---
id: snapshot-before-discarding-uncommitted-work
tags:
  - git
  - safety
  - review
  - sub-agent
summary: Never discard or revert someone else's uncommitted work without a reversible snapshot first
---

git checkout -- <file> and rm/Remove-Item are one-way doors for unstaged, uncommitted changes: there is no commit and nothing to fall back to. A sub-agent's or another process's uncommitted diff you did not author this turn should never be reverted or deleted based on a snap judgement about whether it looks legitimate, no matter how confident that judgement feels.

Concrete rule: before running git checkout --, rm, or any overwrite on a file with uncommitted changes you did not personally just write, first take a zero-cost reversible snapshot (git stash push -m '<reason>', or copy the file/diff aside). Pop or restore it once the judgement about the content is actually confirmed, not assumed.

This also generalizes the evidence-before-verdict principle to your own reasoning, not just a sub-agent's claims: verify a suspicion (e.g. git log -- <file> to check whether a file has real history) before acting on it, not after. A destructive action taken on an unverified belief is unrecoverable even if the belief later turns out to be wrong; a destructive action taken after a cheap direct check, or behind a stash, is not.

Learned from a real incident: a background dispatch worker's uncommitted diff included changes to a file assumed (from style alone: root-level location, bespoke non-idiomatic shape) to be a hallucinated test harness. It was in fact a long-lived, legitimate file with deep commit history. The wrong assumption was corrected only after git checkout -- and rm had already permanently discarded the sub-agent's real, uncommitted new test-case work, which had to be redone from scratch.
