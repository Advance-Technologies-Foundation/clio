# The mobile page converter

**Tool:** `get-mobile-page-conversion-guide` (MCP, `[FeatureToggle("mobile-page-converter")]` — off by default)
**Purpose:** turn a Freedom UI **web** page into a Freedom UI **mobile** page, by handing an LLM caller
enough deterministic data to build the mobile body itself.
**Companion documents:** [metadata before / after](eng-95827-mobile-converter-metadata-before-after.md) —
what the ENG-95827 reshaping changed · [contradiction audit](eng-95827-mobile-converter-contradiction-audit.md)
— the 7-lens audit and what it found.

This file is the single authoritative description of the converter's architecture. Where it and the code
disagree, the code wins and this file is wrong — but every claim here was read out of the code at the
time of writing, not remembered.

---

## 1. Boundary

The converter **reads** the source page and probes the environment. It **writes nothing** — no page body,
no Creatio object, no file. The caller builds the body from the guide and persists it with `create-page`
(mobile template) → `update-page` → `validate-page`.

It **fails rather than degrades** when the mobile template cannot be read. Without that template
`mobileTypesByName` is empty, which silently stops same-name twin detection — so an element the template
already provides (`Feed`, `Tabs`) would fall through to the insert path and the page would ship a
duplicate of a native element. A guide with a footnote about the least of it is worse than no guide.

Supported source type: `freedom-web`. Anything else is detected and reported as not yet supported.

Reading `get-guidance name=freedom-page-web-to-mobile-conversion` is **mandatory** before acting on the
response. That article owns the *procedure*; this response owns the *facts*. The split is deliberate and
is the organising principle of the whole design (§6.1).

---

## 2. The contract

One response, thirty fields. The column that matters is the last one: a field whose caller-action is
"report" must never be planned from, and a field whose action is "paste" must never be rebuilt.

### 2.1 What the caller applies

| Field | What it is | Caller action |
|---|---|---|
| `viewConfigDiff` | The mobile page's `viewConfigDiff`, in the applier's own shape | **Paste verbatim, in order** |
| `modelConfigDiff` | Ready-to-paste data-source diff — focused targeted merges, never one root merge | **Paste verbatim** |
| `viewModelConfigDiff` | Ready-to-paste view-model diff, same shape | **Paste verbatim** |
| `resourceStrings` | Every localized string the converted body references | Register them all |
| `recommendedMobileTemplate` | The mobile template to create the page from | Pass to `create-page` |
| `suggestedTargetSchemaName` | The proposed target schema name | Pass to `create-page` |

### 2.2 What the caller reports

| Field | What it is |
|---|---|
| `droppedElements` | Every source element that did **not** reach the page, each with coded reasons |
| `componentSuggestions` | Per source **type**: what the conversion did to its instances |
| `requestConversions` | Per action binding: converted / dropped / flagged |
| `pageBusinessRules` | Converted rule conditions and actions; dropped rules with reasons |
| `normalizations` | Mobile standards stamped onto inserted containers, and any the stamp refused |
| `dataSectionConflicts` | Template-owned data-section changes no diff operation can express |
| `unresolvedParents` | Inserts whose parent **neither** the diff nor the template provides — report and stop |
| `layoutResolution` | Set only when the source had components but the converted layout is empty |
| `sectionRegistration` | Mobile-client registration state + the steps to propose at Gate S |
| `templateMatch` | `"matched"` or `"generic-fallback"` — how the template was chosen |
| `adaptiveLayout` | Readable index of the per-breakpoint layout already inside the operations |
| `tabAreaLayers` | Readable index of the tab-body / Area containers already inside the operations |
| `webOnlySections` | Handlers / validators / converters the source declares and mobile has no place for |

### 2.3 What the caller reads only to understand

| Field | Why it is here |
|---|---|
| `sourceStructure` | The full resolved source tree — provenance for everything else |
| `modelConfig` / `viewModelConfig` | The configs the two `*Diff` fields were built FROM. **Do not apply them** |
| `containerMap` | The container correspondence the converter has **already applied**. Never derive a `parentName` from it |
| `nameMap` | Source → mobile name, for the elements the converter renamed, and only those |
| `mobileContracts` | Inline registry contracts for the mobile types the diff emits *that the registry describes* |
| `dataSources` | All data-source names the source declares — all of them, carried over in full |
| `sourcePage` / `sourceType` / `sourceTemplate` | Identity of what was converted |
| `guidanceArticle` | The article name the caller must have read |

---

## 3. The pipeline

`WebToMobileAnalysisService.Analyze` is pure: no Creatio I/O, no body generation. Seven numbered steps.

