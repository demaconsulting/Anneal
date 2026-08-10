---
id: massive-decomposition-beats-manual-slicing
tags:
  - helper
  - routing
  - decomposition
summary: route's Massive-effort path already decomposes large work into gated phases - prefer it over hand-rolling a slice-and-stop-condition plan for a sub-agent.
---

`route`'s Massive-effort classification (`TOOLKIT-26`/`27`/`28`) decomposes a large work item into
phases, runs a mandatory cumulative check over the whole proposed phase set before any phase is
routed, forces escalation on any phase touching a protected path, and caps second-level
decomposition at one further level. That is a more rigorous version of any manually-written
"do it in N slices, stop if a slice looks too broad" plan a session might improvise for a sub-agent
prompt - it exists, is gated, and is already reachable through the ordinary `helper` → compiled
`route` path whenever a work item classifies as Massive. Before designing a custom multi-step slicing
plan for a large piece of work, check whether describing the whole item to `helper` and letting
`route` classify Effort would get the same decomposition for free, with a stronger check than a
hand-written one.
