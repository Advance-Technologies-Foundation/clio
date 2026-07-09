# Story 2: Slim core-tool descriptions + dedup environment-name parameter

**Feature**: mcp-lazy-schema
**FR coverage**: ADR Alternative A ("Description slimming only"), "adopted as the complementary first step"
**PRD**: _none — spike-driven feature_
**ADR**: [adr-mcp-lazy-schema.md](../adr/adr-mcp-lazy-schema.md)
**Status**: ready-for-dev
**Size**: M (half day)
**Risk**: LOW — no protocol change, no new types; description text + shared param only
**Blocked by**: story-mcp-lazy-schema-0 (only to confirm scope; technically independent of gating)

---

## As a

clio MCP server author

## I want

the verbose inline-instruction descriptions on the core flat tools trimmed and the `environment-name` parameter description deduplicated (it is repeated 184×)

## So that

clio's `tools/list` shrinks ~14-16k tokens as a low-risk standalone win before any executor work, and the flat core itself is smaller

---

## Acceptance Criteria

- [x] **AC-01** — Given the `environment-name` description repeated ~184× across tool schemas, when this story lands, then the description is sourced from one shared constant/reference, not re-inlined per tool, and `tools/list` byte count drops measurably.
- [x] **AC-02** — Given heavy descriptions (`sync-schemas` 14.4k, `create-entity-business-rule` 12.5k, `update-page` 11.2k), when slimmed, then inline procedural instructions move to the index/`ServerInstructions` (Story 7) or are removed, and each tool keeps a one-line purpose + arg docs.
- [x] **AC-03** — Given slimming must not lose semantics, when a description is trimmed, then no anti-pattern / flow-hint is silently dropped — each is either kept inline if load-bearing or migrated (tracked, handed to Story 7).
- [x] **AC-04** — Given the budget ratchet (Story 11), when this story lands, then the recorded `tools/list` baseline is updated to the new smaller number.
- [x] **AC-ERR** — Given a tool whose behavior depends on description text being parsed by a client, when verified, then no such dependency exists (descriptions are advisory) — documented in the PR.

## Implementation Notes

Key files:
- `clio/Command/McpServer/Tools/*.cs` — `[Description(...)]` on tool methods/args.
- `environment-name` shared description — introduce one constant (e.g. in a `McpToolDescriptions` static) and reference it everywhere.
- Heaviest tools named in ADR Context table.

Pattern: no hardcoded user-facing strings scattered — use a constant (project-context.md Code Style). This is a pure-text refactor; arg contracts (names, required, enums) MUST NOT change.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `environment-name` description resolves to the single shared constant on a sample of tools; arg names/required unchanged | `clio.tests/Command/McpServer/CoreToolDescriptionTests.cs` |
| Integration | n/a | — |
| E2E `[Category("E2E")]` | `tools/list` byte delta vs pre-slim baseline (NOT in CI — manual) | `clio.mcp.e2e/` |

Test naming + AAA + `because` + `[Description]` per policy.

## Definition of Done

- [x] No CLIO* warnings; descriptions via constants (no scattered literals)
- [x] Arg contracts (names/required/enums) unchanged — only description text
- [x] Anti-patterns/flow-hints accounted for (kept or handed to Story 7 with a list)
- [x] Budget baseline updated
- [x] Docs/MCP review: per AGENTS.md, confirm `docs/commands/*` + `help/en/*` unaffected (description-only) — state "docs reviewed, no update required" if so
- [x] PR references this story file

## Dev Agent Record

- Implementation started: 2026-06-19
- Implementation completed: 2026-06-19
- Tests passing: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" -f net10.0` → 1263 passed, 0 failed, 1 skipped.
- Build: `dotnet build clio/clio.csproj -c Release -f net10.0` → 0 errors, no new warnings, CLIO* clean (only the pre-existing CS0168 at line 46:93 in an untouched file).

### Measured `tools/list` before/after (serialized ProtocolTool set, net10.0)

| Mode | Before bytes | After bytes | Before ~tok | After ~tok |
|---|--:|--:|--:|--:|
| LAZY (mcp-lazy-tools ON, 27 tools) | 37,435 | 30,125 | ~9,358 | ~7,531 |
| FULL (default OFF, 126 tools) | 244,839 | 230,610 | ~61,209 | ~57,652 |

`ServerInstructions` (sent once per init, separate from `tools/list`): ~9.6k → ~4.1k chars.

> The lazy floor (~7.5k tok) is now dominated by the **input-schema bodies** of the core tools
> (e.g. `get-component-info` 2950 B, `get-page` 1864 B), which this story does NOT touch
> (description-text-only refactor). Reaching the ADR's ~4-5k aspiration would require slimming arg
> schemas — out of scope for Alternative A.

### What was slimmed

