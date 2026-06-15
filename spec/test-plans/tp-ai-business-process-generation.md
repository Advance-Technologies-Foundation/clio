# Test Plan: AI-Assisted Business Process Generation via clio MCP (Approach 3 / Variant A)

**Feature**: ai-business-process-generation
**Stories**: [story-1](../stories/story-ai-business-process-generation-1.md) · [story-2](../stories/story-ai-business-process-generation-2.md) · [story-3](../stories/story-ai-business-process-generation-3.md) · [story-4](../stories/story-ai-business-process-generation-4.md) · [story-5](../stories/story-ai-business-process-generation-5.md) · [story-6](../stories/story-ai-business-process-generation-6.md) · [story-7](../stories/story-ai-business-process-generation-7.md) · [story-8](../stories/story-ai-business-process-generation-8.md)
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md)
**Author**: QA Planner Agent
**Status**: Draft
**Created**: 2026-06-12

---

## Scope

### In scope

- The four cooperating components: `process-modeling` guidance resource, `ProcessGraphValidator`
  (`validate-process-graph` MCP tool), the CDP `process-add-element` driver/command/tool, and the
  read-side `describe-process` command/tool.
- The Story 1 launcher refactor (`ICdpSession`/`CdpSession` extraction) — verified by **regression**
  (no behavior change to `open-web-app --authenticated`) plus new unit coverage of the extracted helper.
- Story 3 `ManagerMap.ResolveDataId` + role helper — the single classification source of truth shared
  by the validator (Story 4) and `describe-process` (Story 8).
- Validator rule coverage R1–R17 (every error and warning rule the spec enumerates), valid + invalid
  graphs, orphan/unreachable, default-without-conditional, conditional/default on parallel/event-based,
  event-based → catch, start/end cardinality, and the no-false-positive guarantee.
- MCP tool argument-mapping and safety-flag tests for `validate-process-graph`, `process-add-element`,
  and `describe-process`.
- `describe-process` graph extraction from the existing `clio.tests/Examples/ProcessSchema/*.json`
  fixtures.
- The three `clio.mcp.e2e` suites (flagged NOT in CI).

### Out of scope (with reason)

- Process elements other than Read data are **not driven** (PRD non-goal): no Add/Modify/Delete data,
  formula/script/webService/callActivity, gateways, intermediate events. The validator still *validates*
  those `data-id`s (the agent may plan them), so they are exercised at the unit tier only.
- Headless browser parity — only headed is supported this increment (NFR-02); no headless E2E.
- `describe-process` filter/mapping expression decoding (`FilterGroup`/`ParameterExpression`) — v1
  limitation (FR-19); tests assert structure + types + flows + basic params only.
- Playwright / headless CI harness — future work (PRD non-goal); the embedded recipe is the future reuse
  artifact but is not CI-executed here.
- Transactional/rollback semantics across multiple element additions (NFR-04) — single-element only.
- OAuth-only environments — unsupported; tested only via the AC-ERR fail-closed path.
- No test asserts clio makes an LLM call — by design clio makes **none** (NFR-06); E2E network
  inspection that clio adds zero third-party model calls is a manual verification note, not an
  automated assertion.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| **R-05 launcher refactor** breaks shipped `open-web-app --authenticated` (Story 1 moves private CDP plumbing out of `AuthenticatedBrowserLauncher`) | Med | High | Keep `IAuthenticatedBrowserLauncher` contract identical; keep CDP error handling verbatim; existing `AuthenticatedBrowserLauncherTests` MUST stay green (TC-U-CDP-04); full unit suite trigger (Common/ + BindingsModule changed); manual launcher live-DevTools E2E gate (TC-E2E-04). |
| **R-01 UI fragility / selector drift** — designer is diagram-js/bpmn-js in the Ext shell; `data-id`/`data-action` selectors drift across Creatio versions | High | High | Driver targets stable catalog `data-id`/`data-action`; `.djs-validate-outline` live gate (TC-U-DRV-04, AC-09); readback gate via `VwProcessLib` (TC-E2E-02); catalog is the single selector source. Driver logic is unit-tested against a **mocked** `ICdpSession`, so unit tier is selector-drift-immune; only the live E2E is exposed. |
| **Render timing** — SVG canvas + palette render seconds after the metadata card; black-canvas/cache caveat | High | Med | Driver polls `.djs-shape` / `.entry[data-action="create-serviceTask"]` with a bounded timeout and dismisses stray `.djs-popup` before clicking (NFR-03); unit test asserts fail-fast `Error:` on timeout (TC-U-DRV-05, AC-ERR). |
| **Non-transactional recovery** — mid-run failure can leave an unsaved (discarded) or saved-but-partial process (NFR-04) | Med | Med | Driver never reports false-positive save (TC-U-DRV-04/05); pre-SAVE failure closes the browser; post-SAVE failure reports identity; E2E readback proves the actual saved state (TC-E2E-02). |
| **No-false-save gate** — `.djs-validate-outline` must veto a SAVE the static validator would have passed | Med | High | The designer is the final authority; driver asserts the selector is absent after append (TC-U-DRV-04, AC-09); command surfaces the validator-abort path **before** opening a browser (TC-U-CMD-PAE-01, AC-10). |
| **Validator false positives** — a graph the live designer accepts must never be flagged `error` (only R12/R17 may warn) | Med | High | Dedicated no-false-positive test over a designer-accepted graph (TC-U-VAL-18, AC-06); advisory rules emit `warning`, never `error` (TC-U-VAL-15/16/17). |
| **Forms-auth + Chromium env deps** — driver needs a local Chromium + a forms-auth browser session; OAuth-only envs unsupported | High (CI) | Med | Fail closed with user-friendly `Error:` (TC-U-CMD-PAE-04, AC-ERR); reuse `ChromiumNotFoundException`/`CreatioAuthenticationException`; E2E requires `krestov-test` + Chromium and is **not in CI**. |
| **MCP E2E not in CI** — all three E2E suites are unrunnable on the CI runner | Certain | Med | Flag every E2E case explicitly; add a manual-execution gate to the PR checklist; unit tier is the CI safety net. |
| **`describe-process` graph reconstruction** — element-to-element links must be rebuilt from `SourceRefUId`/`TargetRefUId` (A-07) | Med | Med | Extractor unit tests over the real `ProcessSchemaResponse*.json` fixtures assert non-empty elements/flows with correct source/target/kind (TC-U-EXT-01..03); if a fixture lacks flow elements, that is surfaced as a fixture/extractor gap, not a crash. |
| **Secret hygiene** — cookie values must never appear in logs/MCP payloads/stdout/`--debug` (NFR-07) | Low | High | Reuse browser-session redaction (names only); recipe injects only source object + caption (TC-U-DRV-06); no test prints a cookie value. |
| **CDP egress scope** — only loopback `127.0.0.1` is permitted (NFR-06) | Low | Med | `CdpSession` binds to loopback only (TC-U-CDP-03); no LLM/network call in any path. |