```
        template resolution + chrome pruning
                    │
  1.  WalkStructure ─────────────────► sourceStructure, namesByType
                    │
  2.  CollectWebOnlySections / CollectDataSources
                    │
  3.  BuildElementMap ──────────────► the working element map
        │
        ├── RemoveExcludedComponents      (rules-driven positional bans)
        ├── RemoveEmptyContainers         (bottom-up, cascades)
        ├── CompactPositionalIndexes
        ├── AssignConvertedTabIndexes
        ├── BuildAdaptiveLayout
        ├── PlacePositionalGroups
        ├── BuildTabAreaLayers            (adds the synthesized tab layers)
        ├── InitializeContainerChildSlots
        ├── ApplyComponentPropertyOverrides
        ├── NormalizePlacements
        └── StampParentSource             ◄── LAST pass that touches the map
                    │
  4.  BuildComponentSuggestions ────► derived from the FINISHED map
      BuildMobileContracts               (follows the suggestions)
                    │
  5.  Data sections (modelConfig / viewModelConfig + their targeted diffs)
  6.  ConvertPageBusinessRules
  7.  CollectResourceStrings
                    │
              projections ─────────► viewConfigDiff, droppedElements, nameMap, unresolvedParents
```

### 3.1 Ordering constraints that are load-bearing

Each of these is a decision, not an accident. Breaking one fails silently.

| Constraint | Why |
|---|---|
| Step 4 runs **after** every map-mutating pass | A per-type answer computed earlier answers "what *will* happen to this type" while the caller reads it as "what happened to these elements". A grid becomes a `crt.List` in step 3; an exclusion rule drops every `crt.SearchFilter` in step 3. Enforced by the compiler: the calls take `elementMap`. |
| `RemoveEmptyContainers` **before** `InitializeContainerChildSlots` | The emptiness test reads slot *absence*. Seeding the slot first makes every container look occupied and disables the pass. |
| `RemoveExcludedComponents` **before** `RemoveEmptyContainers` | A branch the exclusion empties must then cascade away. |
| `BuildAdaptiveLayout` **before** `BuildTabAreaLayers` | Adaptive indexes children per grid container; running it on the post-synthesis map would shift a child's stacking index. |
| `CompactPositionalIndexes` **before** `AssignConvertedTabIndexes` | Compaction rebases each parent's indexed group to 0; run over tab indexes it would rebase the first-tab offset away. |
| `InitializeContainerChildSlots` **after** `BuildTabAreaLayers` | The synthesized layers are the only other pass adding inserts that other inserts target as parent; running earlier leaves them unseeded. |
| `NormalizePlacements` **after** `ApplyComponentPropertyOverrides` | A rules file that ever declares a `layoutConfig` would otherwise write a partial one after normalization ran. |

---

## 4. The element map

The internal working map carries **four** operations. Only two reach the wire.

| Operation | Meaning | Reaches `viewConfigDiff`? |
|---|---|---|
| `insert` | Create this element on the mobile page | **Yes** |
| `merge` | Layer onto an element the mobile template already provides | **Yes** |
| `drop` | This element did not reach the page | No — projected into `droppedElements` |
| `relocate-children` | This container is not recreated; its children are reparented | No — projected into `droppedElements` |

A drop lives in the map right up to projection because the passes need it there: one is installed by
*replacing* an entry in place, which is what lets the orphan and empty-container cascades see it while
they walk. Only the response separates the two, because they are read for opposite purposes.

The wire projection is an **allow-list** (`IsInsert || IsMerge`), never a deny-list: a future working-map
operation must fail to reach the applier payload rather than land in it silently.

### 4.1 `values`

On an `insert`: the component's `type` and **every** source property except `name` — the value binding
(`control`) included. On a `merge`: only the delta over what the template provides, with no `type`.

**A merge with nothing to apply carries an empty object `{}` — never `null`, never absent.**
`JsonDiffApplier` lists `values` as a required parameter of `merge` and validates *every* operation before
applying *any*, so one null fails the entire array. Three of the seven merges on the OOTB
`Leads_FormPage` carry no delta.

