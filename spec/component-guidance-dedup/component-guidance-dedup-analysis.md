# Component & Composite Guidance Deduplication — Analysis

> Refactor (NOT a fix): remove legacy/duplicated *descriptive* information about Freedom UI
> **components** (`crt.<Name>`) and **composites** (named multi-part recipes, e.g. "Expanded list")
> from clio prose/guidance/docs. The single source of truth is `componentRegistry.json`, served by
> the `get-component-info` MCP tool (generated from the per-component `.md` files in
> `C:\Projects\creatio-ui`). Where a removed fact is genuinely useful, move it into the corresponding
> component `.md` in `creatio-ui` instead of keeping it in clio.

## Locked scope (decisions)

- **Field-control enum** in `PageModificationGuidanceResource.cs` (DataValueType→control) — **KEEP** (clio-owned page-authoring, not a component description). Out of scope.
- **`crt.IFrame` in `ComposableAppSkillGuidanceResources.cs`** — **OUT OF SCOPE** (separate composable-app skill body; different owner/risk). Not touched here.
- **`creatio-ui` `.md`** — when a removed fact concerns a **concrete component**, ADD it to that component's `.md` in `C:\Projects\creatio-ui`. Non-component-specific facts stay out of creatio-ui.

In-scope files: `RelatedListGuidanceResource.cs`, `MobilePageGuidanceResource.cs`, `McpServerInstructions.cs`,
`docs/commands/get-component-info.md`, `Prompts/EntitySchemaPrompt.cs`, plus verify pointers in
`AppModelingGuidanceResource.cs` / `DashboardGuidanceResource.cs` / `IndicatorWidgetGuidanceResource.cs`.

## Guiding rule for classification

| Keep in clio (clio-owned) | Remove / move to registry (`creatio-ui` `.md`) |
|---|---|
| Master-detail **wiring** (`modelConfig.dependencies`, `attributePath`/`relationPath`) | Component **structure / sub-parts / properties** (`inputs`/`outputs`) |
| Page-schema authoring: handlers, converters, validators, requests (`crt.*Request`) | Composite **assembly recipes** (which components combine into "Expanded list", etc.) |
| Container **slot-init** rules (`"items": []`, `"tools": []`), write modes, bundle.json | Component **when-to-use / selection** prose (now `whenToUse`/`synonyms` in registry) |
| Workflow guidance: "call `get-component-info` to discover/fetch …" | **Hard-coded enumerations** of component types or composite names |

`crt.*Request`, `crt.EntityDataSource`, converters (`crt.ToBoolean`…), and validators (`crt.Required`…)
are **NOT** registry visual components — leave them alone.

## Authoritative source set (for test scoping)

- Web registry: **200** component types (snapshot fixture `ComponentRegistry.live-snapshot.json`).
- Composites: resolved from the live registry envelope (`envelope.Composites`) via
  `get-component-info composite="<caption>"` — empty in the pinned snapshot.
- Mobile registry: separate `MobileComponentRegistry.json` (`schema-type=mobile`).

---

## Table 1 — Composites whose info is duplicated in clio

| Composite | clio locations | Severity | What is duplicated | Test after refactor |
|---|---|---|---|---|
| **Expanded list** | `RelatedListGuidanceResource.cs` (full recipe, lines ~46-61, 142-168); `McpServerInstructions.cs:88`; `docs/commands/get-component-info.md:28`; `AppModelingGuidanceResource.cs:120`; `PageModificationGuidanceResource.cs` (~74,150); `MobilePageGuidanceResource.cs:53` | **HIGH** | Full multi-part assembly recipe (ExpansionPanel + grid wrapper + DataGrid + toolbar), `crt.CreateExpandedDataGridItemCommand`, slot rules | `get-component-info composite="Expanded list"`; related-list E2E; verify guidance still routes to tool |
| **Attachments** | `McpServerInstructions.cs:88`; `get-component-info.md:28` | LOW | Name appears in a hard-coded example enumeration | `get-component-info composite="Attachments"` returns recipe |
| **Next steps** | `McpServerInstructions.cs:88`; `get-component-info.md:28` | LOW | Hard-coded enumeration | composite list mode returns it |
| **Approval list** | `McpServerInstructions.cs:88` | LOW | Hard-coded enumeration | composite list mode returns it |
| **Communication options** | `McpServerInstructions.cs:88` | LOW | Hard-coded enumeration | composite list mode returns it |

---

## Table 2 — Components whose descriptive info is duplicated in clio

