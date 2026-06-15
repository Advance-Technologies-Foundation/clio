# Story 6: IProcessDesignerDriver + ProcessDesignerDriver + Embedded Read-data Recipe + DTOs

**Feature**: ai-business-process-generation
**FR coverage**: FR-09 (driver portion), FR-10, NFR-03, NFR-05, NFR-06, NFR-07
**PRD**: [prd-ai-business-process-generation.md](../prd/prd-ai-business-process-generation.md)
**ADR**: [adr-ai-business-process-generation.md](../adr/adr-ai-business-process-generation.md) (Decision 3 / Decision D2, "Driver recipe steps", "JS-recipe storage")
**Status**: ready-for-dev
**Size**: L (full day)

---

## As a

clio developer (the driver layer consumed by `process-add-element`)

## I want

a sibling `IProcessDesignerDriver`/`ProcessDesignerDriver` that runs the proven Read-data PoC recipe over the existing CDP page session (`ICdpSession.EvaluateAsync`), with the JS recipe shipped as a versioned embedded resource and read through a thin `ProcessDesignerRecipes` accessor

## So that

`process-add-element` (Story 7) gets a clean, MCP/CLI-agnostic orchestration boundary that opens/creates the designer, appends a Read data element, configures the source object, sets a caption, asserts no `.djs-validate-outline`, SAVEs, and reads back identity — reusing the channel proven on `krestov-test` / `UsrProcess_493d4c9`

---

## Acceptance Criteria

- [ ] **AC-01** — Given an already-launched authenticated browser on a known `DevToolsPort`, when `AddReadDataElementAsync(request)` runs with `ProcessId = null`, then it opens/creates a new designer (`OpenOrCreateDesignerAsync`) bootstrapping `Start → End`, and with a `ProcessId` set it opens that existing process. (FR-10 step 1, OQ-01)
- [ ] **AC-02** — Given the designer is opening, when the driver waits, then it polls `.djs-shape` (or `.entry[data-action="create-serviceTask"]`) with a bounded timeout and dismisses any stray `.djs-popup.diagram-create-popup-menu` via Escape before interacting. (NFR-03)
- [ ] **AC-03** — Given a rendered Start shape, when the driver selects it, then it uses the `pointer-events:none` overlay + trusted-click trick, then appends via `.djs-context-pad .entry[data-action="add.serviceTask"]` (defaults to `readDataUserTask`, auto-inserts onto the `Start→End` flow → `Start → Read data → End`). (FR-10 steps 3–4, A-01)
- [ ] **AC-04** — Given the Read data setup card, when the driver configures it, then it sets **only** the "Which object to read data from?" lookup to `request.ReadObject` and picks the matching `.listview` option; it does **not** touch read-mode/filter/columns. (FR-10 step 5, OQ-02)
- [ ] **AC-05** — Given the caption step, when the driver runs, then it sets the process caption to `request.Caption` (the deterministic readback handle). (FR-10 step 6, OQ-04)
- [ ] **AC-06** — Given the append/auto-connect completed, when the driver checks the connection, then it asserts the connection element does **not** carry `.djs-validate-outline`; if present it aborts (no SAVE) and returns a failure `ProcessAddElementResult` naming the invalid connection. (FR-13b, AC-09, NFR-05)
- [ ] **AC-07** — Given SAVE is clicked, when the driver detects the result, then it confirms the platform "Successfully saved" signal AND reads back `{Code (Name), UId, Caption}` from the page; it returns `Success = true` only on a real save signal. (FR-14)
- [ ] **AC-08** — Given a missing save signal, a CDP error, or a flagged invalid connection, when the driver finishes, then it returns `Success = false` with a user-friendly `Error:` and never reports a false-positive save. (FR-14, NFR-04)
- [ ] **AC-09** — Given the recipe is parameterised, when the driver injects the source object and caption, then they are passed as **JSON-escaped** values (never raw string-concatenated) for JS-injection hygiene. (Decision D2, NFR-07)
- [ ] **AC-10** — Given the embedded recipe, when `ProcessDesignerRecipes.Get("read-data-element")` is called, then it reads `read-data-element.recipe.js` from the assembly manifest stream once (cached). (Decision D2)
- [ ] **AC-ERR** — Given the canvas never renders within the timeout, when the driver runs, then it fails fast with a designer-never-rendered `Error:` (no stack trace) and leaves no saved partial process. (NFR-03, NFR-04)

## Implementation Notes

The driver owns **recipe orchestration only**; it knows nothing about MCP or CLI options. It executes JS via `ICdpSession.EvaluateAsync` (Story 1) using the embedded recipe. CDP egress is loopback-only (`127.0.0.1`); cookie values are never echoed by the recipe (NFR-06/07). The recipe encodes ui-playbook §6 steps 1–10 (see ADR "Driver recipe steps").

