<!-- TEMPLATE-DIRECTIVE: This is level 1 of the architecture tree — the system inventory and
     the interactions between systems. It names every system, gives each a one-line role, states
     the relationships between them (data flow, control flow, process and deployment boundaries),
     and records repository-wide decisions that constrain every system (language, runtime,
     error-handling philosophy, concurrency model). It does NOT describe what is inside any system.
     Each system listed here links to its level-2 document. Remove this block. -->

[← README](../../README.md)

# Architecture Overview

TODO: One paragraph stating the organizing idea: why these systems exist and how they fit together.

## Systems

- [{SystemName}](./{system-name}.md) — TODO: one-line role

## Interactions

TODO: Describe the data flow and control flow between the systems listed above. A diagram is welcome
here if it adds to prose rather than replacing it.

## Repository-Wide Decisions

TODO: Decisions that apply across all systems: language, runtime, error-handling philosophy,
concurrency model. A decision that applies to only one system belongs in that system's document.