| Component | Flavor | clio locations | Severity | What is duplicated | Test after refactor |
|---|---|---|---|---|---|
| **crt.DataGrid** | web | `RelatedListGuidanceResource.cs` (structure, `features.editable.*`, columns); `PageModificationGuidanceResource.cs` (field-matching) | **HIGH** | Grid structure, editable/itemsCreation behaviour, column wiring | `get-component-info crt.DataGrid` carries `inputs`/`documentation`; related-list E2E |
| **crt.ExpansionPanel** | web | `RelatedListGuidanceResource.cs` (slots, collapsible frame, header, `tools`/`items` init) | **HIGH** | Panel structure + content-slot init prose | `get-component-info crt.ExpansionPanel`; page still renders after insert |
| **crt.IFrame** | web | `ComposableAppSkillGuidanceResources.cs` (~233,236,2835-3023): properties (`isSandbox`,`sandbox`,`contentType`,`urlContent`), single-iframe rule, JSON examples | **HIGH** | Full property list + assembly + code examples | `get-component-info crt.IFrame`; `integrate-creatio-iframe` skill flow |
| **crt.GridContainer** | web/mobile | `ComposableAppSkillGuidanceResources.cs` (2884,3009); `MobilePageGuidanceResource.cs` (54,242,327-358 — `color:"primary"`, card surface) | MED | Container structure + visual styling prose | `get-component-info crt.GridContainer` (both flavors) |
| **crt.ImageInput** | web | `EntitySchemaPrompt.cs` (49,142,252 — repeated 3×) | MED | "use ImageLookup not binary Image; ImageInput can't read/write binary Image" coupling rule | Move rule → `image-input.component.md` in creatio-ui; `get-component-info crt.ImageInput` |
| **crt.Gallery / crt.DataGrid / crt.List** (selection trio) | web | `docs/commands/get-component-info.md:77` | MED | Hard-coded "visually similar components" list (now `whenToUse`/`synonyms` in registry) | list-mode keyword search (`table`→DataGrid, etc.) returns the trio |
| Field-control enum: **crt.Input, crt.NumberInput, crt.ComboBox, crt.Checkbox, crt.PhoneInput, crt.EmailInput, crt.DateTimePicker, crt.WebInput, crt.RichTextEditor, crt.ColorPicker, crt.FileInput, crt.EncryptedInput, crt.Slider** | web | `PageModificationGuidanceResource.cs` (45,356-357 — DataValueType→control mapping) | **REVIEW** | Enumerated component list mapped from column data types | Decide: borderline clio-owned (column→control) vs registry. If kept, leave; if moved, verify each `get-component-info <type>` |
| Mobile set: **crt.Toggle, crt.BarcodeScanner, crt.FloatingActionButton, crt.Sort, crt.QuickFilterGroup, crt.Scaffold** | mobile | `MobilePageGuidanceResource.cs` (180-230 — when-to-use, web-only negatives, Scaffold behaviour) | **HIGH** | Mobile component catalog + when-to-use + Scaffold structure prose | `get-component-info schema-type=mobile` lists them; confirm each exists in MobileComponentRegistry |
| **crt.Button** | web/mobile | `RunProcessButtonGuidanceResource.cs` (placement/wiring — mostly clio-owned); `MobilePageGuidanceResource.cs` (282-309 example) | LOW | Mostly wiring (keep); only incidental structure | No structural removal expected; spot-check `get-component-info crt.Button` |

---

## Table 3 — Files: action per file

| File | Action | Note |
|---|---|---|
| `Resources/RelatedListGuidanceResource.cs` | **Trim heavily** | Remove DataGrid/ExpansionPanel/"Expanded list" structure; KEEP master-detail wiring + slot-init footguns; route structure to `get-component-info composite`. |
| `Resources/MobilePageGuidanceResource.cs` | **Trim heavily** | Remove mobile component catalog & when-to-use (180-230); KEEP Scaffold merge-vs-insert rule & page-authoring conventions; route to `schema-type=mobile`. |
| `Resources/ComposableAppSkillGuidanceResources.cs` | **Trim** | Remove `crt.IFrame`/`crt.GridContainer` property lists & JSON examples; KEEP skill workflow; route to registry. |
| `Prompts/EntitySchemaPrompt.cs` | **De-dupe** | Collapse the 3× `crt.ImageInput`+ImageLookup rule; move the binding rule to `creatio-ui` `.md`. |
| `McpServerInstructions.cs` | **Light edit** | Drop the hard-coded composite enumeration on line 88; keep "call get-component-info with `composite`". |
| `docs/commands/get-component-info.md` | **Light edit** | Remove composite examples (28) and selection trio (77); point to tool output / selection metadata. |
| `Resources/PageModificationGuidanceResource.cs` | **Review** | Field-control mapping (356-357) — decide keep vs move. Everything else (write modes, containers) stays. |
| `Resources/AppModelingGuidanceResource.cs` / `DashboardGuidanceResource.cs` / `IndicatorWidgetGuidanceResource.cs` | **Keep / verify pointer** | Already delegate to registry; only confirm references stay valid. |
| `Resources/PageSchemaHandlers / Converters / Validators / CreatioDevkitCommon` | **Keep** | Handlers/requests/converters/validators — clio-owned, NOT registry components. |
| `Resources/RunProcessButtonGuidanceResource.cs` / `BusinessRulesGuidanceResource.cs` / `ExistingAppMaintenanceGuidanceResource.cs` | **Keep** | Wiring/workflow only; no component description. |
| `Prompts/PagePrompt.cs` / `UserTaskPrompt.cs` / `GuidanceCatalog.cs` / `docs/mcp-server.md` / `docs/McpCapabilityMap.md` | **Keep** | Workflow references only; no hard-coded component/composite data. |

---

## Test scope after refactor (regression checklist)

1. `get-component-info` tool: list mode, detail mode (web), `schema-type=mobile`, and `composite="<caption>"` for the 5 composites above.
2. Components to fetch-verify: `crt.DataGrid`, `crt.ExpansionPanel`, `crt.IFrame`, `crt.GridContainer`, `crt.ImageInput`, `crt.Gallery`, `crt.List`; mobile: `crt.Toggle`, `crt.BarcodeScanner`, `crt.FloatingActionButton`, `crt.Sort`, `crt.QuickFilterGroup`, `crt.Scaffold`.
3. Guidance resources unit tests (`clio.tests/Command/McpServer/McpGuidanceResourceTests.cs`, `GuidanceGetToolTests.cs`) — update expected text.
4. MCP E2E (`clio.mcp.e2e/ComponentInfoToolE2ETests.cs`, `GuidanceGetToolE2ETests.cs`).
5. Docs targets: `help/en/*`, `docs/commands/get-component-info.md`, `Commands.md`, `Wiki/WikiAnchors.txt`.
6. Snapshot guard (`ComponentRegistrySnapshotTests`) — unaffected, but run to confirm.
