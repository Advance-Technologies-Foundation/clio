# Mobile page converter

Tool `get-mobile-page-conversion-guide` · `[FeatureToggle("mobile-page-converter")]` (off by default) ·
`clio/Command/McpServer/Tools/MobilePageConverter/`

Turns a Freedom UI **web** page into the data an LLM caller needs to build the Freedom UI **mobile** page. Read-only:
writes no page, no Creatio object, no file. Where this file and the code disagree, the code wins.

---

## 1. Boundary

| | |
|---|---|
| Input | source page + merged bundle (`get-page` under the tenant lock), its web template, the target mobile template, web + mobile component registries, the conversion rules, two best-effort probes (section registration, page business rules) |
| Output | one `MobilePageConversionGuide` (§3) inside `MobilePageConversionGuideResponse` (+ `resolvedTargetVersion`, `resolvedFrom`, `versionWarning`) |
| Supported source | `freedom-web` only |
| Persistence | none — the caller runs `create-page` (mobile template) → `update-page` → `validate-page` |
| Procedure | `get-guidance name=freedom-page-web-to-mobile-conversion` is mandatory. Article = procedure, response = facts |

**Refusals** — a failure response, no guide:

| Condition | Why it is not degraded |
|---|---|
| Source page unreadable, not Freedom web, or already mobile | Nothing to convert |
| `version` is neither 3-part semver nor `latest` | The raw value reaches CDN URL composition |
| Mobile template named by the rule unreadable, or no rule and no `defaultMobileTemplate` | Without it `mobileTypesByName` is empty → same-name twin detection is off → template natives (`Feed`, `Tabs`) are inserted a second time |
| Web template named by the page but unreadable | Chrome subtraction either skips (template scaffold converted as content) or consumes the whole tree |

Both probes (`MobileSectionRegistrationProbe`, `PageBusinessRuleProbe`) never block: failure → `probeOk = false`, flags absent,
no exception text on the wire.

---

## 2. Composition

```
MobilePageConversionGuideTool.GetMobilePageConversionGuide          I/O + orchestration
 ├ ReadPageUnderTenantLock                        → page, merged bundle
 ├ DetectSourceType · RejectUnsupportedSourceType
 ├ ResolveVersionAsync → IComponentCatalog.LoadAsync ×2   → mobile/web registries, resolvedFrom (worse of the two)
 ├ IWebToMobilePageConversionRulesCatalog.GetRulesAsync    → rules (local override → cache → CDN → bundled)
 ├ ResolveEffectiveTemplateName → ResolveTemplateRule ?? DefaultTemplateRule
 │     climbs past same-named replacing layers; the default is applied by the caller, not inside the resolver
 ├ LoadMobileTemplateProbe   → ContainerParents, positional placements, TypesByName, LayoutConfigsByName,
 │                             merged viewModelConfig / modelConfig, Unavailable
 ├ LoadWebTemplateBaseline   → Names, Nodes, Resources, Unavailable          (the chrome to subtract)
 ├ MobileSectionRegistrationProbe.Probe
 ├ PageBusinessRuleProbe.Probe
 └ WebToMobileAnalysisService.Analyze(...)         PURE — no I/O          → MobilePageConversionGuide
```

Rule of the split: everything that touches an environment or a catalog lives in the tool or a probe; `Analyze` takes plain
data and is unit-tested without an environment. Every environment-dependent input of `Analyze` has a null/empty default.

---

## 3. Contract

Thirty fields, three caller actions. A "report" field is never planned from; a "paste" field is never rebuilt.

### 3.1 Paste / pass

| Field | Content | Caller action |
|---|---|---|
| `viewConfigDiff` | Mobile `viewConfigDiff` in the applier's own shape | Paste verbatim, in order |
| `modelConfigDiff` | Targeted merges/inserts over the mobile template's `modelConfig` (§6) | Paste verbatim |
| `viewModelConfigDiff` | Same, over the template's `viewModelConfig` | Paste verbatim |
| `resourceStrings` | Every localized string the converted body references | Register all |
| `recommendedMobileTemplate` | Mobile template to create the page from | `create-page` |
| `suggestedTargetSchemaName` | Target schema name (argument or `DeriveMobileSchemaName`) | `create-page` |