- **Heaviest core/long-tail tool descriptions:** `get-page` (PageGetTool 5975→1864 B), `update-page`
  (PageUpdateTool, the multi-paragraph routing prose removed), `get-component-info` (ComponentInfoTool),
  `find-app` (FindAppTool), `get-entity-schema-properties` (EntitySchemaTool), `validate-page`
  (PageValidateTool), `create-entity-business-rule` (BusinessRuleTool), and the shared
  `PlatformRequirementDescription` suffix on all 8 DataForge tools.
- **`sync-schemas` reviewed, NOT heavily slimmed:** its ~14.8k byte size is overwhelmingly the
  operations JSON **schema** (arg contract), not prose (~984 B of descriptions total). Prose was
  already terse; arg contracts must not change → left as-is.
- **Ubiquitous param dedup → terse shared consts** in new `clio/Command/McpServer/Tools/McpToolDescriptions.cs`
  (`EnvironmentName`, `Uri`, `Login`, `Password`, `PageResources`), applied across ~45 tool files
  (~130 description sites). Note: referencing a const does NOT shrink the serialized payload — the win
  is that the consts are SHORT; this also stops the verbose forms creeping back.

### Moved-guidance list (for Story 7 — nothing lost, all routing already existed in the target guide)

| Removed-from (tool description) | Routing/how-to text removed | Already present in (target) |
|---|---|---|
| `get-page` (PageGetTool) | per-section routing: business-rules vs handlers/validators; lookup-filter "classify the mechanism, not the wording / apply-static-filter / not crt.InitRequest"; converters; devkit; mobile-vs-web | `page-modification` guide PRE-EDIT GATE table (PageModificationGuidanceResource lines 34-41) + `mobile-page-modification` |
| `update-page` (PageUpdateTool) | the same per-section routing block; run-process-button "resolve codes with get-process-signature first" | `page-modification` GATE table (incl. run-process-button row, line 38) + `run-process-button` guide. **Kept inline (behaviour, not guidance):** conflict-detection contract, Designer Presence, INSERTED-FIELD CONTRACT summary |
| `get-component-info` (ComponentInfoTool) | full `resolvedFrom` / `environment-superset` / `latest-fallback` / `resolvedFromReason` prose | `ServerInstructions` "Freedom UI page work — version-check first" (kept the `requiresVersionConfirmation` / `resolvedFrom` branch instruction inline as a pointer) |
| `validate-page` (PageValidateTool) | the converter/handler/validator contract enumeration | `page-schema-converters` / `page-schema-handlers` / `page-schema-validators` guides (named in the slimmed description) |
| `create-entity-business-rule` (BusinessRuleTool) | the routing-phrase keyword list ("business entity rule / apply static filter / limit lookup to / show only…") | `business-rules` + `business-rule-filters` guides (named in the slimmed description) |
| `ServerInstructions` | verbose multi-example compile/profile-language/component-version/designer-URL prose | compressed in place (all section headings + load-bearing rules retained); deep detail already in `app-modeling` guide. Verified `ProfileLanguageGuidanceTests` invariants (`get-user-culture`, "ASK the user", "once per session") preserved. |

### AC outcomes

- **AC-01** — environment-name (and uri/login/password) deduped to one shared const; `tools/list` dropped measurably (see table). ✅
- **AC-02** — heaviest descriptions slimmed to one-line purpose + arg docs + guidance pointer; procedural detail moved to guides / ServerInstructions (sync-schemas weight is schema, not prose — see note). ✅
- **AC-03** — no anti-pattern / flow-hint silently dropped; each was already present in the target guide (table above). ✅
- **AC-04** — budget baseline tightened: `MaxLazyToolsSerializedBytes` 48k → 33k (above the 30.1k measurement, ~3.6k headroom). ✅
- **AC-ERR** — confirmed no tool behaviour depends on its description being parsed: descriptions are advisory `[Description]` attributes consumed only by the MCP host/model; arg binding uses `[JsonPropertyName]` kebab keys + CommandLineParser, never the prose. ✅

### Docs / MCP review (per AGENTS.md)

- **MCP reviewed:** this IS the MCP surface (tool `[Description]` + ServerInstructions). Prompts/Resources were the *targets* of the moved guidance (page-modification etc.), already accurate — reviewed, no content removed.
- **Docs (`docs/commands/*`, `help/en/*`, `Commands.md`): reviewed, no update required** — change is MCP-description-only; CLI verbs, options, help, and GitHub docs are unaffected (no `[Verb]`/`[Option]`/behaviour change).

### Deferred / out of scope

- Long-tail FULL-mode descriptions beyond the few named heaviest were left mostly intact (FULL is the unchanged default catalog; opt-in lazy mode is the primary token target). Further FULL slimming can ride later stories.
- Arg-schema slimming (the real lazy floor) is explicitly out of scope for Alternative A.
