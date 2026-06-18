# ADR: analytics-widgets guidance floor + thin index routing

Status: Proposed
Date: 2026-06-18

## Context

Freedom UI analytics-widget authoring guidance must reach **every** MCP client (Copilot,
Cursor, generic, direct-clio — not only skill-aware Claude/Codex) through `get-guidance`,
as the single clio-owned source of truth. Two pressures shaped the design:

1. **Context budget / truncation (ENG-91182).** The always-on `McpServerInstructions`
   text had grown to ~2.2k cl100k tokens and was being silently truncated by clients that
   cap the instructions field (~1k tokens), dropping the guidance-routing rules entirely.
2. **Governance (TER "MCP guideline architecture" spec).** Guideline *content* lives once
   in clio as a guidance resource; routing is tiered — a tool names only a generic *domain
   index*; the index routes to the topic guide; the topic guide routes to the precise
   sub-guides. A specific (leaf) guide name must appear in exactly one place and never be
   fanned out across tool descriptions or the always-on router.

## Decision

- **Thin always-on router.** `McpServerInstructions` carries hard invariants
  (compile-not-required, long-running await, profile-culture, component-version check,
  destructive, correlation-id) plus a **names-only routing table to domain-index guides**.
  It does **not** enumerate leaf guide names. (~9.7k → ~2.5k chars; fits under client caps.)
- **analytics-widgets is a thin index, not a content copy.** It orients the agent and routes
  to the deep guides — `dashboards` (layout, 12-column grid, widget catalog, sizing, styling,
  patterns), `indicator-widget` (metric runtime payload), `page-modification` (page structure)
  — plus a net-new `placement-contexts` reference for non-dashboard surfaces (list-page
  analytics view, home page, form-page widget area). No deep content is duplicated.
- **Tiered routing, single source.** The router points page/analytics work at the
  `page-modification` domain index; the `page-modification` pre-edit checklist names
  `analytics-widgets` (and `dashboards`); `analytics-widgets` routes on to the sub-guides.
  Each leaf name appears in exactly one routing surface — no fan-out across tool
  `[Description]`s.
- **Guidance text is generic.** Surfaces are described by role, not by naming specific
  shipped dashboards/views/metrics.

## Consequences

- Always-on instructions fit under client truncation caps; routing rules are no longer
  silently dropped. (Empirically: an agent on this build loads
  `analytics-widgets` → `dashboards` → `indicator-widget` and authors a rule-compliant
  dashboard — 3 metric tiles 4+4+4=12, 2 charts 6+6=12, plain-white cards.)
- Adding a new analytics leaf guide is a bounded change: new resource + catalog entry +
  one index routing row. The router and tool descriptions are untouched.
- The token win is primarily about **truncation-safety**, not raw cost: with prompt caching
  the always-on prefix is cache-read, and the dominant always-on footprint is the tool
  schemas (~52k tokens), not the instructions. Reducing the tool-schema footprint
  (lazy/deferred tool loading) is tracked separately.

## Alternatives considered

- **Leaf names in the always-on router** (flat list of `dashboards` / `indicator-widget` /
  `analytics-widgets` / …): maximally discoverable but does not scale (router grows per
  guide) and re-introduces fan-out; rejected in favor of routing through the domain index.
- **Full content in a toolkit skill only**: reaches only skill-aware clients — a correctness
  hole for every other MCP client; rejected.