### 3.2 Report

| Field | Content |
|---|---|
| `droppedElements` | Every source element that did not reach the page, each with coded reasons |
| `componentSuggestions` | Per source **type**, derived from the finished element map (§8.3) |
| `requestConversions` | Per action binding: converted / dropped / flagged |
| `pageBusinessRules` | Converted rule conditions + surviving actions; dropped rules with reasons |
| `normalizations` | Rule-declared standards stamped onto inserted elements; skipped ones with reasons |
| `dataSectionConflicts` | Template-owned data-section changes no diff operation can express (§6) |
| `unresolvedParents` | Inserts whose parent neither the diff nor the template provides — report and stop |
| `layoutResolution` | Set only when the source had components and the converted layout is empty |
| `sectionRegistration` | Mobile-client registration state + the steps to propose |
| `templateMatch` | `"matched"` (rule has a `web` template) or `"generic-fallback"` (default rule); null without a rule |
| `adaptiveLayout` / `tabAreaLayers` | Readable indexes of layout the operations already carry |
| `webOnlySections` | Handlers / validators / converters the source declares and mobile has no place for |

### 3.3 Read only to understand

| Field | Why it exists |
|---|---|
| `sourceStructure` | The resolved source tree — provenance for everything else |
| `modelConfig` / `viewModelConfig` | The configs the two `*Diff` fields were built FROM. Never applied |
| `containerMap` | Container correspondence the converter has already applied. Never a source of `parentName` |
| `nameMap` | Source → mobile name for renamed elements only |
| `mobileContracts` | Inline registry contracts for the mobile types the diff emits and the registry describes |
| `dataSources` | Data-source names the source declares (= keys of `modelConfig.dataSources`) |
| `sourcePage` / `sourceType` / `sourceTemplate` / `guidanceArticle` | Identity |

---

## 4. Pipeline — `Analyze`

```
 0. PruneTemplateComponents        subtract web-template chrome; keep container twins and nonConvertingScopeContainers
 1. WalkStructure                  → sourceStructure, namesByType
 2. CollectWebOnlySections · CollectDataSources
 3. BuildElementMap                → working map (merge / insert / drop / relocate-children)
                                     request bindings remapped / stripped / flagged in place
    ├ RemoveExcludedComponents     rules-driven positional bans
    ├ RemoveEmptyContainers        bottom-up, cascades
    ├ CompactPositionalIndexes
    ├ AssignConvertedTabIndexes
    ├ BuildRequestConversionInfo   reconciled against both removal passes
    ├ BuildAdaptiveLayout
    ├ PlacePositionalGroups
    ├ BuildTabAreaLayers           synthesizes tab-body + Area layers
    ├ InitializeContainerChildSlots
    ├ ApplyComponentPropertyOverrides
    ├ NormalizePlacements
    └ StampParentSource            LAST map-mutating pass
 4. BuildComponentSuggestions → BuildMobileContracts        from the FINISHED map
 5. PassthroughModelConfig · BuildMobileViewModelConfig · BuildTargetedDiff ×2
 6. ConvertPageBusinessRules
 7. CollectResourceStrings
    projections: ProjectViewConfigDiff · ProjectDroppedElements · ProjectNameMap · ProjectUnresolvedParents
```

### 4.1 Ordering constraints

Each one fails silently when broken.

| Constraint | Consequence of breaking it |
|---|---|
| Step 4 after every map-mutating pass | Per-type answer says "what will happen to this type" while the caller reads "what happened to these elements"; `mobileContracts` follows the suggestions, so the contract set is wrong in both directions |
| `RemoveExcludedComponents` before `RemoveEmptyContainers` | A branch the exclusion empties does not cascade away |
| `RemoveEmptyContainers` before `InitializeContainerChildSlots` | Emptiness is read as slot *absence*; a seeded slot makes every container look occupied and disables the pass |
| `CompactPositionalIndexes` before `AssignConvertedTabIndexes` | Compaction rebases each parent's indexed group to 0; over tab indexes it moves the first web tab before the template's general tab |
| `BuildRequestConversionInfo` after both removal passes | A binding on a removed element is reported as converted for an element the map says not to create |
| `BuildAdaptiveLayout` before `PlacePositionalGroups` and `BuildTabAreaLayers` | Adaptive would overwrite positional grid placement; synthesized layers would shift a child's stacking index |
| `InitializeContainerChildSlots` after `BuildTabAreaLayers` | Synthesized layers are insert parents; earlier seeding leaves them without a slot → differ: `Item X is not a container for other items` |
| `ApplyComponentPropertyOverrides` before `NormalizePlacements` | A rule that declares a `layoutConfig` would write a partial one after normalization |
| `StampParentSource` last | Parent provenance is complete only after the tab layers re-point a tab's children |

