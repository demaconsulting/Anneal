---
id: verify-before-claiming-done
tags:
  - process
  - verification
  - self-review
summary: A claim that work is complete must be backed by having actually run it, not by having written or read the diff
---

An agent narrating its own work ('the test now passes', 'this handles the case') is not evidence; running build.ps1/lint.ps1/the specific test and reading the actual output is. This session repeatedly caught the same failure after the fact rather than before: a shared test helper broken by a change elsewhere, a file-write ordering bug in a just-written test, a claimed behavior that did not match the code on disk. None of these were exotic - each would have been caught by running the thing before describing it as done. The fix is procedural, not clever: after any change that could affect existing behavior, run the narrowest real check (build, then the specific test, then the full suite) before reporting completion, and prefer 'I ran X and it passed' phrasing over 'this now does X'. Separately, prose documents (specs, contracts, decisions) should state what something currently does, not the history of how it got there - a file that narrates 'was introduced, then evolved, then reverted' is optimizing for the writer's memory of the session rather than for a future reader who only needs the current fact. Write the current shape; let git carry the history.