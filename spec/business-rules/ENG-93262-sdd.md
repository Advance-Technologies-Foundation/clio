# SDD — ENG-93262: Data Source & Page parameters as a condition source for page business rules

- **Ticket:** [ENG-93262](https://creatio.atlassian.net/browse/ENG-93262) (Improvement) — *Support Data Source, Page parameters as a condition source for page business rules*
- **Epic:** [ENG-88605](https://creatio.atlassian.net/browse/ENG-88605) — *Give user possibility to create business rules using coding agent*
- **Component:** pixel ninjas · **Sprint:** PN-45 · **Status:** analysis / design (no code in this iteration)
- **Related docs:** [business-rules-spec.md](./business-rules-spec.md), [business-rules-architecture.md](./business-rules-architecture.md)

---

## 1. Problem & context

`create-page-business-rules` (and its read/update/delete siblings) lets a coding agent author
Freedom UI **page** business rules. Today a page-rule condition operand can only be a **page
attribute that is already surfaced on the page** — an entry in `bundle.viewModelConfig.attributes`
bound to an entity datasource column. Two condition sources the platform supports are unreachable
through clio:

1. **Data Source field** — an entity column that lives in the page's DataSource but is *not* bound
   to any view element / declared page attribute (Jira case: *"В entity є колонка, але на сторінку
   не виведена, але є в Data Source"*).
2. **Page parameter** — an input parameter of the page (declared in the schema's `parameters`
   section, exposed at runtime via the `PageParameters` datasource / `parametersStore`). This is the
   channel a **business process** or a **parent page** uses to push a dynamic value into the page
   (Jira case: *hide `Assigned to` when page parameter `RequestType` = `Service request`*).

The AC also requires comparing a page parameter against **a page attribute, a boolean constant, a
data source field, and a system setting**.

This is the coding-agent value proposition: the metadata to reference these sources already exists
and executes at runtime, but the visual designer does not surface non-surfaced datasource columns
and does not yet round-trip page parameters — so the agent can author rules the designer UI cannot.

---

## 2. Current state (why it is blocked)

The page pipeline is: `PageBusinessRuleService` → `PageBusinessRuleSchemaProvider` (builds the merged
`PageBundleInfo`) → `PageBusinessRuleAttributeProvider` (builds the allowed-operand map) →
`PageBusinessRuleValidator` → shared `BusinessRuleValidator` / `SimpleToFullBusinessRuleConverter`.

Two hard constraints block the feature:

| # | Constraint | Location |
|---|---|---|
| C1 | The condition-operand map is built **only** from `viewModelConfig.attributes` whose `modelConfig.path` is a 2-part `datasource.column` that resolves to a real, supported entity column. Non-surfaced columns, unbound/technical attributes, and page parameters never enter the map. | [`PageBusinessRuleAttributeProvider.GetAttributes`](../../clio/Command/BusinessRules/PageBusinessRuleAttributeProvider.cs) L24-45, `TryResolveDatasourcePath` L73-87 |
| C2 | The validator **explicitly rejects** any operand `path` containing a `.`, forcing "declared page attribute name, not datasource path". | [`PageBusinessRuleValidator.RejectDatasourcePaths`](../../clio/Command/BusinessRules/PageBusinessRuleValidator.cs) L64-79 |

Supporting facts confirmed in code:

- The friendly operand model `BusinessRuleExpression` supports `Type` ∈ `{AttributeValue, Const,
  Formula, SysValue}` and has **no notion of scope** ([`BusinessRuleModels.cs`](../../clio/Command/BusinessRules/BusinessRuleModels.cs) L106-158).
- The persisted metadata DTO `BusinessRuleExpressionMetadataDto` has `typeName, uId, type,
  dataValueTypeName, referenceSchemaName, path, value, sysValueName, …` — **no `scopeId`**
  ([`BusinessRuleMetadataDtos.cs`](../../clio/Command/BusinessRules/BusinessRuleMetadataDtos.cs) L145-188).
- The trigger DTO `BusinessRuleTriggerMetadataDto` has `typeName, uId, name, type` — **no scope**
  ([`BusinessRuleMetadataDtos.cs`](../../clio/Command/BusinessRules/BusinessRuleMetadataDtos.cs) L131-143).
- The reader `FullToSimpleBusinessRuleConverter` maps an attribute expression from `path` only and
  **does not read `scopeId`** ([`FullToSimpleBusinessRuleConverter.cs`](../../clio/Command/BusinessRules/Converters/FullToSimpleBusinessRuleConverter.cs) L168-170).
- `SysSetting` as an operand type does not exist anywhere in clio (`grep scopeId|SysSetting` in
  `Command/BusinessRules` → 0 hits).

**Good news (feasibility):** clio *already* reads page parameters. `PageBundleBuilder.BuildParameters`
populates `PageBundleInfo.Parameters` (`PageParameterInfo`: `Name`, `DataValueType` (int),
`ReferenceSchemaName`, `ReferenceSchemaUId`, `Required`, `IsOwnParameter`)
([`PageBundleBuilder.cs`](../../clio/Command/PageBundleBuilder.cs) L167-195). The attribute provider
simply ignores this collection. And `PageBusinessRuleAttributeProvider` already resolves a
datasource → its entity schema → all columns via `IEntityBusinessRuleAttributeProvider`; it just
filters down to surfaced columns.

---

## 3. Platform mechanism — the decisive finding

The Creatio platform (verified against core source `Terrasoft.Core\BusinessRules\Models\Expressions\*`
and shipped package metadata) uses **one** operand class for all three sources. **`scopeId` is the
sole discriminator**, resolved at runtime by
`Context.GetAttributeByPath(path, scopeId)` (`Terrasoft.Core\BusinessRules\Models\Context.cs`):
empty `scopeId` ⇒ resolve `path` in the root view-model; non-empty ⇒ resolve `path` inside the
referenced datasource context whose name equals `scopeId`.

| Operand source | `typeName` | `type` | key fields | **`scopeId`** |
|---|---|---|---|---|
| Page/root view-model attribute *(today)* | `…BusinessRuleAttributeExpression` | `AttributeValue` | `path` = attribute name | `""` (empty) |
| **Data source field** | `…BusinessRuleAttributeExpression` | `AttributeValue` | `path` = column (path in DS) | **`"<datasource name>"`** e.g. `"PDS"` |
| **Page parameter** | `…BusinessRuleAttributeExpression` | `AttributeValue` | `path` = parameter name | **`"PageParameters"`** |
| Constant / boolean | `…BusinessRuleValueExpression` | `Const` | `value` | — |
| System value | `…BusinessRuleSysValueExpression` | `SysValue` | `sysValueName` | — |
| **System setting** | `…BusinessRuleSysSettingExpression` | `SysSetting` | `sysSettingName` | — |

Key consequences:

- **No new expression type** is needed for datasource fields or page parameters — only a `scopeId`
  field on the existing attribute expression.
- **No page-schema mutation** is required. Referencing a non-surfaced datasource column does *not*
  need a technical `viewModelConfig.attributes` entry — `path` + `scopeId` addresses the column in
  the datasource context directly. (Contrast with the user's use-case #3 "technical attributes":
  those are a *separate*, page-local `scopeId=""` mechanism for storing handler-computed values — see
  §5.)
- **A page parameter is modeled as a datasource** named `PageParameters` (`crt.PageParametersDataSource`),
  which is exactly why the AC phrases it as "*Data source → Page parameters*". So parameters and
  datasource fields are the same shape with a well-known scope name.
- The **verbose JSON field name is `scopeId`** (client metadata model `BusinessRuleAttributeExpressionMetadata { path, scopeId }`). clio talks to `AddonSchemaDesignerService.svc` in verbose JSON, so clio adds a `scopeId` property (the on-disk short code `BRX2` is irrelevant to clio).
- **`SysSetting`** is a genuinely new operand type for clio (net-new type name + DTO + friendly field).

Shipped real-world proof (designer-authored) of the datasource-field pattern:
`CrtCustomerInfoInCaseMgmt/…/Cases_FormPageBusinessRule/metadata.json` — condition left operands
`{ type:"AttributeValue", path:"Contact", scopeId:"PDS" }` and `{ …, path:"Account", scopeId:"PDS" }`,
with a matching **scoped trigger** carrying the datasource scope. No shipped example uses
`scopeId:"PageParameters"` yet (7.8.0 packages predate it) — ENG-93262 introduces that usage.

---

## 4. Requirements

### 4.1 Acceptance criteria (from the ticket)

- Configure page-rule conditions based on a **Data source** and a **Page parameter** via the coding agent.
- Select a **Data source** field as a condition source.
- Select a **Page parameter** as a condition source (conceptually "Data source → Page parameters").
- Compare a **Page parameter** value with: a **page attribute**, a **boolean** value, a **data source
  field**, and a **system setting**.

### 4.2 Use cases → mechanism map

| Use case (user + ticket) | Mechanism |
|---|---|
| Business process opens a page with dynamic params; rule reacts to them | Page parameter operand, `scopeId="PageParameters"` |
| Parent page pushes a value into the child page on open | Page parameter operand (same as above) |
| Technical attribute on the page storing a value computed in a handler extension | **Page-local attribute**, `scopeId=""`, unbound `viewModelConfig.attributes` entry (`value`, no `modelConfig.path`) |
| Column exists in the entity/DataSource but is not on the page | Data source field operand, `scopeId="<DS name>"` |
| Hide `Assigned to` when parameter `RequestType` = `Service request` | Page parameter operand `==` `Const` |
| Compare a page parameter to a system setting | `SysSetting` operand |

---

## 5. Proposed design

### 5.1 Friendly contract change (MCP / model)

Add an optional scope to the operand model, matching the platform primitive 1:1 so read/update
round-trips are lossless.

`BusinessRuleExpression` ([`BusinessRuleModels.cs`](../../clio/Command/BusinessRules/BusinessRuleModels.cs)) gains:

```jsonc
{
  "type": "AttributeValue",
  "path": "RequestType",
  "scopeId": "PageParameters"   // NEW — optional; omitted/"" = root page attribute (current behavior)
}
```

- `scopeId` semantics for **page** rules:
  - omitted / `""` → root view-model: a **surfaced page attribute** *or* an **unbound/technical
    page-local attribute** (use-case #3).
  - `"PageParameters"` → a **page parameter**.
  - any other value → a **data source** whose name (in `modelConfig.dataSources`) equals `scopeId`.
- For **entity** rules `scopeId` stays empty and unsupported (single scope) — the entity validator
  rejects a non-empty `scopeId`.
- Optionally expose a friendlier alias in the MCP contract (e.g. a semantic `source` discriminator)
  but persist/round-trip on `scopeId`. **Recommendation:** keep the primitive `scopeId` in the
  contract — it maps directly to the platform, keeps read symmetric, and avoids a translation table.
  Guidance text carries the three well-known values.

Add a new operand type for system settings: `Type = "SysSetting"` with a `sysSettingName` field
(mirrors the existing `SysValue`/`sysValueName` pattern).

### 5.2 Scoped attribute provider (`PageBusinessRuleAttributeProvider`)

Rework the provider to return a **scope-aware** operand catalog instead of a flat
`path → descriptor` map keyed only by surfaced attribute name. Concretely, resolve an operand from
`(scopeId, path)`:

- **scopeId `""`:**
  - surfaced datasource-bound attributes (current behavior), plus
  - **unbound/technical page-local attributes** — `viewModelConfig.attributes` entries that have a
    declared type / `value` and no `modelConfig.path`. Data value type comes from the attribute's
    declared type. *(New — supports use-case #3.)*
- **scopeId = datasource name:** every supported column of that datasource's entity schema
  (`modelConfig.dataSources[scopeId].config.entitySchemaName`), **not** limited to surfaced columns.
  The entity column resolution already exists via `IEntityBusinessRuleAttributeProvider` (reuse it;
  it also supports forward-reference column paths within the schema).
- **scopeId `"PageParameters"`:** every entry of `bundle.Parameters` (`PageParameterInfo`), keyed by
  `Name`; data value type from `DataValueType` (int → name via `CreatioDataValueType.GetName`),
  reference schema from `ReferenceSchemaName`.

Implementation note: the shared `BusinessRuleValidator`/converter currently index the attribute map
by a plain `path` string. Introduce a composite key (e.g. `scopeId + "::" + path`) or a small
`ScopedOperandKey` so the same shared code paths resolve, validate, and type-infer scoped operands
without duplicating logic. This is the main internal refactor.

### 5.3 Validator changes

- **Remove / replace `RejectDatasourcePaths`** (C2): a `.`-containing `path` is now legitimate when
  the operand carries a `scopeId` (forward path within a datasource scope). Preserve a targeted
  message for the *bad* case (a datasource path smuggled into `path` while `scopeId` is empty).
- Scope-aware operand resolution in the shared `BusinessRuleValidator` (the existing
  `ResolveAttribute` / "Unknown attribute" path, [`BusinessRuleValidator.cs`](../../clio/Command/BusinessRules/BusinessRuleValidator.cs) L440-460, L643):
  resolve against the scoped catalog; error messages should name the scope and list candidates for
  that scope.
- Validate `scopeId` values: `""`, `"PageParameters"`, or a name present in
  `modelConfig.dataSources`; reject unknown scopes with the available datasource names.
- Type-compatibility checks (both operands same data value type; lookup operands same reference
  schema; Const inherits type from the typed operand) must work when either/both operands are
  scoped — `ResolveOperandTypeContext` ([`SimpleToFullBusinessRuleConverter.cs`](../../clio/Command/BusinessRules/Converters/SimpleToFullBusinessRuleConverter.cs) L172-186) becomes scope-aware.
- `SysSetting` operand validation: known/resolvable setting; data value type compatible with the
  compared operand.

### 5.4 Converter changes (write path)

- `SimpleToFullBusinessRuleConverter.BuildOperandExpression` / `BuildAttributeExpression`
  ([`SimpleToFullBusinessRuleConverter.cs`](../../clio/Command/BusinessRules/Converters/SimpleToFullBusinessRuleConverter.cs) L188-250) emit `scopeId` onto the new DTO field for attribute operands.
- **Scoped triggers.** The platform pairs a scoped condition operand with a scoped change-trigger
  (shipped `Cases_FormPageBusinessRule` triggers carry the datasource scope). `BuildTriggers` /
  `EnumerateTriggerNames` ([`SimpleToFullBusinessRuleConverter.cs`](../../clio/Command/BusinessRules/Converters/SimpleToFullBusinessRuleConverter.cs) L376-406, L649-664) must emit the operand's `scopeId` on the trigger, which requires a
  new scope field on `BusinessRuleTriggerMetadataDto`. **Verify the exact verbose field name** for
  the scoped trigger before implementing (short code observed is `BRT1`).
- Add a `SysSetting` branch producing `BusinessRuleSysSettingExpression` metadata.

### 5.5 Reader changes (round-trip)

- `FullToSimpleBusinessRuleConverter` ([`FullToSimpleBusinessRuleConverter.cs`](../../clio/Command/BusinessRules/Converters/FullToSimpleBusinessRuleConverter.cs) L168-170) reads `scopeId` back onto the friendly
  `BusinessRuleExpression` so `read → edit → update` preserves scope; add a `SysSetting` read branch.
  This keeps the documented "read returns the create/update contract shape" invariant intact.

### 5.6 DTO / constants summary

| File | Change |
|---|---|
| `BusinessRuleModels.cs` | `BusinessRuleExpression.ScopeId` (opt); `SysValueName`-style `SysSettingName`; `"SysSetting"` in the `type` description |
| `BusinessRuleMetadataDtos.cs` | `BusinessRuleExpressionMetadataDto.ScopeId` (`scopeId`); scope field on `BusinessRuleTriggerMetadataDto` |
| `BusinessRuleConstants.cs` | `BusinessRuleSysSettingExpressionTypeName`; `"SysSetting"` const; well-known `PageParameters` scope const |
| `PageBusinessRuleAttributeProvider.cs` | scoped catalog (params + full DS columns + unbound attrs) |
| `PageBusinessRuleValidator.cs` | drop/relax `RejectDatasourcePaths`; scope validation |
| `BusinessRuleValidator.cs` | scope-aware operand resolution + type inference |
| `SimpleToFullBusinessRuleConverter.cs` | emit `scopeId`; scoped triggers; `SysSetting` |
| `FullToSimpleBusinessRuleConverter.cs` | read `scopeId`; `SysSetting` |

### 5.7 Data value type mapping

Page parameter `DataValueType` is an `int` — reuse `BusinessRuleHelpers.MapDataValueTypeName` /
`CreatioDataValueType.GetName` ([`BusinessRuleHelpers.cs`](../../clio/Command/BusinessRules/BusinessRuleHelpers.cs) L58-69). Datasource columns already map through
`BusinessRuleHelpers.BuildAttributeDescriptor`. Lookup reference schema comes from
`PageParameterInfo.ReferenceSchemaName` (params) or the column's `ReferenceSchema.Name` (DS columns).

---

## 6. Out of scope (this iteration)

- Entity-rule scopes (single scope only).
- Auto-creating a technical `viewModelConfig.attributes` entry (not required — `scopeId` addresses
  columns directly). If a future case *needs* a persisted technical attribute (use-case #3 authoring,
  not just referencing), that is a separate page-schema-mutation capability.
- `apply-filter` / `apply-static-filter` on pages (already unsupported, unchanged).
- Formula operands referencing scoped values.

---

## 7. Risks & dependencies

1. **Legacy 7.x condition designer round-trip (platform dependency, HIGH).** The Freedom UI condition
   editor is the legacy 7.x filter designer loaded in an iframe; `BusinessRule7xConverterService`
   declares a `PARAMETER` enum value but throws `Unsupported expression type` for it, and its
   attribute-scope handling is limited. A clio-authored **page-parameter** rule may **execute
   correctly at runtime** (`Context.GetAttributeByPath` supports `scopeId`) but **not be editable** in
   the legacy visual editor until the platform (pixel ninjas / business-rules-designer) adds parameter
   + scoped-attribute support to that converter. → Confirm the runtime path and coordinate the
   designer-side change; document the "authored by agent, not yet editable in designer" posture if the
   converter lands later.
2. **Runtime `PageParameters` reference-context registration (SPIKE).** Datasource-field scope is proven
   at runtime by shipped packages; the `PageParameters` scope needs verification that it is registered
   as a business-rule reference context at runtime on a target build. Verify on a sandbox before
   committing the contract.
3. **Scoped forward paths.** A datasource column path may itself be a forward reference (`Contact.Account`
   within `PDS`). Decide MVP support (direct columns only vs forward paths) and align validator +
   trigger generation.
4. **Shared validator/converter refactor blast radius.** Introducing scope into the shared operand map
   touches entity rules' code paths; entity behavior must be unchanged (scope stays empty). Full
   `Module=Command`-plus-shared regression required.

---

## 8. Open questions

- Exact **verbose field name** for the scoped **trigger** (short code `BRT1`) — verify against the
  addon payload before implementing §5.4.
- `SysSetting` operand: does the platform expect a setting **code/name** or UId, and how is its data
  value type resolved for type-compatibility checks?
- Contract ergonomics: raw `scopeId` vs a semantic `source` discriminator (recommend raw `scopeId`).
- Whether unbound/technical page-local attributes (use-case #3) are in scope now or deferred (they are
  the smallest add and directly satisfy a stated user case — recommend include).

---

## 9. Test strategy (per repo policy)

- **Unit (`clio.tests`, `Module=Command`/business-rules):**
  - Attribute provider: datasource-field (surfaced + non-surfaced), page-parameter, unbound-attribute,
    unknown-scope resolution.
  - Validator: scope acceptance/rejection; datasource-path-without-scope still rejected; type
    compatibility across scoped operands; `SysSetting`.
  - `SimpleToFull` converter: `scopeId` emission on operands + scoped triggers; `SysSetting`.
  - `FullToSimple` reader: `scopeId` round-trip; `SysSetting`; the `Cases_FormPageBusinessRule` shape
    (`path:"Contact", scopeId:"PDS"`) as a fixture.
  - MCP contract deserialization for the new fields.
- **E2E (`clio.mcp.e2e`, mandatory):** create → read → update → delete a page rule whose condition uses
  a page parameter and a datasource field on a real sandbox; assert runtime persistence and lossless
  read round-trip. Extend the harness if it cannot yet target a parameterized page.
- AAA + `because` + `[Description]`; `BaseCommandTests<TOptions>` where applicable.

---

## 10. MCP & documentation maintenance (mandatory review targets)

Triggered because command behavior, options/contract, and validation change:

- **MCP surface:** `BusinessRuleTool.cs` (page condition contract + `scopeId`/`SysSetting` on the
  shared `BusinessRuleExpression`), `ToolContractGetTool.cs` (page condition examples with
  datasource-field + page-parameter operands), `BusinessRulesGuidanceResource.cs` (page-condition
  source section: the three `scopeId` values, `SysSetting`, the "runtime vs legacy-designer" caveat).
- **Docs:** `business-rules-spec.md` §"Page scope conditions" (relax the "declared page attribute only"
  wording; document scopes + `SysSetting`), `business-rules-architecture.md` (scoped operand/trigger
  DTO shape). No CLI `-H`/`docs/commands/*.md` (business rules are MCP-only, no CLI verb).
- **ClioRing compatibility gate:** if the page-rule MCP contract changes shape, run the Ring consumer
  checks or state `ClioRing compatibility reviewed, no Ring-consumed contract changed` with inspected
  paths.
- Use the `create-mcp-tool` + `test-mcp-tool` skills for the MCP + test work; `document-command` for docs.

---

## 11. Rollout

Existing business-rule tools are **public (no feature toggle)** — see `business-rules-spec.md`
"Decisions". If runtime `PageParameters` support (Risk #2) or the legacy-designer round-trip (Risk #1)
is not guaranteed on supported builds at ship time, gate the **new page condition sources** behind a
`[FeatureToggle]` (options + MCP type) until the platform side lands, rather than shipping a contract
that authors non-editable rules by default.

---

## 12. Spike outcomes — resolved from platform source (2026-07-16)

The Phase 0 spikes were resolved authoritatively against the local platform source
(`C:\Projects\core\…\Terrasoft.Core\BusinessRules\…`), the shipped fixture
(`CrtCaseManagementApp\…\Cases_FormPageBusinessRule\metadata.json`), and the client verbose model
(`creatio-ui\…\addons\business-rules-metadata.ts`) — stronger than a live sandbox for the *contract
shape*. Runtime behavioral confirmation (S2) still wants a sandbox pass; see below.

### S1 — verbose field names (RESOLVED)

Verbose JSON field names come from `[DesignModeProperty(Name=…)]` on the core model classes
(`AddonSchemaToDtoMapper.GetAddonConfigMetadata` → `MetaUtilities.GetReadableMetaData`); clio's existing
camelCase DTOs already round-trip these, and the client TS model mirrors them:

| Concept | Core type + property | on-disk short code | verbose JSON field (clio) |
|---|---|---|---|
| expression scope | `BusinessRuleAttributeExpression.ScopeId` | `BRX2` | **`scopeId`** |
| expression path | `BusinessRuleAttributeExpression.Path` | `BRX1` | `path` |
| trigger name | `Trigger.Name` | `BRT1` | `name` |
| trigger type | `Trigger.Type` | `BRT2` | `type` |
| trigger scope | `Trigger.ScopeId` | `BRT3` | **`scopeId`** |

**Decisive trigger finding.** In the shipped `Cases_FormPageBusinessRule`, every scoped-condition rule
(`{path:"Account"|"Contact"|"Status"|…, scopeId:"PDS"}`) pairs the condition with a **DataLoaded
trigger (`type:2`) whose `name` = the datasource name** (`BRT1:"PDS"`), while the trigger's own
`scopeId` (`BRT3`) stays **empty** in all shipped triggers. The change-value trigger present in those
rules targets the auto-named *surfaced* view attribute (e.g. `LookupAttribute_c08bwtk`), which only
exists when the column is surfaced. → For clio's write path, a scoped operand must emit a
**DataLoaded trigger `{name:<scopeId>, type:2}`** (in addition to the existing root `{name:"",type:2}`);
this is the proven mechanism that makes a non-surfaced datasource-column condition re-evaluate. The
`Trigger.scopeId` field exists but is unused by shipped 7.8.0 rules, so clio does not rely on it.

### S2 — PageParameters runtime (PARTIALLY RESOLVED — behavioral verification still recommended)

- Runtime resolution is generic: `Context.GetAttributeByPath(path, scopeId)` (base/ViewModel context)
  resolves any non-empty scope via `ReferenceContexts.Find(rc => rc.ScopeId == scopeId)`. `ModelContext`
  overrides this to ignore `scopeId` — so scoped operands are a **ViewModel (page) scope** capability,
  matching this feature's target. Datasource scope (`"PDS"`) is proven by shipped packages.
- **Caveat:** the client `BusinessRulesManager.getBusinessRulesByContexts` explicitly excludes
  `modelType === 'crt.PageParametersDataSource'` from the rule-owning scopes it loads. That is about
  which scope *owns* rules (not operand lookup), but it shows `PageParameters` is special-cased on the
  client and that its registration as a business-rule **reference context** (needed for operand lookup)
  is **not confirmable from static source**. Combined with Risk #1 (legacy 7.x designer can't
  round-trip a `PARAMETER`), the page-parameter source is the one that is *not* provably safe on an
  arbitrary supported build. → Per §11, gate the **page-parameter source** behind a feature flag until
  confirmed on the target build; datasource-field is runtime-proven and need not be gated.

### S3 — SysSetting shape (RESOLVED)

- Core `BusinessRuleSysSettingExpression`
  (`Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleSysSettingExpression`), property
  `SysSettingName` (short code `BSS1`, verbose **`sysSettingName`**). Mirrors `BusinessRuleSysValueExpression`
  (`sysValueName`) 1:1.
- `Evaluate` = `SysSettings.GetValue(context.UserConnection, SysSettingName)` then
  `Normalize(value, DataValueType)`. So `sysSettingName` is the **setting code/name** (string), *not* a
  UId, and the data value type is taken from the expression's own `dataValueTypeName`. clio therefore
  sets the SysSetting operand's `dataValueTypeName` from the compared (typed) operand — the same
  type-inheritance path a `Const` operand already uses — and cannot statically verify the setting name
  exists (documented as a caller responsibility, like `sysValueName`).

### Net contract impact confirmed

`BusinessRuleExpressionMetadataDto` gains `scopeId` (after `path`); `BusinessRuleTriggerMetadataDto`
gains `scopeId`; a `SysSetting` type + `sysSettingName` field + `BusinessRuleSysSettingExpression`
type-name constant are added. No page-schema mutation. Entity rules keep empty scope only.
