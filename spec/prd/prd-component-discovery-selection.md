# PRD: Component Discovery & Selection for AI Agents

**Status**: Draft
**Author**: Claude (code-delivery, on behalf of Alex Kravchuk)
**Created**: 2026-06-15
**Epic**: [ENG-89871](https://creatio.atlassian.net/browse/ENG-89871) — Support MMP components
**Origin**: [ENG-91134](https://creatio.atlassian.net/browse/ENG-91134) (Paused) — split into solutions A/B/C/D/E
**Sub-tasks**: [ENG-91571](https://creatio.atlassian.net/browse/ENG-91571) (A), [ENG-91572](https://creatio.atlassian.net/browse/ENG-91572) (B), [ENG-91573](https://creatio.atlassian.net/browse/ENG-91573) (C), [ENG-91574](https://creatio.atlassian.net/browse/ENG-91574) (D), [ENG-91583](https://creatio.atlassian.net/browse/ENG-91583) (E)

---

## Problem Statement

An AI agent working through the clio MCP server systematically mis-picks Freedom UI components from
the ~200-component catalog. The reopened evidence (ENG-91134, k.bondarenko "Property management app"
session) shows two distinct failure heads:

1. **Discovery/selection.** The agent **never called `get-component-info` in list mode** and authored
   from memory — even though `McpServerInstructions.cs`, the tool `[Description]`, and the
   `page-modification` guidance resource **already** instruct list-mode discovery and name
   `crt.Gallery`. It locked into a "DataGrid vs FileList" frame before consulting the catalog and, on a
   direct question, still called `crt.FileList` the "only" gallery viewer. Compounding this, the
   registry description for `crt.Gallery` — *"Gallery list component with selectable cards, pagination
   events, and bulk menu actions"* — never signals image/photo/preview, so even on the catalog it does
   not read as a gallery viewer.
2. **Version detection.** The version signal (`environment-superset` / `latest-fallback` /
   `versionWarning`) was present in tool responses but **never communicated to the user**.

The root cause of head 1 is **behavioral** — passive instructions are skipped — layered on a **data**
problem (capability-poor descriptions). A point fix for `crt.Gallery` would not hold; the same class of
mis-pick recurs across the catalog. ENG-91134 is therefore Paused and decomposed into five solutions.

## Goals

- [ ] **G1 (D)** — The agent reliably discovers the full component space (faceted, not a flat 200-item
  dump) and stops authoring from memory.
  - SM-G1: in the ENG-91134 evidence scenario re-run, the agent selects `crt.Gallery` (or surfaces it
    as an option) for an image-collection prompt **without** an explicit user search request.
- [ ] **G2 (A)** — Every component in the catalog carries structured selection metadata.
  - SM-G2: 100% of components in the `latest` registry at story-close have non-empty
    `synonyms`/`useCases`/`whenToUse` (presence — snapshot-guarded); the confusable set is human-reviewed.
- [ ] **G3 (B)** — Component search returns a relevance-ranked shortlist, not an unordered substring filter.
  - SM-G3: on a shared labelled query→component set, deterministic-stage recall@5 ≥ threshold (set in ADR).
- [ ] **G4 (C)** — The vector-vs-lexical decision is made on measured evidence, not assumption.
  - SM-G4: an ADR records the measured gap and an explicit adopt/stop recommendation.
- [ ] **G5 (E)** — When the platform version is unknown, the agent is forced to communicate it.
  - SM-G5: on `latest-fallback`, the response carries a machine-readable confirmation flag and the
    active prompt requires the agent to relay it (asserted by e2e).

## Non-goals

- Will NOT make semantic/vector search the default — it is a measured contingency (C), not a goal.
- Will NOT add ML dependencies to clio unless C's ADR justifies them on measured evidence.
- Will NOT change behavior of non-component commands.
- Will NOT author caption/UI text for the agent (covered by the localizable-text work, ENG-91442).

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| AI agent (MCP) | a faceted catalog I cannot skip, with rich "when to use" metadata | I pick the right component (e.g. `crt.Gallery`) without being told to search |
| developer | the agent to suggest the correct Freedom UI component for my prompt | I do not get a grid/file-list workaround for a photo gallery |
| developer | to be told when the platform version is unknown | I can confirm the version before the agent assumes a component set |
| maintainer | a measured decision on vector search | clio does not gain a 100 MB ML dependency on a hunch |

## Feature Requirements

| ID | Requirement | Solution | Priority |
|----|------------|----------|---------|
| FR-01 | Registry entries carry `synonyms[]`, `useCases[]`, `whenToUse`, `whenNotToUse`, and an **applicability/entity-coupling constraint** field (for Contact/Account-coupled components like `crt.CommunicationOptions`). | A | Must |
| FR-02 | A use-case **category taxonomy** (~5–15 categories) is defined and every component is tagged against it. | A | Must |
| FR-03 | clio surfaces the new fields via **typed POCO mapping** (not opaque passthrough) so search can weight them and the snapshot guard stays green. | A | Must |
| FR-04 | Metadata is **derived at scale** (from `@CrtInput`/`@CrtOutput` JSDoc + `.component.md`), with a named human owner signing off the confusable set. | A | Must |
| FR-05 | `get-component-info` search returns a **scored, ranked top-N** (replacing the binary substring filter), weighting `synonyms`/`useCases` > `description` > type/parents/children > inputs/outputs, with a deterministic tie-break. | B | Must |
| FR-06 | The **deterministic** CLI and MCP search produce identical ranking. Any LLM reranking is **client-side, out of scope for clio**, and explicitly excluded from the parity contract. | B | Must |
| FR-07 | List mode offers **faceted category discovery** (drill-down by category, full category space always visible — the Amazon `find-tools` pattern) plus a **compact selection-index** (type + one-line `whenToUse` + `synonyms`). | D | Must |
| FR-08 | Detail responses for collection/visual types (`crt.DataGrid`/`crt.List`/`crt.FileList`/`crt.MultiList`/`crt.ImageInput`) carry a **decision-point `relatedComponents`** signal (e.g. "consider `crt.Gallery`"). | D | Must |
| FR-09 | A **discovery breadcrumb** nudges the agent toward the catalog when it requests detail without first listing. **Stateless** by default (see OQ-D1). | D | Must |
| FR-10 | **Fail-fast UX**: when a requested component cannot be built for the target entity, the response steers the agent to tell the user up front instead of silently substituting (covers ENG-91134 comment 453013). | D | Should |
| FR-11 | C runs a measured **spike + ADR**: a shared labelled query→component set, a deterministic top-k metric and threshold; vector is adopted **only** if a measured gap remains that lexical+facets cannot close. | C | Must |
| FR-12 | On `latest-fallback` (version unknown) the response carries a machine-readable **`requiresVersionConfirmation`** flag; the **active** page-work prompt requires the agent to relay it. | E | Must |
| FR-13 | Touching `get-component-info` triggers the full MCP-maintenance sweep (tool + prompts + resources + `clio.tests` + `clio.mcp.e2e` + `help/docs` + `McpCapabilityMap.md`). | A,B,D,E | Must |
| FR-14 | Existing component lookup/usage flows behave identically where unaffected (regression). | all | Must |

## CLI Impact

Behavior is primarily MCP-response-shape and search-ranking driven. Any new option is kebab-case
(CLIO001); any new `CLIO*` warning fails CI. The CLI `get-component-info` verb keeps parity with the
MCP tool on the **deterministic** ranking (FR-06).

## Acceptance Criteria

- [ ] AC-01 (G1/D) — Re-running the ENG-91134 evidence scenario, the agent surfaces/selects `crt.Gallery`
  for an image-collection prompt without an explicit search request.
- [ ] AC-02 (A) — Every `latest` component has non-empty `synonyms`/`useCases`/`whenToUse`; the snapshot
  fixture is de-truncated to a representative payload and the guard asserts presence.
- [ ] AC-03 (A) — The category taxonomy exists, is tagged on every component, and is the single source D consumes.
- [ ] AC-04 (B) — On the shared labelled set, deterministic recall@k ≥ the ADR threshold; ranking is
  identical between CLI and MCP; tie-break is deterministic across macOS/Linux/Windows.
- [ ] AC-05 (C) — An ADR records the measured deterministic-stage gap and an explicit adopt-vector / stop-at-A+B+D recommendation.
- [ ] AC-06 (D) — Detail responses for the listed collection/visual types carry `relatedComponents`; the
  breadcrumb fires on detail-without-list; both covered by `[Category("Unit")]` tests over `ComponentInfoGrouping`.
- [ ] AC-07 (E) — On `latest-fallback`, the response carries `requiresVersionConfirmation: true` and the
  active prompt requires the agent to communicate it (e2e assertion).
- [ ] AC-08 (all) — No new `CLIO*` warnings; targeted `Category=Unit&Module=McpServer` suite green; MCP
  surface + docs updated per FR-13.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | The registry snapshot guard is **strict** (`UnmappedExtensions` must be empty) — new top-level fields require typed POCO properties in clio AND a refreshed fixture, coordinated with the producer push. | A breaks the guard test on `master` between producer push and clio merge. |
| A-02 | The category taxonomy is owned by **A** (the data foundation); D only consumes it. Existing hardcoded `CategoryOrder` and the empty producer `category` field must be reconciled into one source. | D has three conflicting taxonomy sources and no owner. |
| A-03 | LLM reranking lives in the **MCP client** (the consuming agent), not in clio; the CLI has no LLM. "Ranking parity" means the deterministic tool output only. | B and C contradict on what "ranking" means; B's parity AC is unsatisfiable. |
| A-04 | A's "at-scale" generation produces reviewable diffs in `static-files-mcp`; presence is automatable, quality needs a named human owner for the confusable set. | Hallucinated metadata pollutes the registry and degrades B/D. |
| A-05 | The version signal already exists in code (`ComponentInfoResolution` + surfaced `versionWarning`); E's work is **enforcement/communication**, not surfacing. | E re-implements a no-op. |
| A-06 | `clio.mcp.e2e` is **not in CI** — deterministic assertions must live in Unit tests to actually gate. | B/D/E "e2e coverage" gives a false sense of a gate. |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-A1 | Final field names/shapes for selection metadata (scalar `whenToUse` vs array; merge `whenToUse`/`whenNotToUse`?). B and D must reference A's ADR output, not re-specify. | Architect | ADR |
| OQ-A2 | Who runs at-scale generation (producer Jenkins job is "planned", `static-files-mcp` is updated manually today)? Is a deterministic generator feasible now, or is the confusable set seeded manually via `fix-component-registry` first? | Architect/Producer | ADR |
| OQ-B1 | One **shared** labelled query→component set owned where? (B's ranking target and C's spike must use the same set to avoid drift.) | Architect | ADR |
| OQ-C1 | The exact deterministic metric + threshold that makes "a gap only embeddings can close" true vs false. | Architect | C-ADR |
| OQ-D1 | Is the breadcrumb stateless (always-on tip on detail responses) or session-aware (requires new per-session state in a stdio, transient-tool MCP server — its own ADR)? | Architect | ADR |
| OQ-D2 | Should A's split into A1 (schema + confusable set, unblocks B/C/D) / A2 (full ~200 backfill, trails) be formalized as separate stories? | Architect | story-writer |

## Dependencies

- **A → B, C, D**: B's weighting, D's selection-index/facets, and C's embeddings all consume A's data;
  B/D **code** can land before A, but their **acceptance** is blocked on A's data reaching the CDN
  (a separate repo's CI).
- **C** is benchmarked against **B + D** and is the gate that can cancel embeddings work; pull a minimal
  C measurement forward rather than running it strictly last.
- **E** is independent of A/B; shares the "enforced signal vs passive prose" pattern with D.
- Blocks: the umbrella ADR (`adr-component-discovery-selection.md`) and downstream stories/test plan.