---

## Regression scope (smart-testing policy)

Module-to-story mapping and the exact targeted filters:

| Story | Changed source paths | Module trait(s) | Full-suite trigger? |
|-------|---------------------|-----------------|---------------------|
| 1 | `clio/Common/BrowserSession/**`, `clio/BindingsModule.cs` | `Common` | **YES** — `Common/` **and** `BindingsModule.cs` (rule 4) |
| 2 | `clio/Command/McpServer/Resources/**` | `McpServer` | No |
| 3 | `clio/Command/ProcessModel/Schema.cs` | `ProcessModel` | No |
| 4 | `clio/Command/ProcessModel/**`, `clio/BindingsModule.cs` | `ProcessModel` | **YES** — `BindingsModule.cs` (rule 4) |
| 5 | `clio/Command/McpServer/Tools/**`, `Prompts/**` | `McpServer` | No |
| 6 | `clio/Common/ProcessDesigner/**`, `clio/BindingsModule.cs`, `clio/clio.csproj` | `Common` | **YES** — `Common/` **and** `BindingsModule.cs` (rule 4) |
| 7 | `clio/Command/ProcessDesigner/**`, `clio/Command/McpServer/**`, `clio/BindingsModule.cs`, `clio/Program.cs` | `Command`, `McpServer` | **YES** — `BindingsModule.cs` **and** `Program.cs` (rule 4) |
| 8 | `clio/Command/ProcessModel/**`, `clio/Command/DescribeProcessCommand.cs`, `clio/Command/McpServer/**`, `clio/BindingsModule.cs`, `clio/Program.cs` | `Command`, `McpServer`, `ProcessModel` | **YES** — `BindingsModule.cs` **and** `Program.cs` (rule 4) |

Exact `dotnet test` filters (run from repo root `C:\Projects\clio`):

```powershell
# Story 2, 5 (McpServer only)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build

# Story 3 (ProcessModel only)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=ProcessModel" --no-build

# Story 7 (Command + McpServer) — also a full-suite trigger; the targeted filter for fast local feedback:
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build

# Story 8 (Command + McpServer + ProcessModel) — also a full-suite trigger; targeted local feedback:
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer|Module=ProcessModel)" --no-build

# Common-only fast feedback (not sufficient alone for Stories 1/6 — see full suite below)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Common" --no-build

# MANDATORY full unit suite for Stories 1, 4, 6, 7, 8 (BindingsModule.cs / Program.cs / Common/ changed)
dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit" --no-build
```

> Stories 1, 4, 6, 7, 8 each touch `BindingsModule.cs`, `Program.cs`, or `clio/Common/` →
> **the full unit suite is mandatory before commit** for those stories (smart-testing rule 4). The
> targeted filters above are for fast iteration only. Include the filter used in each PR description.

Existing tests explicitly at risk (regression guard table below): `AuthenticatedBrowserLauncherTests`,
`OpenAppCommandTests`, `ChromiumLocatorTests`, `SchemaTestFixture`, `GenerateProcessModelToolTests`,
`McpGuidanceResourceTests`, `GuidanceGetToolTests`.

---

## Test data needs

| Need | Detail | Used by |
|------|--------|---------|
| Schema fixtures | `clio.tests/Examples/ProcessSchema/ProcessSchemaResponse0.json` / `1.json` / `2.json` (already present, parsed by `ProcessSchemaResponse.FromJson`; `2.json` carries 9 process-level parameters) | TC-U-EXT-01..04 |
| Embedded recipe | `clio/Common/ProcessDesigner/Recipes/read-data-element.recipe.js` (Story 6 `<EmbeddedResource>`) read via `ProcessDesignerRecipes.Get("read-data-element")` | TC-U-DRV-01 |
| Seeded live process | A **known existing** process on the live env for `describe-process` readback (e.g. a `VwProcessLib` row; the PoC `UsrProcess_493d4c9` / "AI PoC Read Contact" qualifies) with at least one element + one flow + ≥1 process parameter | TC-E2E-DP-01 |
| Live env | `krestov-test` with **forms-auth** (NFR-01) + locally installed **Chromium** (NFR-02); A-05 assumes handoff works there | TC-E2E-PAE-01, TC-E2E-DP-01, TC-E2E-04 |
| Read object | `Contact` entity must exist/queryable on the live env for the Read-data source-object lookup | TC-E2E-PAE-01 |
| Readback view | `VwProcessLib` queryable via `execute-esq`; `generate-process-model --code <code>` must exit 0 | TC-E2E-PAE-02 |

In-memory unit tests need **no** environment: the validator, extractor, recipe accessor, tool
arg-mapping, and guidance registration are all NSubstitute-mocked or pure.

---

## Unit Tests (`clio.tests/`)

All unit tests: `[Category("Unit")]`, `[Property("Module", "<trait>")]`, NUnit 4 + FluentAssertions +
NSubstitute, naming `MethodName_ShouldBehavior_WhenCondition`, explicit AAA, a `because` on every
assertion, a `[Description]` on every test. Command tests use `BaseCommandTests<TOptions>` (resolve the
SUT from the container in `AdditionalRegistrations`, `ClearReceivedCalls` in teardown).

### Story 1 — `CdpSession` + launcher refactor (`Module=Common`)

**File**: `clio.tests/Common/BrowserSession/CdpSessionTests.cs`

