# Story: Component Discovery & Selection — Solution A (selection metadata + taxonomy)

- **id**: story-component-discovery-A
- **jira**: ENG-91571
- **epic**: ENG-89871 · **feature**: component-discovery-selection
- **status**: in-progress (A1 slice)
- **PRD**: spec/prd/prd-component-discovery-selection.md
- **ADR**: spec/adr/adr-component-discovery-selection.md (Decision 1; Accepted 2026-06-15)

## Scope

Data foundation for B/C/D. Split per OQ-D2:

- **A1 (this slice)** — typed POCO selection-metadata fields + category taxonomy (single source) +
  de-truncated snapshot fixture + confusable-set seed + clio-self-sufficient tests. clio-only,
  fixture-gated, unblocks B/C/D.
- **A2 (trails)** — full ~200-catalog backfill + live CDN seed in `static-files-mcp` + at-scale
  JSDoc / `.component.md` derivation in `creatio-ui` (producer Jenkins job is "planned").

## Definition of Done (A1)

- [x] Typed POCO fields (`synonyms`, `useCases`, `whenToUse`, `whenNotToUse`,
  `appliesToCustomEntities` + `entityCouplingNote`) on `ComponentRegistryEntry` with
  `[JsonPropertyName]`; `UnmappedExtensions` stays empty (snapshot guard green).
- [x] Category taxonomy (`ComponentCategories`, controlled ~5–15 vocabulary) as the single source;
  fills the existing-but-empty producer `category` field. (Correction: no prior hardcoded
  `CategoryOrder` existed anywhere — nothing to reconcile; assumption A-02 was false.)
- [x] New fields surfaced on `CreateDetailResponse`; searchable via `ComponentInfoGrouping.Matches`
  (binary filter; Solution B will replace it with scored ranking).
- [x] Live-snapshot fixture de-truncated (3 → 19, representative + full confusable set); presence,
  taxonomy-validity, and applicability assertions added.
- [x] MCP sweep: tool `[Description]`, page-modification guidance, CLI docs. (`McpCapabilityMap.md`
  is absent from the repo — not applicable.) E2E coverage via local-override catalog.
- [x] Unit + e2e tests; build clean; targeted `Category=Unit&Module=McpServer` green.
- [ ] Named human owner signs off the confusable-set prose quality (presence ≠ quality).
- [ ] A2: full catalog backfill + live CDN seed (separate, sequenced so the snapshot guard never
  goes red on `master` between the producer push and the clio merge — assumption A-01).

## Does NOT touch

`ComponentInfoListItem` and `ComponentRelations` are owned by Solution D (ENG-91574); A only provides
the data those consume.
