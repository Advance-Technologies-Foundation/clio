# Test Plan: Read Column Default Values — Schema-Designer Path vs OData Approach

**Feature**: read-column-default-values
**Jira**: ENG-91318 (epic ENG-85256 "AI no-code agents")
**PRD**: [prd-read-column-default-values.md](../prd/prd-read-column-default-values.md)
**ADR**: [adr-read-column-default-values.md](../adr/adr-read-column-default-values.md)
**Stories**: [story-read-column-default-values-1..8](../stories/) (`spec/stories/story-read-column-default-values-{1..8}.md`)
**Sprint tracker**: [sprint-status.yaml](../sprint-status.yaml)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-06-12

---

## Scope

### In scope

- **Part A (Phase A, stories 1–5)** — document-acceptance verification of the
  investigation artifacts in `spec/read-column-default-values/`: review checklists per
  evidence doc, environment-matrix execution checks, the machine-verifiable predicate
  evaluation procedure (PRD AC-03), the runtime-verification insert check (FR-03
  step 6), and the empty-code-diff regression guard (SM-01).
- **Part B (Phase B, stories 6–8, CONDITIONAL)** — ready-to-implement automated test
  cases: unit (`clio.tests`), DI-composition integration, and MCP E2E
  (`clio.mcp.e2e`) covering DRAFT-AC-05 (readback enrichment), DRAFT-AC-06
  (write-side Const-GUID validation), and FR-07 (docs + MCP surface).

### Out of scope

- Page-level defaults and business-rule defaults — PRD non-goal.
- OData write-path migration — PRD non-goal (write stays on `SaveSchema`).
- `Settings` / `SystemValue` / `Sequence` semantics changes — released 8.0.2.47
  contract, explicitly frozen by the PRD; covered here only as regression guards.
- General-purpose `$metadata` reader — ADR-rejected alternative (probe is
  `call-service`, zero code diff).
- CI integration of `clio.mcp.e2e` — known gap, flagged below; not solved by this plan.

### Phase B gate (read before implementing any TC)

Part B test cases are **inert until story 4 flips the gate**
(`spec/sprint-status.yaml`: stories 6–8 `deferred` → `ready-for-dev` when the FR-03
predicate verdict = fail). If story 4 closes FR-05/FR-06/FR-07 as "not needed",
Part B of this plan is struck with evidence — record the strike in this file's
header (Status → Struck-Part-B) and in the comparison doc.

---

## Risk Assessment

| # | Risk | Likelihood | Impact | Mitigation |
|---|------|-----------|--------|-----------|
| R-01 | **TOCTOU window on write validation** (DRAFT-AC-06): record deleted between existence check and `SaveSchema` — validation reduces but does not eliminate broken defaults | Med | Med | TC-U-14 proves rejection; AC-ERR honesty check in RC-A4/story-7 DoD verifies the caveat is documented in XML docs + command docs (story 8); no test can close the window — the plan verifies *honest documentation*, not impossibility |
| R-02 | **Row-level security masking**: empty lookup query is indistinguishable from a deleted record — a dishonest marker (`not-found` alone) would overclaim | Med | High | Marker-honesty tests TC-U-02/TC-U-03 assert the exact strings `no-access` / `not-found-or-no-access`; TC-E2E-03 verifies the degraded readback end to end; doc review RC-B1 requires verbatim marker semantics |
| R-03 | **Designer-service contract drift across platform versions** (`GetSchemaDesignItem` / `SaveSchema` are private contracts) | Med | High | Phase A AC-ERR / version-coverage rows in the comparison matrix (RC-A4); E2E suite re-runs on release against the sandbox (TC-E2E-01 mirrors the FR-03 six-step scenario); N-02 platform-dependency trigger forces second-matrix-row execution |
| R-04 | **MCP E2E not in CI** — Phase B contract changes can merge unverified | High | Med | Hard manual-execution gate: story 8 AC-04 + PR-checklist item below; this plan marks every TC-E2E as "manual only" |
| R-05 | **New DI registration breaks existing fixtures**: `RemoteEntitySchemaColumnManager` gains a constructor dependency (`ILookupDefaultDisplayValueResolver`) — fixtures constructing it directly or via container will fail to compile/resolve | High | Med | TC-I-01 composition-root test; regression-guard table lists every fixture that constructs the manager; pre-commit filter `Category=Unit&(Module=Command|Module=McpServer)` |
| R-06 | **A-02: `SaveSchema` mangles plain-GUID lookup defaults** — write path silently broken today | Low | High | Phase A: FR-03 step 6 runtime insert check (procedure below) catches it before any Phase B code exists; if confirmed, story 7 scope grows (flagged in story 4) |
| R-07 | **Phase A code-diff leakage** — investigation accidentally ships product code (e.g. extending `ODataReadTool` for `$metadata`) | Low | High | Regression Guard A: mechanical `git diff` check on every Phase A PR (command below); story 2 AC-04 explicitly forbids the `ODataReadTool` extension |
| R-08 | **Legacy shorthand regression**: existing `default-value-source` / `default-value` shorthand consumers and the released `default-value-config` readback change shape | Low | High | TC-U-09 (byte-for-byte null enrichment fields for non-lookup sources), TC-U-19 (Settings/SystemValue resolution untouched), regression-guard suite must stay green |
| R-09 | **Display value that parses as a GUID** resolved as a name (or vice versa) — silent wrong-record default | Med | High | TC-U-16 pins GUID-first-then-alias precedence; TC-U-18 pins case-duplicate ambiguity rejection |

