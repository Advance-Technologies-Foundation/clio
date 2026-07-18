# clio MCP ESQ filters guidance family

## Purpose
This article is the stable entry point for ESQ filter work. It routes to exactly one
construction guide for the caller's API surface and to the parsing guide only when code
receives a runtime C# filter tree. Detailed filter rules live in the child guides, not here.

## GATE: choose the owner before writing code
- JavaScript, Freedom UI, page JSON, or a DataService SelectQuery payload:
  read `esq-filters-frontend`.
- Native Creatio backend C# using `EntitySchemaQuery`:
  read `esq-filters-backend`.
- Runtime C# code that receives and interprets `EntitySchemaQuery.Filters`:
  read `esq-filter-parsing`.
- Comparing a filter created through DataService with a native backend filter:
  read the matching construction guide and `esq-filter-parsing`; compare the runtime
  tree, not the two authoring syntaxes.

## Shared boundary
- `esq` owns the surrounding SelectQuery envelope, selected columns, expressions,
  aggregation, and the master enum tables.
- `esq-filters-frontend` owns serialized JavaScript/DataService filter construction.
- `esq-filters-backend` owns native C# `EntitySchemaQuery` filter construction.
- `esq-filter-parsing` owns runtime C# traversal and interpretation.
- Do not copy detailed filter rules between these articles. Cross-link to the owner.

## Current backend validation status
The backend construction and parsing guides currently publish the lab-verified group
envelope, nesting, disabled nodes, group negation, primitive Integer/MediumText Compare,
MediumText null checks, Integer membership cardinalities, inclusive Between ranges, typed
Boolean/Guid comparisons, lookup equality/membership, temporal literals/macros/date parts,
Exists/NotExists/aggregate subqueries over backward paths, and saved Segment membership.
New filter families remain pending until the same native-vs-DataService runtime-shape test
proves and promotes them.