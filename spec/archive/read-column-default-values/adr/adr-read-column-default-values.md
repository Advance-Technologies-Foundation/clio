# ADR: Read Column Default Values — Schema-Designer Path vs OData Approach

**Status**: Accepted (FR-04 decision recorded 2026-06-13 — see "Decision outcome" below)
**Author**: Architect Agent
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**Jira**: [ENG-91318](https://creatio.atlassian.net/browse/ENG-91318) (epic ENG-85256 "AI no-code agents")
**Created**: 2026-06-12
**stepsCompleted**: [1, 2, 3, 4]

---

## Context

AI no-code agents need a machine-verifiable readback of lookup-column default values
(per the PRD predicate: source + record GUID + referenced schema name + display value
or honest unavailability marker). Today's read path —
`get-entity-schema-column-properties` → `RemoteEntitySchemaColumnManager.GetColumnProperties()`
(`clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs:158`) →
`EntitySchemaDesignerSupport.CreateDefaultValueConfig`
(`clio/Command/EntitySchemaDesigner/EntitySchemaDesignerSupport.cs:503`) — returns the
raw `Const` GUID with no display value and no existence guarantee, while other Creatio
teams reportedly read defaults via OData. ENG-91318 is an **investigation ticket with a
conditional implementation phase**: this ADR must (a) define how the investigation is
executed so its output is decision-grade, (b) fix the decision framework *before*
evidence arrives, and (c) pre-design the conditional Phase B changes so implementation
stories can start the moment the gap is confirmed.

## Decision

**Two-phase architecture.** Phase A (unconditional): a documents-only investigation
executed entirely through existing clio capabilities — `call-service -m GET` as the
`$metadata` probe vehicle (via `IApplicationClient`, with `ServiceUrlBuilder`
auto-handling the `0/` prefix across the environment matrix) and clio/MCP commands for
the six-step ticket scenario — producing evidence docs in
`spec/read-column-default-values/` and an explicit keep/adopt/hybrid decision scored
against the criteria D1–D6 below. Phase B (conditional, gated on FR-03/FR-04 confirming
the gap): **keep the designer-service read path as the readback backbone and enrich it
in clio** — extend `EntitySchemaDefaultValueConfig` with fail-soft `display-value` /
`record-resolution` properties resolved by a new `ILookupDefaultDisplayValueResolver`
service, and add Const-GUID existence validation in
`EntitySchemaDefaultValueSourceResolver` on the write side. The enrichment-query
transport (OData data endpoint vs DataService `SelectQuery`) is a named Phase A output
(D7), isolated behind the resolver interface so the decision cannot churn the design.

> **Honest prediction, to be validated — not assumed — by FR-02**: OData v4 CSDL
> defines `DefaultValue` on *primitive* properties only; lookup columns surface as
> navigation properties plus a primitive `<Name>Id` FK. Even if Creatio emits
> `DefaultValue` for the FK property, `$metadata` is metadata — it structurally cannot
> carry the referenced record's display value. The probe therefore decides *whether
> OData contributes anything*, not *whether the predicate can be met by metadata alone*
> (it cannot; component (d) always requires a data query someone must issue — the
> design question is whether clio issues it internally). If the probe falsifies this
> prediction (e.g. Creatio annotates `$metadata` with display data), the decision
> framework below still governs.

## Decision outcome (FR-04 — Accepted)

Evidence ([comparison](../read-column-default-values/read-column-default-values-comparison.md), [probe](../read-column-default-values/read-column-default-values-odata-probe.md), [e2e](../read-column-default-values/read-column-default-values-e2e-scenario.md)) **confirms the gap**. Final decisions:

- **Read-path direction → Option B (Hybrid), confirmed.** OData `$metadata` carries **zero** `DefaultValue` facets for any column (0 in 1.43 MB CSDL on a live .NET Framework 8.x instance) — scores 0/4 on D1, so Option A is rejected on evidence; the designer-service path stays the readback backbone and is enriched in clio.
- **D7 → OData data endpoint** (`GET odata/{Ref}({guid})?$select={displayColumn}` via `IApplicationClient`), live-verified; **DataService `SelectQuery` fallback** for OData-disabled envs.
- **OQ-04 → enrichment default-on with fail-soft** (one extra OData round-trip on top of a sub-second designer call; acceptable).
- **OQ-03 / A-04 → resolver required** (persisted `defValue.Value` is a plain GUID, no display metadata; story 6 does not shrink to a mapping change).
- **A-02 cleared** (runtime applies the `Const` default correctly — E2E step 6); FR-06 does not escalate.
- **Phase B (stories 6, 7, 8) is IN SCOPE.** Gaps → DRAFT-AC-05 (readback enrichment) and DRAFT-AC-06 (write-side Const-GUID validation).
- **Side finding (separate ticket):** `clio call-service` is broken on duplicate-URI appsettings (`Sequence contains more than one matching element`) in 8.1.0.58 — not a feature DRAFT-AC.

## Decision Framework (fixed before evidence — FR-04 input)

The keep/adopt/hybrid decision in `read-column-default-values-comparison.md` MUST be
scored against exactly these criteria; adding criteria post-hoc requires an ADR update.

| ID | Criterion | What "good" looks like |
|----|-----------|------------------------|
| D1 | **Informational completeness** for the machine-verifiable predicate | Source supplies (a) source kind, (b) GUID, (c) referenced schema, (d) display value or enough signal for an honest marker — without the *agent* issuing extra queries |
| D2 | **Auth/permission model** | Works under the credentials clio already holds; permission failures are distinguishable (schema-level denial vs empty result) so markers stay honest |
| D3 | **Environment coverage** | Works on both matrix rows: .NET Framework (`0/odata/$metadata`, `0/ServiceModel/...`) and .NET Core (no prefix) — `ServiceUrlBuilder` handles the prefix only if the path is routed through it |
| D4 | **Version stability** | Contract documented/stable across supported 8.x; not reverse-engineered from one build. AC-ERR rows record where `$metadata` is absent/disabled |
| D5 | **Alignment with other teams** | OQ-01 answer; fallback per PRD: empirical probe alone + explicit risk note if unanswered when FR-02/FR-03 evidence is complete |
| D6 | **Implementation cost in clio** | Reuse of existing designer DTO mapping vs new CSDL/XML parsing surface; maintenance of a second read path |
| D7 | **Enrichment transport** (Phase B only) | Which query clio issues internally for display value/existence: OData data endpoint vs DataService `SelectQuery` — chosen by D2/D3 evidence |

**Decision rule**: keep the designer-service read path unless OData `$metadata`
strictly dominates on D1 *without regressing* D2/D3/D4. "Hybrid" means: designer
service remains the structured-readback source; OData (data endpoint) serves only as
Phase B enrichment transport if D7 evidence favors it. A full "adopt OData" outcome
requires `$metadata` to carry lookup defaults *and* display metadata — per the
prediction above, this is expected to fail D1, but the matrix must show the captured
CSDL fragments either way (SM-01: no speculation).

## Alternatives Considered

### Read-path direction (the FR-04 decision this framework governs)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Adopt OData `$metadata` as the readback source | Aligns with reported other-team practice (D5); standards-based CSDL | CSDL `DefaultValue` is primitive-only; no display value possible in metadata (fails D1-d); new XML/CSDL parsing surface (D6); `$metadata` availability varies (D4, AC-ERR); `odata-read` cannot fetch it today | Expected-reject pending FR-02 evidence |
| B: Keep designer-service read path, enrich in clio (hybrid enrichment) | Already returns structured `default-value-config` incl. `reference-schema-name` (3 of 4 predicate components); same contract the designer UI uses (D4); zero regression to released sources; enrichment is additive + fail-soft | One extra internal query per lookup-Const readback (A-05 latency; OQ-04 opt-in question); designer service is a private contract | **Chosen (provisional — confirmed or overturned by FR-04 evidence)** |
| C: Read defaults from `SysSchema`/`SysEntitySchemaColumn` rows via OData data queries | No designer-service dependency | Reverse-engineers persisted schema internals; brittle across versions (D4); duplicates mapping logic clio already has (D6) | Rejected: highest maintenance cost for no D1 gain; revisit only if OQ-01 reveals this is what other teams actually do |

### FR-02 `$metadata` probe vehicle

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Raw `HttpClient` script | Trivial | Violates hard rule (`project-context.md`: `IApplicationClient` only) | Rejected: policy violation |
| B: Extend `ODataReadTool` to accept `$metadata` | Reuses MCP tool | Ships product code for a one-off probe; violates Phase A "empty code diff" counter (SM-01); non-goal (no general `$metadata` reader) | Rejected: scope creep |
| C: `clio call-service --service-path "odata/\$metadata" -m GET -d <file> -e <env>` | Zero code diff; GET via `IApplicationClient.ExecuteGetRequest` (`clio/Query/DataServiceQuery.cs:162-167`); `ServiceUrlBuilder.Build(ServicePath)` auto-prepends `0/` for .NET Framework rows — one command serves both matrix rows | Output beautifier targets JSON, CSDL is XML (cosmetic only; `-d` saves raw response) | **Chosen** |

### Phase B enrichment transport (D7 — provisional, bound to Phase A evidence)

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: OData data endpoint GET `odata/{ReferenceSchema}({guid})?$select={displayColumn}` via `IApplicationClient` | Same path shape `odata-read` already proves in production; aligns with D5 if decision lands hybrid; 403/404 split maps to `no-access` / `not-found-or-no-access` | Requires OData enabled on the instance (AC-ERR environments would lose enrichment) | **Chosen provisionally** |
| B: DataService `SelectQuery` POST via `IApplicationClient` | Always available (no OData dependency); explicit security-error payloads map cleanly to `no-access` | Heavier payloads; second precedent style in the same module (resolver currently uses designer client) | Fallback if FR-02/AC-ERR shows OData-disabled environments matter |
| C: Designer-service lookup display endpoints (as `EntitySchemaDefaultValueSourceResolver` does for Settings/SystemValue) | Single client dependency | Designer endpoints enumerate lookup display values per data-value-type, not arbitrary record-by-GUID lookups; wrong shape for this query | Rejected: contract mismatch |

### Write-side Const-GUID validation placement

| Option | Pros | Cons | Status |
|--------|------|------|--------|
| A: Validate inside `EntitySchemaDesignerSupport.ValidateDefaultValueConfig` (static) | Co-located with existing validation | Static helper cannot take an injected query service without breaking the DI policy | Rejected |
| B: Extend `EntitySchemaDefaultValueSourceResolver.Resolve` to handle `Const` on lookup columns | Exactly where Settings/SystemValue resolution already lives; DI-injected, interface-backed (`IEntitySchemaDefaultValueSourceResolver`); GUID-first-then-alias precedent already implemented there | Resolver name becomes slightly broader than today | **Chosen** |

## Implementation Plan

### Phase A — Investigation (unconditional; **empty code diff** is the SM-01 counter)

Evidence artifacts land in `spec/read-column-default-values/` per the repo feature-doc
naming convention (`<feature-name>-<logical-block>.md`):

| Story | FR / AC | Deliverable |
|-------|---------|-------------|
| **A1 — Current-path evidence doc** | FR-01 / AC-01 | `read-column-default-values-current-path.md`: endpoint (`ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem`), full DTO→`default-value-config` mapping with file/line refs (`EntitySchemaDesignerSupport.cs:503`, `RemoteEntitySchemaColumnManager.cs:158-191`, `EntitySchemaReadModels.cs:65-89`) |
| **A2 — OData `$metadata` probe** | FR-02 / AC-02, AC-ERR | `read-column-default-values-odata-probe.md`: captures via `call-service --service-path "odata/\$metadata" -m GET -d <file>` on **both** matrix rows (.NET Framework + .NET Core, supported 8.x). Must contain CSDL fragments for one **lookup-Const** column AND one **primitive-default** column (AC-02 rationale: primitive-only proof doesn't answer the ticket; lookup-only without baseline doesn't prove `$metadata` carries defaults at all). `$metadata` unavailable → AC-ERR version-coverage row, not a failure |
| **A3 — Ticket-case E2E scenario** | FR-03 / AC-03 | `read-column-default-values-e2e-scenario.md`: the six normative steps (create lookup → **insert record + capture GUID** → add lookup column → set Const default → readback `defValue` payload → **runtime-verify** via insert without the column), each with the exact clio/MCP call + response, ending in a strict pass/fail verdict against the machine-verifiable predicate. **N-01**: OQ-03 designer-UI evidence (persisted `defValue` shape from the UI save path) goes in a separate **"Supplementary evidence (non-normative)"** section — it must not sit among the six steps, so the SM-02 counter ("clio commands/MCP tools only") stays honest. **N-02**: execute on at least one matrix row; the second row becomes mandatory iff any observation (e.g. persisted `defValue` shape, runtime application) turns out platform-dependent — record the trigger explicitly. OQ-05 (`SystemValue` for lookup columns, e.g. `CurrentUserContact`) is probed here as a supplementary check |
| **A4 — Comparison matrix + decision** | FR-04 / AC-04 | `read-column-default-values-comparison.md`: matrix scored against D1–D7, exactly one chosen option, every rejection reasoned, evidence-row references (incl. environment-matrix rows). **N-03**: the gap section maps each confirmed gap to its DRAFT-AC item by ID (gap "no display value on readback" → DRAFT-AC-05; gap "unvalidated Const GUID on write" → DRAFT-AC-06; new gaps → new DRAFT-AC-N), satisfying SM-03. On completion: update this ADR — Status → Accepted, record the final option, resolve D7. If the gap is **not** confirmed, FR-05/FR-06/FR-07 close as "not needed" with evidence and Phase B below is struck |
| **A5 — Jira closure** | FR-08 | Comment on ENG-91318 with decision + links to comparison doc and evidence; surface to epic ENG-85256. OQ-01 answer (or its documented absence + risk note) recorded in A4 before this story closes |

Story ordering: A1 ∥ A2 → A3 → A4 → A5 (A4 needs A2+A3 evidence; OQ-01 runs in
parallel from day one with the PRD-defined fallback).

### Phase B — Conditional implementation (gated on A4 confirming the gap)

No new CLI flags; all changes live inside the existing `default-value-config` JSON
object and the existing verbs (PRD CLI-impact table). All JSON property names
kebab-case (CLIO001 applies to options; we mirror the convention in JSON as the
existing models already do).

#### Files to create

| File | Purpose |
|------|---------|
| `clio/Command/EntitySchemaDesigner/LookupDefaultDisplayValueResolver.cs` | `ILookupDefaultDisplayValueResolver` + implementation: record-by-GUID query against the referenced schema via `IApplicationClient` (transport per D7), returns display value or honest marker; never throws for expected failures |
| `clio.tests/Command/EntitySchemaDesigner/LookupDefaultDisplayValueResolverTests.cs` | `[Category("Unit")]` NSubstitute tests: marker mapping, fail-soft paths |

#### Files to modify

| File | Change description |
|------|-------------------|
| `clio/Command/EntitySchemaDesigner/EntitySchemaDefaultValueConfig.cs` | Add `[JsonPropertyName("display-value")] string? DisplayValue` and `[JsonPropertyName("record-resolution")] string? RecordResolution` (init-only; null for non-lookup sources — released `Settings`/`SystemValue`/`Sequence` semantics untouched, per PRD non-goal) |
| `clio/Command/EntitySchemaDesigner/RemoteEntitySchemaColumnManager.cs` | In `GetColumnProperties` (line 158): when `source == Const` and `column.ReferenceSchema != null`, invoke the resolver and enrich the config; constructor-inject `ILookupDefaultDisplayValueResolver`. `PrintColumnProperties` prints the new fields |
| `clio/Command/EntitySchemaDesigner/EntitySchemaDefaultValueSourceResolver.cs` | Extend `Resolve` for `Const` on lookup columns: validate GUID exists in the referenced table (DRAFT-AC-06 error: `Error: default value record '{guid}' not found in '{ReferenceSchema}'`, non-zero exit, schema not saved); optionally accept display value resolved to GUID — GUID-first-then-alias precedent (lines 72-90), ambiguity rejected with candidate list as the existing `RequireSingleMatch` does. TOCTOU caveat documented in XML docs + command docs |
| `clio/BindingsModule.cs` | Register `ILookupDefaultDisplayValueResolver` |
| `clio/Command/McpServer/Tools/EntitySchemaTool.cs` | Tool descriptions for `get-entity-schema-column-properties` / `modify-entity-schema-column`: document enrichment + markers |
| `clio/Command/McpServer/Prompts/EntitySchemaPrompt.cs` | Lookup-default usage pattern guidance (where does the agent get the GUID → FR-03 step 2 answer) |
| `clio/Command/McpServer/Tools/ToolContractGetTool.cs` | Advertise the extended `default-value-config` readback contract |
| `clio/docs/commands/get-entity-schema-column-properties.md`, `clio/docs/commands/modify-entity-schema-column.md`, `clio/help/en/get-entity-schema-column-properties.txt`, `clio/help/en/modify-entity-schema-column.txt`, `clio/Commands.md` | FR-07: docs for enrichment, markers, validation error, TOCTOU caveat |
| `clio.tests/Command/...` (existing entity-schema test fixtures) | Unit coverage for enrichment mapping + write validation |
| `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` | E2E: lookup-Const readback with display value; readback with marker; write rejection for nonexistent GUID; empty-just-created-lookup edge (DRAFT-AC-06 caveat) |

#### Key interfaces / contracts

```csharp
// New resolver (Phase B) — clio/Command/EntitySchemaDesigner/LookupDefaultDisplayValueResolver.cs
internal interface ILookupDefaultDisplayValueResolver
{
    /// <summary>
    /// Resolves the display value of the referenced record for a lookup Const default.
    /// Fail-soft: expected failures map to markers, never exceptions.
    /// </summary>
    LookupDefaultResolution Resolve(string referenceSchemaName, Guid recordId, RemoteCommandOptions options);
}

/// <summary>Display value, or a record-resolution marker ("no-access" | "not-found-or-no-access").</summary>
internal sealed record LookupDefaultResolution(string? DisplayValue, string? RecordResolution);
```

Marker semantics (verbatim from PRD Definitions — markers must not claim more than the
query proves):
- `no-access` — schema-level read denial on the referenced entity (explicit security
  error / 403); readback degrades to GUID + marker; command does NOT fail.
- `not-found-or-no-access` — query succeeds but returns no row; deleted vs row-level
  hidden are indistinguishable, so the marker says so. Split only if the platform makes
  the cases distinguishable.

Fail-soft implementation rule: the resolver catches **specific** exception types from
the `IApplicationClient` call path (no bare `catch (Exception)` — repo policy) and maps
them to markers; a truly unexpected exception degrades to the marker + a
`WriteWarning`, never a command failure (no regression vs today's GUID-only readback —
SM-01 counter).

Display-column discovery: referenced schema's `primary-display-column-name` is already
exposed by the designer read path (`EntitySchemaReadModels.cs:45`); resolve once per
referenced schema per command execution (in-memory cache, same pattern as
`EntitySchemaDefaultValueSourceResolver._settingsCache`). OQ-06 (culture/localization,
`imageLookup` → `SysImage`) must be answered before story B1 starts — it is an explicit
story precondition, not an open thread inside it. OQ-04 (default-on vs opt-in
enrichment, A-05 latency) is resolved in A4 using FR-03 timing evidence; the design
default is enrichment-on with fail-soft, switched to opt-in only if A4 latency evidence
demands it.

#### CLI flag specification

No new CLI flags (Phase A is documents-only; Phase B extends the existing
`default-value-config` JSON object). Existing verbs and options are untouched —
CLIO001 surface unchanged.

#### Test strategy

| Layer | Framework | What to cover | File |
|-------|-----------|---------------|------|
| Unit | NUnit + NSubstitute + FluentAssertions, `[Category("Unit")]` | Resolver marker mapping (found / 403 / empty result / unexpected error → warn+marker); config enrichment only for lookup-Const; write validation (missing GUID rejected, empty-table edge, display-name ambiguity rejected, GUID-first precedence); no enrichment for `Settings`/`SystemValue`/`Sequence` | `clio.tests/Command/EntitySchemaDesigner/LookupDefaultDisplayValueResolverTests.cs` + existing fixtures |
| Integration | n/a | No file-system/DB surface in this feature | — |
| E2E | clio.mcp.e2e | Full DRAFT-AC-05/DRAFT-AC-06 scenarios against a real instance, incl. runtime-default application (FR-03 step 6 analogue) | `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` |

Test naming `MethodName_ShouldExpectedBehavior_WhenCondition`; AAA + `because` on every
assertion + `[Description]` on every test. **MCP E2E is NOT in CI — it must be run
manually before the Phase B merge** (this is the SM-01 Phase B counter; flag it in the
PR description). Targeted pre-commit filter:
`dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"`.

### Story candidates (handoff to story-writer)

| # | Story | Phase | Gate |
|---|-------|-------|------|
| 1 | Document current read path (FR-01/AC-01) | A | none |
| 2 | OData `$metadata` probe across environment matrix (FR-02/AC-02/AC-ERR) | A | none |
| 3 | Execute ticket-case E2E scenario + supplementary OQ-03/OQ-05 evidence (FR-03/AC-03, N-01, N-02) | A | env access |
| 4 | Comparison matrix + keep/adopt/hybrid decision + gap→DRAFT-AC mapping + ADR status update (FR-04/AC-04, N-03, OQ-01 fallback, D7 resolution, OQ-04 resolution) | A | stories 2+3 |
| 5 | Jira ENG-91318 closure + epic surfacing (FR-08) | A | story 4 |
| 6 | **[conditional]** Readback enrichment: `display-value` + `record-resolution` (DRAFT-AC-05; OQ-06 precondition) | B | story 4 confirms gap |
| 7 | **[conditional]** Write-side Const-GUID validation + display-name resolution (DRAFT-AC-06) | B | story 4 confirms gap |
| 8 | **[conditional]** Docs + MCP surface + E2E + manual E2E run (FR-07) | B | stories 6+7 |

## Consequences

- **Positive**: agents get a single-call, machine-verifiable lookup-default readback;
  the decision (whatever it lands on) is evidence-backed and reusable by other teams
  via the comparison doc; zero regression risk to the released 8.0.2.47
  `default-value-config` contract (additive nullable fields, fail-soft enrichment);
  Phase A ships with an empty code diff, so investigation cannot destabilize anything.
- **Trade-offs**: one extra internal HTTP query per lookup-Const readback (A-05 —
  bounded by per-execution caching; opt-in escape hatch reserved via OQ-04); write-side
  validation is point-in-time (TOCTOU) — reduces but does not eliminate broken-default
  risk, documented honestly; the designer service remains a private platform contract
  (mitigated: it is the same contract the product UI uses, and the comparison doc
  records the version-stability evidence).
- **Breaking change**: **No.** Additive readback fields; write-side validation rejects
  only payloads that were already semantically broken (nonexistent GUIDs). If A4
  evidence shows real callers depend on saving dangling GUIDs, the rejection gains a
  documented bypass before merge and RELEASE.md is updated — decision recorded in A4.

## Risks

| Risk | Mitigation |
|------|-----------|
| `$metadata` probe inconclusive (OData disabled on available envs) | AC-ERR path: record as version-coverage row; decision falls back to D1/D2/D6 + designer path (which demonstrably works today) |
| OQ-01 unanswered before FR-04 | PRD fallback: decide on empirical probe alone + explicit risk note in the comparison doc |
| `SaveSchema` rejects or mangles plain-GUID `defValue.Value` for lookups (A-02) | FR-03 step 6 runtime check catches it; if confirmed, FR-06 escalates Should→Must and story 7 scope grows — flagged in A4 |
| Designer DTO already carries display metadata (A-04) | FR-03/OQ-03 supplementary evidence checks the raw payload; if present, story 6 shrinks to a pure mapping change (resolver unnecessary) — cheaper, same contract |
| Enrichment latency breaks agent budgets (A-05/OQ-04) | FR-03 timing evidence drives default-on vs opt-in in A4; fail-soft guarantees the readback never blocks on enrichment failure |
| Platform-dependent behavior between matrix rows | N-02 trigger: second-row FR-03 execution becomes mandatory; decision matrix gains a row |

## Pre-implementation Checklist (Phase B)

- [ ] Phase A story 4 confirmed the gap and resolved D7, OQ-04, OQ-06; ADR Status updated to Accepted with the final option recorded
- [ ] No new CLI options (if that changes: kebab-case, CLIO001)
- [ ] `ILookupDefaultDisplayValueResolver` registered in `BindingsModule.cs`; consumed via constructor injection (no `new` for behavior classes)
- [ ] All HTTP via `IApplicationClient` — no raw `HttpClient` anywhere, including the probe
- [ ] Error messages user-friendly: `Error: default value record '{guid}' not found in '{ReferenceSchema}'`
- [ ] Markers honest: `no-access` / `not-found-or-no-access` exactly per PRD Definitions
- [ ] Existing affected tests identified: entity-schema designer fixtures in `clio.tests`, `EntitySchemaToolE2ETests.cs`
- [ ] MCP surface updated together with command behavior (tool + prompt + `get-tool-contract` + `clio.tests` + `clio.mcp.e2e`) — AGENTS.md MCP maintenance policy
- [ ] Docs updated (`docs/commands/*.md`, `help/en/*.txt`, `Commands.md`); "docs reviewed" / "MCP reviewed" statements in the PR description
- [ ] MCP E2E run manually before merge (not in CI) and recorded in the PR
- [ ] No bare `catch (Exception)`; fail-soft via specific exception types + warning