| ID | Test (`MethodName_Should…_When…`) | AC | Notes |
|----|-----------------------------------|----|-------|
| TC-U-CDP-01 | `SendAsync_ShouldReturnMatchingResultFrame_WhenIdMatches` | S1-AC-01 | drains frames to the matching id; verbatim CDP framing behavior |
| TC-U-CDP-02 | `SendAsync_ShouldThrow_WhenCdpErrorFrameReturned` | S1-AC-01 | throws the same exception type as pre-extraction (no new failure mode) |
| TC-U-CDP-03 | `EvaluateAsync_ShouldIssueRuntimeEvaluate_WhenAwaitPromiseHonored` | S1-AC-02 | asserts `Runtime.evaluate` method + `awaitPromise` flag; returns awaited JSON result |
| TC-U-CDP-04 | `ConnectAsync_ShouldBindLoopbackOnly_WhenConnecting` | S1-AC-05, NFR-06 | target is `127.0.0.1` only |
| TC-U-CDP-05 | `ConnectAsync_ShouldThrowExistingExceptionType_WhenPortUnreadableOrNoPageTarget` | S1-AC-ERR | same exception type the launcher raised before extraction |

**File (existing — regression)**: `clio.tests/Common/BrowserSession/AuthenticatedBrowserLauncherTests.cs`

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-CDP-06 | (existing 7 tests) **kept green** after refactor + a new `LaunchAsync_ShouldExposeDevToolsPort_WhenLaunched` | S1-AC-03, S1-AC-04 | launcher still launches + injects + navigates with a mocked `ICdpSession`; exposes the chosen `DevToolsPort` for the driver |

### Story 2 — `process-modeling` guidance (`Module=McpServer`)

**File**: `clio.tests/Command/McpServer/ProcessModelingGuidanceResourceTests.cs`

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-GUI-01 | `GuidanceCatalog_ShouldRegisterProcessModeling_WhenQueried` | S2-AC-05, AC-01 | `get-guidance --name process-modeling` resolves a non-empty `TextResourceContents` |
| TC-U-GUI-02 | `Guide_ShouldStateNoLlmAndAgentOwnsTranslation_WhenRead` | S2-AC-01, FR-02 | text contains the explicit no-LLM + agent-owns-translation statement |
| TC-U-GUI-03 | `Guide_ShouldInstructValidateBeforeDriving_WhenRead` | S2-AC-03, FR-03 | text instructs calling `validate-process-graph` first and treats `.djs-validate-outline` as final authority |
| TC-U-GUI-04 | `Guide_ShouldScopeToSupportedSlice_WhenRead` | S2-AC-04, FR-04 | marks non-slice elements as "described for context, not yet drivable by clio" |
| TC-U-GUI-05 | `GetGuidance_ShouldReturnExistingUnknownError_WhenNameUnknown` | S2-AC-ERR | reuses the existing unknown-guidance path; no new failure mode |

### Story 3 — `ManagerMap.ResolveDataId` + role helper (`Module=ProcessModel`)

**File**: `clio.tests/Command/ProcessModel/ManagerMapResolveDataIdTests.cs`

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-MAP-01 | `ResolveDataId_ShouldReturnStartEventType_WhenStartDataId` (TestCase: `startEvent`/`startEventSignal`/`startEventTimer`/`startEventMessage`) | S3-AC-01 | one parameterised case per start id |
| TC-U-MAP-02 | `ResolveDataId_ShouldReturnEndEvent_WhenEndDataId` | S3-AC-02 | Simple end and Terminate share `endEvent` |
| TC-U-MAP-03 | `ResolveDataId_ShouldReturnActivityRole_WhenActivityDataId` (TestCase: `readDataUserTask`/`addDataUserTask`/`changeDataUserTask`/`deleteDataUserTask`/`userTask`/`formulaTask`/`scriptTask`/`webService`/`callActivity`) | S3-AC-03 | maps to `UserTask`/`FormulaTask`/`ScriptTask`/`WebServiceTask`/`SubProcess` per enum |
| TC-U-MAP-04 | `ResolveDataId_ShouldReturnGatewayType_WhenGatewayDataId` (TestCase: 4 gateways) | S3-AC-04 | `exclusiveGateway`/`parallelGateway`/`inclusiveGateway`/`eventBasedGateway` |
| TC-U-MAP-05 | `ResolveDataId_ShouldReturnIntermediateType_WhenIntermediatePrefix` | S3-AC-05 | `intermediateCatchEvent*` / `intermediateThrowEvent*` prefix match |
| TC-U-MAP-06 | `ResolveRole_ShouldCollapseToFiveRoles_WhenGivenEventType` | S3-AC-06 | role helper → Start / End / Activity / Gateway / Intermediate |
| TC-U-MAP-07 | `ResolveDataId_ShouldReturnUnknown_WhenDataIdUnrecognized` | S3-AC-ERR | never throws; returns `EventType.Unknown` |

### Story 4 — `ProcessGraphValidator` rules R1–R17 (`Module=ProcessModel`)

**File**: `clio.tests/Command/ProcessModel/ProcessGraphValidatorTests.cs`

Errors (R1, R2, R3, R10, R11, R13, R14, R15) and warnings (R7/R9, R12, R17). At least one
valid + one invalid graph per rule.

