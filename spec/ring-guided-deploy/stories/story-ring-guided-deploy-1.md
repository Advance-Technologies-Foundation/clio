# Story 1: Typed ClioStageEvent envelope + StageIds constants + JSON fixture contract test

**Feature**: ring-guided-deploy
**Repo**: `C:\Projects\clio` (foundation)
**FR coverage**: FR-09 (versioned/ordered envelope), FR-10 (stage shape), FR-11 (manifest / run-completed shapes), FR-12 (unknown-field / secret contract), FR-15 (shared versioned contract type)
**AC coverage**: AC-09 (event shapes), AC-11 (unknown-field tolerance contract), AC-12 (no-secret field contract)
**PRD**: [prd-ring-guided-deploy.md](../prd/prd-ring-guided-deploy.md)
**ADR**: [adr-ring-guided-deploy.md](../adr/adr-ring-guided-deploy.md) (D1, D2)
**Status**: review
**Size**: M (half day)
**Depends on**: —
**Blocks**: story-ring-guided-deploy-2, story-ring-guided-deploy-3, story-ring-guided-deploy-6

---

## As a

QA engineer / developer instrumenting deploy & uninstall progress

## I want

one versioned, ordered `ClioStageEvent` envelope record (with `manifest` / `stage` / `run-completed` shapes), a `StageIds` set of stable kebab-case string constants, and a committed JSON sample fixture asserted by a contract test

## So that

both the MCP emitter and the on-disk receipt reader share a single typed contract, and the Ring can mirror the exact same byte-level shape (sync-by-contract, not code-sharing)

---

## Acceptance Criteria

- [x] **AC-01** — Given a `ClioStageEvent` of `eventType=manifest`, when serialized, then it carries `schemaVersion` (int, =1), `eventType`, `runId` (guid), `sequence` (int), `operation` (`deploy`|`uninstall`), and a `stages[]` array of `{ stageId, name, index, total, conditional }` — field names exactly per ADR D2.
- [x] **AC-02** — Given a `ClioStageEvent` of `eventType=stage`, when serialized, then it carries `stage` = `{ stageId, name, index, total, status (running|done|failed|skipped), startedAtUtc?, durationMs?, message, detail?, errorCode?, skipReason? }`; optional fields are omitted when null (no null noise).
- [x] **AC-03** — Given a `ClioStageEvent` of `eventType=run-completed`, when serialized, then it carries `runCompleted` = `{ outcome (success|failure), summary, detail?, errorCode?, derivedUrl?, derivedPath? }`.
- [x] **AC-04** — Given `StageIds`, when referenced, then it exposes stable kebab-case string constants for every deploy stage (`stage-build`, `unzip`, `copy-files`, `restore-db`, `deploy-app`, `configure-conn-strings`, `register-env`, `wait-ready`) and every uninstall stage (`stop-iis`, `read-config`, `delete-iis`, `drop-db`, `delete-files`, `unregister`, `delete-apppool-profile`) — string keys, NOT enum ordinals.
- [x] **AC-05** — Given the committed JSON sample fixture, when the contract test deserializes it into `ClioStageEvent` and re-serializes, then the round-trip is byte-identical to the fixture (this fixture is the cross-repo compatibility anchor; the Ring mirror asserts against the identical bytes in story 6).
- [x] **AC-06** — Given a fixture with an unknown extra field, when deserialized, then the unknown field is tolerated (no throw) — proving FR-12 unknown-field tolerance at the type level.
- [x] **AC-ERR** — Given a fixture whose `schemaVersion` differs from the emitter's version, when read, then `schemaVersion` is exposed so a consumer can gate on it (the version is the compatibility gate per D2).

## Implementation Notes

From ADR "Implementation Plan → clio files to create" + D1/D2:

- `clio/Command/McpServer/Progress/ClioStageEvent.cs` — versioned envelope DTO as a plain `record` (DTO carrier — `new` is allowed per DI policy). Ordered fields exactly per the ADR D2 JSONC block. Use `System.Text.Json` with explicit `[JsonPropertyName]` (camelCase wire names as shown: `schemaVersion`, `eventType`, `runId`, `sequence`, `operation`, `stages`, `stage`, `runCompleted`) and `JsonIgnoreCondition.WhenWritingNull` on optional members.
- `clio/Command/McpServer/Progress/StageIds.cs` — `static` class of `const string` stage keys (both operations), defined ONCE here so the manifest, emitters (stories 2/3) and contract test all reference the same source.
- Commit the JSON sample fixture (one representative `manifest`, `stage`, `run-completed` triple) under the test project; this exact byte content is duplicated identically into the Ring repo in story 6.
- `schemaVersion` starts at `1`; document that it is bumped only on a breaking field change.
- No CLI flags, no HTTP, no DI behavior class in this story — types + fixture only.

Key files: `clio/Command/McpServer/Progress/ClioStageEvent.cs`, `clio/Command/McpServer/Progress/StageIds.cs`
Pattern to follow: existing MCP DTO records serialized via `System.Text.Json`; `ProgressNotificationParams.Meta` shape probed in ADR fact 5.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | Serialize/deserialize of each `eventType`; optional-field omission; unknown-field tolerance; fixture byte round-trip | `clio.tests/Command/McpServer/ClioStageEventContractTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition`. AAA + `because` on every assertion + `[Description]` on every test.

Targeted run: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer"`

## Definition of Done

- [x] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO005); no new `CLIO*` warnings
- [x] No CLI flags introduced (types + fixture only)
- [x] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`; AAA + `because` + `[Description]`
- [x] `schemaVersion = 1`; `stageId`s are string constants in one place; JSON fixture committed (byte-identical copy goes to the Ring repo in story 6)
- [x] XML doc comments on the public record/members
- [x] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-07-11
- Implementation completed: 2026-07-11
- Tests passing: 7/7 (`ClioStageEventContractTests`, TC-U-01..07) + full `Module=McpServer` regression green (1879/1879)
- Notes:
  - Types + fixture + contract test only (no emitter/tools/DI — stories 2+), per Implementation Notes.
  - Wire vocabularies (`eventType`/`operation`/`status`/`outcome`/`skipReason`) modeled as `string` on the DTO with stable `const string` companions in `ClioStageEventContract` (kept byte-identical round-trip trivial; ADR shows string wire values). `StageIds` holds the kebab-case stage keys.
  - `schemaVersion = 1` (`ClioStageEventContract.SchemaVersion`). Canonical `JsonSerializerOptions` is compact (`WriteIndented=false`) so each event is one line — matches the `_meta` envelope and NDJSON receipt.
  - Fixture is NDJSON (manifest+stage+run-completed triple) generated from the serializer itself and pinned to LF in `.gitattributes`. This is the cross-repo byte anchor for Ring story 6.
  - Secret contract (AC-12/FR-12): the envelope has NO field that can carry a connection string/password/token by design; redaction remains the emitter's job in stories 2/3.
  - Unknown-field tolerance (AC-06) is satisfied by System.Text.Json default skip behavior (no `JsonExtensionData` needed).