---

# Part A — Phase A verification (stories 1–5, documents-only)

Phase A produces an **empty production-code diff**; verification is
**document acceptance**, not automated tests. Each story's PR is accepted by its
review checklist below plus Regression Guard A. The reviewer records the filled
checklist in the PR description.

## Regression Guard A — empty code diff (every Phase A PR; stories 1–5 AC-ERR, SM-01)

Mechanical check, run on the PR branch:

```bash
git fetch origin master
git diff --name-only origin/master...HEAD | grep -v '^spec/' || echo "PASS: spec/** only"
```

- **Pass**: the grep output is empty (only `spec/**` paths changed).
- **Fail**: any non-`spec/` path appears — the PR is rejected regardless of content
  quality. In particular: no changes under `clio/`, `clio.tests/`, `clio.mcp.e2e/`,
  no new CLI flags, no `ODataReadTool` extension (story 2 AC-04).

## RC-A1 — Review checklist: current-path evidence doc (story 1, FR-01)

Artifact: `spec/read-column-default-values/read-column-default-values-current-path.md`

| # | Check | Traces to |
|---|-------|-----------|
| RC-A1.1 | Exact readback endpoint documented: `ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem` | Story 1 AC-01 (PRD AC-01) |
| RC-A1.2 | Full chain covered: verb/MCP tool → `GetEntitySchemaColumnPropertiesCommand` → `RemoteEntitySchemaColumnManager.GetColumnProperties()` → `RemoteEntitySchemaDesignerClient.GetSchemaDesignItem()` → HTTP POST via `IApplicationClient` | Story 1 AC-02 |
| RC-A1.3 | `DefValue` DTO fields enumerated (`ValueSourceType`, `Value`, `ValueSource`, `SequencePrefix`, `SequenceNumberOfChars`) and `CreateDefaultValueConfig` mapping to `None \| Const \| Settings \| SystemValue \| Sequence` documented | Story 1 AC-02 (PRD AC-01) |
| RC-A1.4 | Lookup-`Const` weak spot recorded explicitly: raw-GUID-only readback, no display value, no existence validation, `ValidateDefaultValueConfig` blocks `Const` only for binary-like types | Story 1 AC-03 |
| RC-A1.5 | Every code claim carries a file/line reference; reviewer spot-checks at least 3 references against current `master` (e.g. `EntitySchemaDesignerSupport.cs:503`, `RemoteEntitySchemaColumnManager.cs:158-191`, `EntitySchemaReadModels.cs:65-89`) and they resolve | Story 1 DoD |
| RC-A1.6 | Regression Guard A passes | Story 1 AC-ERR |

## RC-A2 — Review checklist: OData `$metadata` probe doc (story 2, FR-02)

Artifact: `spec/read-column-default-values/read-column-default-values-odata-probe.md`

| # | Check | Traces to |
|---|-------|-----------|
| RC-A2.1 | Captured CSDL fragments present for **both** a lookup-`Const` column AND a primitive-default column (Boolean/Text), each stating explicitly whether/where defaults appear (CSDL `DefaultValue` attribute / annotations) and from which platform version | Story 2 AC-01 (PRD AC-02) |
| RC-A2.2 | **Environment-matrix execution check**: both rows captured — see procedure EM-1 below | Story 2 AC-02 |
| RC-A2.3 | Every capture annotated with the exact `call-service` command line next to its output file; **no raw `HttpClient`** anywhere (grep the doc for `HttpClient` — must be absent except as a policy mention) | Story 2 AC-03 |
| RC-A2.4 | If `$metadata` was unavailable on any environment: recorded as an AC-ERR version-coverage row for the FR-04 matrix, NOT as an investigation failure | Story 2 AC-ERR (PRD AC-ERR) |
| RC-A2.5 | OQ-01 outreach status recorded (what other teams actually read); assessment of whether existing `odata-read` could serve the need without new code | Story 2 / FR-02 |
| RC-A2.6 | The ADR's "honest prediction" (CSDL `DefaultValue` is primitive-only) is validated against the captured fragments, not assumed — the doc states which way the evidence went | Story 2 / ADR |
| RC-A2.7 | Regression Guard A passes; `ODataReadTool` untouched | Story 2 AC-04 |

### EM-1 — Environment-matrix execution procedure (RC-A2.2, RC-A3 step checks)

For each matrix row the reviewer verifies the recorded command + effective URL:

| Row | Probe command (recorded verbatim in the doc) | Effective URL must show |
|-----|---------------------------------------------|------------------------|
| .NET Framework | `clio call-service --service-path "odata/\$metadata" -m GET -d <file> -e <fw-env>` | `0/odata/$metadata` — the `0/` prefix auto-prepended by `ServiceUrlBuilder` |
| .NET Core | same command, `-e <core-env>` | `odata/$metadata` — no prefix |

