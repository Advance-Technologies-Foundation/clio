# PRD: Detect External Schema Changes and Reload Before Applying Updates

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-06-12
**Jira**: [ENG-91317](https://creatio.atlassian.net/browse/ENG-91317) (parent epic: ENG-85256 "AI no-code agents")

---

## Problem Statement

When a user modifies a Freedom UI page outside the agent session (e.g. manually deletes a component in the Creatio designer), the AI agent keeps working from its locally cached schema snapshot and silently reverts the user's manual edits on the next `update-page` / `sync-pages` operation. Creatio already maintains a change signal — `SysSchema.Checksum` — but no clio page tool reads it today, so external modifications are undetectable and user work is destroyed without warning.

## Goals

- [ ] Goal 1 — Detect external schema modifications before any page write. Success metric SM-01: 100% of `update-page` / `sync-pages` writes against a schema with a captured baseline perform a checksum comparison before saving (verified by unit tests covering every write path). / Counter: zero false-positive conflicts in flows with no external modification (regression E2E scenario passes).
- [ ] Goal 2 — Never silently overwrite user edits. Success metric SM-02: in the E2E reproduction of the ticket scenario (get-page → external delete in designer → update-page), the write is blocked and a structured conflict is returned in 100% of runs. / Counter: deliberate overwrite remains possible — `force=true` succeeds in the same scenario.
- [ ] Goal 3 — Guide the upper-layer LLM agent to a correct recovery path. Success metric SM-03: conflict response contains machine-readable conflict details plus agent-guiding error text instructing re-run of `get-page`, rebase, retry, and force-only-after-user-confirmation (asserted by unit tests on response contract). / Counter: response size and tool latency in the no-conflict path do not measurably regress (at most one additional SysSchema metadata query per write).
- [ ] Goal 4 — Full backward compatibility. Success metric SM-04: all existing page-tool unit tests pass unchanged except where constructor signatures change; legacy/missing `meta.json` without a baseline block skips the check silently. / Counter: no new required arguments on any existing tool or CLI verb.

## Non-goals

- Will NOT: cover `update-client-unit-schema` — explicitly deferred to a separate ticket per approved scope decision.
- Will NOT: implement locking or eliminate the TOCTOU window between the checksum check and `SaveSchema` — within that window behavior remains last-write-wins (documented risk, see A-02).
- Will NOT: detect changes to parent schemas in the inheritance hierarchy — only the editable (own) schema is baselined, because the agent writes only the own body and parent changes would produce false positives.
- Will NOT: have clio converse with the end user — clio returns a structured conflict; the LLM agent above clio owns the user conversation and confirmation for `force`.
- Will NOT: compare `ModifiedOn` values — it is carried as opaque informational data only; `Checksum` is the sole comparison key.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| user of an AI no-code agent | the agent to detect that I edited the page in the designer and reload it before changing anything | my manual edits are not reverted by the agent's stale local copy |
| LLM agent (MCP client) | a structured conflict response with clear recovery instructions when the schema changed externally | I can re-fetch the page, rebase my change, and retry without guessing |
| LLM agent (MCP client) | a `force` argument on page-write tools | I can deliberately overwrite after the user explicitly confirms |
| developer using clio CLI | the `update-page` verb to support the same checksum guard via options | scripted page updates get the same protection as MCP flows |
| QA engineer | conflict detection to behave identically across `update-page` and `sync-pages` (per page) | I can verify one contract instead of N divergent behaviors |

## Feature Requirements

| ID | Requirement | Priority |
|----|------------|----------|
| FR-01 | `get-page` captures a baseline of the editable schema into `.clio-pages/{schema-name}/meta.json`: editable schema UId, `SysSchema.Checksum`, `ModifiedOn` (raw/opaque), environment identity (name + URI), `editableSchemaExists` flag, capture timestamp | Must |
| FR-02 | Before saving, `update-page` compares the expected (baseline) checksum with the current server-side `SysSchema.Checksum` of the editable schema and blocks the write on mismatch | Must |
| FR-03 | `sync-pages` performs the same pre-save check per page, reporting conflict status per page in the batch result without aborting other pages | Must |
| FR-04 | On conflict, the tool response carries `Conflict=true` plus structured `ConflictDetails` (reason code, expected/actual checksum, expected/actual schema UId, modifiedOn) and agent-guiding error text: do NOT retry with the same body; re-run `get-page`, re-apply the change on top, retry; use `force=true` only after the user explicitly confirms overwriting | Must |
| FR-05 | A `force` argument on `update-page` (and per-page on `sync-pages` input) skips the conflict check and performs a deliberate overwrite | Must |
| FR-06 | Conflict reason codes distinguish at minimum: `checksum-mismatch`, `schema-deleted-externally`, `schema-created-externally` (baseline says editable schema absent but it now exists), `schema-uid-mismatch` | Must |
| FR-07 | Missing or legacy `meta.json` without a `baseline` block → check is silently skipped (full backward compatibility) | Must |
| FR-08 | Environment mismatch between baseline identity and the environment of the write call → check is silently skipped (cross-environment write is not an external change) | Must |
| FR-09 | After a successful save, the saved schema's fresh checksum/modifiedOn are returned in the response and the local baseline is refreshed; if the post-save metadata query fails, the baseline is removed (fail toward "no check", never toward a false conflict) | Must |
| FR-10 | Checksum capture failure during `get-page` degrades to "no baseline" and never fails `get-page` itself | Must |
| FR-11 | The conflict check lives in a single chokepoint (`PageUpdateCommand`) so the MCP tools `update-page`, `sync-pages`, and the CLI verb share one implementation; CLI exposure via `--expected-checksum` / `--force` options (kebab-case) | Must |
| FR-12 | Dry-run mode of `update-page` also reports conflicts (check runs before the dry-run short-circuit) | Should |
| FR-13 | `sync-pages` verify path rewrites a full fresh `meta.json` next to the verified body (fixes the existing stale-baseline gap) | Should |
| FR-14 | MCP tool descriptions, page-flow guidance resource, and prompts explain the conflict contract and force semantics | Must |

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| New CLI option on `update-page` | `--expected-checksum` — baseline checksum to validate before save | No (optional) |
| New CLI option on `update-page` | `--force` — skip the external-modification check and overwrite | No (optional, default false) |
| New MCP arg on `update-page` tool | `force` (boolean); plus `output-directory` for baseline discovery | No (optional) |
| New MCP arg on `sync-pages` tool | per-page `force` on page input items | No (optional) |
| MCP-internal options (no `[Option]`) | `ExpectedSchemaUId`, `ExpectedSchemaAbsent` on `PageUpdateOptions` | No |
| Response contract extension | `PageUpdateResponse` / `PageSyncPageResult` gain `Conflict` / `ConflictDetails` and post-save `NewChecksum` / `NewModifiedOn` / `SavedSchemaUId` | No (additive) |
| File format extension | `meta.json` gains optional `baseline` block; legacy files remain valid | No (additive) |

All flags: **kebab-case only** (CLIO001 enforced).

This feature touches the MCP surface (`update-page`, `sync-pages`, `get-page` tools, page guidance resource/prompts) — keep `docs/McpCapabilityMap.md` and `clio.mcp.e2e` coverage aligned per repo MCP maintenance policy.

## Acceptance Criteria

- [ ] AC-01: Given a baseline captured by `get-page` and the schema subsequently modified externally (checksum differs on server), when `update-page` runs without `force`, then the schema is NOT saved and the response contains `Conflict=true` with `ConflictDetails.Reason="checksum-mismatch"`, expected and actual checksums, and agent-guiding recovery text.
- [ ] AC-02: Given the same conflict state, when `update-page` runs with `force=true`, then the schema IS saved and the response contains the fresh `NewChecksum`.
- [ ] AC-03: Given a baseline where `editableSchemaExists=false`, when `update-page` runs and an editable schema now exists on the server, then the write is blocked with `Reason="schema-created-externally"`.
- [ ] AC-04: Given a baseline with a checksum, when the editable schema no longer exists on the server (would be re-created), then the write is blocked with `Reason="schema-deleted-externally"`.
- [ ] AC-05: Given no `meta.json` or a legacy `meta.json` without a `baseline` block, when `update-page` or `sync-pages` runs, then no check is performed and behavior is identical to the pre-feature flow.
- [ ] AC-06: Given a baseline captured against environment A, when the write targets environment B, then the check is silently skipped.
- [ ] AC-07: Given a `sync-pages` batch with one stale page and one fresh page, when the batch runs, then the stale page reports a per-page conflict, the fresh page is saved successfully, and the batch does not abort.
- [ ] AC-08: Given a successful save, when the post-save metadata re-read succeeds, then the local baseline reflects the new checksum; when it fails, then the baseline file's baseline data is removed and the save still reports success.
- [ ] AC-09: Given `get-page` runs and the checksum query fails, when the tool completes, then the page is still returned successfully and `meta.json` is written without a baseline block.
- [ ] AC-10: Given `update-page` in dry-run mode against a stale baseline, when it runs, then the conflict is reported without any write.
- [ ] AC-11 (regression): Given a normal agent flow with no external modifications (get-page → update-page → update-page), when it runs, then every write succeeds with no conflict and no behavior change versus the pre-feature flow.
- [ ] AC-ERR: Given `--expected-checksum` supplied via CLI and the server checksum differs, clio prints an error message identifying the external-modification conflict (including expected vs actual checksum) and exits non-zero.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | Creatio updates `SysSchema.Checksum` on every schema save from the designer (precedent: `SaveSettingsToManifestCommand` reads it) | Detection silently misses external edits — the core scenario fails. Mitigation: run the first E2E scenario early to verify checksum semantics before building the rest. |
| A-02 | The TOCTOU window between checksum check and `SaveSchema` is acceptably small for interactive agent use; last-write-wins inside the window | Rare lost updates remain possible; documented limitation, not solvable without server-side locking (out of scope). |
| A-03 | Baselining only the editable (own) schema is sufficient — agent writes only the own body, parent-schema changes are irrelevant | Changes merged from parents would be invisible; accepted by design to avoid false positives. |
| A-04 | `ModifiedOn` from DataService is format-unstable across versions and must be treated as opaque | If compared, false conflicts on format drift; mitigated by comparing Checksum only (Non-goal). |
| A-05 | One extra SysSchema metadata query per write (and possibly one post-save) is negligible latency | Perceptible slowdown of agent loops; counter-metric in SM-03 guards this. |
| A-06 | `.clio-pages/{schema-name}/meta.json` location is discoverable from tool args (`output-directory` or `body-file` sibling) reliably enough for baseline lookup | Baseline silently not found → check skipped (fails safe toward old behavior, but reduces protection coverage). |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Does the `SaveSchema` / `GetSchema` response already include the new checksum (would eliminate the post-save SysSchema query)? Verify against a live response during implementation; SysSchema query is the safe fallback. | Architect / implementer | Before implementation of FR-09 |
| OQ-02 | Exact early-E2E confirmation that designer saves bump `SysSchema.Checksum` on the supported Creatio versions (A-01) | Implementer | First E2E scenario, before completing stories |

## Dependencies

- Depends on: existing page tooling — `PageGetTool` / `PageUpdateTool` / `PageSyncTool` (`clio/Command/McpServer/Tools/`), `PageUpdateCommand` / `PageGetCommand` (`clio/Command/PageUpdateOptions.cs`, `PageGetOptions.cs`), `PageSchemaMetadataHelper`, `.clio-pages` output convention (`PageOutputDirectoryResolver`).
- Depends on: authoritative approved technical design (root cause, conflict contract, mechanics) — see plan referenced in ENG-91317 delivery notes.
- Blocks: ADR `spec/adr/adr-detect-external-schema-changes.md`, stories, and test plan `spec/test-plans/tp-detect-external-schema-changes.md` for ENG-91317.
- Related (out of scope): future ticket extending the same guard to `update-client-unit-schema`.
