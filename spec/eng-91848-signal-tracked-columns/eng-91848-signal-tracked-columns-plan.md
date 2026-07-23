# ENG-91848 — Signal start: tracked-change columns (implementation plan)

- **Jira:** [ENG-91848](https://creatio.atlassian.net/browse/ENG-91848) — *Signal start element: usability status + tracked-change columns* (Story, component `bpms tools`, priority Major).
- **Parent research:** ENG-90883 (backend process designer). Source: Task 10 of *Add business process generation via AI instructions*.
- **Status:** planned. Awaiting implementation.
- **Repos involved:** `clioprocessbuilder` (Creatio package — server logic) at `C:\Projects\workspace\ProcessBuilder`, and `clio` (MCP surface + read DTO + guidance) at `C:\Projects\clio`.

---

## 0. Implementation status (2026-07-23)

Implemented on branch `feature/ENG-91848-signal-tracked-columns` in **both** repos (D1 = Option A, in-place `setSignal`). Not yet committed (awaiting approval).

- **Server (`clioprocessbuilder`)**: contracts + `SignalTriggerBinder` + handler create/describe + `setSignal` op/applier + DI. **411 unit tests pass** (net472).
- **clio**: `DescribedSignal.ChangedColumns` + tool descriptions + `process-modeling` guidance + prompts; guidance test repurposed, 2 describer tests, 2 E2E round-trips (compile clean).
- **Gates**: regression green both repos; ClioRing — no Ring-consumed contract changed; 3-lens adversarial review ran — no Blocker/High, all Medium + worthwhile Low fixed (setSignal `on`-omission now preserves the current change type; entity-retarget clears a stale filter; describe gated on the `modified` token; UId de-dup; doc alignment).
- **Pending**: deploy the rebuilt package to a stand + run the two E2E round-trips (the authoritative persist→reload check); commit + PRs (needs approval); final ready-to-merge review gate.

## 1. Background and the one architectural fact that shapes the work

The signal-start feature is split across two repos:

- **`clioprocessbuilder`** is a Creatio package exposing `ProcessDesignService` (WCF/REST at `/rest/ProcessDesignService/*`). It owns the C# DataContracts (`ProcessElementDescriptor`, `ProcessSignalDescriptor`, `DescribeSignalInfo`, `ModifyProcessRequest`/operations) and *all* real logic: JSON deserialization, validation, name→UId resolution, schema mutation, describe read-back.
- **`clio`** owns the MCP tools (`create-business-process`, `modify-business-process`, `describe-business-process`) and the agent-facing guidance/prompts.

**Decisive fact:** clio's *write path is opaque JSON passthrough.* The MCP tools accept the element/operation descriptor as a raw JSON string and forward it verbatim (wrapped under `["request"]`, with an optional `packageName` override). There is **no clio-side C# mirror of `ProcessSignalDescriptor` on the write side** — `signal` / `entity` / `on` / `changedColumns` reach the server only as text, described for the agent purely in the tool `[Description]` prose and the guidance resource.

Consequence: the *serialization and validation* of the new field live entirely in `clioprocessbuilder`. clio's write-side changes are **prose** (tool descriptions, guidance, prompts). Only the **read-back** path has a clio C# DTO (`DescribedSignal`) that gains a real field.

---

## 2. Scope

**In scope (the only unbuilt piece):** restrict an "on modify" signal to fire only when specific columns change — i.e. set `HasEntityColumnChange` + `EntityChangedColumns` from an agent-supplied list of column names, allow changing them on an existing signal, and read them back via describe.

**Already done / out of scope:**
- Signal start create, replace-of-existing-start, describe round-trip, correct designer display — working and verified live per the ticket.
- Record **filters** (`EntityFilters`) — the ticket defers them to "Task 4", and `SignalStartFilterTarget.cs` already implements them for signals.
- **Multiple / combined triggers** — struck through in the AC; not in scope.

**Target end state (contract):**
```jsonc
"signal": {
  "entity": "Order",
  "on": "modified",                        // columns are valid ONLY here (Updated)
  "changedColumns": ["Amount", "StatusId"] // column NAMES; omitted/empty = fire on any change
}
```
Rationale for the shape:
- Column **names** (not UIds) mirror the existing `entity`(name) → `entitySchemaUId`(UId) split — agent-friendly and consistent.
- **Presence-based** (non-empty list ⇒ track those columns; omitted/empty ⇒ any change). This mirrors the designer's `ExpectChanges = AnySelectedField` with fallback to `AnyField` when no column is chosen, so no separate boolean is needed on the wire.
- **Modify-only:** tracked columns are meaningful only when `on` resolves to `Updated`; reject them otherwise (the designer only shows "expect changes" for the Updated change type).

---

## 3. Correctness anchors (verified in platform + package source — do not re-derive)

1. **Naming trap.** In `Terrasoft.Core/Process/ProcessSchemaStartSignalEvent.cs`, the C# property literally named `NewEntityChangedColumns` is a **stray bool**. The real column list is `EntityChangedColumns` (`Collection<string>`), serialized under the meta slot whose *design name* is (confusingly) "NewEntityChangedColumns" = `DZ12`. **Work against `EntityChangedColumns`.**
2. **Column identifier format = bare column UId string** (`Guid.ToString()`, lowercase, dashed, no braces). Confirmed three ways:
   - Platform unit spec `process-start-signal-schema.unit.spec.js` serializes `Collection[[System.Guid]]` with `$values:["dbbf0e10-…","e07f0e4a-…"]`.
   - `DcmSchema.cs:376` does `signal.EntityChangedColumns.Add(StageColumnUId.ToString())`.
   - `ProcessSchemaStartSignalEvent.AnalyzePackageDependencies` feeds the collection to `SchemaColumnsLocator.CreateFromMetaPaths` (there "meta path" is just how the dependency reporter tags the raw UId strings).
3. **Design → runtime bridge.** On save, `BaseProcessSchemaManager.AssignEntityStartEvent` writes the runtime subscription as:
   `sysEntityPrcStartEvent.ChangedColumns = HasEntityColumnChange ? JsonConvert.SerializeObject(EntityChangedColumns, Formatting.None) : string.Empty;`
   So `Guid.ToString()` per column is exactly right, and the columns matter only when `HasEntityColumnChange` is true (else the runtime persists `""` and fires on any change).
4. **Designer parity.** `BaseSignalEventPropertiesPage.js` sets `hasEntityColumnChange = (expectChanges === AnySelectedField)` and `newEntityChangedColumns = [control.get("Id"), …]` (column UIds), and only exposes "expect changes" when `EntityChangeType === Updated` — i.e. our modify-only rule matches the UI.

---

## 4. Decision record

### D1 — how to "change columns on an existing signal": **DECIDED = Option A (in-place `setSignal` operation)**

The modify flow is operation-based (`ProcessOperationExecutor`). "Replace start with signal" = `removeElement` + `addElement(signalStart)` + `addFlow`, and `addElement` routes back through the same `SignalStartElementHandler.Create`. So threading `changedColumns` through the `signal` descriptor already covers **create** and **replace-via-modify**. What it does *not* cover is changing columns on an *existing* signal element without recreating it.

- **Chosen — Option A: a new in-place `setSignal` operation**, mirroring the existing `setFilter`/`clearFilter` pair. It finds the `ProcessSchemaStartSignalEvent` by name and updates its change type + tracked columns in place, preserving the element UId and its flows. The ticket explicitly requires "allow changing them on an existing signal," and filters set the exact precedent.
- Rejected — Option B (replace-only): no new operation, but changing columns loses element identity and re-wires flows. Kept here only as the fallback rationale.

### D2 — contract field name & semantics: `changedColumns: string[]` of column **names**, presence-based, **modify-only**. (See §2.)

### D3 — validation strictness: **reject** `changedColumns` when `on` ≠ `modified`, and **reject** an unknown column name, with a clear `ArgumentException` (fail loud, do not silently ignore). Matches the "fail loud on caller mistake" style already used by `SignalStartFilterTarget`.

---

## 5. Work breakdown — Repo A: `clioprocessbuilder` (the real logic)

All paths under `C:\Projects\workspace\ProcessBuilder\packages\clioprocessbuilder\Files\src\cs\`.

**A1. Contracts**
- `Contracts/ProcessDescriptorContracts.cs` — `ProcessSignalDescriptor` += `[DataMember(Name="changedColumns")] public string[] ChangedColumns { get; set; }`, with XML docs stating names + modify-only + presence semantics.
- `Contracts/DescribeContracts.cs` — `DescribeSignalInfo` += `[DataMember(Name="changedColumns")] public string[] ChangedColumns`.
- `Contracts/ModifyContracts.cs` — `ProcessOperationDescriptor` += `[DataMember(Name="signal")] public ProcessSignalDescriptor Signal` for the `setSignal` op; extend the op list in the class `[DataContract]` summary and the executor error message.

**A2. Element handler** — `Elements/SignalStartElementHandler.cs`
- `Create`: after setting `EntitySignal`, if `descriptor.Signal.ChangedColumns?.Length > 0`:
  - validate the resolved `EntitySignal == EntityChangeType.Updated` (else throw — "tracked columns require `on: modified`");
  - resolve each column **name → UId** on the trigger entity;
  - set `HasEntityColumnChange = true` and populate `((ProcessSchemaStartSignalEvent)el).EntityChangedColumns` with `col.UId.ToString()`.
- `Describe`: emit `changedColumns` by mapping stored UIds → names. Needs the `EntitySchema` instance (via `EntitySchemaManager.FindInstanceByUId` — `Find*` returns null on miss per platform semantics), not just the manager item currently used for the entity name.
- Refactor `ResolveEntitySchemaUId` to also expose the resolved `EntitySchema` (reuse `.Columns` in both directions). Add `ResolveColumnUId(EntitySchema, name)` / `ResolveColumnName(EntitySchema, uid)` helpers that throw a clear error on an unknown column.

**A3. In-place `setSignal` (Option A)**
- New `Operations`/service seam (e.g. `IProcessSignalApplier` + `ProcessSignalApplier`) that, given `schema`, `elementName`, and a `ProcessSignalDescriptor`, finds the `ProcessSchemaStartSignalEvent` (via `ProcessSchemaElementLocator`) and updates `EntitySignal` + `HasEntityColumnChange`/`EntityChangedColumns` (and optionally the entity) in place. Reuse the same name→UId helpers from A2 (extract to a shared collaborator so `Create` and `setSignal` don't drift).
- `Operations/ProcessOperationExecutor.cs` — add a `setsignal` case dispatching to the new applier; require `ElementName` + `Signal`.
- `ClioProcessBuilderApp.cs` — register the new applier in the composition root.

**A4. Server tests** (`C:\Projects\workspace\ProcessBuilder\tests\clioprocessbuilder\`; NUnit + FluentAssertions, `[Description]`, `because:`, `UserConnection` fixture with substituted `EntitySchemaManager`):
- `ProcessElementHandlerTests.cs` — Create with columns sets `HasEntityColumnChange` + UIds; rejects columns when `on != modified`; rejects an unknown column. Arrange a substituted `EntitySchema.Columns` for name→UId.
- `ProcessElementHandlerDescribeTests.cs` — Describe maps stored UIds → names; empty/absent when `HasEntityColumnChange == false`.
- `ProcessOperationExecutorTests.cs` — `setSignal` updates an existing signal in place (change type + columns), preserving UId.
- `ProcessDesignerRoundTripTests.cs` — full create → describe columns round-trip.

**A5. Build & deploy**
- Bump package version (`set-pkg-version`), `compress` to the shipped `.gz`, `push-pkg` to the target stand, verify with `list-packages`.
- ⚠️ On the .NET Framework stand, run schema-write MCP operations **sequentially** — a parallel burst trips IIS rapid-fail and downs the app pool.

---

## 6. Work breakdown — Repo B: `clio` (MCP surface + read DTO + guidance)

**B1. Read-back DTO** — `clio/Command/ProcessModel/IProcessDescriber.cs`
- `DescribedSignal` (around `:226`) += `[JsonPropertyName("changedColumns")] public List<string> ChangedColumns { get; set; }`. Surfaces automatically in `describe-business-process` output (`DescribeProcessResult` is serialized verbatim with `WhenWritingNull`, so it stays absent when null).

**B2. Tool descriptions (the write-side contract is prose here)**
- `clio/Command/McpServer/Tools/ProcessDesigner/CreateBusinessProcessTool.cs` (~`:43-46`) — extend the `signal:{entity, on}` shape to `signal:{entity, on, changedColumns?}` and state the modify-only rule.
- `clio/Command/McpServer/Tools/ProcessDesigner/ModifyBusinessProcessTool.cs` (~`:33`, and the op list ~`:45-49`) — document `changedColumns` in `addElement`'s signal, and add the new `setSignal` operation.
- `clio/Command/McpServer/Tools/ProcessDesigner/DescribeProcessTool.cs` (~`:29`) — mention that tracked columns are read back.

**B3. Guidance** — `clio/Command/McpServer/Resources/ProcessDesigner/ProcessModelingGuidanceResource.cs`
- **`:92-96`** — rewrite the "tracked-change columns are not buildable yet / column-level restriction cannot be built yet" passage into the now-supported capability (with the modify-only constraint and the `changedColumns` example).
- `:87` — update the signal element shape; keep the filter section (`:107-171`, incl. the signal-start restriction) coherent.
- `:47` — the element-catalog line may be lightly updated.

**B4. Prompts** — `clio/Command/McpServer/Prompts/ProcessDesigner/`
- `CreateBusinessProcessPrompt.cs`, `ModifyBusinessProcessPrompt.cs`, `DescribeProcessPrompt.cs` — add a `changedColumns` example and (modify) a `setSignal` example.

**B5. clio tests**
- ⚠️ **Must update** `clio.tests/Command/McpServer/ProcessModelingGuidanceResourceTests.cs` (~`:160-172`, `GetGuide_ShouldDiscloseSignalTriggerLimits_WhenRead`) — it pins the exact "column-level restriction cannot be built yet" / "ANY field change" wording; flip it to assert the new capability text.
- `clio.tests/Command/ProcessModel/ServerProcessDescriberTests.cs` — add a test asserting `DescribedSignal.ChangedColumns` deserializes (no current test pins the `signal` object directly).
- **E2E (mandatory per AGENTS.md MCP policy)** — `clio.mcp.e2e/CreateBusinessProcessToolE2ETests.cs` + `clio.mcp.e2e/ModifyBusinessProcessToolE2ETests.cs`: build a `signalStart` with `changedColumns` and read it back; add a `setSignal` round-trip on modify.

**B6. Targeted regression** before commit (smart-testing policy): `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&(Module=Command|Module=McpServer)" --no-build`.

---

## 7. Cross-cutting gates (AGENTS.md)

- **MCP review:** covered — tool descriptions, prompts, guidance, unit + E2E all in scope. State "MCP reviewed" per touched tool in the PR.
- **ClioRing compatibility gate:** the change is *additive* (one optional descriptor field + one optional new operation) on MCP passthrough tools. Still required: verify Ring does not pin the describe schema, run `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release`, run the Windows x64 NativeAOT publish, and state `ClioRing compatibility reviewed` + results (or `…no Ring-consumed contract changed` with inspected paths) in the PR.
- **Docs:** these tools are MCP-only (no CLI `-H` / GitHub docs). Agent-facing docs *are* the tool descriptions + guidance (B2/B3). State "docs reviewed, no CLI/GitHub doc update required" in the PR.
- **Feature gate:** all three tools + prompts + guidance are gated by `process-designer` (default `true` in `clio/appsettings.json`). No toggle change.
- **Code review:** run the pre-PR comprehensive parallel review, and the final ready-to-merge review, per policy.

---

## 8. Test matrix (summary)

| Layer | File | New/updated coverage |
|---|---|---|
| Server unit | `ProcessElementHandlerTests.cs` | Create sets flag+UIds; reject non-modified; reject unknown column |
| Server unit | `ProcessElementHandlerDescribeTests.cs` | Describe UIds→names; empty when flag false |
| Server unit | `ProcessOperationExecutorTests.cs` | `setSignal` in-place update |
| Server unit | `ProcessDesignerRoundTripTests.cs` | create→describe columns round-trip |
| clio unit | `ProcessModelingGuidanceResourceTests.cs` | **update** pinned "not buildable" wording |
| clio unit | `ServerProcessDescriberTests.cs` | `DescribedSignal.ChangedColumns` deserialization |
| clio E2E | `CreateBusinessProcessToolE2ETests.cs` | build signalStart + changedColumns, describe read-back |
| clio E2E | `ModifyBusinessProcessToolE2ETests.cs` | `setSignal` round-trip |

---

## 9. Estimate & sequencing

Ticket estimate ~1.5 days; realistic with Option A + full E2E: **~2 days**.

Order:
1. A1 → A2 → A4 (server contract + handler logic + unit tests)
2. A3 (in-place `setSignal` + executor + DI + unit test)
3. A5 (version bump, compress, deploy to stand, smoke)
4. B1 → B3 (read DTO + tool prose + guidance + prompts)
5. B5 (clio unit + E2E round-trips)
6. Gates: targeted regression, MCP review, ClioRing compatibility, pre-PR + final code review.
