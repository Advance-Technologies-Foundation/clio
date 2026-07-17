# Implementation work order — ENG-93262

> Paste this whole file as the task prompt for the implementing agent/session. It is self-contained
> but the **authoritative design is** [`ENG-93262-sdd.md`](./ENG-93262-sdd.md) — read it first (esp. §3
> the `scopeId` mechanism, §5 the change map, §7 risks). Do not re-derive the design; implement it.

## Task

Extend clio **page** business rules so a coding agent can use two new condition-operand sources and
one new operand type:

1. **Data Source field** — an entity column in the page's DataSource, including columns **not**
   surfaced on the page. Persisted as `BusinessRuleAttributeExpression` with `scopeId = "<datasource
   name>"` (e.g. `"PDS"`).
2. **Page parameter** — an input parameter (the `PageParameters` datasource). Persisted as
   `BusinessRuleAttributeExpression` with `scopeId = "PageParameters"`.
3. **System setting** operand — new `type = "SysSetting"` (`sysSettingName`), so a page parameter can
   be compared against a system setting per the AC.

Also allow an **unbound / technical page-local attribute** (`scopeId = ""`, `viewModelConfig.attributes`
entry with a `value`/declared type and no `modelConfig.path`) as a condition operand — smallest add,
satisfies a stated user case.

`scopeId` is the sole platform discriminator: `""` = root page attribute (current behavior),
`"<DS name>"` = datasource field, `"PageParameters"` = page parameter. **No new attribute-expression
type and no page-schema mutation.** Ticket: https://creatio.atlassian.net/browse/ENG-93262.

## Phase 0 — spikes BEFORE touching the contract (blockers, do these first)

Run against a real sandbox environment and record findings in the SDD §8 / diary:

- **S1 (trigger scope field).** Create a page rule in the designer whose condition uses a datasource
  column (like the shipped `CrtCustomerInfoInCaseMgmt/.../Cases_FormPageBusinessRule/metadata.json`,
  which uses `path:"Contact", scopeId:"PDS"`). Read its add-on metadata via clio's existing
  `read-page-business-rules` path / the addon service and capture the **exact verbose JSON field name**
  the scoped change-trigger uses (on-disk short code is `BRT1`). The converter in Phase 2 must emit it.
- **S2 (PageParameters runtime).** Verify a page-parameter-scoped condition (`scopeId="PageParameters"`)
  is registered as a business-rule reference context and evaluates at runtime on a supported build.
  If it cannot be confirmed, plan to gate the page-parameter source behind the feature toggle (Phase 6).
- **S3 (SysSetting shape).** Confirm whether `BusinessRuleSysSettingExpression` expects a setting
  code/name or UId, and how its data value type is resolved for type-compatibility.

If S1/S2 cannot be resolved, still implement the datasource-field + unbound-attribute + SysSetting
sources, and gate page-parameter behind the toggle.

## Phase 1 — friendly contract + DTOs

- `BusinessRuleModels.cs`: add `BusinessRuleExpression.ScopeId` (optional, `[JsonPropertyName("scopeId")]`,
  XML+`[Description]`); add `SysSettingName` (mirror `SysValueName`); extend the `type` description to
  include `SysSetting`. Update the ctor.
- `BusinessRuleMetadataDtos.cs`: add `ScopeId` (`scopeId`) to `BusinessRuleExpressionMetadataDto`
  (respect the existing `[JsonPropertyOrder]` sequencing — put it near `path`); add the scoped-trigger
  field (from S1) to `BusinessRuleTriggerMetadataDto`.
- `BusinessRuleConstants.cs`: add `BusinessRuleSysSettingExpressionTypeName`
  (`Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleSysSettingExpression`), a `"SysSetting"`
  const, and a `PageParameters` well-known scope const.
- MCP contract `BusinessRuleTool.cs`: the shared `BusinessRuleExpression` is reused by the page
  contract, so the new fields flow through — verify page rules accept them and entity rules reject a
  non-empty `scopeId`.

## Phase 2 — providers, validator, converters

- `PageBusinessRuleAttributeProvider.cs`: return a **scope-aware** operand catalog:
  - `scopeId ""` → surfaced datasource-bound attributes (current) **plus** unbound page-local
    `viewModelConfig.attributes` (declared type / `value`, no `modelConfig.path`).
  - `scopeId = DS name` → all supported columns of `modelConfig.dataSources[scopeId].config.entitySchemaName`
    (reuse `IEntityBusinessRuleAttributeProvider`; not limited to surfaced columns).
  - `scopeId "PageParameters"` → `bundle.Parameters` (`PageParameterInfo`), type via
    `BusinessRuleHelpers.MapDataValueTypeName(DataValueType)`, reference schema from `ReferenceSchemaName`.
  - Introduce a composite operand key (`scopeId::path` or a `ScopedOperandKey`) so the shared code
    resolves scoped operands without duplicating logic.
- `PageBusinessRuleValidator.cs`: **remove/replace `RejectDatasourcePaths`**; keep a targeted error only
  for a datasource path smuggled into `path` while `scopeId` is empty. Validate `scopeId` ∈ {`""`,
  `"PageParameters"`, a name in `modelConfig.dataSources`}; unknown scope → error listing available scopes.