---

## 5. Element map

Four working operations; two reach the wire.

| Operation | Meaning | In `viewConfigDiff`? |
|---|---|---|
| `insert` | Create the element on the mobile page | yes |
| `merge` | Layer onto an element the mobile template provides (same-name twin, `mobileTypesByName`) | yes |
| `drop` | Element did not reach the page | no → `droppedElements` |
| `relocate-children` | Container not recreated; children reparented | no → `droppedElements` |

- A drop stays in the map until projection: passes replace an entry in place, and the orphan / empty-container cascades read it.
- Wire projection is an **allow-list** (`IsInsert || IsMerge`), never a deny-list: a new working operation must fail to reach
  the applier rather than land in it.
- `values` on `insert`: `type` + every source property except `name`, the value binding (`control`) included. On `merge`:
  only the delta over the template, no `type`. Nothing is pruned against the mobile registry until it publishes real
  per-component property lists.
- A merge with nothing to apply carries `{}` — never `null`, never absent. `JsonDiffApplier` requires `values` on `merge`
  and validates every operation before applying any.
- `name` is not unique: two operations may target one element (`Tabs → Tabs` and `CardToggleTabPanel → Tabs`). Apply in
  order; never deduplicate by name. A payload-free merge beside another operation on the same name is not emitted; two
  payload-carrying operations on one name are a genuine conflict and are both shipped.
- Synthesized names: `StableSuffix` = SHA-256 over `"{sourcePage}:{tabName}"`, base36, first 7 characters, extended on
  collision. Stable across runs and machines.

---

## 6. Data sections

`modelConfig` is the source page's merged config verbatim (mobile has identical structural support). `viewModelConfig` is
the source config minus attributes referenced only by dropped components; a removal by the empty-container or excluded-
components pass is layout cleanup, and its attributes are kept.

`BuildTargetedDiff(pageConfig, templateBase, section)` diffs each against the mobile template's OWN merged base:

| Case | Emitted |
|---|---|
| Key exists in the base | Recurse — only the real delta; every path exists in the base |
| New page-owned key (attribute, list collection, data source) | One `merge` at the parent path carrying the whole subtree |
| Array exists in the base | Never merged (merge replaces arrays wholesale) — each new entry is an `insert` at the array's path |
| Scalar changed inside a template-owned collection (`isCollection: true`) | Dropped, reported in `dataSectionConflicts` |
| Template base unreadable | Single root `merge` of the whole config (`BuildRootMergeDiff`) |

Conflict kinds (closed): `changed-named-element` · `changed-scalar` · `nameless-changed-in-place`.

---

## 7. Requests and business rules

