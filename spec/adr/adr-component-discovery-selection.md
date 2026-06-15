# ADR: Component Discovery & Selection for AI Agents (umbrella)

**Status**: Accepted (2026-06-15, Alex Kravchuk)
**Author**: Claude (code-delivery, on behalf of Alex Kravchuk)
**PRD**: [prd-component-discovery-selection.md](../prd/prd-component-discovery-selection.md)
**Epic**: ENG-89871 · **Origin**: ENG-91134 (Paused)
**Sub-tasks**: ENG-91571 (A), ENG-91572 (B), ENG-91573 (C), ENG-91574 (D), ENG-91583 (E)
**Created**: 2026-06-15

> This is an **umbrella** ADR over five solution sub-tasks. C carries its own child ADR (the
> vector-search go/no-go). It was authored after a three-lens adversarial review of the sub-tasks;
> each Decision below closes a review finding. **Accepted 2026-06-15** (Alex Kravchuk) — the BMAD gate
> (Decision 7) is cleared; A/B/D/E may proceed (C remains gated behind its child vector-search ADR).

---

## Context

ENG-91134 reopened: an AI agent via the clio MCP server picks the wrong Freedom UI component
(grid/file-list instead of `crt.Gallery`) across the ~200-component catalog, and never communicates an
unknown platform version. Root cause is **behavioral** (the agent skips the discovery the existing
passive instructions already mandate) layered on a **data** problem (capability-poor descriptions).
Research on LLM tool/component selection (Anthropic ">30–50 tools degrades selection"; Amazon Prime
Video `find-tools` faceted discovery; BM25-vs-embeddings for small technical corpora; MCP tool-
description quality studies) points away from semantic/vector retrieval as the primary lever and
toward **rich metadata + faceted discovery + ranked lexical search with the LLM as a free reranker**.

The five solutions: **A** (registry selection metadata — foundation), **B** (ranked lexical search),
**C** (measured vector go/no-go), **D** (proactive + faceted discovery — carries the root cause),
**E** (version-detection enforcement).

## Decision (overall)

Adopt **A (data) + D (faceted discovery, behavioral teeth) as the primary fix**, with **B (ranked
search)** as the cheap retrieval upgrade and **E** closing the version head. **C demotes vector search
to a measured contingency**: it ships only if a deterministic baseline (A+B+D) leaves a measured gap.
The LLM reranker is a property of the **client**, never of clio. Sequence: **A → (B + D + E) → C**.

## Decision 1 (A) — Typed mapping + A owns the taxonomy; "passthrough" is rejected

**Finding closed:** "forward-compatible passthrough" is false against the strict snapshot guard, and
D references a taxonomy A does not produce.

- New entry fields (`synonyms[]`, `useCases[]`, `whenToUse`, `whenNotToUse`, `appliesToCustomEntities`/
  applicability constraint) are mapped onto **typed POCO properties** on `ComponentRegistryEntry` with
  `[JsonPropertyName]` and surfaced through `CreateDetailResponse`/`CreateListResponse`. They are **not**
  left in `[JsonExtensionData] UnmappedExtensions` — the guard test
  (`ComponentRegistrySnapshotTests.Live_Registry_Snapshot_Should_Have_No_Unmapped_Fields`) would fail
  the moment the producer ships them. A is therefore a **coordinated producer+clio change**, sequenced
  so the guard never goes red on `master` between the two.
- The **live-snapshot fixture is de-truncated** to a representative subset (≥1-per-category, incl. the
  full confusable set); a coverage assertion checks selection-metadata presence. In the **A1** slice the
  presence bar covers the confusable/seed set; the full ~200 catalog backfill (presence on every entry)
  is **A2**.
