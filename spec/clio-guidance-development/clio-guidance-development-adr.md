# ADR: evidence-backed clio guidance development

## Status

Accepted.

## Decision

Use a two-artifact model:

1. A disposable lab records questions, runtime observations, failures, debugger/source evidence, and exact acceptance results.
2. clio publishes a concise projection containing prerequisites, decisions, executable patterns, success assertions, failure recovery, safety constraints, and an explicit verification boundary.

The shared workflow lives once in `.ai/skills/clio-guidance-development/SKILL.md`. Claude and Codex use small validated redirect skills because Git symlinks are not portable to ordinary Windows checkouts.

ESQ filters form one routed family:

- `esq-filters` routes by responsibility.
- `esq-filters-frontend` owns serialized JavaScript/DataService construction.
- `esq-filters-backend` owns native backend C# construction.
- `esq-filter-parsing` owns fail-closed runtime interpretation.

`virtual-entities` owns the schema/executor/provider lifecycle and delegates filter and generic event-listener details to their canonical guides.

## Consequences

- Guidance changes require evidence, catalog/routing integration, decision-point discoverability, unit tests, and MCP E2E tests.
- Unsupported behavior remains explicit instead of being inferred from adjacent cases.
- Provider implementations must enforce caller/tenant authorization and bounded query pushdown.
- Remote Creatio feature enablement uses the exact CLI fallback until a dedicated MCP tool exists.