**Requests** (action bindings, e.g. a button's `clicked`): `rules.requests` maps web → mobile request with a category; the
map is applied while each insert's values are built. Not in the map → `flag-request-unmapped`; unsupported → dropped;
element removed by a later pass → reported as discarded. Summary: `requestConversions`.

**Page business rules** (add-on metadata, read by `PageBusinessRuleProbe`): an action converts only for elements that
survive (`merge`/`insert`), names remapped web → mobile. Condition operand paths are remapped from the source DS column
path to the mobile viewModel attribute name via the source `viewModelConfig`. Dropped whole: mixed AND/OR, unsupported
comparison, unconvertible operand, no surviving action. Output is ready for `create-page-business-rule`.

---

## 8. Vocabularies

### 8.1 Reason codes — one vocabulary, five wire fields

`droppedElements[].reason` · `requestConversions.droppedRequests[].reason` / `.flaggedRequests[].reason` ·
`pageBusinessRules.droppedRules[].reason` · `normalizations.*.skipped[].reason`. One set, so one cause cannot acquire
two spellings; the `drop-` prefix is not asserted — `flag-` and `skip-` are first-class. Constants: `ReasonCodes`.

```
NOT LOSS          drop-inherited-chrome  drop-excluded-by-rule  drop-parent-excluded
                  drop-empty-container   drop-container-no-mobile-equivalent
GENUINE LOSS      drop-unsupported-request  drop-unknown-request  drop-type-not-in-mobile-registry
RULES DEFECT      drop-target-missing
IN SCOPE          drop-no-rule-in-scope  drop-not-an-action-in-scope  drop-non-converting-scope
A BINDING         drop-request-chrome-native  drop-request-unsupported
                  drop-request-element-empty-container  drop-request-element-excluded  flag-request-unmapped
A BUSINESS RULE   drop-rule-condition-mixed-and-or  drop-rule-condition-unsupported-comparison
                  drop-rule-condition-unconvertible  drop-rule-no-action-converts
A NORMALIZATION   skip-normalization-path-blocked
```

Pairs to keep distinct:

- `drop-unsupported-request` — the **element** is gone · `drop-request-unsupported` — the element survives, its binding was removed.
- `drop-container-no-mobile-equivalent` — a **container**, flattened, children preserved · `drop-type-not-in-mobile-registry` — a **leaf**, genuine loss.

### 8.2 `params`

1. A param never repeats a field the record already carries (`webType` is a field, not a param).
2. The param set is a property of the **code**, not the call site: every site passes every key, `null` included; the
   `Reason()` factory drops nulls. Callers may branch on key presence. `Reason()` is `internal` so every pass uses it.
3. One key, one referent, one format: `targetParent` + `targetSlot` (never dotted), `missingParent` (the parent that does
   **not** exist — never paste it), `newParent`.

### 8.3 Component categories — `ComponentMappingCategory`, PascalCase

| Value | Means |
|---|---|
| `DirectMapping` | Converted under the same type |
| `AlternativeAvailable` | Converted under a different mobile type, named in `suggestedMobileTypes` |
| `WithAdaptation` | Transferred, needs adjustment — declared by a rule only |
| `Unsupported` | No operation, type known to the **web** registry |
| `RequiresManualDecision` | No operation, type unknown to both registries |

Derived from the finished map; registry membership is not evidence of conversion. A rule may substitute its category only
where nothing nameable was emitted. A type shipped nested inside another element's `values` gets no row.

---

## 9. Invariants

### 9.1 Prose is not a channel

A line that reads the same on every conversion is not a fact about this page. Resolution order for any candidate free text:
**code** (enforce, or fail) → **metadata** (typed field) → **article** (procedure) → **delete** (duplicate).

Free text still on the wire, and why:

| Channel | Why |
|---|---|
| `sectionRegistration.registrationActions[]` | Each step names a clio tool and its arguments |
| `componentSuggestions[].note`, `containerMap[].note`, `pageBusinessRules` notes | Authored by a rules author or the probe, never synthesized |
| `layoutResolution` | Diagnostic for an otherwise indistinguishable empty layout |

`normalizations` carries entries only. The rule that a normalized element's web value is discarded rather than translated
reads the same on every page, so it is the article's, not the response's.

### 9.2 A field must not assert what was not established

`null` = not established; a value = measured. A non-nullable `bool` fed from an optional source collapses the two.

- `ComponentRegistryEntry.Container` is `bool?` (no live entry publishes the key). `sourceStructure[].isContainer` is
  derived from the tree (a node with child components); registry + name heuristic only for an empty container.
- `SectionRegistrationInfo.SourcePageIsSection` / `MobileSectionRegistered` are `bool?` and absent when `probeOk` is false.
- A field with no producer is not a contract — it is deleted, not left null.

### 9.3 Determinism

- Every wire collection is ordered by construction or sorted explicitly (`SortedSet(OrdinalIgnoreCase)` for
  `suggestedMobileTypes`; element-map order for operations = parent-before-child).
- No dictionary-iteration order, culture, clock or catalog-load order reaches a wire value.
- All name comparisons are `StringComparer.OrdinalIgnoreCase`.

### 9.4 Enforced downstream, not stated

Invariants the caller must not violate are checked by the write/validate path, not described in the guide:

| Check | Where |
|---|---|
| A second `crt.Scaffold` — anywhere in an insert/set's `values` subtree, or an insert named `Scaffold` — is rejected; `merge` onto `Scaffold` is allowed | `SchemaValidationService.ValidateMobileSingleScaffoldRoot` |
| A `merge` whose `values` author children into a slot is rejected — children are authored with `insert`/`set` | `SchemaValidationService.ValidateMobileMergeSlotAuthoring` |
| Type in neither registry → warning naming `get-component-info schema-type=mobile` | `SchemaValidationService` |
| The diff is **applied** through the client-engine clones (`JsonDiffApplier`, `JsonPathDiffApplier`); the differ's own exception is returned. Path-diff base: body's own base → target page merged config (`MobilePageMergedConfigResolver`) → empty base seeded at every insert path | `MobileDiffApplyValidator` (`validate-page`, `update-page`, `sync-pages`) |

---

## 10. Rules file — `WebToMobilePageConversionRules.json`

Loaded per version through `IWebToMobilePageConversionRulesCatalog` (local override → cache → CDN → bundled resource
`Clio.Command.McpServer.Data.WebToMobilePageConversionRules.json`). Every section is a data switch: absent → the pass is
a no-op.

| Section | Shape | Drives |
|---|---|---|
| `defaultMobileTemplate` | string | Fallback rule when no `templates` entry matches → `templateMatch: generic-fallback` |
| `templates[]` | `web`, `mobile`, `containers`, `components`, `note` | Template pairing, container twins (`containerMap`), component twins, positional `:top` / `:bottom` placements |
| `components[]` | `filters`, `path?`, `viewConfigTemplates` | Type conversion by filter + value skeleton (`ResolveTemplateTargetType` reads `viewConfigTemplates[].value.type`); `path` scopes to an ancestor |
| `requests[]` | `web`, `mobile`, `category` | Action-binding map (§7); only supported requests are listed |
| `componentPropertyOverrides[]` | `filters`, `values`, `mergeNestedObjects` | Standards stamped on inserted elements (`normalizations`) |
| `excludedComponents[]` | `filters{type, parentType, propertiesContainerName?}` | Positional bans (`drop-excluded-by-rule`); a filter missing `type` or `parentType` is unusable and skipped |
| `emptyContainerRemoval.removableTypes` | string[] | Closed set of container types removable when empty |
| `contentContainerTypes` | string[] | Container types treated as content |
| `nonConvertingScopeContainers` | string[] | Kept in the tree as `path` ancestors, emit no element (`drop-non-converting-scope`) |
| `tabAreaLayers` | `tabComponentType`, `mainTabContainer` | The designer's two-layer tab body |

`components[]` entries carry **no `web` key**. `FindRule` (matches on `web`) therefore returns `null` against the bundled
file; it stays load-bearing through `ResolveConvertedMobileType`, where `rule.Mobile` is the last fallback for a leaf's
target type — a published file that adds `web` changes **which types convert**. A test that asserts a category through a
hand-built `Web` rule tests a path production does not take.

---

## 11. Tests

| Layer | Fixture | Guards |
|---|---|---|
| `WebToMobileConversionServiceTests` | hand-built bundles | Every pass, projection and code; the `Analyze` contract |
| `WebToMobileRealPageRegressionTests` | `Fixtures/LeadsFormPage.live-snapshot.json` | A real OOTB page end to end |
| `WebToMobileGeneralInfoTabRegressionTests` | `ServicesFormPageTabbed.live-snapshot.json` + `MobileComponentRegistry.live-snapshot.json` | Tabbed template, general tab, registry-dependent placement |
| `WebToMobilePageConversionRulesCatalogTests` | bundled rules | Rule shape; a filter that would silently disable a pass fails at authoring time |
| `MobileDropReasonCodeVocabularyTests` | `ReasonCodes` constants | Code set = the set the article publishes; kebab-case |
| `MobilePageConversionGuideToolTests` / `…LockTests` | substitutes | Orchestration, refusals, version validation, tenant lock |
| `MobileSectionRegistrationProbeTests` / `PageBusinessRuleProbeTests` | substitutes | Probe degradation (`probeOk = false`) |
| `clio.mcp.e2e/MobilePageConversionGuide*E2ETests` | seeded stand | Real MCP round trip; `Assert.Ignore` only for a missing precondition, never for a runtime error |

Three sources of truth, kept in step:

| Source | Owns | Guard |
|---|---|---|
| Code | Response content | Unit + regression tests |
| Rules file | Which types / containers / requests map where; value skeletons | Catalog tests; drift guard keeping hand-written test maps a subset of the shipped rule |
| clio-knowledge articles | Procedure; reason-code dictionary | `MobileDropReasonCodeVocabularyTests` (clio) ↔ `MobileDropReasonCodeCoverageTests` (clio-knowledge) |

---

## 12. Changing the converter

| Change | Steps |
|---|---|
| Add / rename / remove a reason code | `ReasonCodes` constant → `Reason()` call sites → `MobileDropReasonCodeVocabularyTests` → article `web-to-mobile-reason-codes.md` in clio-knowledge (PR there, `libraryVersion` + `sequence` bump) → re-pin `clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` |
| Add a pass | Place it in §4 by the constraints in §4.1; it takes and mutates `elementMap`; add its constraint row; it runs before `StampParentSource` and step 4 |
| Add a wire field | Model + `///` contract (nullability, ordering, derivation) → producer in `Analyze` → unit assertion → e2e assertion → article if the caller acts on it. No field without a producer |
| Remove / rename a wire field | Every reader in `clio/`, `clio.tests/`, `clio.mcp.e2e/`, both articles; a `!` commit |
| Change the rules file | Catalog test for the new shape; an unusable filter must fail at authoring time; no caller-facing prose in rules |
| New refusal | `Fail(...)` in the tool with the concrete cause; test in `MobilePageConversionGuideToolTests` |
| Free text on the wire | Apply §9.1 first; if it survives, add its row to the table there |
| Silent behaviour, workaround, rejected alternative | Record under `docs/knowledge/McpServer/` (`grep -ril mobile docs/knowledge/McpServer/`), `applies-to` pointing at the file |

Validation before commit: `dotnet test clio.tests/clio.tests.csproj --filter "Category=Unit&Module=McpServer" --no-build`.
E2E: the `clio.mcp.e2e` converter fixtures against a seeded stand.

---

## 13. Known gaps

| Gap | Status |
|---|---|
| Properties a mobile component cannot accept are copied verbatim | Blocked on `MobileComponentRegistry.json` publishing real per-component property lists |
| `crt.MenuItem` has no inline contract | Rules emit it; the mobile registry does not describe it |
| `adaptiveLayout`, `tabAreaLayers`, `modelConfig`, `viewModelConfig` re-serialize data the operations / diffs carry | Provenance the caller reads, not applies; removal is a contract decision |
| A type whose every instance vanishes is reported per type, not per element | `componentSuggestions` only |
| `BuildRootMergeDiff` fallback is indistinguishable by field | A root merge and a targeted diff share the `*Diff` field |

---

## 14. Where things live

| Path | Role |
|---|---|
| `WebToMobileAnalysisService.cs` | Pure engine: passes, projections, `BuildTargetedDiff`, `Reason()` |
| `MobilePageConversionGuideModels.cs` | Wire contract, `ReasonCodes`, `DataSectionConflict` |
| `MobilePageConversionGuideTool.cs` | MCP tool: I/O, template resolution, refusals, `[Description]` trigger |
| `ExcludedComponentsPass.cs` | Positional exclusion |
| `PageBusinessRuleProbe.cs` · `MobileSectionRegistrationProbe.cs` | Best-effort environment probes |
| `WebToMobilePageConversionRulesCatalog.cs` · `WebToMobilePageConversionRulesModels.cs` | Rules loading and model |
| `clio/Command/McpServer/Data/WebToMobilePageConversionRules.json` | Bundled rules |
| `clio/Command/PageConversionModels.cs` | `ComponentMappingCategory` and shared DTOs |
| `clio/Command/McpServer/Tools/MobileDiffApplyValidator.cs` · `clio/Command/SchemaValidationService.cs` | Downstream enforcement (§9.4) |
| `clio-knowledge/guidance/mcp/guides/platform/mobile/` | `web-to-mobile-conversion.md` (procedure) · `web-to-mobile-reason-codes.md` (dictionary) · `page-modification.md` |