| ID | Test | Rule | AC | Notes |
|----|------|------|----|-------|
| TC-U-VAL-01 | `Validate_ShouldReturnNoErrors_WhenStartReadDataEndGraphIsValid` | (valid) | S4-AC-01, AC-02 | `Start → readDataUserTask → End`, 2 sequence edges; `HasErrors == false` |
| TC-U-VAL-02 | `Validate_ShouldEmitR1Error_WhenStartHasIncomingFlow` | R1 | S4-AC-02, AC-03 | start with an incoming edge → `Error`/`R1` |
| TC-U-VAL-03 | `Validate_ShouldEmitR1Error_WhenStartHasNotExactlyOneOutgoing` | R1 | S4-AC-06 | start with 0 or ≥2 outgoing |
| TC-U-VAL-04 | `Validate_ShouldEmitR2Error_WhenEndHasOutgoingFlow` | R2 | S4-AC-06 | end-as-source → `Error`/`R2` |
| TC-U-VAL-05 | `Validate_ShouldEmitR2Error_WhenEdgeReferencesMissingNode` | R2 | S4-AC-ERR | edge from/to a missing node id → `Error`/`R2`, no exception |
| TC-U-VAL-06 | `Validate_ShouldEmitR3Error_WhenNoStartEventPresent` | R3 | S4-AC-06 | zero start events |
| TC-U-VAL-07 | `Validate_ShouldEmitR3Error_WhenMoreThanOneStartEvent` | R3 | S4-AC-06 | >1 start event |
| TC-U-VAL-08 | `Validate_ShouldEmitR10Error_WhenEventBasedGatewayOutgoingNotToCatchEvent` | R10 | S4-AC-06 | event-based gateway outgoing that does not lead directly to an intermediate catch event |
| TC-U-VAL-09 | `Validate_ShouldEmitR11Error_WhenParallelGatewayCarriesConditionalFlow` | R11 | S4-AC-06 | conditional/default on a parallel gateway |
| TC-U-VAL-10 | `Validate_ShouldEmitR11Error_WhenEventBasedGatewayCarriesDefaultFlow` | R11 | S4-AC-06 | conditional/default on an event-based gateway |
| TC-U-VAL-11 | `Validate_ShouldEmitR13Error_WhenConditionalFlowNotFromGatewayOrActivity` | R13 | S4-AC-06 | conditional flow leaving e.g. a start event |
| TC-U-VAL-12 | `Validate_ShouldEmitR14Error_WhenDefaultFlowHasNoSiblingConditional` | R14 | S4-AC-03, AC-04 | default with no sibling conditional → `Error`/`R14` |
| TC-U-VAL-13 | `Validate_ShouldEmitR15Error_WhenNodeIsOrphanFromStart` | R15 | S4-AC-06 | node unreachable from start |
| TC-U-VAL-14 | `Validate_ShouldEmitR15Error_WhenNodeCannotReachEnd` | R15 | S4-AC-04, AC-05 | orphan/dead-end node → `Error`/`R15` |
| TC-U-VAL-15 | `Validate_ShouldEmitR7R9Warning_WhenDivergingExclusiveOrInclusiveMissingDefault` | R7/R9 | S4-AC-07 | `Warning`, never `Error` |
| TC-U-VAL-16 | `Validate_ShouldEmitR12Warning_WhenMultipleOutgoingSequenceFlows` | R12 | S4-AC-07 | implicit parallel split = `Warning` |
| TC-U-VAL-17 | `Validate_ShouldEmitR17Warning_WhenAddDataConsumedWithoutInterveningReadData` | R17 | S4-AC-07 | advisory `Warning`, never `Error` |
| TC-U-VAL-18 | `Validate_ShouldReturnNoError_WhenGraphIsDesignerAccepted` | (no-FP) | S4-AC-05, AC-06 | designer-accepted graph → zero `error` (R12/R17 `warning` permitted) — **false-positive guard** |
| TC-U-VAL-19 | `Validate_ShouldSurfaceFinding_WhenNodeDataIdIsUnknown` | (Unknown) | S4-AC-08 | unknown `data-id` classifies to `Unknown` via `ResolveDataId` → finding, no crash |
| TC-U-VAL-20 | `Validate_ShouldBeResolvableFromDi_WhenRegistered` | (DI) | S4-AC-09 | resolved behind `IProcessGraphValidator`; no `new`, no MediatR |

### Story 5 — `validate-process-graph` MCP tool (`Module=McpServer`)