Nothing is pruned against the mobile registry. While
`MobileComponentRegistry.json` publishes no real per-component property list, every property is copied
from the web component verbatim; removing what a mobile component cannot accept is
[ENG-96589](https://creatio.atlassian.net/browse/ENG-96589) and is blocked on that registry.

### 4.2 `name` is not unique

Two operations may legitimately target one element — the shipped rules map both `Tabs → Tabs` and
`CardToggleTabPanel → Tabs`, and three other templates have the same shape. **Apply in order; never
deduplicate by name.** The one duplicate that used to invite the wrong choice — a payload-free merge
beside an operation that already declares the element — is no longer emitted.

---

## 5. The closed vocabularies

### 5.1 Reason codes — 22, across five wire fields

One vocabulary spans `droppedElements[].reason`, `requestConversions.droppedRequests[].reason` and
`.flaggedRequests[].reason`, `pageBusinessRules.droppedRules[].reason`, and
`normalizations.*.skipped[].reason`, because a caller reads them all the same way and a per-collection
vocabulary would let one cause acquire two spellings. That is also why the `drop-` prefix is not asserted
anywhere: `flag-` and `skip-` are first-class.

```
NOT LOSS            drop-inherited-chrome  drop-excluded-by-rule  drop-parent-excluded
                    drop-empty-container   drop-container-no-mobile-equivalent
GENUINE LOSS        drop-unsupported-request  drop-unknown-request
                    drop-type-not-in-mobile-registry
RULES DEFECT        drop-target-missing
IN SCOPE            drop-no-rule-in-scope  drop-not-an-action-in-scope
                    drop-non-converting-scope
A BINDING           drop-request-chrome-native  drop-request-unsupported
                    drop-request-element-empty-container  drop-request-element-excluded
                    flag-request-unmapped
A BUSINESS RULE     drop-rule-condition-mixed-and-or  drop-rule-condition-unsupported-comparison
                    drop-rule-condition-unconvertible  drop-rule-no-action-converts
A NORMALIZATION     skip-normalization-path-blocked
```

Two pairs are deliberately distinct and are the ones to get right:

- `drop-unsupported-request` — the **element** is gone. `drop-request-unsupported` — the element
  **survives** and only its binding was removed.
- `drop-container-no-mobile-equivalent` — a **container**, flattened, children preserved.
  `drop-type-not-in-mobile-registry` — the same cause on a **leaf**, where it *is* loss.

### 5.2 `params` rules

Three rules, and each exists because it was once broken:

1. **A param never echoes a field the record already carries.** `webType` was a param on two codes while
   `droppedElements[].webType` sat beside it — two places for one fact to drift.
2. **A param set is a property of the CODE, not of the call site.** Every site of a code passes every key
   it declares, `null` included; the `Reason()` factory drops the nulls. So a caller can branch on a key's
   presence. This is why `Reason()` is `internal` rather than private — `ExcludedComponentsPass` used to
   hand-build its dictionary, which meant the null-dropping contract held for every code except the one
   that pass emits.
3. **One key, one referent, one format.** `params.target` once carried three referents in two formats.
   It is now `targetParent` + `targetSlot` (never dotted), `missingParent` (the one parent name that does
   *not* exist, so it must never be pasted into an operation), and `newParent`.

### 5.3 Component categories — 5, PascalCase

Derived from the finished map. Registry membership is **not** evidence that anything converted.

| Value | Means |
|---|---|
| `DirectMapping` | Converted under this same type |
| `AlternativeAvailable` | Converted under a **different** mobile type, which `suggestedMobileTypes` names |
| `WithAdaptation` | Transferred but needs adjustment — a judgement no operation carries, so only a rules file declares it |
| `Unsupported` | No nameable operation, for a type the **web** registry knows |
| `RequiresManualDecision` | The same for a type unknown to **both** registries — probably custom |

A rules file may substitute its own declared value only where nothing nameable was emitted: where an
operation exists and its target can be named, there is no outcome for a type table to contradict. A type
whose configuration shipped **nested** inside another element's `values` gets no row at all.

---

## 6. The invariants

### 6.1 Prose is not a channel

A line that would read the same on any other conversion says nothing about the page in front of the
caller. Every such line was resolved in this order:

**code** (enforce it, or fail) → **metadata** (a typed field) → **article** (where it is procedure) →
**delete** (where it duplicated something).

What survives on the wire as free text, and why:

| Channel | Why it stays |
|---|---|
| `sectionRegistration.registrationActions[]` | Each step names a clio tool and its arguments — a procedure to execute |
| `componentSuggestions[].note` | Only when a **rules author** wrote one. The converter synthesizes none |
| `containerMap[].note`, `pageBusinessRules` notes | Same: authored, not synthesized |
| `normalizations[].note` | Composed from the actual counts |

### 6.2 A field must not assert what was not established

`null` means "not established"; a value means "measured". A non-nullable `bool` fed from an optional
source collapses those two, and the collapse is invisible:

- `ComponentRegistryEntry.Container` is `bool?`. No entry in the live catalog publishes the key, so a
  non-nullable bool made silence read as a published "no" — and `sourceStructure[].isContainer` shipped
  `false` for elements whose own children the same payload listed with `parentName` pointing back at them.
  `isContainer` is now derived from the **tree** (a node holding child components is a container), with
  the registry and a name heuristic left to the one case the tree cannot settle: an empty container.
- `sectionRegistration`'s environment-derived flags are `bool?` and **absent** when `probeOk` is false.
  The probe's `catch` no longer puts the exception message on the wire: a caller once received a raw
  `System.Text.Json` parser complaint as the explanation of a registration probe.
- A field with **no producer** is not a contract. `sourcePageIsDefaultEditPage` and
  `mobileDefaultEditPageExists` were deleted for this reason.

### 6.3 Determinism

The response must be reproducible for the same inputs.

- Every collection that reaches the wire is ordered by construction or sorted explicitly
  (`SortedSet` with `OrdinalIgnoreCase` for `suggestedMobileTypes`; element-map order for the operations,
  which is also the parent-before-child guarantee).
- `StableSuffix` is SHA-256 over `$"{sourcePage}:{tabName}"`, first 7 lowercase base36 characters — a
  synthesized name is stable across runs and across machines.
- No dictionary-iteration order, culture, or catalog-load order reaches a wire value.

### 6.4 The three sources of truth, and how they stay in step

| Source | Owns | Guarded by |
|---|---|---|
| The code | What the response contains | its own tests |
| `WebToMobilePageConversionRules.json` | Which types/containers/requests map where, and the value skeletons | `WebToMobilePageConversionRulesCatalogTests`, and the drift guard that keeps hand-written test maps a subset of the shipped rule |
| The clio-knowledge articles | The procedure, and the reason-code dictionary | `MobileDropReasonCodeVocabularyTests` (clio) ↔ `MobileDropReasonCodeCoverageTests` (clio-knowledge) |

**A code cannot be added, renamed or removed in clio alone.** The vocabulary test compares the constants
against the set the article publishes; the coverage test compares the article against a list of codes each
with a stated reason it needs its own entry. Both must move together, and the article change needs a
`libraryVersion` + `sequence` bump plus a re-pin of
`clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`.

One trap: the rules file's `components` entries carry **no `web` key** — all of them are filter/template
groups. `FindRule` matches on `web` alone, so against the bundled rules it always returns `null`. See
[the knowledge record](../knowledge/McpServer/conversion-rules-components-carry-no-web-key.md); the short
version is that `FindRule` is still load-bearing through its *other* call site, which decides what
converts.

---

## 7. Known gaps

| Gap | Status |
|---|---|
| `crt.MenuItem` has no inline contract | The bundled rules emit it; the captured mobile registry does not describe it. Named in the `mobileContracts` doc rather than implied fixed. Adjacent to ENG-96589 |
| Properties a mobile component cannot accept are still copied | Deliberate until `MobileComponentRegistry.json` publishes real property lists — [ENG-96589](https://creatio.atlassian.net/browse/ENG-96589) |
| `adaptiveLayout` + `tabAreaLayers` re-serialize what the operations already carry | ~10 KB. They exist for gate narration; whether that is worth the bytes is a product call, not a defect |
| `modelConfig` + `viewModelConfig` duplicate their diffs | ~13 KB, same reasoning: provenance the caller reads, not applies |
| `sourceStructure` overlaps `viewConfigDiff` | 111 of 155 entries repeat name/type/parentName on the reference page |
| A type lost with **no** entry anywhere | `drop-non-converting-scope` closed the container case. A type whose every instance vanishes with no entry is still reported per type but not per element |
| The full `clio.mcp.e2e` suite reports failures in unrelated fixtures | Baseline not established. Both converter fixtures pass or skip |

---

## 8. Where things live

| Path | Role |
|---|---|
| `WebToMobileAnalysisService.cs` (332 KB) | The engine: every projection, every pass, the `Reason()` factory |
| `MobilePageConversionGuideModels.cs` (89 KB) | The wire contract and `ReasonCodes` |
| `MobilePageConversionGuideTool.cs` (48 KB) | The MCP tool, template resolution, the trigger `[Description]` |
| `WebToMobilePageConversionRulesModels.cs` (43 KB) | The rules-file model |
| `ExcludedComponentsPass.cs` (33 KB) | Positional exclusion |
| `PageBusinessRuleProbe.cs` (19 KB) | Reads the source page's business-rule add-on |
| `MobileSectionRegistrationProbe.cs` (10 KB) | Reads SysModule / workplace registration |
| `WebToMobilePageConversionRulesCatalog.cs` (4 KB) | Loads the rules (bundled today; CDN not published) |
| `clio/Command/McpServer/Data/WebToMobilePageConversionRules.json` | The rules data |
| `clio/Command/PageConversionModels.cs` | The shared, converter-agnostic category enum and DTOs |

Tests: `clio.tests/Command/McpServer/Tools/MobilePageConverter/` (unit, real-page regression, vocabulary) ·
`clio.mcp.e2e/MobilePageConversionGuide*E2ETests.cs` (sandbox oracles, skipped without a seeded stand).

Articles: `clio-knowledge/guidance/mcp/guides/platform/mobile/web-to-mobile-conversion.md` (the mandated
procedure) · `web-to-mobile-reason-codes.md` (the dictionary) · `page-modification.md` (mobile page editing).