- `BusinessRuleValidator.cs`: make operand resolution + type inference scope-aware (the `ResolveAttribute`
  / "Unknown attribute" path); messages name the scope and list scope candidates. Add `SysSetting` operand
  validation. Entity rules unchanged (empty scope only).
- `SimpleToFullBusinessRuleConverter.cs`: emit `scopeId` on attribute operands (`BuildAttributeExpression`
  / `BuildOperandExpression`); emit **scoped triggers** (from S1) in `BuildTriggers` /
  `EnumerateTriggerNames`; add a `SysSetting` branch; make `ResolveOperandTypeContext` scope-aware.
- `FullToSimpleBusinessRuleConverter.cs`: read `scopeId` back onto the friendly expression; add a
  `SysSetting` read branch. Keep read → edit → update lossless.

## Phase 3 — tests (mandatory; unit is necessary but NOT sufficient)

Follow test-style policy (AAA, `because` on every assert, `[Description]`, `BaseCommandTests<TOptions>`
where applicable; no OS-specific paths).

- Unit (`clio.tests`): attribute provider (surfaced/non-surfaced DS field, page parameter,
  unbound attribute, unknown scope); validator (scope accept/reject, `.`-path-without-scope still
  rejected, cross-scope type compatibility, SysSetting); `SimpleToFull` (`scopeId` + scoped trigger +
  SysSetting); `FullToSimple` round-trip with a `Cases_FormPageBusinessRule`-shaped fixture
  (`path:"Contact", scopeId:"PDS"`); MCP contract deserialization of the new fields.
- **E2E (`clio.mcp.e2e`) — mandatory:** create → read → update → delete a page rule whose condition
  uses a page parameter and a datasource field on a real sandbox; assert persistence + lossless read.
  Extend the harness if it can't target a parameterized page yet (do it in this task, don't defer).

## Phase 4 — MCP surface (use `create-mcp-tool` + `test-mcp-tool` skills)

- `ToolContractGetTool.cs`: add page condition examples for a datasource-field operand and a
  page-parameter operand (and a SysSetting comparison).
- `Resources/BusinessRulesGuidanceResource.cs`: extend the page-scope condition section — the three
  `scopeId` values, `SysSetting`, and the "runs at runtime but may not be editable in the legacy 7.x
  designer" caveat (SDD §7 Risk #1).
- Review the matching guidance article + the trigger line in the tool `[Description]` per the MCP
  maintenance policy. If MCP artifacts stay accurate, state "MCP reviewed, no update required".

## Phase 5 — docs

- `spec/business-rules/business-rules-spec.md`: relax the "declared page attribute only" wording in
  "Page scope"; document the scopes + `SysSetting` + updated rejection list.
- `spec/business-rules/business-rules-architecture.md`: document the scoped operand/trigger DTO shape.
- No CLI `-H` / `docs/commands/*.md` (business rules are MCP-only). State "docs reviewed" for the rest.

## Phase 6 — feature toggle (conditional)

If S1/S2 leave the page-parameter source (or the whole set) not safely round-trippable on supported
builds, add `[FeatureToggle("...")]` to the options class **and** the MCP `[McpServerToolType]` class,
register via `McpFeatureToggleFilter.RegisterEnabledPrimitives` (do NOT reintroduce
`WithToolsFromAssembly`). Otherwise document "no toggle" like the existing tools.

## Constraints (repo policy — non-negotiable)

- **DI:** no `new` for behavior classes; interface + DI registration; constructor injection. Keep
  `CLIO*` diagnostics clean in edited files; introduce no new warnings.
- **Build:** `dotnet build clio/clio.csproj -f net10.0` (kill a running `clio.dll mcp-server` net8.0
  process first if the DLL is locked).
- **Smart regression:** run `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=Command" --no-build`.
  This change touches the **shared** validator/converter — also run the **full** unit suite
  (`--filter "Category=Unit"`) before commit and cite the filter used in the commit/PR.
- **ClioRing gate:** the page-rule MCP contract shape changes. Run
  `dotnet test clio-ring/ClioRing.Tests/ClioRing.Tests.csproj -c Release` and the Windows x64 AOT
  publish, OR prove via inspection that Ring does not consume this contract and state
  "ClioRing compatibility reviewed, no Ring-consumed contract changed" with inspected paths.
- **C# XML docs** on public API (interface-level).
- **Code review gates:** run the parallel 3-lens review over the full diff before opening the PR and
  before ready-to-merge; resolve all Blocker/High.

## Definition of done

- [ ] Spikes S1–S3 recorded; contract reflects their outcome.
- [ ] Datasource-field, page-parameter, unbound-attribute, and SysSetting operands create/read/update/
      delete correctly on a sandbox; read round-trips `scopeId` losslessly.
- [ ] Entity-rule behavior unchanged (regression green).
- [ ] Unit + `clio.mcp.e2e` coverage added; targeted + full unit suites green; Ring gate satisfied.
- [ ] MCP contract/examples/guidance and `spec/business-rules/*` docs updated (or explicitly reviewed).
- [ ] `CLIO*` clean in edited files; diary entry appended; code-review gates passed.
