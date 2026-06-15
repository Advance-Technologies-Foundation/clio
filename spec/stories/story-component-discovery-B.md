# Story: Component Discovery & Selection — Solution B (ranked synonym/use-case search)

- **id**: story-component-discovery-B
- **jira**: ENG-91572
- **epic**: ENG-89871 · **feature**: component-discovery-selection
- **status**: in-progress
- **PRD**: spec/prd/prd-component-discovery-selection.md
- **ADR**: spec/adr/adr-component-discovery-selection.md (Decision 2; Decision 3 metric; Decision 6 labelled set)
- **depends_on**: story-component-discovery-A (B weights A's `synonyms`/`useCases` POCO fields — stacked on A's branch)

## Scope

Replace the binary substring filter in `get-component-info` list mode with a deterministic scored
ranking, so a natural-language need ("photo gallery for property cards") surfaces the best-fit
component first instead of relying on an exact substring hit.

clio: `ComponentInfoGrouping.cs`, `ComponentInfoTool.cs`, `ComponentInfoCommand.cs`, and the MCP
surface (tool `[Description]`, `McpServer/AGENTS.md`, CLI docs). LLM reranking stays client-side and
is explicitly out of clio scope.

## Definition of Done

- [x] `ComponentInfoGrouping.RankEntries` replaces the binary `Matches`/`FilterEntries` filter with a
  tokenised weighted scorer: tier order `synonyms`/`useCases` > `description`/`whenToUse` >
  identity (`componentType`/`category`/parents/children) > inputs/outputs/properties.
- [x] Deterministic tie-break: score descending, then `ComponentType` `OrdinalIgnoreCase` ascending
  (stable on macOS/Linux/Windows). Empty query → full catalog alphabetically.
- [x] **Parity scoped to the deterministic tool output**: the CLI verb and the MCP tool both call
  `RankEntries`, producing identical ordered output. LLM rerank is client-side, out of clio scope,
  not measured in `clio.mcp.e2e`.
- [x] Not-found suggestion path **unified** across CLI/MCP through `SuggestForUnknown` (the CLI
  previously used the full keyword filter; now both surfaces return the same bounded Levenshtein
  shortlist). Stated in the ADR/PR.
- [x] Shared labelled `query → expected-component(s)` set (Decision 6) authored in-repo
  (`clio.tests/Command/McpServer/Fixtures/component-discovery-labelled-set.json` + a shared loader),
  used by B now and reusable by C/E. Includes the Gallery case, the `crt.CommunicationOptions`
  applicability case, and the version-unknown case.
- [x] Deterministic ranking assertions in `[Category("Unit")]` over `ComponentInfoGrouping`
  (`ComponentInfoRankingTests`): tier weighting, tie-break, determinism, and deterministic recall@5
  ≥ 0.8 (ADR Decision 3) on the shared labelled set — since `clio.mcp.e2e` is not in CI.
- [x] MCP e2e coverage added (`ComponentInfoToolE2ETests` ranked-order test via local-override
  catalog) — not in CI, run manually against the real `clio mcp-server`.
- [x] Docs/MCP sweep: tool `[Description]`, `search` arg description, `McpServer/AGENTS.md`,
  `clio/docs/commands/get-component-info.md`. `Commands.md` one-line summary unchanged.
- [ ] **Full acceptance blocked on A2**: the producer backfill (`synonyms`/`useCases`/`description`)
  reaching the live CDN. The ranking code lands earlier; the labelled-set recall test benchmarks the
  algorithm against a self-contained curated catalog until A2's data is live (then C re-measures).

## Does NOT touch

`ComponentInfoListItem` and `ComponentRelations` are owned by Solution D (ENG-91574); the A POCO
selection-metadata fields are owned by Solution A (ENG-91571). B only reads those and reorders the
list — it adds no wire-shape fields (scores are an internal ranking detail asserted in unit tests).