- **A owns the category taxonomy** (a controlled ~5–15 vocabulary, the single source — scalar `category`
  per component). Implementation note (2026-06-15): the assumed pre-existing hardcoded `CategoryOrder`
  does **not** exist anywhere (clio, generator, or creatio-ui). The only prior category notion is a
  human-authored `## Metadata · Category` line in some `.component.md` recipe docs (e.g. `crt.Gallery` =
  "interactive"), reconciled into this controlled taxonomy. A defines the taxonomy from scratch; the
  canonical machine source is a new `@category` JSDoc tag on the component class that the registry
  generator emits onto the producer `category` field (A2 producer change). D consumes it; D does not define it.
- **At-scale generation** produces reviewable diffs in `static-files-mcp`; presence is snapshot-gated
  (automatable), quality for the confusable set (Gallery/DataGrid/List/FileList/ImageInput/Timeline…)
  has a **named human owner** signing off. Presence ≠ quality — both bars are explicit.

**Rejected:** opaque `JsonElement` passthrough (snapshot guard forbids; B cannot weight opaque fields);
taxonomy owned by D (D needs it as input, cannot be its own source).

## Decision 2 (B) — "Ranking" = deterministic lexical tool output; LLM rerank is client-side

**Finding closed:** B's "identical CLI/MCP ranking" contradicts C's "agent as reranker" (LLM exists
only in MCP; CLI is synchronous, no model client).

- B replaces the binary `ComponentInfoGrouping.Matches` filter with a **scored ranking**: weight
  `synonyms`/`useCases` > `description` > type/parents/children > inputs/outputs; sort score desc, then
  `ComponentType` `OrdinalIgnoreCase` asc (deterministic, cross-OS stable).
- **Parity is scoped to the deterministic tool output** — the ordered list+scores clio returns. Any LLM
  reranking happens in the MCP **client** and is explicitly **out of clio's scope**, not part of the
  parity contract, and not measured in `clio.mcp.e2e`.
- The pre-existing not-found asymmetry (MCP `SuggestForUnknown` Levenshtein vs CLI `FilterEntries`) is
  called out: B states whether parity covers the suggestion path and, if so, unifies it.

**Rejected:** putting the LLM rerank "in the retrieval stack we ship" (non-deterministic, not a clio
artifact, breaks parity).

## Decision 3 (C) — Deterministic, falsifiable go/no-go; measure invocation before ranking

**Finding closed:** C had no falsifiable criterion, and the epic risks fixing ranking when the agent
never invokes search.

- One **shared, versioned labelled set** (Decision 6) and a pinned metric/threshold are defined
  **before** any spike work, e.g. "deterministic (no-LLM) recall@5 < 0.8 on the labelled set →
  embeddings warranted; ≥ 0.8 → stop at A+B+D."
- The measured baseline is the **deterministic** B+D stage (reproducible). The LLM-reranked number is
  reported as informational only, never as the gate.
