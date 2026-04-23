---
name: clio-data-bindings
description: Canonical workflow guidance for Creatio lookup seeding and local or remote data-binding artifacts on top of the clio MCP surface.
---

# Clio Data Bindings

Use this skill when the task involves lookup seed rows or explicit data-binding artifacts.
This skill defines the base binding mechanism only; use separate specialized skills for domain-specific workflows built on top of it.

This skill is not an MCP API reference.
Resolve live parameter names, aliases, defaults, and response shapes through `get-tool-contract`.

## Read-First Contract

Read these sources in order:

1. `get-guidance {"name":"data-bindings"}`
2. `get-tool-contract` for the exact tools you need
3. `references/workflow.md`

## Canonical Responsibilities

- choose between inline lookup seeding, standalone DB-first binding work, and local binding artifact work
- require read-before-write when current binding or schema context matters
- require read-back or artifact verification after mutation
- keep the base binding mechanism generic and reusable

## Mandatory Anti-Patterns

- do not copy parameter tables from docs instead of using `get-tool-contract`
- do not treat lookup seed rows as default implementation
- do not use direct SQL as canonical MCP behavior
- do not leave `DisplayValue` semantics implicit for non-null lookup or image-reference rows

## References

- `references/workflow.md`