Checks: (a) both environments are on a currently supported 8.x version, versions
recorded; (b) the same single command served both rows (no per-row script forks);
(c) raw responses saved via `-d` (CSDL is XML — beautifier output is cosmetic only).

## RC-A3 — Review checklist: ticket-case E2E scenario doc (story 3, FR-03)

Artifact: `spec/read-column-default-values/read-column-default-values-e2e-scenario.md`

| # | Check | Traces to |
|---|-------|-----------|
| RC-A3.1 | All six normative steps recorded, each with the exact clio/MCP call and its response payload; **clio commands / MCP tools only — zero manual UI steps** in the normative section (SM-02 counter) | Story 3 AC-01/AC-02 (PRD AC-03) |
| RC-A3.2 | Step 2 ("where does the agent get the GUID"): the lookup-record insert call (`odata-create` or `create-data-binding` + `add-data-binding-row`) and the captured GUID are recorded verbatim | Story 3 AC-02 step 2 |
| RC-A3.3 | Step 5: persisted `defValue` payload recorded raw (answers OQ-03: plain GUID string vs structured object; also tests A-04 — if display metadata already present, story 6 shrinks to a mapping change) | Story 3 AC-02 step 5, OQ-03, A-04 |
| RC-A3.4 | **Runtime-verification insert check (step 6)** executed per procedure RV-1 below, result recorded | Story 3 AC-02 step 6, A-02 |
| RC-A3.5 | **Predicate verdict** evaluated per procedure MV-1 below — verdict table present and mechanically correct | Story 3 AC-01 (PRD AC-03) |
| RC-A3.6 | OQ-03 designer-UI evidence sits in a separate "Supplementary evidence (non-normative)" section — never among the six steps (N-01) | Story 3 AC-03 |
| RC-A3.7 | Supplementary section answers OQ-05: whether `SystemValue` defaults (e.g. `CurrentUserContact`) apply to lookup columns | Story 3 AC-04 |
| RC-A3.8 | N-02 statement present: which matrix row(s) executed; explicit statement whether any observation appeared platform-dependent; if yes — second-row execution trigger recorded | Story 3 AC-05 |
| RC-A3.9 | Rough readback timing captured (input for OQ-04 default-on vs opt-in resolution in story 4) | Story 3 notes, A-05 |
| RC-A3.10 | Regression Guard A passes | Story 3 AC-ERR |

### MV-1 — Machine-verifiable predicate evaluation procedure (PRD AC-03)

Evaluated **strictly and mechanically** against the recorded step-5 readback JSON
(`default-value-config`). No subjective judgement; the doc must contain this table
filled in:

| Component | Predicate check (binary) | Pass/Fail |
|-----------|--------------------------|-----------|
| (a) source | `default-value-config` source field equals `Const` | |
| (b) value | value parses as a GUID **and** equals the step-2 captured GUID | |
| (c) referenced schema | the referenced schema name is present in the readback | |
| (d) display value or marker | a display value of the referenced record is present **OR** an explicit `record-resolution` marker (`no-access` / `not-found-or-no-access`) is present | |

**Verdict** = AND of (a)–(d), evaluated on the single readback response **without
issuing additional queries**. All four → **pass** (Phase B is closed "not needed"
by story 4). Any missing → **fail**, and the verdict section lists the missing
components by letter (these letters feed the N-03 gap→DRAFT-AC mapping in story 4).

### RV-1 — Runtime-verification insert check (FR-03 step 6)

Metadata persistence alone is insufficient (risk A-02 / R-06). Procedure:

1. Insert a record into the target Object via `odata-create` **without** supplying
   the lookup column.
