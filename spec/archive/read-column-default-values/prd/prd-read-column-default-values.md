# PRD: Read Column Default Values — Schema-Designer Path vs OData Approach

**Status**: Draft
**Author**: PM Agent
**Created**: 2026-06-12
**Revised**: 2026-06-12 — adversarial review findings applied (L1-01..L1-07, L2-01..L2-06, L3-01..L3-07)
**Jira**: [ENG-91318](https://creatio.atlassian.net/browse/ENG-91318) (parent epic: ENG-85256 "AI no-code agents")

---

## Problem Statement

AI no-code agents using clio need a reliable way to read default column values after
configuring them, but it is unclear whether clio's current read path (Entity Schema
Designer design-time service) is the right long-term approach when other Creatio teams
reportedly read default values via OData. For the epic's flagship scenario — create a
lookup, add a lookup column to an object, set a lookup-record default — the current
readback may return only a raw GUID with no display value and no existence guarantee,
which is not verifiable by an agent without extra queries.

## Background — Current State (verified in code, 2026-06-12)

### How clio reads default column values today

The read path is **design-time, via the Entity Schema Designer service — not OData**:

```
get-entity-schema-column-properties (CLI verb / MCP get-entity-schema-column-properties)
  → GetEntitySchemaColumnPropertiesCommand            clio/Command/GetEntitySchemaColumnPropertiesCommand.cs
  → RemoteEntitySchemaColumnManager.GetColumnProperties()
                                                      clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs
  → RemoteEntitySchemaDesignerClient.GetSchemaDesignItem()
                                                      clio/Command/EntitySchemaDesigner/RemoteEntitySchemaDesignerClient.cs
  → HTTP POST ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem
    (via IApplicationClient; same service the Creatio designer UI uses)
```

The response is an `EntityDesignSchemaDto` whose columns carry a `DefValue` DTO
(`ValueSourceType`, `Value`, `ValueSource`, `SequencePrefix`, `SequenceNumberOfChars`).
`EntitySchemaDesignerSupport.CreateDefaultValueConfig` maps it to the structured
`default-value-config` readback object (sources: `None | Const | Settings | SystemValue | Sequence`),
shipped in clio 8.0.2.47 (see `spec/entity-schema-default-values/entity-schema-default-values-plan.md`).

### Where OData stands in clio today

- MCP tools `odata-read`, `odata-create`, `odata-update`, `odata-delete`
  (`clio/Command/McpServer/Tools/OData*.cs`) query **data endpoints only**
  (`odata/{EntitySet}` with `$filter/$select/$expand/$orderby/$top`).
- **No clio code path reads OData `$metadata`** — `ODataReadTool` cannot target it —
  and OData is **not used anywhere for default values**, neither read nor write.

### Known weak spot — the ticket case (lookup-record default)

For a lookup column with a `Const` default (a lookup record reference):

- **Write**: caller must supply the raw record **GUID** in `default-value-config.value`.
  `EntitySchemaDefaultValueSourceResolver` resolves friendly names only for
  `Settings` and `SystemValue` — there is **no display-name → GUID resolution** for
  lookup-record constants, and **no validation** that the GUID exists in the
  referenced lookup table.
- **Read**: `CreateDefaultValueConfig` returns the raw scalar `Value` (the GUID) —
  **no display value** of the referenced record is resolved.
- **Validation**: `ValidateDefaultValueConfig` blocks `Const` only for binary-like
  types (`Binary`, `Image`, `File`); lookup `Const` passes through unvalidated.
- It is **unverified** whether the platform `SaveSchema` contract for a lookup
  `defValue.Value` expects a plain GUID string or a structured value (the designer UI
  may persist display metadata alongside) — see OQ-03.

## Definitions

**Machine-verifiable readback** (binary predicate; gates FR-05/FR-06/FR-07 and the
AC-03 verdict):

> A lookup-default readback is **machine-verifiable** iff, **without issuing
> additional queries**, it contains all of:
> (a) the default-value **source** (`Const`),
> (b) the **value** (record GUID),
> (c) the **referenced schema name**, and
> (d) the **display value** of the referenced record **OR** an explicit
> unavailability marker (see `record-resolution` markers below).

All four present → pass; anything missing → fail. No subjective judgement involved.

**`record-resolution` markers** (honest semantics — must not claim more than the
query can prove):