**File**: `clio.tests/Command/McpServer/ValidateProcessGraphToolTests.cs`

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-VPG-01 | `ValidateProcessGraphTool_ShouldAdvertiseStableToolName_WhenInspected` | S5-AC-06 | name `validate-process-graph` |
| TC-U-VPG-02 | `ValidateProcessGraph_ShouldMapArgsToProcessGraph_WhenNodesAndEdgesGiven` | S5-AC-01, AC-07 | `nodes:[{id,type}]` + `edges:[{source,target,flow-kind}]` → `ProcessGraph`; `flow-kind` parses to `ProcessFlowKind` |
| TC-U-VPG-03 | `ValidateProcessGraph_ShouldReturnZeroErrors_WhenGraphIsValid` | S5-AC-01 | valid `Start → Read data → End` args |
| TC-U-VPG-04 | `ValidateProcessGraph_ShouldReturnR1Finding_WhenStartHasIncoming` | S5-AC-02 | R1 surfaces in the response shape |
| TC-U-VPG-05 | `ValidateProcessGraph_ShouldReturnR14Finding_WhenDefaultWithoutConditional` | S5-AC-03 | R14 in response |
| TC-U-VPG-06 | `ValidateProcessGraph_ShouldReturnR15Finding_WhenOrphanNode` | S5-AC-04 | R15 in response |
| TC-U-VPG-07 | `ValidateProcessGraph_ShouldNotReturnError_WhenDesignerAcceptedGraph` | S5-AC-05 | advisory `warning` permitted |
| TC-U-VPG-08 | `ValidateProcessGraph_ShouldCarryReadOnlySafetyFlags_WhenInspected` | S5-AC-06 | reflection on `McpServerToolAttribute`: `ReadOnly=true`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false` |
| TC-U-VPG-09 | `ValidateProcessGraph_ShouldInjectValidatorDirectly_WhenNotEnvironmentSensitive` | S5-AC-07 | injects `IProcessGraphValidator`; does **not** use `IToolCommandResolver`; not in `Program.cs` |
| TC-U-VPG-10 | `ValidateProcessGraph_ShouldReturnFindingNotThrow_WhenArgsMalformed` | S5-AC-ERR | missing-node edge / empty `nodes` → structured finding (R2 / R3), no stack trace |

### Story 6 — `ProcessDesignerDriver` + embedded recipe (`Module=Common`)

**File**: `clio.tests/Common/ProcessDesigner/ProcessDesignerDriverTests.cs` (mocked `ICdpSession`)

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-DRV-01 | `Get_ShouldReadAndCacheEmbeddedRecipe_WhenRecipeNameGiven` | S6-AC-10 | `ProcessDesignerRecipes.Get("read-data-element")` reads the manifest stream once (cached) |
| TC-U-DRV-02 | `AddReadDataElement_ShouldOpenOrCreateDesigner_WhenProcessIdNullOrSet` | S6-AC-01 | null → create (`Start→End`); set → open existing |
| TC-U-DRV-03 | `AddReadDataElement_ShouldConfigureOnlySourceObjectAndCaption_WhenRunning` | S6-AC-04, AC-05 | sets only the read-object lookup + caption; never touches read-mode/filter/columns |
| TC-U-DRV-04 | `AddReadDataElement_ShouldFailWithoutSave_WhenValidateOutlinePresent` | S6-AC-06, AC-08; AC-09 | `.djs-validate-outline` present → `Success=false`, `Error:` names the invalid connection, no SAVE |
| TC-U-DRV-05 | `AddReadDataElement_ShouldFailFast_WhenCanvasNeverRenders` | S6-AC-ERR | `.djs-shape` timeout → designer-never-rendered `Error:`, no stack trace, no saved partial |
| TC-U-DRV-06 | `AddReadDataElement_ShouldJsonEscapeRecipeParameters_WhenInjectingObjectAndCaption` | S6-AC-09, NFR-07 | source object + caption JSON-escaped, never raw-concatenated |
| TC-U-DRV-07 | `AddReadDataElement_ShouldReturnSuccessAndIdentity_WhenSaveSignalDetected` | S6-AC-07 | `Success=true` + `{Code, UId, Caption}` only on a real "Successfully saved" signal |
| TC-U-DRV-08 | `AddReadDataElement_ShouldReturnFailure_WhenSaveSignalMissingOrCdpError` | S6-AC-08 | no false-positive save on missing signal / CDP error |

### Story 7 — `process-add-element` command + MCP tool (`Module=Command`, `Module=McpServer`)

**File**: `clio.tests/Command/ProcessDesigner/ProcessAddElementCommandTests.cs`
(`BaseCommandTests<ProcessAddElementOptions>`)

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-CMD-PAE-01 | `Execute_ShouldAbortBeforeLaunch_WhenValidatorReportsError` | S7-AC-04, AC-10 | mocked `IProcessGraphValidator` returns error → `IAuthenticatedBrowserLauncher` **never** called |
| TC-U-CMD-PAE-02 | `Execute_ShouldReturnError_WhenElementTypeIsNotReadData` | S7-AC-07 | slice guard: only `read-data` accepted |
| TC-U-CMD-PAE-03 | `Execute_ShouldAutoGenerateCaption_WhenProcessCaptionOmitted` | S7 impl note | e.g. `clio-pae-<utc>-<short>` |
| TC-U-CMD-PAE-04 | `Execute_ShouldReturnSpecificError_WhenChromiumMissingOrNoFormsAuthSession` | S7-AC-ERR | `ChromiumNotFoundException` / `CreatioAuthenticationException` → user-friendly `Error:`, non-zero exit, no partial designer |
| TC-U-CMD-PAE-05 | `Execute_ShouldReturnSavedIdentity_WhenDriverReportsSuccess` | S7-AC-01, AC-08 | `{code, uId, caption}` returned (driver mocked) |

**File**: `clio.tests/Command/McpServer/ProcessAddElementToolTests.cs`

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-TOOL-PAE-01 | `ProcessAddElementTool_ShouldAdvertiseStableToolName_WhenInspected` | S7-AC-05 | name `process-add-element` |
| TC-U-TOOL-PAE-02 | `ProcessAddElement_ShouldMapArgsToOptions_WhenInvoked` | S7-AC-06, AC-08 | kebab-case args → options (`element-type`/`read-object`/`process-id`/`process-caption`/`headed`/`environment-name`) |
| TC-U-TOOL-PAE-03 | `ProcessAddElement_ShouldUseEnvAwarePath_WhenExecuted` | S7-AC-05 | `InternalExecute<ProcessAddElementCommand>` via `IToolCommandResolver` |
| TC-U-TOOL-PAE-04 | `ProcessAddElement_ShouldCarrySafetyFlags_WhenInspected` | S7-AC-05 | `ReadOnly=false`, `Idempotent=false`; Destructive semantics (conservative static default; documented existing-process case) |

### Story 8 — `describe-process` (`Module=ProcessModel`, `Module=Command`, `Module=McpServer`)

**File**: `clio.tests/Command/ProcessModel/ProcessGraphExtractorTests.cs`
(reuses `clio.tests/Examples/ProcessSchema/*.json` via `ProcessSchemaResponse.FromJson`)

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-EXT-01 | `Extract_ShouldProjectElements_WhenSchemaParsedFromFixture` | S8-AC-01, AC-02 | non-flow `FlowElement`s → `{id, dataId, type(role), label, params}`; type via `ManagerMap.Resolve`/role helper |
| TC-U-EXT-02 | `Extract_ShouldProjectFlowsWithKind_WhenSchemaHasFlows` | S8-AC-03 | flow elements → `{source(SourceRefUId), target(TargetRefUId), kind}`; `FlowTypeSequence`/`EventType` → sequence/conditional/default |
| TC-U-EXT-03 | `Extract_ShouldPreserveProcessParameters_WhenSchemaHasParameters` | S8-AC-04 | `ProcessSchemaResponse2.json` → 9 parameters with name/type/direction/caption (parity with `generate-process-model`) |
| TC-U-EXT-04 | `Extract_ShouldReturnStructuredGraphNotRawMetadata_WhenProjecting` | S8-AC-05 | output is structured DTO, **not** the escaped `metaData`; filter/mapping not decoded (v1) |

**File**: `clio.tests/Command/DescribeProcessCommandTests.cs`
(`BaseCommandTests<DescribeProcessOptions>`)

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-CMD-DP-01 | `Execute_ShouldRequireExactlyOneIdentity_WhenCodeUidCaptionSupplied` | S8 OQ-06 | zero or >1 of code/uid/caption → `Error:` |
| TC-U-CMD-DP-02 | `Execute_ShouldEmitStructuredJson_WhenProcessResolved` | S8-AC-01 | emits `elements`/`flows`/`parameters` JSON (mocked schema-read collaborator) |
| TC-U-CMD-DP-03 | `Execute_ShouldReturnSpecificError_WhenProcessNotFound` | S8-AC-ERR | `Error: process '<code>' not found in environment '<env>'`, non-zero exit, no partial structure |
| TC-U-CMD-DP-04 | `Execute_ShouldMapCultureAndIdentity_WhenOptionsProvided` | S8-AC-01 | `--culture` default `en-US`; identity routed to the correct lookup |

**File**: `clio.tests/Command/McpServer/DescribeProcessToolTests.cs`

| ID | Test | AC | Notes |
|----|------|----|-------|
| TC-U-TOOL-DP-01 | `DescribeProcessTool_ShouldAdvertiseStableToolName_WhenInspected` | S8-AC-06 | name `describe-process` |
| TC-U-TOOL-DP-02 | `DescribeProcess_ShouldMapArgsToOptions_WhenInvoked` | S8-AC-01 | kebab-case args → options (`environment-name`/`process-code`/`process-uid`/`process-caption`/`culture`) |
| TC-U-TOOL-DP-03 | `DescribeProcess_ShouldCarryReadOnlySafetyFlags_WhenInspected` | S8-AC-06 | `ReadOnly=true`, `Destructive=false`, `Idempotent=true`, `OpenWorld=false` |
| TC-U-TOOL-DP-04 | `DescribeProcess_ShouldUseEnvAwarePath_WhenExecuted` | S8-AC-06 | env-sensitive → `InternalExecute<DescribeProcessCommand>` (mirrors `GenerateProcessModelTool`) |

---

## Integration Tests (`clio.tests/`)

This feature has **no pure Integration-tier (`[Category("Integration")]`) cases**. The non-mockable
behavior is all UI-driven (CDP + live designer) and lands in the E2E tier below; everything else is
pure in-memory and lands in the Unit tier. The embedded-recipe accessor (TC-U-DRV-01) reads from the
assembly manifest, which is in-process and remains a Unit case (no external I/O).

---

## E2E Tests (`clio.mcp.e2e/`)

> **⚠️ CI status — NONE of these run in CI.** `clio.mcp.e2e` is not wired into CI (project-context.md
> "MCP E2E tests are NOT in CI yet"). `process-add-element` and `describe-process` E2E additionally
> require a **live forms-auth Creatio env (`krestov-test`)**; `process-add-element` also requires a
> locally installed **Chromium** (headed). **Manual gate**: every E2E case below MUST be added to the
> PR checklist and executed manually before the owning story is marked `done`.

### TC-E2E-VPG-01 — `validate-process-graph` over the real MCP server (Story 5)

- **Tool**: `validate-process-graph`
- **File**: `clio.mcp.e2e/ValidateProcessGraphToolE2ETests.cs`
- **Input**: valid `Start → Read data → End` graph; and an R1 (start-incoming) graph
- **Expected**: valid → zero `error` findings; R1 graph → `error` finding `ruleId=R1`
- **Env deps**: none (pure in-memory over the MCP transport) — **automatable now** in the e2e harness,
  but still **not in CI**
- **AC**: S5 E2E row

### TC-E2E-PAE-01 — live build `Start → Read data → End` (Story 7)

- **Tool**: `process-add-element`
- **File**: `clio.mcp.e2e/ProcessAddElementToolE2ETests.cs`
- **Input**: `{"environment-name":"krestov-test","element-type":"read-data","read-object":"Contact","process-caption":"<deterministic>"}`
- **Expected**: opens authenticated designer via CDP, appends Read data onto the Start→End flow,
  configures the source-object lookup to `Contact`, SAVEs, returns `{success:true, code, uId, caption}`
- **Env deps**: `krestov-test` + forms-auth + local Chromium (headed)
- **AC**: S7-AC-01 / PRD AC-07 — **blocked on the e2e harness + live env (not in CI)**

### TC-E2E-PAE-02 — readback verification (Story 7, the PoC bar)

- **Tools**: `generate-process-model`, `execute-esq`
- **File**: `clio.mcp.e2e/ProcessAddElementToolE2ETests.cs`
- **Steps**:
  1. After TC-E2E-PAE-01 succeeds, run `generate-process-model --code <code>` → exit 0 and emits
     `[BusinessProcess("<code>")]`
  2. Run `execute-esq` on `VwProcessLib` filtered by the caption → exactly one row
- **Expected**: both readback checks pass (proof the process exists, not just a UI toast)
- **AC**: S7-AC-02 / PRD AC-08, OQ-04 — **blocked on the e2e harness + live env (not in CI)**

### TC-E2E-PAE-03 — no-false-save gate (Story 7)

- **Tool**: `process-add-element`
- **File**: `clio.mcp.e2e/ProcessAddElementToolE2ETests.cs`
- **Expected**: if the append/connect is flagged `.djs-validate-outline`, the tool does **not** SAVE,
  does **not** report success, and returns an `Error:` naming the invalid connection
- **AC**: S7-AC-03 / PRD AC-09 — **blocked on the e2e harness + live env (not in CI)**

### TC-E2E-DP-01 — `describe-process` live read of a known process (Story 8)

- **Tool**: `describe-process`
- **File**: `clio.mcp.e2e/DescribeProcessToolE2ETests.cs`
- **Input**: `{"environment-name":"krestov-test","process-code":"<known process>"}`
  (e.g. the PoC `UsrProcess_493d4c9`)
- **Expected**: structured JSON with **non-empty** `elements`, `flows`, and `parameters`; element types
  labelled via the same `data-id` vocabulary; parameters match what `generate-process-model` surfaces
  (reuse-symmetry); output is **not** the raw escaped `metaData`
- **Env deps**: `krestov-test` + a seeded/known process (no browser, no CDP)
- **AC**: S8-AC-01..05 / PRD AC-13 — **blocked on the e2e harness + live env (not in CI)**

### TC-E2E-04 — `AuthenticatedBrowserLauncher --authenticated` regression (Story 1)

- **Command**: `open-web-app --authenticated`
- **File**: reuse the `story-browser-session-handoff-9` manual E2E gate
- **Expected**: after the `CdpSession` extraction, launch + cookie-inject + navigate behave identically;
  live DevTools navigation still works
- **Env deps**: local Chromium + a forms-auth env
- **AC**: S1-AC-03 — **blocked on a live env + Chromium (manual; not in CI)**

---

## Regression Guard

Tests that MUST pass after this feature ships:

| Test file | Test(s) | Why at risk |
|-----------|---------|------------|
| `clio.tests/Common/BrowserSession/AuthenticatedBrowserLauncherTests.cs` | all 7 existing (`BuildShellUrl_*`, `BuildSetCookieParams_*`, `LaunchAsync_*`) | Story 1 moves `CdpSendAsync`/`FindPageTargetAsync`/`ReadDevToolsPortAsync` out of the launcher into `CdpSession`; the launcher consumes the new helper — contract/behavior must be unchanged |
| `clio.tests/Command/OpenAppCommandTests.cs` | all | Uses `IAuthenticatedBrowserLauncher`; if `LaunchAsync` returns `LaunchResult{DevToolsPort}` (S1-AC-04), the open-web-app path must keep working |
| `clio.tests/Common/BrowserSession/ChromiumLocatorTests.cs` | all | Shared `IChromiumLocator`/`ChromiumNotFoundException` reused by the new driver (FR-15) |
| `clio.tests/Command/ProcessModel/SchemaTestFixture.cs` | `Should_Parse_*` | Story 3 adds `ResolveDataId` to `Schema.cs`; the existing GUID `managerItemUId` map and `ProcessSchemaResponse.FromJson` parsing must be untouched |
| `clio.tests/Command/McpServer/GenerateProcessModelToolTests.cs` | all | `describe-process` reuses the `ProcessSchemaRequest`/`ProcessModelGenerator` read path; reused unchanged ("MCP reviewed, no update required") |
| `clio.tests/Command/McpServer/McpGuidanceResourceTests.cs` | all | Story 2 adds a `GuidanceCatalog` entry; existing guidance resources/registrations must still resolve |
| `clio.tests/Command/McpServer/GuidanceGetToolTests.cs` | all | `get-guidance` must still resolve existing guidance names and the unknown-name error path |
| Full unit suite | `dotnet test --filter "Category=Unit"` | Stories 1, 4, 6, 7, 8 touch `BindingsModule.cs`/`Program.cs`/`Common/` (DI composition root + shared infra) → smart-testing rule 4 |

---

## Coverage Estimate

| Layer | New tests | Modified tests | Notes |
|-------|-----------|---------------|-------|
| Unit | ~60 across 11 files (CDP 5, launcher 1, guidance 5, map 7, validator 20, vpg 10, driver 8, pae-cmd 5, pae-tool 4, extractor 4, dp-cmd 4, dp-tool 4) | +1 launcher (`DevToolsPort`) | All `[Category("Unit")]`; CI-runnable |
| Integration | 0 | 0 | No I/O between unit and E2E for this feature |
| E2E | 6 cases across 3 new + 1 reused suite | 0 | **Manual only — not in CI**; TC-E2E-VPG-01 automatable in the harness; the rest blocked on live env/Chromium |

---

## Automatable-now vs blocked

| Status | Cases |
|--------|-------|
| **Automatable now (CI unit tier)** | All TC-U-* (CDP, launcher regression, guidance, map, validator R1–R17, vpg, driver with mocked `ICdpSession`, pae command/tool, extractor, dp command/tool) |
| **Automatable in the e2e harness but NOT in CI** | TC-E2E-VPG-01 (`validate-process-graph` over MCP; no env needed) |
| **Blocked on live env + Chromium (manual, not in CI)** | TC-E2E-PAE-01, TC-E2E-PAE-02, TC-E2E-PAE-03 (`krestov-test` + forms-auth + Chromium) |
| **Blocked on live env + a known seeded process (manual, not in CI)** | TC-E2E-DP-01 (`krestov-test`) |
| **Blocked on live env + Chromium (manual regression, not in CI)** | TC-E2E-04 (launcher `--authenticated`) |

---

## Traceability Matrix — FR-01..FR-19

| FR | Description | Test case(s) |
|----|-------------|--------------|
| FR-01 | `process-modeling` resource + `GuidanceCatalog` registration | TC-U-GUI-01, TC-E2E-VPG-01 (catalog reachability) |
| FR-02 | Guidance consolidates catalog + R1–R17 + recipe; no-LLM statement | TC-U-GUI-02 |
| FR-03 | Guidance: validate-before-drive; `.djs-validate-outline` final authority | TC-U-GUI-03 |
| FR-04 | Guidance scopes to the supported slice | TC-U-GUI-04 |
| FR-05 | `IProcessGraphValidator`/`ProcessGraphValidator` (DI, structured findings) | TC-U-VAL-01..20, TC-U-VAL-20 (DI) |
| FR-06 | Reuse `ManagerMap.EventType`/`ResolveDataId` for classification | TC-U-MAP-01..07, TC-U-VAL-19, TC-U-EXT-01 |
| FR-07 | Errors R1/R2/R3/R10/R11/R13/R14/R15; warnings R7/R9/R12/R17 | TC-U-VAL-02..17 |
| FR-08 | `validate-process-graph` MCP tool (ReadOnly, non-env, direct injection) | TC-U-VPG-01..10 |
| FR-09 | CDP-driven driver reusing the launcher; CDP plumbing behind an interface | TC-U-CDP-01..06, TC-U-DRV-01..08 |
| FR-10 | Slice = Read data only (overlay-select, append, configure object, SAVE) | TC-U-DRV-02..03, TC-U-CMD-PAE-02, TC-E2E-PAE-01 |
| FR-11 | Kebab-case CLI inputs (`--element-type`/`--read-object`/`--process-id`) | TC-U-CMD-PAE-* , TC-U-TOOL-PAE-02 |
| FR-12 | `process-add-element` MCP tool, Destructive semantics, env-aware path | TC-U-TOOL-PAE-03, TC-U-TOOL-PAE-04 |
| FR-13 | Pre-SAVE validate (abort on error) + `.djs-validate-outline` gate | TC-U-CMD-PAE-01 (a), TC-U-DRV-04 (b), TC-E2E-PAE-03 |
| FR-14 | Deterministic save detection + saved identity; never false success | TC-U-DRV-07, TC-U-DRV-08, TC-U-CMD-PAE-05, TC-E2E-PAE-02 |
| FR-15 | User-friendly `Error:` per failure class | TC-U-DRV-05, TC-U-CMD-PAE-02, TC-U-CMD-PAE-04 |
| FR-16 | Unit tests (validator, arg-mapping, guidance, extractor) | all TC-U-* |
| FR-17 | MCP E2E for all three tools (not in CI) | TC-E2E-VPG-01, TC-E2E-PAE-01..03, TC-E2E-DP-01 |
| FR-18 | Docs + `McpCapabilityMap.md` for the three tools | Doc review (manual) per PR checklist; safety flags asserted by TC-U-VPG-08, TC-U-TOOL-PAE-04, TC-U-TOOL-DP-03 |
| FR-19 | `describe-process` structured graph from reused parsing | TC-U-EXT-01..04, TC-U-CMD-DP-01..04, TC-U-TOOL-DP-01..04, TC-E2E-DP-01 |

## Traceability Matrix — Acceptance Criteria (PRD AC-01..AC-13, AC-ERR)

| AC | Description | Test case(s) |
|----|-------------|--------------|
| AC-01 | `get-guidance --name process-modeling` returns consolidated guidance + no-LLM statement | TC-U-GUI-01, TC-U-GUI-02 |
| AC-02 | Valid `Start → Read data → End` → zero `error` findings | TC-U-VAL-01, TC-U-VPG-03, TC-E2E-VPG-01 |
| AC-03 | Start with incoming flow → `error` R1 | TC-U-VAL-02, TC-U-VPG-04, TC-E2E-VPG-01 |
| AC-04 | Default flow without sibling conditional → `error` R14 | TC-U-VAL-12, TC-U-VPG-05 |
| AC-05 | Orphan node that cannot reach an end → `error` R15 | TC-U-VAL-14, TC-U-VPG-06 |
| AC-06 | Designer-accepted graph → no `error` (R12/R17 warning permitted) — no false positives | TC-U-VAL-18, TC-U-VPG-07 |
| AC-07 | `process-add-element --read-object Contact` opens designer, appends, configures, SAVEs, reports identity | TC-U-CMD-PAE-05, TC-E2E-PAE-01 |
| AC-08 | Readback: `generate-process-model --code` exit 0 + `VwProcessLib` row via `execute-esq` | TC-E2E-PAE-02 |
| AC-09 | `.djs-validate-outline` flagged → no SAVE, no success, `Error:` | TC-U-DRV-04, TC-E2E-PAE-03 |
| AC-10 | Validator error on planned graph → abort before opening a browser | TC-U-CMD-PAE-01 |
| AC-11 | `McpCapabilityMap.md` lists `validate-process-graph` + `process-add-element` with correct flags | Doc review (PR checklist) + TC-U-VPG-08, TC-U-TOOL-PAE-04 |
| AC-12 | `dotnet test --filter "Category=Unit&Module=Command"` (and `Module=McpServer`) — validator/arg/guidance unit tests pass | all TC-U-* (gated by the targeted + full-suite filters above) |
| AC-13 | `describe-process` returns structured `elements`/`flows`/`parameters` labelled with the `data-id` vocabulary | TC-U-EXT-01..04, TC-E2E-DP-01 |
| AC-ERR | No forms-auth / Chromium missing (`process-add-element`) **and** process not found (`describe-process`) → specific `Error:`, non-zero exit, no partial output | TC-U-CMD-PAE-04, TC-U-DRV-05, TC-U-CMD-DP-03 |

> NFR coverage cross-reference: NFR-01/02 → TC-U-CMD-PAE-04; NFR-03 → TC-U-DRV-05; NFR-04 →
> TC-U-DRV-04/05/08, TC-E2E-PAE-03; NFR-05 → TC-U-DRV-04 + TC-E2E-PAE-02 (readback gate); NFR-06 →
> TC-U-CDP-04; NFR-07 → TC-U-DRV-06. NFR-06 "no LLM call" is verified by manual E2E network inspection
> (not an automated assertion).

---

## Definition of Done for QA

- [ ] All TC-U-* implemented with `[Category("Unit")]` — NOT `[Category("UnitTests")]`
- [ ] Every unit test has `[Property("Module", "<trait>")]`, explicit AAA, a `because` on every assertion, and a `[Description]`
- [ ] Command tests use `BaseCommandTests<ProcessAddElementOptions>` / `BaseCommandTests<DescribeProcessOptions>` (SUT resolved from DI; `ClearReceivedCalls` in teardown)
- [ ] Validator has one+ test per R-rule error (R1, R2, R3, R10, R11, R13, R14, R15) and per warning (R7/R9, R12, R17), plus the no-false-positive guard and the `Unknown` data-id case
- [ ] `describe-process` extractor tests reuse `clio.tests/Examples/ProcessSchema/*.json` fixtures
- [ ] Regression guard tests green (launcher, open-app, chromium-locator, schema parser, generate-process-model tool, guidance)
- [ ] No Integration-tier cases needed (documented); UI behavior covered at E2E
- [ ] All MCP E2E cases documented and added to the PR checklist as a **manual execution gate** (not in CI)
- [ ] Test naming follows `MethodName_ShouldBehavior_WhenCondition`
- [ ] Targeted `dotnet test --filter` recorded in each PR; **full unit suite** run for Stories 1, 4, 6, 7, 8 (0 new failures)
- [ ] PR includes the test files in the changed-files list

## PR Checklist — Manual E2E Gate (copy into the PR)

- [ ] TC-E2E-VPG-01 executed (real MCP path; no env)
- [ ] TC-E2E-PAE-01 executed on `krestov-test` (Chromium headed + forms-auth)
- [ ] TC-E2E-PAE-02 readback verified (`generate-process-model` exit 0 + `VwProcessLib` row)
- [ ] TC-E2E-PAE-03 no-false-save gate verified
- [ ] TC-E2E-DP-01 executed against a known process on `krestov-test`
- [ ] TC-E2E-04 launcher `--authenticated` regression verified (Story 1)
- [ ] Manual network inspection: clio made **zero** third-party LLM/model calls (NFR-06)