Contracts (ADR "Key interfaces"):
```csharp
public interface IProcessDesignerDriver {
    Task<ProcessAddElementResult> AddReadDataElementAsync(ProcessAddElementRequest request, CancellationToken ct = default);
}
public sealed record ProcessAddElementRequest(EnvironmentSettings Environment, int DevToolsPort,
    string? ProcessId, string ReadObject, string Caption);
public sealed record ProcessAddElementResult(bool Success, string? Code, string? UId, string Caption, string? Error);
```

Files to create:
- `clio/Common/ProcessDesigner/IProcessDesignerDriver.cs`
- `clio/Common/ProcessDesigner/ProcessDesignerDriver.cs`
- `clio/Common/ProcessDesigner/ProcessAddElementRequest.cs` / `ProcessAddElementResult.cs` (records — DTOs)
- `clio/Common/ProcessDesigner/Recipes/read-data-element.recipe.js` (embedded; ui-playbook §6 steps 1–5 verbatim)
- `clio/Common/ProcessDesigner/ProcessDesignerRecipes.cs` (thin cached accessor)

Files to modify:
- `clio/clio.csproj` — `<EmbeddedResource Include="Common/ProcessDesigner/Recipes/read-data-element.recipe.js" />`
- `clio/BindingsModule.cs` — `services.AddTransient<IProcessDesignerDriver, ProcessDesignerDriver>();`

Feasibility reference (do not re-prove the channel — it is LOCKED): env `krestov-test`, process `UsrProcess_493d4c9` ("AI PoC Read Contact"), `spec/ai-business-process-generation/ai-bp-ui-playbook.md` §6. Depends on `ICdpSession` (Story 1). `BindingsModule.cs`/`Common/` changed → **full unit suite trigger**.

## Test Requirements

| Type | What to test | File |
|------|-------------|------|
| Unit `[Category("Unit")]` | `ProcessDesignerRecipes.Get` reads + caches the embedded recipe; parameters are JSON-escaped (injection hygiene); driver returns `Success=false` with `Error:` when `ICdpSession` reports the canvas never rendered / a flagged `.djs-validate-outline` / a missing save signal (mock `ICdpSession`); `Success=true` + identity on a save signal | `clio.tests/Common/ProcessDesigner/ProcessDesignerDriverTests.cs` |

Test naming: `MethodName_ShouldBehavior_WhenCondition` (AAA + `because` + `[Description]`). The live build is covered by Story 7's E2E (driver alone is unit-tested with a mocked `ICdpSession`).

## Definition of Done

- [ ] Code compiles without Roslyn analyzer warnings (CLIO001-CLIO004)
- [ ] `IProcessDesignerDriver` registered in `BindingsModule.cs` (no `new`, no MediatR)
- [ ] JS recipe shipped as a versioned `<EmbeddedResource>`; parameters JSON-escaped, not concatenated
- [ ] Driver knows nothing about MCP/CLI; CDP loopback-only; cookie values never echoed
- [ ] Never reports a false-positive save (no save signal / CDP error / `.djs-validate-outline` → failure)
- [ ] Public API documented with `///` XML doc comments
- [ ] Unit tests added with `[Category("Unit")]` — never `[Category("UnitTests")]`
- [ ] Full unit suite run (BindingsModule.cs/Common/ changed): `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit"` — 0 new failures
- [ ] PR description references this story file

## Dev Agent Record

- Implementation started: 2026-06-12
- Implementation completed: 2026-06-12
- Tests passing: yes — `ProcessDesignerDriverTests` (8: happy path, never-rendered, invalid-connection abort-without-save, no-save-signal, append-fail, JSON-escaped object, recipe cache, BuildDesignerUrl). Full unit suite 3896 passed, 0 failed, 20 skipped.
- Notes: Created `IProcessDesignerDriver` (+ `ProcessAddElementRequest`/`Result` records), `ProcessDesignerDriver`, `ProcessDesignerRecipes` (cached embedded-resource accessor; suffix-match on the manifest name), and `Recipes/read-data-element.recipe.js` (phase-based: prepare/append/fillObject/setCaption/checkValid/saveCoords/saveResult). Registered the recipe as `<EmbeddedResource>` in clio.csproj. Driver mixes TRUSTED CDP `Input.dispatchMouseEvent` (select Start / pick dropdown option / SAVE) with the QA-proven UNTRUSTED append drag via `ICdpSession.EvaluateAsync`; params JSON-escaped (AC-09); aborts without SAVE on `.djs-validate-outline`; never reports a false-positive save (requires the "Successfully saved" signal). Internal ctor exposes a short render-timeout test seam. DI via auto-registration (no explicit BindingsModule line). The recipe's LIVE correctness is deferred to Story 7's E2E on krestov-test (channel LOCKED, not re-proven here). Built/tested in Release (clio MCP server locks bin/Debug).