2. Read the created record back (`odata-read` with `$select` on the column's FK).
3. **Pass**: the column value equals the step-2 GUID (the default was applied at
   runtime). **Fail**: null/other value — record as A-02 evidence; story 4 must then
   escalate FR-06 Should → Must and flag story 7 scope growth.
4. Both the insert and readback payloads are recorded verbatim in the doc.

## RC-A4 — Review checklist: comparison matrix + decision doc (story 4, FR-04)

Artifacts: `spec/read-column-default-values/read-column-default-values-comparison.md`,
updated `spec/adr/adr-read-column-default-values.md`, updated `spec/sprint-status.yaml`

| # | Check | Traces to |
|---|-------|-----------|
| RC-A4.1 | Matrix scored against **exactly** D1–D7 (no post-hoc criteria without an ADR update); ADR decision rule applied: keep designer-service path unless `$metadata` strictly dominates D1 without regressing D2/D3/D4 | Story 4 AC-02 |
| RC-A4.2 | Decision section is ADR-ready: exactly one chosen option (keep / adopt OData / hybrid), every rejected option with reason, references to evidence-matrix rows including environment-matrix and AC-ERR rows | Story 4 AC-01 (PRD AC-04) |
| RC-A4.3 | If MV-1 verdict = fail: each confirmed gap mapped by ID — no display value → DRAFT-AC-05; unvalidated Const GUID → DRAFT-AC-06; new gaps → new DRAFT-AC-N (N-03/SM-03). If pass: FR-05/06/07 closed "not needed" with evidence and Phase B struck | Story 4 AC-03 |
| RC-A4.4 | D7 (enrichment transport) resolved from D2/D3 evidence; OQ-04 resolved using RC-A3.9 timing evidence; OQ-01 answered OR the PRD fallback applied with explicit risk note | Story 4 AC-04 |
| RC-A4.5 | ADR updated: Status → Accepted, final option + D7 recorded; A-02/breaking-change note (dangling-GUID bypass, RELEASE.md) addressed if RV-1 triggered it | Story 4 AC-05 |
| RC-A4.6 | `sprint-status.yaml`: stories 6–8 flipped `deferred` → `ready-for-dev` (gap confirmed) or annotated as struck (gap not confirmed) — consistent with RC-A4.3 | Story 4 DoD |
| RC-A4.7 | Regression Guard A passes | Story 4 AC-ERR |

## RC-A5 — Review checklist: Jira closure (story 5, FR-08)

| # | Check | Traces to |
|---|-------|-----------|
| RC-A5.1 | ENG-91318 comment posted: chosen option, one-paragraph rationale, repo links to the comparison doc and all three evidence docs (stories 1–3) | Story 5 AC-01 |
| RC-A5.2 | Epic ENG-85256 surfacing done, including the "where does the agent get the GUID" answer (FR-03 step 2 evidence) | Story 5 AC-02 |
| RC-A5.3 | OQ-01 answer (or documented absence + risk note) already in the comparison doc before this story closes | Story 5 AC-03 |
| RC-A5.4 | Phase B trigger status stated explicitly in the comment (triggered / struck) | Story 5 AC-04 |
| RC-A5.5 | Any repo touch limited to `spec/**` (Regression Guard A); story status updated in `sprint-status.yaml` | Story 5 AC-ERR/DoD |

---

# Part B — Phase B test cases (stories 6–8, CONDITIONAL — gated on story 4)

**Framework**: NUnit 4.5.1 + FluentAssertions 7.2.0 + NSubstitute 5.3.0.
**Conventions** (mandatory): `[Category("Unit")]` — never `"UnitTests"`; naming
`MethodName_ShouldExpectedBehavior_WhenCondition`; explicit AAA; `because` on every
assertion; `[Description]` on every test; `[Property("Module", "Command")]` /
`"McpServer"` for filterability. Command-class tests prefer
`BaseCommandTests<TOptions>` (DI-resolved SUT, `AdditionalRegistrations`,
`ClearReceivedCalls` in teardown); service-class tests follow the existing flat
fixture pattern (`EntitySchemaDefaultValueSourceResolverTests.cs`).

**File-location note**: the ADR proposes
`clio.tests/Command/EntitySchemaDesigner/LookupDefaultDisplayValueResolverTests.cs`,
but every existing entity-schema-designer fixture lives flat in
`clio.tests/Command/` (e.g. `EntitySchemaDefaultValueSourceResolverTests.cs`,
`RemoteEntitySchemaColumnManagerTests.cs`). **Recommendation**: place the new
fixture flat as `clio.tests/Command/LookupDefaultDisplayValueResolverTests.cs` with
`[Property("Module", "Command")]` for filter compatibility; if the subfolder is used
instead, keep the same Module property.

**Pre-commit filter**:
`dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build`

## Unit Tests (`clio.tests/`)

### B1 — `ILookupDefaultDisplayValueResolver` (story 6, new fixture `LookupDefaultDisplayValueResolverTests.cs`)

#### TC-U-01: Happy path — display value resolved
**Story 6 / AC-01 (DRAFT-AC-05)**
`Resolve_ShouldReturnDisplayValue_WhenReferencedRecordExists`

```csharp
[Test]
[Category("Unit")]
[Description("Resolves the referenced record's display value for a lookup Const default via IApplicationClient (transport per D7).")]
public void Resolve_ShouldReturnDisplayValue_WhenReferencedRecordExists()
{
    // Arrange
    IApplicationClient client = Substitute.For<IApplicationClient>();
    // stub the D7-transport response for odata/{ReferenceSchema}({guid})?$select={displayColumn}
    // (or DataService SelectQuery — fixed by story 4's D7 resolution)
    client.ExecuteGetRequest(Arg.Any<string>()).Returns(/* payload with Name = "Active" */);
    ILookupDefaultDisplayValueResolver sut = CreateSut(client);

    // Act
    LookupDefaultResolution result = sut.Resolve("UsrStatusLookup", _knownGuid, _options);

    // Assert
    result.DisplayValue.Should().Be("Active",
        because: "a resolvable referenced record must yield its primary display column value");
    result.RecordResolution.Should().BeNull(
        because: "no marker is emitted when the display value was resolved");
}
```

#### TC-U-02: `no-access` marker on schema-level denial
**Story 6 / AC-01 (DRAFT-AC-05)** — `Resolve_ShouldReturnNoAccessMarker_WhenReferencedEntityReadIsDenied`
- Arrange: `IApplicationClient` throws the **specific** security/403 exception type
  used by the client path (no bare `catch (Exception)` in the SUT — assert via a
  specific exception substitute).
- Assert: `RecordResolution == "no-access"` (exact string, because markers are a
  published contract), `DisplayValue == null`, **no exception escapes** the resolver
  (because fail-soft is DRAFT-AC-05's core guarantee).

#### TC-U-03: `not-found-or-no-access` marker on empty result
**Story 6 / AC-01 (DRAFT-AC-05)** — `Resolve_ShouldReturnNotFoundOrNoAccessMarker_WhenQueryReturnsNoRow`
- Arrange: query succeeds, zero rows.
- Assert: marker is exactly `not-found-or-no-access` (because an empty result cannot
  distinguish deleted from row-level-hidden — the marker must not overclaim; R-02).

#### TC-U-04: Fail-soft on unexpected error
**Story 6 / AC-02** — `Resolve_ShouldDegradeToMarkerWithWarning_WhenUnexpectedErrorOccurs`
- Arrange: client throws an unexpected (non-security, non-empty) exception type.
- Assert: no exception escapes; marker present; a `WriteWarning` was emitted on the
  injected logger/console abstraction (because unexpected failures must degrade,
  never fail the command — SM-01 counter).

#### TC-U-05: Per-execution display-column cache
**Story 6 / Implementation Notes** — `Resolve_ShouldReuseCachedDisplayColumn_WhenSameReferenceSchemaQueriedTwice`
- Act: two `Resolve` calls for the same reference schema.
- Assert: display-column discovery call `Received(1)` (because the resolver caches
  per referenced schema per command execution — `_settingsCache` pattern, A-05
  latency budget).

#### TC-U-06: Undeterminable display column degrades honestly
**Story 6 / AC-ERR** — `Resolve_ShouldReturnNotFoundOrNoAccessMarkerWithWarning_WhenDisplayColumnCannotBeDetermined`
- Arrange: referenced schema metadata lacks `primary-display-column-name`.
- Assert: GUID-preserving degradation, marker + warning, no throw, because clio must
  never print a stack trace or exit non-zero for an enrichment failure.

### B2 — Readback enrichment mapping (story 6; `RemoteEntitySchemaColumnManagerTests.cs`, `EntitySchemaDesignerSupportTests.cs`, `GetEntitySchemaColumnPropertiesCommandTests.cs`)

#### TC-U-07: Enrichment applied for lookup-`Const`
**Story 6 / AC-01 (DRAFT-AC-05)** — `GetColumnProperties_ShouldEnrichConfigWithDisplayValue_WhenLookupConstDefaultPresent`
- Arrange: designer DTO with `Const` defValue on a column with
  `ReferenceSchema != null`; resolver substitute returns a display value.
- Assert: `default-value-config` carries GUID + display value + referenced schema
  name (all machine-verifiable predicate components), resolver `Received(1)`.

#### TC-U-08: Command never fails when enrichment degrades
**Story 6 / AC-02** — `GetColumnProperties_ShouldReturnGuidWithMarker_WhenResolutionDegrades`
- Arrange: resolver substitute returns `(null, "no-access")`.
- Assert: command completes successfully (exit 0 path), config has GUID + marker,
  because degraded enrichment must be no worse than today's GUID-only readback.

#### TC-U-09: No-regression GUID-only path for non-lookup sources
**Story 6 / AC-03 (PRD non-goal, R-08)** — `GetColumnProperties_ShouldLeaveEnrichmentFieldsNull_WhenSourceIsSettingsSystemValueOrSequence`
- Parameterized over `Settings` / `SystemValue` / `Sequence`.
- Assert: `DisplayValue` and `RecordResolution` are null AND the serialized
  `default-value-config` for these sources is **byte-for-byte identical** to the
  pre-change snapshot (because the released 8.0.2.47 contract is frozen); resolver
  `DidNotReceive()`.

#### TC-U-10: Resolver not invoked for non-lookup `Const`
**Story 6 / AC-03** — `GetColumnProperties_ShouldNotInvokeResolver_WhenConstDefaultIsNotLookup`
- Arrange: `Const` default on a Text/Boolean column (`ReferenceSchema == null`).
- Assert: resolver `DidNotReceive()`; config equals today's shape, because enrichment
  is scoped strictly to lookup-`Const`.

#### TC-U-11: Kebab-case JSON property names
**Story 6 / AC-04** — `EntitySchemaDefaultValueConfig_ShouldSerializeKebabCasePropertyNames_WhenEnrichmentFieldsPresent`
- Act: serialize an enriched config with `System.Text.Json`.
- Assert: JSON contains exactly `"display-value"` and `"record-resolution"` (because
  the JSON surface mirrors the CLIO001 kebab-case convention); when fields are null
  they are absent/null per the existing model's null-handling convention.

#### TC-U-12: Console output prints enrichment
**Story 6 / Implementation Notes** — `PrintColumnProperties_ShouldPrintDisplayValueAndRecordResolution_WhenPresent`
- Assert: printed output includes the display value (and the marker in the degraded
  variant), because agents reading CLI output need the same predicate components as
  JSON consumers.

### B3 — Write-side Const-GUID validation (story 7; extend `EntitySchemaDefaultValueSourceResolverTests.cs`)

#### TC-U-13: Valid GUID — no regression
**Story 7 / AC-04** — `Resolve_ShouldAcceptConstGuid_WhenRecordExistsInReferencedTable`
- Assert: resolution succeeds and the save path proceeds exactly as today (designer
  client save invoked with the unchanged GUID), because validation must not change
  behavior for already-correct payloads.

#### TC-U-14: Missing record — exact error, no save
**Story 7 / AC-01 (DRAFT-AC-06)** — `Resolve_ShouldRejectWithNotFoundError_WhenConstGuidMissingInReferencedTable`
- Assert: error message is exactly
  `Error: default value record '{guid}' not found in '{ReferenceSchema}'` (because
  DRAFT-AC-06 fixes the user-facing string as a contract); non-zero exit path taken;
  `SaveSchema` **`DidNotReceive()`** (because the schema must not be saved).

#### TC-U-15: Empty just-created lookup
**Story 7 / AC-02 (DRAFT-AC-06 caveat)** — `Resolve_ShouldRejectWithNotFoundError_WhenReferencedLookupTableIsEmpty`
- Arrange: existence query returns zero rows for a freshly created table.
- Assert: same exact AC-01 error (because the honest error prompts the agent to seed
  a record first — FR-03 step 2).

#### TC-U-16: GUID-first precedence over display name
**Story 7 / AC-03, R-09** — `Resolve_ShouldTreatValueAsGuid_WhenSuppliedDisplayNameParsesAsGuid`
- Arrange: a lookup whose display column contains a value that parses as a GUID.
- Assert: the value is treated as a GUID and validated for existence — display-name
  resolution is NOT attempted (because the GUID-first-then-alias precedent in
  `EntitySchemaDefaultValueSourceResolver` lines 72-90 must hold).

#### TC-U-17: Display name resolves to GUID
**Story 7 / AC-03** — `Resolve_ShouldResolveDisplayNameToGuid_WhenExactlyOneRecordMatches`
- Assert: the persisted `defValue.Value` equals the matched record's GUID (because
  agents reference records the way humans do).

#### TC-U-18: Case-duplicate display names rejected
**Story 7 / AC-03, R-09** — `Resolve_ShouldRejectWithCandidateList_WhenDisplayNamesDifferOnlyByCase`
- Arrange: two records `"active"` / `"Active"`.
- Assert: rejection with a clear error listing both candidates (because ambiguity
  must never silently pick one — existing `RequireSingleMatch` behavior); no save.

#### TC-U-19: Settings/SystemValue paths unchanged
**Story 7 / AC-04, R-08** — `Resolve_ShouldKeepExistingResolution_WhenSourceIsSettingsOrSystemValue`
- Assert: existing alias→GUID resolution for `Settings`/`SystemValue` produces
  identical results to the pre-change fixture expectations (because the released
  paths are frozen by the PRD non-goal).

### B4 — MCP surface (story 8; `clio.tests/Command/McpServer/EntitySchemaToolTests.cs`, `ToolContractGetToolTests.cs`)

#### TC-U-20: Tool descriptions advertise enrichment
**Story 8 / AC-02** — `GetToolDescription_ShouldDocumentDisplayValueAndMarkers_WhenEntitySchemaToolInspected`
- Assert: descriptions for `get-entity-schema-column-properties` and
  `modify-entity-schema-column` mention `display-value`, `record-resolution`, and
  both marker strings verbatim (because the MCP surface must not drift from command
  behavior — AGENTS.md policy; markers copied from implementation constants, not
  paraphrased).

#### TC-U-21: `get-tool-contract` advertises the extended contract
**Story 8 / AC-02** — `GetContract_ShouldAdvertiseExtendedDefaultValueConfig_WhenEntitySchemaContractRequested`
- Assert: contract payload includes the kebab-case enrichment properties and the
  DRAFT-AC-06 error shape (because agents discover the contract through this tool).

#### TC-U-22: Prompt carries the GUID-seeding guidance
**Story 8 / AC-02** — `GetPrompt_ShouldIncludeLookupDefaultGuidSeedingGuidance_WhenEntitySchemaPromptRequested`
- Assert: prompt text answers "where does the agent get the GUID" (seed via
  `odata-create` / data binding, then use the captured GUID), because that is the
  ticket's core agent-workflow question (FR-03 step 2).

## Integration Tests (`clio.tests/`)

### TC-I-01: DI composition root resolves the enriched graph
**Stories 6+7 / DoD; guards R-05**

- **Setup**: build the real `BindingsModule` container (composition-root test, same
  vehicle as the existing `Program`/`BindingsModule` fixtures).
- **Category**: `[Category("Integration")]`, `[Property("Module", "Command")]`
- **Steps**:
  1. Resolve `ILookupDefaultDisplayValueResolver` from the container.
  2. Resolve `GetEntitySchemaColumnPropertiesCommand` and
     `ModifyEntitySchemaColumnCommand` (transitively exercising the new
     `RemoteEntitySchemaColumnManager` constructor dependency).
- **Expected**: all resolutions succeed without manual `new` (because the DI policy
  forbids constructing behavior classes manually, and a missing registration is the
  single most likely fixture-breaking change of Phase B — R-05).
- **Teardown**: dispose the container.

No further TC-I cases: the feature has no file-system/DB surface (ADR test-strategy
table) — anything beyond DI composition belongs to Unit or E2E.

## E2E Tests (`clio.mcp.e2e/EntitySchemaToolE2ETests.cs`)

> **⚠️ CI status: `clio.mcp.e2e` is NOT in CI.** Every TC-E2E below is
> manual-execution-only. The full entity-schema E2E suite MUST be run manually
> against a real instance **before the Phase B merge**, and the run result recorded
> in the PR description (story 8 AC-04 — this is the SM-01 Phase B counter).
> **PR checklist gate**: add "manual `clio.mcp.e2e` entity-schema run recorded" as a
> blocking checklist item on the Phase B PR.

**Test-data prerequisites** (per the existing `EntitySchemaToolE2ETests.cs` pattern):
destructive sandbox environment; destructive cases gated behind
`AllowDestructiveMcpTests=true`; unique package per test via the arrange-context
helper; `[Category("E2E")]` + `[Description]` + Allure descriptions on every test.
TC-E2E-06 additionally requires a restricted-permission user on the sandbox (no read
rights on the referenced lookup) — provision before the run or document the skip.

### TC-E2E-01: Full ticket scenario — machine-verifiable readback
**Story 8 / AC-03; DRAFT-AC-05; mirrors FR-03 steps 1–6**
- **Tools**: `create-lookup` → `odata-create` (seed record, capture GUID) →
  `modify-entity-schema-column` (add lookup column + `Const` default with the GUID)
  → `get-entity-schema-column-properties` → `odata-create` (insert WITHOUT the
  column) → `odata-read`.
- **Expected**: readback satisfies all four MV-1 predicate components in a single
  call (source `Const`, GUID, referenced schema name, `display-value`); the runtime
  insert receives the default (RV-1 analogue — FR-03 step 6).

### TC-E2E-02: Readback degrades to marker for a deleted record
**Story 8 / AC-03; DRAFT-AC-05 markers**
- **Steps**: arrange as TC-E2E-01 through the default save, then `odata-delete` the
  referenced record; call `get-entity-schema-column-properties`.
- **Expected**: GUID preserved + `record-resolution: not-found-or-no-access`; exit
  code 0 (fail-soft); no stack trace in output.

### TC-E2E-03: Degraded-permission readback (`no-access`)
**Story 8 / AC-03; DRAFT-AC-05 markers; R-02**
- **Steps**: as TC-E2E-01 through the default save; re-run the readback under the
  restricted-permission user (no schema-level read on the referenced lookup).
- **Expected**: GUID + `record-resolution: no-access`; exit 0.
- **Note**: if a restricted user cannot be provisioned on the sandbox, record the
  skip in the manual-run report — the marker mapping itself stays covered by
  TC-U-02; the skip must be explicit, not silent.

### TC-E2E-04: Write rejection for a nonexistent GUID
**Story 8 / AC-03; DRAFT-AC-06**
- **Steps**: `modify-entity-schema-column` setting a `Const` default with a random
  GUID absent from the referenced (seeded) lookup.
- **Expected**: exact error `Error: default value record '{guid}' not found in
  '{ReferenceSchema}'`, non-zero exit; follow-up readback shows the schema was NOT
  saved with the dangling default.

### TC-E2E-05: Empty just-created lookup edge
**Story 8 / AC-03; DRAFT-AC-06 caveat; story 7 AC-02**
- **Steps**: `create-lookup` (no seeding) → immediately set a `Const` default
  referencing it (any GUID).
- **Expected**: same DRAFT-AC-06 error (honest prompt to seed a record first —
  FR-03 step 2); non-zero exit; no save.

### TC-E2E-06: Display-name write resolution round-trip
**Story 8 / AC-03; story 7 AC-03**
- **Steps**: seed a uniquely named record; set the `Const` default by **display
  name** via `modify-entity-schema-column`; read back.
- **Expected**: readback shows the resolved GUID of the seeded record + its
  `display-value` — the full human-style round-trip.

---

## Regression Guard (Part B)

Tests/files that MUST stay green after Phase B ships:

| Test file | Why at risk |
|-----------|------------|
| `clio.tests/Command/EntitySchemaDefaultValueSourceResolverTests.cs` | Story 7 extends `Resolve` for lookup-`Const`; existing `Settings`/`SystemValue` expectations must not shift (TC-U-19) |
| `clio.tests/Command/RemoteEntitySchemaColumnManagerTests.cs` | New constructor dependency `ILookupDefaultDisplayValueResolver` — fixture construction/DI breaks first here (R-05) |
| `clio.tests/Command/EntitySchemaDesignerSupportTests.cs` | `CreateDefaultValueConfig` / `ValidateDefaultValueConfig` mapping changes shape |
| `clio.tests/Command/GetEntitySchemaColumnPropertiesCommandTests.cs` | Readback output surface (JSON + printed) changes |
| `clio.tests/Command/ModifyEntitySchemaColumnCommandTests.cs` | Write path gains validation; legacy `default-value-source`/`default-value` shorthand must keep working (R-08) |
| `clio.tests/Command/UpdateEntitySchemaCommandTests.cs` + `.BatchExecution.Tests.cs` | Shares the designer save path that story 7's validation hooks into |
| `clio.tests/Command/CreateLookupCommandTests.cs`, `CreateEntitySchemaCommandTests.cs` | Share `RemoteEntitySchemaCreator`/designer client graph |
| `clio.tests/Command/McpServer/EntitySchemaToolTests.cs` | Tool description/contract assertions change with story 8 |
| `clio.tests/Command/McpServer/ToolContractGetToolTests.cs` | `get-tool-contract` payload extends |
| `clio.tests/Command/McpServer/UpdateEntitySchemaToolTests.cs` | Adjacent entity-schema MCP surface |
| `clio.mcp.e2e/EntitySchemaToolE2ETests.cs` | Existing `SystemValue`/`Settings` default E2E scenarios (~lines 312, 341) must still pass in the same manual run |

Regression run: pre-commit filter above; full unit suite additionally required because
`clio/BindingsModule.cs` changes (full-suite trigger per the smart-regression policy):
`dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"`.

---

## Traceability Matrix (TC → story → AC)

| TC | Story | AC / DRAFT-AC |
|----|-------|---------------|
| RC-A1.* | 1 | PRD AC-01; story-1 AC-01..AC-03, AC-ERR |
| RC-A2.* / EM-1 | 2 | PRD AC-02, AC-ERR; story-2 AC-01..AC-04 |
| RC-A3.* / MV-1 / RV-1 | 3 | PRD AC-03; story-3 AC-01..AC-05, AC-ERR; A-02/A-04; OQ-03/OQ-05 |
| RC-A4.* | 4 | PRD AC-04; story-4 AC-01..AC-05, AC-ERR; SM-03/N-03 |
| RC-A5.* | 5 | FR-08; story-5 AC-01..AC-04, AC-ERR |
| TC-U-01..06 | 6 | DRAFT-AC-05; story-6 AC-01, AC-02, AC-ERR |
| TC-U-07..08 | 6 | DRAFT-AC-05; story-6 AC-01, AC-02 |
| TC-U-09..10 | 6 | story-6 AC-03 (PRD non-goal) |
| TC-U-11 | 6 | story-6 AC-04 (kebab-case JSON) |
| TC-U-12 | 6 | story-6 implementation notes (print surface) |
| TC-U-13 | 7 | story-7 AC-04 |
| TC-U-14 | 7 | DRAFT-AC-06; story-7 AC-01 |
| TC-U-15 | 7 | DRAFT-AC-06 caveat; story-7 AC-02 |
| TC-U-16..18 | 7 | story-7 AC-03 |
| TC-U-19 | 7 | story-7 AC-04 |
| TC-U-20..22 | 8 | story-8 AC-02 |
| TC-I-01 | 6+7 | DoD items (DI registration), R-05 |
| TC-E2E-01 | 8 | story-8 AC-03; DRAFT-AC-05; FR-03 steps 1–6 analogue |
| TC-E2E-02..03 | 8 | story-8 AC-03; DRAFT-AC-05 markers; story-6 AC-01 |
| TC-E2E-04..05 | 8 | story-8 AC-03; DRAFT-AC-06; story-7 AC-01/AC-02 |
| TC-E2E-06 | 8 | story-8 AC-03; story-7 AC-03 |

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Part A document acceptance | 5 checklists (RC-A1..RC-A5) + 3 procedures (EM-1, MV-1, RV-1) + Regression Guard A | — | Manual review; recorded per PR |
| Unit | 22 (TC-U-01..22) | ~4 fixtures touched (resolver, manager, support, MCP) | `[Category("Unit")]`, Module=Command/McpServer |
| Integration | 1 (TC-I-01) | 0 | DI composition only — no FS/DB surface |
| E2E | 6 (TC-E2E-01..06) | existing SystemValue/Settings E2Es re-run | **Manual only — NOT in CI** |

---

## Definition of Done for QA

Phase A (per story PR):
- [ ] Matching RC-A checklist filled and recorded in the PR description
- [ ] Regression Guard A command output attached (spec/** only)
- [ ] Story 3 only: MV-1 verdict table + RV-1 result present in the evidence doc

Phase B (gated; only if story 4 confirms the gap):
- [ ] All TC-U-01..22 implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`
- [ ] TC-I-01 implemented with `[Category("Integration")]`
- [ ] All TC-E2E-01..06 implemented with `[Category("E2E")]` in `clio.mcp.e2e/EntitySchemaToolE2ETests.cs`
- [ ] Test naming `MethodName_ShouldExpectedBehavior_WhenCondition`; AAA + `because` on every assertion + `[Description]` on every test
- [ ] Regression-guard suite green: pre-commit filter + full unit suite (BindingsModule trigger)
- [ ] Manual `clio.mcp.e2e` entity-schema run executed against a real instance and recorded in the PR description (SM-01 Phase B counter); TC-E2E-03 skip, if any, recorded explicitly
- [ ] Marker strings and the DRAFT-AC-06 error asserted verbatim from implementation constants, not paraphrased
- [ ] PR includes all new/modified test files in the changed-files list; "docs reviewed" / "MCP reviewed" statements present (story 8 AC-ERR)