- C **first measures invocation** ("does the agent call discovery at all" after D's teeth) and isolates
  "tool-not-invoked" from "tool-invoked-but-ranked-poorly". Only the latter can justify embeddings.
- If vector is adopted: item embeddings are producer-computed and published on the CDN beside
  `ComponentRegistry.json` (no runtime cost); the query-embedding path (local ONNX ~100 MB vs Creatio
  AI service via `IApplicationClient`, never raw `HttpClient`) is decided in C's child ADR with the
  +100 MB footprint regression weighed.

## Decision 4 (D) — Faceted discovery + decision-point signals; breadcrumb stateless by default

**Finding closed:** D's only enforcement lever was "optional"; the breadcrumb silently needed
per-session state the MCP server does not have.

- **Faceted discovery** (Amazon `find-tools` pattern): list mode exposes the use-case categories
  (Decision 1) and the agent drills down category→components while the full category space stays
  visible — the opposite of semantic-only retrieval, which hides the space and caused the bug.
- **Compact selection-index** (type + one-line `whenToUse` + `synonyms`) so the agent picks without
  loading ~200 full detail blobs.
- **Decision-point `relatedComponents`** on detail responses for collection/visual types — the lever
  hits the call the agent actually makes (`crt.DataGrid`/`crt.ImageInput` detail), not the startup
  instructions it skips. Promoted from optional to **required**.
- **Breadcrumb is stateless by default** (OQ-D1): an always-attached "tip: list the catalog" on detail
  responses, requiring no per-session memory. Session-aware "you skipped list mode" is deferred to its
  own ADR — the server is stdio with **transient** tools and has no session-state container; introducing
  one is not an "optional" checkbox.
- **Fail-fast** (FR-10, ENG-91134 comment 453013): when a component is not applicable to the target
  entity (using A's applicability field), the response steers the agent to tell the user, not substitute.
- D owns the `ComponentInfoListItem` shape extension (adds `synonyms`/`whenToUse`); B consumes it — one
  owner for the POCO change.

## Decision 5 (E) — Version enforcement, not surfacing

**Finding closed:** "surface version warnings" is already done in code (`ComponentInfoResolution` +
`versionWarning`); the real gap is communication.

- On `latest-fallback`, the response carries a machine-readable **`requiresVersionConfirmation: true`**
  flag (parallel to D's `relatedComponents`), and the **active** `PagePrompt.cs` requires the agent to
  relay the unknown version + request confirmation — not just the passive surfaces it skips.
- `PlatformVersionResolver` degrade paths are reviewed to separate "genuinely undeterminable" from
  transient failure. `environment-superset` keeps its soft caveat; `latest-fallback` is the hard stop.

## Decision 6 — One shared labelled set

A single versioned `query → expected component(s)` set (incl. the Gallery case, the
`crt.CommunicationOptions`-not-applicable case, and the version-unknown case) is owned in-repo and used
by **both** B (ranking target) and C (go/no-go) and E (version-communication case) — no per-task
duplicate sets, no drift.

## Decision 7 — BMAD gate & tracking

Per `project-context.md` ("no code before the ADR exists"), A/B/D/E are gated behind acceptance of this
umbrella ADR; C is additionally gated behind its child ADR. All five are registered in
`spec/sprint-status.yaml` with `feature: component-discovery-selection`.

## Consequences

- **Positive**: fixes the behavioral root cause (faceted discovery + decision-point signals the agent
  cannot skip) on a rich-metadata foundation; zero ML footprint unless measured-justified; version head
  closed with teeth; reproducible go/no-go for vector; one labelled set prevents drift.
- **Trade-offs**: A is a coordinated cross-repo change (producer + clio + fixture) on independent
  release cadences; B/D acceptance is blocked on A's CDN data; taxonomy definition is real design work.
- **Risks**: at-scale AI-generated metadata quality (mitigated by named human sign-off on the confusable
  set + snapshot presence gate); `clio.mcp.e2e` not in CI (mitigated by putting deterministic assertions
  in Unit tests over `ComponentInfoGrouping`).

## Pre-implementation Checklist

- [ ] A: typed POCO properties + `[JsonPropertyName]`; snapshot fixture de-truncated; guard updated
- [ ] A: category taxonomy defined + reconciled with `CategoryOrder`; applicability field added
- [ ] A: named human owner for confusable-set sign-off; presence vs quality bars separated
- [ ] B: scored ranking with deterministic tie-break; parity scoped to deterministic output only
- [ ] B: shared labelled set used (not a B-only anecdote); not-found path parity stated
- [ ] C: pinned metric + threshold before spike; measures invocation first; deterministic baseline
- [ ] D: `relatedComponents` required (not optional); breadcrumb stateless; faceted categories from A
- [ ] D: fail-fast applicability steering (comment 453013)
- [ ] E: `requiresVersionConfirmation` flag + active `PagePrompt`; degrade-path review
- [ ] All: kebab-case options (CLIO001); no new `CLIO*`; `[Category("Unit")]`; MCP-maintenance sweep + docs
- [ ] AC-01 acceptance: ENG-91134 evidence scenario re-run selects `crt.Gallery`
