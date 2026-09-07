# ENG-95827 — `get-mobile-page-conversion-guide`: contradiction audit

**Ticket:** [ENG-95827](https://creatio.atlassian.net/browse/ENG-95827) — *[Mobile Page converter] Optimize converter mcp tool to work with Sonnet 5*
**Branch:** `feature/ENG-95827-mobile-converter-should-be-deterministic-oriented` (clio + clio-knowledge)
**Pull requests:** [clio#1365](https://github.com/Advance-Technologies-Foundation/clio/pull/1365) · [clio-knowledge#131](https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/131)
**Companion document:** [eng-95827-mobile-converter-metadata-before-after.md](eng-95827-mobile-converter-metadata-before-after.md) — what the change *did*. This document is what the change *left*.
**Method:** 7 independent audit lenses over the converter surface → 56 raw findings → 16 after dedupe → 3 adversarial verifiers each (a finding dies on 2 refutations) → **14 confirmed, 2 refuted**.
**Evidence base:** the OOTB `Leads_FormPage` guide captured 2026-09-02 (155 source elements, 226 795 characters of compact JSON) plus two recorded end-to-end conversion runs (`Leads_FormPage`, `Orders_FormPage`).

---

## 1. The one defect worth reading this document for

The guide answers the same question twice.

Once from an **advisory pass keyed by web component TYPE**, computed at pipeline steps 2 and 3 — before the
element map exists. Once from the **authoritative element map**, computed per INSTANCE at step 5. On the
reference page the two channels **disagree about the page's five largest elements**, and the advisory answer
is the one the mandatory conversion gate is built from.

Everything else in this audit is documentation drift, a half-closed reason vocabulary, and dead fields:
**11 558 bytes** of verified removable weight. The reliability win is not in the bytes. It is in making one
channel derive from the other.

---

## 2. Blocker 1 — the advisory sections classify against a rules key nothing populates

### 2.1 The mechanism

`BuildComponentSuggestions` classifies each distinct present web type through `FindRule`
([`WebToMobileAnalysisService.cs:1060`](../../clio/Command/McpServer/Tools/MobilePageConverter/WebToMobileAnalysisService.cs)):

```csharp
foreach (ComponentEquivalenceRule rule in rules.Components) {
    if (rule?.Web is not null &&
        rule.Web.Any(w => string.Equals(w, webType, StringComparison.OrdinalIgnoreCase))) {
        return rule;
    }
}
return null;
```

It matches on `ComponentEquivalenceRule.Web` and on nothing else. The shipped rules file carries four
`components` entries, and **none of them has a `web` key**:

```
$ python -c "... json.load(WebToMobilePageConversionRules.json)['components']"
components entries: 4
  keys: ['filters', 'viewConfigTemplates']            web= None
  keys: ['filters', 'viewConfigTemplates']            web= None
  keys: ['filters', 'viewConfigTemplates']            web= None
  keys: ['path', 'filters', 'viewConfigTemplates']    web= None
```

`Web` defaults to `[]`, so `FindRule` **always returns `null`**. The entire first branch of
`BuildComponentSuggestions` — `rule.Category`, `rule.Mobile` (which feeds `suggestedMobileTypes`),
`rule.Note`, and `BuildPrimaryWebMergeNote` — is unreachable. `primaryWebMerge` consequently never appears on
any response.

What survives is a pure registry-membership test: present in the mobile registry → `DirectMapping`; present
only in the web registry → `Unsupported`; in neither → `RequiresManualDecision`.

### 2.2 Why ordering makes that unfixable in place

```
step 1  WalkStructure            -> structure, namesByType
step 2  BuildComponentSuggestions(namesByType, rules, mobileTypes, webTypes)     <- line 162
step 3  BuildMobileContracts(suggestions, mobileByType)                          <- line 165
step 4  CollectWebOnlySections / CollectDataSources
step 5  BuildElementMap(...)                                                     <- line 191
```

The advisory sections are computed three steps before the decision they describe. They cannot see the
outcome, and no amount of rules authoring changes that: `crt.DataGrid` becoming `crt.List` is decided by the
element map's per-instance grid pass, not by a type table.

### 2.3 What the caller is actually told, measured

| `webType` | `componentSuggestions` says | the element map actually does |
|---|---|---|
| `crt.DataGrid` | **`Unsupported`**, `suggestedMobileTypes: []` | `insert crt.List` × **5**, each with a prebuilt `itemLayout` |
| `crt.SearchFilter` | `DirectMapping`, *"carry it over as-is"* | **`drop` × 5** (excluded by rule) |
| `crt.Button` | `DirectMapping`, `[crt.Button]` | 14 × `crt.Button`, 1 × **`crt.MenuItem`**, 4 × `drop` |
| `crt.TabContainer` | `DirectMapping`, `[crt.TabContainer]` | 5 × `crt.TabContainer`, 2 × **`crt.GridContainer`** |
| `crt.ComboboxSearchTextAction` | `Unsupported`, *"find a supported mobile alternative"* | shipped **nested inside** another operation's `values` (6 ×) |
| `crt.EmailComposer` / `crt.FeedComposer` / `crt.MessageComposerSelector` | `Unsupported`, same imperative | shipped **nested** (2 / 3 / 1 occurrences) |
| `crt.NextSteps` | `Unsupported` | `drop` × 1 — **correct** |

Of 29 suggestion rows, exactly one `Unsupported` label is true.

### 2.4 The propagation into `mobileContracts`

`BuildMobileContracts` iterates `suggestion.SuggestedMobileTypes`. Because that list comes from the
registry-only fallback, the contract set is wrong in both directions:

```
mobileTypes the element map emits:  23
emitted types WITHOUT a contract:  ['crt.List']          <- inserted 5 times
contracts for types never emitted: ['crt.SearchFilter']  <- every instance dropped
```

### 2.5 The observed cost, and the cost that did *not* materialise

Verified against two recorded runs. **The predicted harm did not happen**: neither run rebuilt the five
prebuilt `crt.List` inserts — the mandatory article's paste-as-is rule for grids
(`web-to-mobile-conversion.md:246-251`, `:395-397`) held both times. The actual damage was different:

- the missing `crt.List` contract produced **8 `viewConfigDiff binds to '$X' but no matching attribute is
  declared` errors that blocked `update-page --dry-run`**, recoverable only by a manual round trip;
- nine components that shipped *inside* pasted `values` were reported to the developer at the conversion gate
  as unavailable.

### 2.6 The fix

Move both advisory sections after `BuildElementMap` and derive them from the emitted operations:

- `suggestedMobileTypes` = the distinct mobile types the map actually emitted for that source web type;
- `Unsupported` where nothing nameable was emitted and the web registry knows the type — deliberately
  **not** keyed on "every instance was dropped", because the one thing this branch adds over
  `droppedElements` (which already carries the per-element cause) is the registry distinction between a
  web component mobile lacks and a probable custom component;
- omit the row entirely for a type that shipped nested inside another operation's `values` — the caller has
  nothing to do about it, and the current label instructs them to undo work already done;
- delete the three synthesized `note` constants and `ComponentInfoHint` in the same release (see §5.2 —
  deleting the note first removes the only explanation while leaving the wrong label).

`mobileContracts` then follows for free: `crt.List` gains a contract, `crt.SearchFilter` loses one.

---

## 3. Blocker 2 — 31 of 136 inserts ship without their value binding, and the response contradicts itself about where it goes

### 3.1 Measured

```
inserts whose values carry neither `value` nor `control`:
  crt.ComboBox 20 · crt.Input 6 · crt.NumberInput 2 · crt.DateTimePicker 2 · crt.WebInput 1  =  31 / 136
```

`pendingBindings` — added on this branch — reports `name`, `sourceProperty` and `sourceValue`. It does **not
carry the mobile target property**, which is the one thing the caller cannot derive.

### 3.2 Four channels, no agreement

| channel | says |
|---|---|
| `mobileContracts[].allowedProperties` (ComboBox) | `control` **and** `value` **and** `items` — does not disambiguate |
| `mobileContracts[].description` (ComboBox) | *"Key inputs … `control`, `label`, …"* |
| shipped mobile registry snapshot, `inputs` | `control: string`, `items: string` — **no `value` input at all** |
| [`MobilePageConversionGuideModels.cs:461`](../../clio/Command/McpServer/Tools/MobilePageConverter/MobilePageConversionGuideModels.cs) | *"a mobile `crt.ComboBox` binds via `value`, while `control` requires `items` or the page crashes"* |

Both recorded runs **refused to decide from the response** and spent an extra full `get-page` round trip to
recover the property (`Leads:219` — *"Both keys appear in the mobile crt.ComboBox/crt.Input
allowedProperties, so the contract is ambiguous"*; `Orders:203` — *"Extracted 188 element bindings to source
the value bindings"*). The Leads run then wrote `control` on all 31.

### 3.3 The fix, and why it is not step one

The audit proposed deriving the target property from a `FormControl` marker in the mobile registry. **That
mechanism does not hold on the shipped data**: 0 of 35 mobile components declare a `FormControl` input in
`MobileComponentRegistry.live-snapshot.json` (the web registry declares 44). A variant built on *"exactly one
of `control`/`value` is declared"* is worse — it passes only against the trimmed test fixture and never fires
against the live registry, where the affected types declare both.

And the premise in the XML doc — *"`control` requires `items` or the page crashes"* — **has no source
anywhere in either repository.** Encoding `targetProperty` from the current prose would hard-code an
unsourced claim onto 20 of 31 fields on the reference page.

So: settle `control`-vs-`value` empirically on a stand first. Then `targetProperty` is a one-line addition at
the capture site, where the mobile type is already in scope.

---

## 4. The remaining twelve, by root cause

### 4.1 The coded-reason vocabulary is half-finished

The 21-code vocabulary closed the code *names*. It did not close the param keys.

| # | Finding | Sev. |
|---|---|---|
| 2 | `params.target` carries **three referents in two incompatible formats** (dotted `Parent.slot` at three sites, bare at three others); four documented `Params:` lines do not match what is emitted. A follow-up turn re-adding a dropped element uses the dotted string as a parent; the applier accepts it silently and the element is saved at the `viewConfig` root, invisible in the designer — and `validate-page` does not catch it | high |
| 12 | The reason-code article misstates three outcomes and gates the dictionary load on one of the five fields that carry codes | medium |

`ExcludedComponentsPass.BuildDropReason` also hand-builds its params dictionary instead of routing through
the `Reason()` factory whose documented invariant is that null pairs are dropped. The vocabulary test pins
code *names* only, so neither class of drift is caught.

### 4.2 The mandatory article drifted from the wire contract after the renames

The article lives in a second repository and is edited in a separate commit. Nothing validates its field
names, operation kinds or value shapes against the wire contract.

| # | Finding | Sev. |
|---|---|---|
| 1 | The per-operation instructions name **four deleted field names**, and the `viewConfigDiff` catalogue entry **ends mid-sentence** without ever naming `values` | high |
| 8 | `containerMap`'s field entry tells the caller to set `parentName` from it; the same bullet list **forbids exactly that 14 lines later**. It is the only place the article states the field's purpose | medium |
| 3 | The article documents `relocate-children` as a `viewConfigDiff` operation **the projection can never emit** (its twin `drop` was removed in the same edit) | low |
| 4 | The taught no-delta detector `values: null` **can never fire** — the wire always ships `values: {}` since `3900acdda` | low |

### 4.3 Decisions the converter can make are exported as prose or as an ambiguous shape

| # | Finding | Sev. |
|---|---|---|
| 10 | Whether the mobile template was **matched or fell back to the generic base** is a binary the engine computes and emits only as an English `templateNote`, on a response whose `[Description]` asserts it carries no prose. The structural marker (`containerMap == []`) is regression-pinned but undocumented | medium |
| 11 | `viewConfigDiff[].name` **is not unique** — two merges land on `Tabs`, and one of them holds the only copy of a `layoutConfig`. The contract says only *"apply in order"*, and the payload-free twin carries no marker, so the classic dedupe instinct discards a shift the article itself calls unreportable | medium |

### 4.4 Fields that are structurally constant or duplicated

| # | Finding | Sev. | Bytes |
|---|---|---|---|
| 6 | `container` / `isContainer` are non-nullable `bool` fed from a registry key **absent from all 235 live entries**, so they ship a hard `false` that contradicts the same payload's parent graph and their own sibling `description`. The sibling `get-component-info` surface already omits the same `false` through a `bool?` | medium | 3 509 |
| 9 | `spacingNormalization` is a **byte-identical duplicate** of `normalizations["spacing"]` whose contract names its own removal condition — already met by the published article — and which **no consumer anywhere reads** (only the producer, the contract and its own tests) | medium | 4 512 |
| 7 | `componentSuggestions[].note` is code-generated: **two distinct sentences across 29 rows**, each a pure function of `category` plus a `sourceType` interpolation the caller already has | medium | 2 788 |
| 13 | Ten source elements have **no outcome record at all**; nine shipped nested while labelled `Unsupported`, and the tenth (`MainHeader`) is named by no operation and no `parentName` | medium | 0 |

---

## 5. What did *not* survive verification

Recorded so nobody spends a day re-raising it.

1. **`mobileContracts[].allowedProperties` "contradicts" `values` 253 times** — the divergence is
   **deliberate and documented** at `WebToMobileAnalysisService.cs:2881`: *"A property is NOT dropped because
   the mobile registry fails to declare it: the generated mobile registry is currently incomplete … so
   pruning against it would discard required, genuinely-supported properties."* The proposed pruning would
   break working pages.
2. **`mobileContracts[].description` is 5 KB of generic prose** — the measurements held (23 entries,
   4 961 chars) but every load-bearing claim failed, and the proposed replacement would make the response
   **less** deterministic. One real sub-defect survives as a one-character fix: `crt.SearchFilter` ships an
   **empty-string** description because `Description` defaults to `string.Empty` and `WhenWritingNull` does
   not suppress it.
3. **"One bad `parentName` fails the whole pasted array"** — false. `page-modification.md:350-353`: an
   unresolvable `parentName` is not refused; the differ falls back to the `viewConfig` root and the element
   is saved outside every container. The validate-all-before-apply rule is scoped to **required parameters**
   (`values` on `merge`), not to parent resolution.
4. **`drop-request-unsupported` keeps the element and strips the binding** — cannot fire against the bundled
   rules (25 request entries, none with a blank `mobile`), so the article's current wording happens to hold.
   The near-homograph pair `drop-unsupported-request` / `drop-request-unsupported` is a forward-looking
   hazard, not a live one. The *live* half of that finding is the dual call site of
   `drop-unsupported-request` with two different param sets, carried as finding 2.
5. **`containerMap` mis-places elements** — refuted for the class originally named: replaying naive
   `containerMap` placement over 136 inserts **agrees with the element map on 113**, and the 23 divergences
   are the synthesized tab layer it does not model. Finding 8 is about the *instruction*, not the data.

---

## 6. What this audit structurally could not see

The audit's own brief measured **string bytes only** — 101 779 of 225 625 — which rendered **55 % of the
response invisible** to the redundancy and ambiguity lenses. Three converter files were absent from the file
table entirely. The completeness critic recovered four items that no lens would have reached:

- **The four config fields: 25 KB, 11 % of the payload, ~11 KB of verbatim duplication.** `modelConfig` says
  *"APPLY IT VERBATIM via `modelConfigDiff`"*; `modelConfigDiff` says *"paste it VERBATIM … do NOT hand-build
  it, **collapse it back into one root merge**, or source it from a pre-existing body"*. The cheapest reading
  of the first produces exactly the single root merge the second forbids.
- **`dataSources`' contract says *"(mobile supports one)"*** while the reference response ships **12**, and
  `modelConfig` two fields later orders *"keep every attribute and ALL of its properties exactly as
  provided"* with a named failure mode. A caller that believes the first prunes 11 and breaks every
  `modelConfig.path`.
- **`sectionRegistration` leaks a raw `System.Text.Json` exception onto the wire** — *"The input does not
  contain any JSON tokens … BytePositionInLine: 0"* — beside **six default-initialised `false` values**
  under a heading that calls them *"read-only facts"*. `probeOk` is the only disambiguator, and the mandatory
  article mentions `sectionRegistration`, `probeOk` and `registrationActions` **zero times**.
  `registrationActions[]` is itself an undeclared imperative-English channel.
- **`adaptiveLayout` (9 129 B) and `tabAreaLayers` (1 030 B)** both declare in their own contract that they
  duplicate `viewConfigDiff` and that *"there is nothing separate to apply"* — the same self-declared-dead
  class as `spacingNormalization`, of which the audit found only the member its brief named.

---

## 7. Sequencing, and why the order is forced

**1. Reorder the advisory pass** (§2.6). The only fix with a measured cost today, and three other items
depend on it: the `note` deletion must ship with it, and the nested-carriage suppression is only knowable
after the element map exists.

**2. Settle `control`-vs-`value` on a stand** (§3.3) before any code encodes a `targetProperty`.

**3. All remaining CODE changes** — split `params.target` into `targetParent` / `targetSlot`, make param sets
per code, route `ExcludedComponentsPass.BuildDropReason` through `Reason()`, delete the
`spacingNormalization` alias (keeping the `SpacingGroup` constant, which names the surviving section), make
`ComponentRegistryEntry.Container` a `bool?`, add the non-converting-scope reason code. **Code must precede
the article PR, because the article publishes its names.**

**4. One clio-knowledge PR** with a `libraryVersion` + `sequence` bump and a re-pin of
`clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json`: the four stale field names, the
truncated clause at `:31-32`, the `relocate-children` deletion, `values: null` → `values: {}`, the
`containerMap` reference-only rewrite, the four reason-code corrections, the widened dictionary trigger.
Batch them — it is one sequence increment either way.

**5. Last:** add `templateMatch` (or document `containerMap == []` as the marker), branch the article's step 3
on it, and only *then* delete `templateNote`. A model cannot rely on a field the article does not yet name,
and on the fallback path `templateNote` is currently the sole carrier of the "review in the designer"
advisory. This needs a second bump, which is why it is not folded into step 4.

---

## 8. Ledger

| | |
|---|---|
| Confirmed findings | 14 (2 blocker, 2 high, 8 medium, 2 low) |
| Refuted by majority | 2, plus 8 disproved sub-claims (§5) |
| Verified removable response bytes | 11 558 |
| Duplication found by the critic, not yet costed | ~11 KB (config fields) + 10 159 B (`adaptiveLayout`, `tabAreaLayers`) |
| Reference response | 226 795 chars, 155 source elements, 136 inserts / 7 merges / 12 drops |