| Marker | When |
|--------|------|
| `no-access` | The read on the referenced entity is denied at schema level (explicit security error) — readback degrades to GUID + marker, the command does NOT fail (fail-soft; no regression vs today's GUID-only readback) |
| `not-found-or-no-access` | The lookup query succeeds but returns no row — an empty result **cannot distinguish** a deleted record from one hidden by row-level security, so the marker must not pretend to. Split into distinct markers only where the platform makes the cases distinguishable |

## Goals

- [ ] Goal 1 — Produce a documented, evidence-based comparison of the two read
  approaches (schema-designer service vs OData) with an explicit adopt/keep decision.
  **SM-01**: comparison document + decision exists in `spec/` and is referenced by the
  follow-up ADR; every claim about OData capabilities is backed by a captured response
  from a real Creatio instance (no speculation). / **Counter**: existing
  `get-entity-schema-column-properties` structured readback contract does not regress —
  for **Phase A** the counter is an **empty code diff** (investigation produces
  documents only); for **Phase B**, unit tests stay green and the MCP E2E suite is run
  **manually before merge** (MCP E2E is not in CI).
- [ ] Goal 2 — Verify the ticket case end to end on a real environment: create lookup
  entity → seed a lookup record → add lookup column to an object → set lookup-record
  default → read it back → confirm the default applies at runtime.
  **SM-02**: the scenario is executed against a real Creatio instance and the exact
  request/response payloads (including the persisted `defValue` shape for the lookup
  column) are recorded in the investigation document. / **Counter**: the FR-03 ticket
  scenario does not require any manual UI step — clio commands/MCP tools only. (The
  FR-02 `$metadata` probe is exempt from this counter: clio has no `$metadata` reader,
  so that capture uses a scripted authenticated GET — see FR-02 — recorded as evidence,
  not shipped.)
- [ ] Goal 3 — If the lookup-default readback is found insufficient for agent
  verification (fails the machine-verifiable predicate in Definitions), define closure
  requirements precise enough for the Architect to design against. **SM-03**: each
  identified gap maps to a numbered FR with a binary acceptance criterion. /
  **Counter**: no scope creep into page-level defaults or OData write-path migration.

## Non-goals

- Will NOT: migrate the default-value **write** path to OData — write stays on the
  Entity Schema Designer `SaveSchema` contract regardless of the read decision.
- Will NOT: implement a general-purpose OData `$metadata` reader/browser in clio
  (only what the decision requires, if anything). If Phase B does add a `$metadata`
  reader, it **MUST go through `IApplicationClient`** — never raw `HttpClient`
  (hard rule, `project-context.md`).
- Will NOT: touch page-level default values or business-rule defaults (out of scope of
  the schema-side contract, same as the 8.0.2.47 feature).
- Will NOT: change `default-value-config` semantics for non-lookup sources
  (`Settings`, `SystemValue`, `Sequence`) — they are released and stable.

## User Stories (high level)

| As a | I want | So that |
|------|--------|---------|
| AI no-code agent (epic ENG-85256) | to read back a lookup column's default value in a machine-verifiable form (per Definitions: source + GUID + referenced schema + display value or marker) | I can confirm my mutation succeeded without guessing or issuing ad-hoc OData queries |
| developer | a documented comparison of the designer-service vs OData read approaches | I align clio with other teams' practice deliberately, not accidentally |
| QA engineer | a reproducible E2E script for the lookup-default case | I can regression-test default-value readback on every release |
| architect | precise gap requirements with evidence from a real instance | the follow-up ADR designs against facts, not assumptions |

## Feature Requirements

Phase A — investigation (unconditional):

| ID | Requirement | Priority |
|----|------------|---------|
| FR-01 | Document the current read path end to end (commands, MCP tools, service endpoint, DTO shape, mapping code) in an investigation doc under `spec/read-column-default-values/` | Must |
| FR-02 | Investigate the "OData approach": capture from real Creatio instances (per the environment matrix below) whether OData v4 `$metadata` exposes column default values (e.g. CSDL `DefaultValue` attribute / annotations) and from which platform version (absorbs former OQ-02), and what exactly other teams read (confirm with them — see OQ-01); assess whether clio's existing `odata-read` could serve the need without new code. **Probe vehicle**: `ODataReadTool` targets `odata/{EntitySet}` only and **cannot fetch `$metadata`**; the capture uses a scripted authenticated GET via `IApplicationClient`-based tooling (e.g. clio `call-service` or an equivalent script) — never raw `HttpClient` | Must |
| FR-03 | Execute the ticket case end to end on a real environment, per the scenario steps below, and record actual payloads, including the persisted `defValue` shape and the runtime-applied value | Must |
| FR-04 | Produce a comparison matrix (capability, fidelity for the lookup case, auth/permission requirements, version coverage **including the environment-matrix rows below**, performance, maintenance cost) and record an explicit decision: keep schema-designer read path, adopt OData read path, or hybrid. If OQ-01 is unanswered by the time FR-02/FR-03 evidence is complete, record the decision based on the empirical probe alone, with an explicit risk note (see OQ-01 fallback) | Must |
| FR-05 | Define the contract for usable lookup-default readback: when `source=Const` on a lookup column, readback satisfies the **machine-verifiable predicate** (Definitions) — record GUID **and** (resolved display value **or** a `record-resolution` marker: `no-access` / `not-found-or-no-access`). Display-value enrichment is **fail-soft**: a permission or resolution failure degrades to GUID + marker and never fails the whole command | Should |
| FR-06 | Define the write-side gap closure: accept a lookup record reference by display value (resolved to GUID, ambiguity rejected) and/or validate that a supplied GUID exists in the referenced lookup table before save | Should |
| FR-07 | Update docs (`clio/docs/commands/get-entity-schema-column-properties.md`, `modify-entity-schema-column.md`, `clio/help/en/*.txt`, `clio/Commands.md`) and MCP guidance (`EntitySchemaTool`/`EntitySchemaPrompt`, `get-tool-contract`) with the lookup-default usage pattern resulting from the decision | Must (conditional: mandatory together with any implementation of FR-05/FR-06 — AGENTS.md makes docs + MCP surface updates mandatory for any command behavior change) |
| FR-08 | Close the loop on the ticket: record the investigation outcome (decision + link to the comparison doc and evidence) as a comment in Jira ENG-91318 and surface it to epic ENG-85256 | Must |

FR-05/FR-06/FR-07 are conditional on FR-03/FR-04 confirming the gap; if the
investigation proves current readback already satisfies the machine-verifiable
predicate, they are closed as "not needed" with evidence, and the PRD is updated.

### FR-03 scenario steps (normative)

1. Create the lookup entity via `create-lookup` / `create-entity-schema`
   (this creates an **empty** table — a `Const` default needs an existing record).
2. **Insert at least one record into the new lookup** (via `odata-create` or
   `create-data-binding` + `add-data-binding-row`) and capture its GUID. This step is
   the heart of the ticket question — **"where does the agent get the GUID"** for the
   `Const` default — and must be recorded with the exact call used.
3. Add the lookup column to the target Object via `modify-entity-schema-column` /
   `update-entity-schema`.
4. Set the lookup-record `Const` default using the GUID captured in step 2.
5. Read back via `get-entity-schema-column-properties`; record the persisted
   `defValue` payload.
6. **Runtime verification**: insert a record into the Object (e.g. via `odata-create`)
   **without** supplying the column, then read it back and confirm the column actually
   received the default value. Metadata persistence alone is insufficient — A-02 names
   exactly this risk.

### Environment matrix (FR-02 / FR-04)

| Dimension | .NET Framework | .NET Core |
|-----------|----------------|-----------|
| OData metadata URL | `0/odata/$metadata` | `odata/$metadata` |
| Designer service | `0/ServiceModel/EntitySchemaDesignerService.svc/...` | `ServiceModel/EntitySchemaDesignerService.svc/...` |
| Minimum platform version where `$metadata` carries defaults (if at all) | to be determined empirically by FR-02 | to be determined empirically by FR-02 |

**Sufficiency rule**: one .NET Framework instance + one .NET Core instance on a
currently supported 8.x version is sufficient evidence for the keep/adopt/hybrid
decision. An older-version probe is added only if the decision turns out to hinge on
version coverage (it then lands as an AC-ERR-style row in the comparison matrix).

## CLI Impact

| Change | Details | Breaking? |
|--------|---------|-----------|
| None (Phase A) | Investigation produces documents only | No |
| Possible (Phase B, post-decision) | Readback enrichment lives inside the existing `default-value-config` JSON object (e.g. `display-value`, `record-resolution` properties) — no new CLI flags anticipated | No |
| Possible (Phase B, post-decision) | If write-side name resolution is adopted, it reuses the existing `default-value-config.value` / `value-source` contract — no new flags anticipated | No |

All flags and JSON property names: **kebab-case only** (CLIO001 enforced). Any change
to `EntitySchemaTool` triggers the mandatory MCP review per `docs/McpCapabilityMap.md`
and the MCP maintenance policy (tool + prompt + `get-tool-contract` + `clio.tests` +
`clio.mcp.e2e`).

## Acceptance Criteria

Phase A (this PRD's Definition of Done) — AC-01..AC-04 + AC-ERR only. Conditional
implementation criteria moved to "Draft requirements for ADR / Phase B" below.

- [ ] AC-01: Given the investigation doc, when the Architect reads it, then the exact
  HTTP endpoint clio calls for default-value readback
  (`ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem`) and the full
  DTO-to-`default-value-config` mapping are documented with file/line references.
- [ ] AC-02: Given a real Creatio instance with a configured **lookup-column `Const`
  default** and at least one configured **primitive-column default** (e.g. Boolean or
  Text), when its OData `$metadata` is fetched via the FR-02 probe mechanism, then the
  investigation doc contains the captured CSDL fragments for **both** columns, stating
  explicitly whether/where default values appear. Rationale: CSDL `DefaultValue` is
  defined on **primitive properties, not navigation properties** — a primitive-only
  proof would not answer the ticket case, and a lookup-only fragment without the
  primitive baseline would not prove whether `$metadata` carries defaults at all.
- [ ] AC-03: Given a fresh environment, when the FR-03 scenario is executed (all six
  normative steps, including the lookup-record insert and the runtime verification
  insert into the Object), then the doc records each clio/MCP call, its response, the
  persisted `defValue` payload, and the runtime-applied column value — with a
  **pass/fail verdict evaluated strictly against the machine-verifiable predicate in
  Definitions** (all four components present → pass; otherwise fail, listing the
  missing components).
- [ ] AC-04: Given the comparison matrix, when the decision is recorded, then the
  Decision section contains **ADR-ready inputs**: exactly one chosen option
  (keep / adopt OData / hybrid), every rejected option with its rejection reason, and
  references to the evidence-matrix rows (including environment-matrix rows) that
  drove the choice — sufficient for the Architect to draft
  `spec/adr/adr-read-column-default-values.md` without re-investigation.
- [ ] AC-ERR: Given an environment where `$metadata` is unavailable (older Creatio /
  OData disabled), when the OData probe runs during investigation, then the limitation
  is recorded as a version-coverage row in the comparison matrix rather than failing
  the investigation.

## Draft requirements for ADR / Phase B (conditional)

These are **not** acceptance criteria of this investigation PRD. They are drafted
inputs for the follow-up ADR and become acceptance criteria of Phase B implementation
stories **only if** FR-03/FR-04 confirm the gap.

- [ ] DRAFT-AC-05 (former AC-05): Given a lookup column with a `Const` default, when
  `get-entity-schema-column-properties` is called, then `default-value-config` returns
  the record GUID **and (the referenced record's display value OR a
  `record-resolution` marker)**: `no-access` when the read on the referenced entity is
  denied at schema level; `not-found-or-no-access` when the lookup query returns no
  row (an empty result cannot distinguish a deleted record from one hidden by
  row-level security — markers must stay honest). Enrichment is fail-soft and never
  fails the command (no regression vs today's GUID-only readback).
- [ ] DRAFT-AC-06 (former AC-06): Given a GUID supplied as a lookup `Const` default
  that does not resolve in the referenced table, when the mutation is executed, then
  clio prints `Error: default value record '{guid}' not found in '{ReferenceSchema}'`
  and exits non-zero, without saving the schema.
  **Caveats**: validation is point-in-time (TOCTOU) — the record may be deleted
  between validation and save, so validation reduces but does not eliminate the
  broken-default risk; the edge "lookup just created in the same session, possibly
  still empty" must be covered (validation against an empty table must produce the
  same honest error, prompting the agent to seed a record first — FR-03 step 2).
- Display-value semantics to define (→ OQ-06): which column is "the display value"
  (primary display column), culture/localization handling, and the
  `imageLookup` → `SysImage` case.
- Display value that itself parses as a GUID: follow the GUID-first-then-alias
  precedent in `EntitySchemaDefaultValueSourceResolver`; define behavior for
  case-duplicate display names (ambiguity → reject with a clear error).
- Test coverage for any Phase B change: `[Category("Unit")]` tests (NSubstitute) for
  mapping and marker logic in `clio.tests`, plus MCP E2E coverage in `clio.mcp.e2e`
  for the tool contract. **Flag**: MCP E2E is not in CI — it must be run manually
  before the Phase B merge (this is also the SM-01 Phase B counter).
- Any Phase B `$metadata` reader MUST go through `IApplicationClient` — never raw
  `HttpClient`.

## Assumptions Index

| # | Assumption | Risk if wrong |
|---|-----------|--------------|
| A-01 | Creatio OData v4 `$metadata` may expose column defaults; this is unverified (absorbs former OQ-02 — answered empirically by the FR-02 probe across the environment matrix) | If `$metadata` never carries defaults, the "OData approach" reduces to reading data records or `SysSchema` content — comparison must pivot to what other teams actually do |
| A-02 | `SaveSchema` accepts a plain GUID string in `defValue.Value` for lookup `Const` defaults and the runtime applies it (verified by FR-03 step 6 runtime check) | Write path for the ticket case is silently broken; FR-06 becomes Must and scope grows |
| A-03 | "Other teams use OData" refers to reading default values (metadata), not writing them | If they write via OData too, the non-goal on write-path migration may be challenged — decision still ours, but the comparison must note it |
| A-04 | The design-time `GetSchemaDesignItem` response for a lookup default carries only the raw value, no display metadata | If display metadata is already in the DTO, FR-05 shrinks to a mapping change instead of an extra resolution query |
| A-05 | Resolving a display value on readback (one extra query against the referenced entity) is acceptable for agent latency budgets | Readback enrichment may need an opt-in flag instead of default behavior |

## Open Questions

| # | Question | Owner | Due |
|---|---------|-------|-----|
| OQ-01 | Which teams use OData for default values, and what exactly do they read — `$metadata` `DefaultValue`, `SysSchema`/`SysEntitySchemaColumn` rows via OData, or post-insert record observation? (Ask in epic ENG-85256 channel.) **Fallback**: if no answer by the time FR-02/FR-03 evidence is complete, FR-04 records the decision based on the empirical probe only, with an explicit risk note | Alex Kravchuk (epic ENG-85256 channel) | before FR-04 decision |
| OQ-02 | *Merged into FR-02 / A-01* — does `$metadata` carry column defaults, and from which platform version; answered empirically by the FR-02 probe | — | — |
| OQ-03 | What is the persisted shape of `defValue.Value` for a lookup `Const` default — plain GUID string or structured object with display value (designer UI evidence)? | Alex Kravchuk | during FR-03 |
| OQ-04 | Should lookup display-value resolution on readback be default behavior or opt-in (permissions: the agent's user may lack read rights on the referenced entity — fail-soft markers per Definitions mitigate, but the latency question remains, see A-05)? | Alex Kravchuk | before ADR |
| OQ-05 | Does the ticket's phrase "default value to current lookup column" mean only `Const` (record reference), or also `SystemValue` cases like `CurrentUserContact` for lookup columns? Verify `SystemValue` coverage for lookup types in FR-03 | Alex Kravchuk | during FR-03 |
| OQ-06 | Display-value semantics for enrichment: which column is the display value (primary display column), how is culture/localization handled, and what does `imageLookup` → `SysImage` resolve to? | Alex Kravchuk (with Architect) | before Phase B ADR |

## Dependencies

- Depends on: `spec/entity-schema-default-values/entity-schema-default-values-plan.md`
  (shipped 8.0.2.47 — `default-value-config` contract this PRD extends);
  existing OData MCP tools (`clio/Command/McpServer/Tools/ODataReadTool.cs` et al.)
  for data-endpoint steps of FR-03 (note: **not** a `$metadata` probe vehicle — see
  FR-02); a scripted authenticated GET mechanism via `IApplicationClient` (e.g. clio
  `call-service`) for the FR-02 `$metadata` capture; access to real Creatio
  environments per the environment matrix for FR-02/FR-03.
- Blocks: `spec/adr/adr-read-column-default-values.md` (Architect decision record);
  any Phase B implementation stories for lookup-default readback/validation;
  alignment answer back to ENG-91318 / epic ENG-85256 (FR-08).
