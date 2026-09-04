# ENG-95827 — `get-mobile-page-conversion-guide`: metadata before / after

**Ticket:** [ENG-95827](https://creatio.atlassian.net/browse/ENG-95827) — *[Mobile Page converter] Support mobile converter on Sonnet 4.6*
**Branch:** `feature/ENG-95827-mobile-converter-should-be-deterministic-oriented` (clio + clio-knowledge)
**Pull requests:** [clio#1365](https://github.com/Advance-Technologies-Foundation/clio/pull/1365) · [clio-knowledge#131](https://github.com/Advance-Technologies-Foundation/clio-knowledge/pull/131)
**Measured on:** the OOTB `Leads_FormPage` response captured 2026-09-02 (155 elements, 227 225 characters total).

---

## 1. Goal of the change

The ticket asks the converter to work on weaker models. The root cause is not model capability — it is that
the tool's response asked the model to **interpret** instead of **apply**.

Three things followed from that, and each is a goal of this change:

### 1.1 Remove non-deterministic data from the response

The response shipped **9 811 bytes of English prose** in every reply (`constraints`, `nextSteps`) plus a
per-element `reason` sentence. Prose is the least deterministic thing a tool can return: a weaker model reads
it differently on every run, and — more importantly — **a rule stated in prose is a rule nothing enforces.**

The rule applied to every line was: *a line that would read the same on any other conversion says nothing
about the page in front of the caller.* Each line was then resolved in this order of preference:

**code** (enforce it, or fail) → **metadata** (a typed field) → **article / skill** (where it is procedure,
not data) → **delete** (where it duplicated something or said nothing).

### 1.2 Make the response applicable without transcription

`elementMap` mixed the *operation* with conversion *metadata*, so the caller had to transcribe each entry
into a mobile diff operation by hand. A transcription is a place to make mistakes, and a weaker model makes
more of them. The response now hands over the page's `viewConfigDiff` in the applier's own shape.

### 1.3 Stop the naming from lying about the source — *inventoried, NOT yet done*

The converter is being extended from *web → mobile* to also *old mobile → new mobile*. Every `web*` field
name becomes wrong at that moment.

**This branch does not rename them.** What it did do is remove the two that mattered most, by removing the
entries that carried them: `webName` / `webType` are gone from the 145 element-map entries (see §4). The
remaining source-side names are inventoried in §15 as follow-up work — 7 sites. Target-side names
(`mobileContracts`, `recommendedMobileTemplate`) need no change: the target stays mobile either way.

---

## 2. Top-level guide fields

| Field | Before | After | Note |
|---|---|---|---|
| `sourcePage`, `sourceType`, `sourceTemplate` | ✓ | ✓ | unchanged |
| `sourceStructure` | ✓ | ✓ | unchanged |
| `layoutResolution`, `webOnlySections`, `dataSources` | ✓ | ✓ | unchanged |
| `modelConfig`, `viewModelConfig` | ✓ | ✓ | unchanged (reference form) |
| `modelConfigDiff`, `viewModelConfigDiff` | ✓ | ✓ | unchanged (paste verbatim) |
| **`dataSectionConflicts`** | — | **NEW** | `{section, path[], entry, kind}` — a template-owned value the page changed that the diff vocabulary cannot express |
| `recommendedMobileTemplate`, `templateNote` | ✓ | ✓ | + generic-base fallback via `defaultMobileTemplate` |
| `containerMap`, `componentSuggestions` | ✓ | ✓ | unchanged |
| **`elementMap`** | ✓ | **REMOVED** | → `viewConfigDiff` + 4 siblings |
| **`viewConfigDiff`** | — | **NEW** | applier operations only, paste in order |
| **`nameMap`** | — | **NEW** | source name → mobile name, **renames only** |
| **`pendingBindings`** | — | **NEW** | the value binding the converter cannot place itself |
| **`unresolvedParents`** | — | **NEW** | parent provided by neither the diff nor the template |
| **`droppedElements`** | — | **NEW** | what did NOT convert, with coded reasons |
| `mobileContracts` | ✓ | ✓ | unchanged |
| `sectionRegistration`, `pageBusinessRules`, `requestConversions` | ✓ | ✓ | unchanged |
| `adaptiveLayout`, `tabAreaLayers` | ✓ | ✓ | unchanged |
| `spacingNormalization`, `normalizations` | ✓ | ✓ | normalization note now emitted once, not 3× |
| `resourceStrings` | ✓ | ✓ | now includes a **declared-empty** caption (was dropped) |
| **`constraints`** | ✓ 14 entries | **REMOVED** | 5 337 chars of prose |
| **`nextSteps`** | ✓ 12 entries | **REMOVED** | 4 358 chars of prose |
| `guidanceArticle`, `suggestedTargetSchemaName` | ✓ | ✓ | unchanged |

---

## 3. The central field: `elementMap` → `viewConfigDiff`

### 3.1 Entry shape

**Before** — 12 possible keys, mixing operation and metadata:

| key | present on |
|---|---|
| `operation` | 155/155 |
| `reason` | 155/155 |
| `webName` | 145/155 |
| `webType` | 145/155 |
| `mobileName` | 143/155 |
| `mobileType` | 143/155 |
| `mobileValues` | 140/155 |
| `parentName` | 136/155 |
| `propertyName` | 136/155 |
| `captionResource` | 35/155 |
| `index` | 6/155 |
| `parentExistsOnTemplate` | **1/155** |

**After** — 6 keys, all of them read by the mobile diff applier:

`operation` · `name` · `parentName` · `propertyName` · `index` · `values`

The shape was **verified against the applier**, not designed on paper: `Insert` resolves its target through
`parentName` + `propertyName`, reads the position from `config["index"]` and the component from
`config["values"]`; a `merge` resolves by `name` alone.

### 3.2 A real entry, before and after

One entry here; **§16 is a gallery of eleven more**, every "before" copied verbatim from the same captured
response — both merge shapes, five drops with five different codes, the request bindings, the page business
rule, the skipped normalization, and a slice of the diff as a caller pastes it.

```jsonc
// BEFORE
{
  "webName": "LeadDisqualifyReason",
  "webType": "crt.ComboBox",
  "operation": "insert",
  "mobileName": "LeadDisqualifyReason",
  "mobileType": "crt.ComboBox",
  "parentName": "SideAreaProfileFieldFlexContainer",
  "propertyName": "items",
  "captionResource": { "key": "LeadDisqualifyReason_caption", "sourceValue": "Disqualify reason" },
  "mobileValues": { "type": "crt.ComboBox", "label": "#ResourceString(LeadDisqualifyReason_label)#", … },
  "reason": "field/leaf; mobile-supported"
}

// AFTER — viewConfigDiff[]
{
  "operation": "insert",
  "name": "LeadDisqualifyReason",
  "parentName": "SideAreaProfileFieldFlexContainer",
  "propertyName": "items",
  "values": { "type": "crt.ComboBox", "label": "#ResourceString(LeadDisqualifyReason_label)#", … }
}

// AFTER — pendingBindings[]  (the binding the converter USED TO DISCARD)
{ "name": "LeadDisqualifyReason", "sourceProperty": "control", "sourceValue": "$LeadDisqualifyReason" }
```

### 3.3 Operation vocabulary

| Before | After |
|---|---|
| `insert` | `insert` |
| `merge` | `merge` |
| `relocate-children` | **removed from the diff** → `droppedElements` (`drop-container-no-mobile-equivalent`) |
| `drop` | **removed from the diff** → `droppedElements` |

`relocate-children` was never an applier operation — the applier's vocabulary is
`insert / merge / set / move / remove`. No caller could apply it, and none needed to: every child of such a
container already carries the reparented `parentName` in its own operation.

`set` / `move` / `remove` exist in the applier and are available when the converter needs them (`remove` is
the one the ticket discussion flagged for future use — an element the base template provides being deleted).

---

## 4. Where each removed key went, and why it is smaller

| Removed key | Where it went | Size then → now |
|---|---|---|
| `webName` / `webType` | `nameMap` (renames only); everything else joins to `sourceStructure` by name | pair on 145 entries → **5 map entries** |
| `mobileName` | `viewConfigDiff[].name` | renamed, not removed |
| `mobileValues` | `viewConfigDiff[].values` | renamed, not removed |
| `mobileType` | `values.type` on an insert; a `merge` re-declares no type by design | folded in |
| `captionResource` | **deleted** — `resourceStrings` is the single source | 35 entries → **0** |
| `parentExistsOnTemplate` → `parentSource` → | `unresolvedParents` (only the non-derivable case) | 1/155 → **usually absent** |
| `reason` | `droppedElements[].reason` only | 155 entries → **12** |

Measured facts behind two of those rows:

* **`captionResource` was 100 % duplication.** All **35** of its keys were already in `resourceStrings` with
  the same text — 0 missing.
* **Renames are rare.** 5 of 155 elements are renamed (`AttachmentList→AttachmentFileList`,
  `CardContentWrapper→GeneralTabContainer`, `CardToggleTabPanel→Tabs`, `FeedTabContainer→FeedContainer`,
  `AttachmentsTabContainer→AttachmentsContainer`). 10 elements are converter-synthesized and have no source
  counterpart at all — recognisable because their name is in neither `sourceStructure` nor `nameMap`.

---

## 5. `reason`: from prose to codes, then off the operations entirely

### 5.1 What it was

`reason` was the largest block of prose left after the `constraints`/`nextSteps` removal — **10 234
characters** of the field on one page, more than `constraints` ever was — while carrying only **31 distinct
values** across 155 entries. It was already an enum, stringly typed and concatenated with `"; "`.

**119 of the 155 entries** said one of exactly two things:

```
"field/leaf; mobile-supported"      × 79
"container; mobile-supported"       × 40
```

Both restate `operation: "insert"` plus the presence of `mobileType`. 85 % of all `reason` values contained
no page-specific name at all.

### 5.2 What was checked before removing it

For each code, whether the fact is derivable from the operation the caller already reads:

| Code | Already said by |
|---|---|
| `leaf-supported` / `container-supported` | `operation: "insert"` + presence of `values.type` |
| `leaf-retargeted` / `container-retargeted` | `parentName` / `propertyName` — the RESULT |
| `leaf-positioned` / `container-positioned` | `parentName` + `index` |
| `container-no-mobile-equivalent` | the element is not in the diff at all |
| `synthesized-by-converter` | name in neither `sourceStructure` nor `nameMap`; `tabAreaLayers` names both layers |
| `tab-indexed-before-template-tabs` | `index` on the operation |
| `anchor-moved-down` | `values.layoutConfig` carries the new placement |
| `action-retargeted` | `parentName` is the action host |
| `component-twin-prebuilt` / `-structural` | `values` null-or-not, `values.type` vs the source type |

Only one distinction is **not** derivable: `component-twin-no-baseline` vs
`component-twin-nothing-to-carry` (the discriminator is the web-template baseline, which the response does
not carry). It is **accepted as a deliberate loss**: it occurs **0 times** on the real page, and the safe
default for a `merge` with no payload — do nothing — is exactly what `nothing-to-carry` asks for.

### 5.3 Why `droppedElements` keeps its codes

A drop is the opposite case: nothing is built, so there is no operation to read the cause off — and the cause
is **not derivable from the element's type**. Measured on the real page:

> **11 of 12 dropped elements have `componentSuggestions[].category = "DirectMapping"`** — a type that
> converts perfectly well elsewhere on the same page.

Without a code, `{"webName": "SaveButton", "webType": "crt.Button", "operation": "drop"}` reads as
conversion loss, and the natural response to conversion loss is to re-insert — putting a duplicate Save
button beside the mobile template's native one.

Those 12 drops need **four different things said to the user**:

| Cause | Count | What to tell the user |
|---|---|---|
| inherited web chrome | 3 | not loss — the mobile template has its own |
| unsupported request | 1 | **genuine loss** — the action is gone |
| positional exclusion rule | 5 | not loss — and must **not** be re-added anywhere |
| emptied container | 2 | automatic housekeeping, nothing to say |
| type not in mobile registry | 1 | the only one also derivable (`category: "unsupported"`) |

### 5.4 The surviving vocabulary — 27 codes → 11 for elements

```
NOT LOSS            drop-inherited-chrome · drop-excluded-by-rule · drop-parent-excluded ·
                    drop-empty-container · drop-container-no-mobile-equivalent
GENUINE LOSS        drop-unsupported-request · drop-unknown-request ·
                    drop-type-not-in-mobile-registry
RULES DEFECT        drop-target-missing
IN SCOPE, NO-OP     drop-no-rule-in-scope · drop-not-an-action-in-scope
```

`drop-unsupported-request` vs `drop-unknown-request` is a distinction the converter makes deliberately:
the first means clio **knows** the request is unavailable on mobile; the second means it is in neither the
versioned map nor the bundled set, so clio can only say it does not know it — and if that custom request
**is** implemented on mobile, the action can be re-added by hand.

### 5.5 The four sibling collections, which the first pass missed

Coding `droppedElements` left FOUR other fields still declaring `reason` as a plain `string`:
`pageBusinessRules.droppedRules[]`, `requestConversions.droppedRequests[]`,
`requestConversions.flaggedRequests[]` and `normalizations.*.skipped[]`. Measured on the same page:
**1 445 characters across 13 entries, 4 distinct texts** — ten of the thirteen were the identical
107-character sentence, and `WebToMobileAnalysisService.cs` piped `rule.Note` (prose authored in the
RULES FILE) straight to the wire. They now carry codes: **1 445 → 457 characters, −68 %.**

Two findings shaped the design, and neither was about bytes:

- **`ScopeDropReason` returned a `(ReasonCode, string)` pair, and the string restated the code in every
  one of its five branches** — same cause, same params, two vocabularies. The duplication was built into
  the method signature. It now returns the code alone, and a binding lost because its ELEMENT was dropped
  carries **that element's own code object**. The two collections agree by construction rather than by two
  sentences someone has to keep saying the same thing, and a test pins it as
  `Codes(binding.Reason) == Codes(element)`.
- **A param must never repeat a sibling field.** The first draft of `drop-request-chrome-native` carried
  `params.name` and `params.request` — both already on the record as `elementName` and `webRequest`. Caught
  and removed before commit; it is the same redundancy this ticket exists to delete, and removing it is
  what turned a −36 % saving into −68 %. The article now states the rule: `params` carries only what the
  code ADDS, and every record names its own subject.

Inherited chrome keeps a code of its own rather than the element's, because it is the one case where the
element's drop and the binding's loss are **different facts**: the mobile control provides the standard
request natively, so nothing is lost — unless the page had overridden that button with a custom `usr.*`
request, which IS lost and which only this record reveals. The reader is sent to the entry's own
`webRequest` to tell the two apart.

The vocabulary is 21 codes in one namespace — not five per-collection vocabularies — so a cause cannot
acquire two spellings. `flag-` and `skip-` join `drop-`, which is why the guard deliberately does not
assert a `drop-` prefix. One code, `drop-rule-condition-unconvertible`, is **unreachable today** and kept
on purpose: it is the default arm of a switch over `PageRuleConditionIssue`, whose only two non-`None`
values have codes of their own. Both the constant and the guard fixture say so, so nobody reads it as
tested.

---

## 6. `pendingBindings` — a value the converter used to throw away

`ExcludedSourceProps = { "name", "type", "control", "value" }` held the value binding out of the prebuilt
values, and the caller was told **in prose** to "add ONLY the value binding … which is type-specific and
intentionally left out" — with no way to know what it was.

* **31 of 136 inserts** need a binding (`crt.ComboBox` ×20, `crt.Input` ×6, `crt.NumberInput` ×2,
  `crt.DateTimePicker` ×2, `crt.WebInput` ×1).
* **0 of them carried one.**

Which property the mobile component wants is **not derivable from the response**:
`mobileContracts[].allowedProperties` lists **both** `control` and `value` for `crt.ComboBox` and
`crt.Input`, and that contract's own `description` names `control` while the converter's code comment says
`value`.

So the converter now reports the binding it found instead of guessing where to put it. **Remaining work
(stage 2):** a per-mobile-type binding rule in the conversion rules file, after which the binding folds into
`values` and this field disappears — making the diff 100 % verbatim-pasteable. That needs authored data
verified on a real stand, because getting it wrong crashes the page (`control` without `items`).

---

## 7. Defects the audit exposed

Treating each prose line as a *promise* and checking it against real data found **nine** real defects. Six in
the first pass:

| Defect | Consequence before |
|---|---|
| Component-twin trigger tested `componentMap.Count > 0` — a property of the **rules file** | Fired on **every** conversion; pure false positive |
| One `hasPrebuiltPayload` bool drove the twin instruction across **four** distinct states | Two of them were told to merge-by-name when the correct action is **nothing** |
| `parentExistsOnTemplate` was sparse, retarget-only, set from 3 code paths | Caller could not tell "the template owns this parent" from "nobody does" |
| `data-section-root-merge-fallback` fired whenever the fallback fired | False positive on every page whose template lacks that section |
| `MaxSearchDepth` 32 counted JSON **nodes** → cut-off ≈ component depth 16 | A banned component below it **stays on the page**, silently, with no drop entry |
| `CollectResourceStrings` gated on `!IsNullOrEmpty(text)` | A **declared-empty** caption was dropped → token renders as the **raw token** on the device |

Three more found by the mandatory pre-PR review, **two of them regressions introduced by this branch**:

| Defect | Consequence |
|---|---|
| `parentSource` claimed `"template"` for any parent the map does not insert | Shipped rules hit it (`BlankPageTemplate` maps `MainContainer`, but `BlankMobilePageTemplate` is a bare `crt.Scaffold`) → caller skips a required insert → applier throws *"is not a container for other items"* |
| The declared-empty caption fix was **incomplete** | The `CaptionResource` branch still gated on non-empty text, and `?? sourceKey` registered the **key name** as the caption — the device rendered the literal `GeneralInfoTab_caption` |
| An unreadable **web** template was silently unguarded | Empty baseline ⇒ chrome pruning skipped and no same-name twins ⇒ the page ships duplicates of native elements, with `success: true` |

Two invariants also turned out to be enforced by nothing at all and are now validators: a single mobile
scaffold root (`ValidateMobileSingleScaffoldRoot`, new) and a component type present in **neither** registry
(`ValidateMobileComponentTypes`, previously silent).

### One pre-existing defect the pure diff made visible

The general-info-tab fixture produces **two `merge` operations onto `GeneralTabContainer`** (a container-map
twin and the general-tab twin). The old `elementMap` told them apart by `webName`, but a caller iterating it
would have applied **both** — the second merge can overwrite the first. With `name` as the only identity the
duplicate is visible. Coalescing merges changes conversion behaviour and needs its own decision, so it is
recorded and left out of scope.

**Correction to an earlier draft of this section, found while assembling §16: this is not fixture-only.** The
OOTB `Leads_FormPage` does the same thing — `Tabs` (`crt.TabPanel`) and `CardToggleTabPanel` (`crt.TabPanel`)
both merge onto the mobile `Tabs`:

```json
{ "operation": "merge", "name": "Tabs", "values": { "layoutConfig": { "column": 1, "colSpan": 1, "row": 2, "rowSpan": 1 } } }
{ "operation": "merge", "name": "Tabs" }
```

The second carries no `values`, so on this page applying both is harmless — but that is luck, not design,
and it is the same defect on a real page rather than a hand-written one. It raises the priority of the
decision in §15.5 without changing what the decision is.

---

## 8. Fail instead of degrading

| State | Before | After |
|---|---|---|
| Mobile template named but unreadable | degraded guide + a footnote | **tool fails**, error names the environment check |
| No mobile template determined at all | degraded guide + a footnote | **tool fails**, error names the rules-file fix |
| **Web** template named but unreadable | `success: true`, silently wrong | **tool fails**, error names the source-package check |

The footnote understated the damage: with no mobile template bundle `MobileTypesByName` is empty, so the
same-name twin check falls through to the insert path and the page ships a **duplicate of a native element**
(Feed, Tabs), and `RetargetTargetMissing` **fails open**.

Rules-file authoring errors (a `parenttype` typo) are now caught in **CI, for the person who wrote them**,
instead of being reported to every caller who cannot fix them.

---

## 9. Size

| Item | Before | After |
|---|---|---|
| `constraints` | 5 337 | **0** |
| `nextSteps` | 4 358 | **0** |
| `elementMap[].reason` (field incl. key) | 10 234 | **≈1 100** (12 drops only) |
| `captionResource` (35 entries) | ~1 700 | **0** |
| `webName` + `webType` (145 entries) | ~5 100 | **≈250** (`nameMap`, 5 entries) |
| **Removed from the payload, total** | | **≈26 000 characters** (~11 % of the 227 KB response) |

**An earlier estimate in this ticket was wrong and is corrected here:** coding `reason` was estimated to take
it from 8 424 to ~1 500 characters. That ignored the JSON structural overhead per entry —
`"reason":[{"code":"leaf-supported"}]` is barely shorter than the sentence it replaced. Coding alone was
**−25 %**, not −82 %. The size win came from *removing* the field from operations, not from coding it.

**The byte count was never the point.** The wins that matter:

* the four component-twin states are **machine-distinguishable** instead of separated by wording — their
  remedies are opposite, one of them "do nothing at all", and a format-string edit could previously merge two
  of them silently;
* `params` are **addressable** instead of parsed out of a sentence;
* the reason-code vocabulary is **closed and enforced** by an e2e test that reflects over `ReasonCodes` and
  fails on any code the guidance article does not document;
* the operation key set is **closed and enforced** — every serialized operation's keys must be a subset of
  what the applier reads.

---

## 10. Guidance (clio-knowledge)

Everything that was procedure rather than data moved into the published articles, which are now the only home
for those rules. `libraryVersion` **1.13.67 → 1.13.83**.

| Article | Before | After |
|---|---|---|
| `freedom-page-web-to-mobile-conversion` | 41 253 chars | **49 809** |
| `freedom-page-mobile-reason-codes` | — | **NEW**, 12 957 chars |

The code table became its own article because folding it into the conversion article took that article to
**52 919** characters — past the recorded probes where `page-schema-handlers` spilled at 50 351 and
`mobile-page-modification` at 52 655. A spilled article writes to a single-line file that `Read` cannot page,
so the codes would have been undocumented *in practice* — strictly worse than the prose they replaced, since
a sentence at least arrives with the payload.

Two guard fixtures that pinned drop-reason **sentences** now pin the **codes**, which is strictly stronger: a
code is a closed-vocabulary token the converter cannot reword by accident.

A third pair pins the vocabulary as a WHOLE, and it exists because this document's own audit caught what
nothing else could. The commit that folded `relocate-children` into `droppedElements` minted
`drop-container-no-mobile-equivalent` and never added its article entry; every suite stayed green, because
that code fires on **zero** of the 12 drops of the page everything here was measured on. No sample, no
fixture and no read of a real response would have shown it missing. The guard is two-sided by necessity —
neither repository references the other — so each half pins the same eleven codes at its own end:
`MobileDropReasonCodeVocabularyTests` (clio) reads them off the constants by reflection and fails on any
addition with a message naming the article; `MobileDropReasonCodeCoverageTests` (clio-knowledge) fails if an
entry is deleted, or if the article documents a code the converter cannot emit. Both were verified
non-vacuous by mutation.

The same audit found three shipped references to `elementMap` that outlived the field — and only one of them
wanted its name corrected.

- **`bundle-source.json`'s title and description for the codes article. Rewritten.** The worst of the three:
  the description still advertised "the four component-twin states, insert classifications, placement changes
  the converter made on the caller's behalf" — content the article no longer has at all, since those codes
  were deleted when `reason` came off the operations. A model chooses an article by this text, so a
  description promising removed content is worse than a terse one.
- **The ROUTE in `routing.md`. Deleted, not fixed.** The conversion article already routes onward to the
  codes article twice, and one of those also says *when* — "load it once per run when `droppedElements` is
  non-empty" — which a routing entry cannot. That duplicate was the only mention outside the conversion
  guides and it was the copy that rotted. `routing.md` already treats `dashboards` this way: the entry is
  listed, the sub-guides are reached through it. No test required the line.
- **The hand-off sentence in `page-modification.md`. Decoupled, not renamed.** The sentence is load-bearing,
  but not for the reason its guard fixture gave. It is not the `Scaffold`/`"actions"` prohibition — the
  converter never inserts there, it targets `MainContainer`, a FAB's `menuItems`, header tools and
  `floatAction`. It is the clause directly above: *"read the names from get-page."* A conversion diff inserts
  into containers the same diff creates, which `get-page` cannot show yet, so an agent obeying that line
  literally reroutes the converter's own inserts. The sentence now says exactly that and names no converter
  field: `viewConfigDiff` already appears nine times in that file meaning the mobile page's own body
  property, so a tenth use meaning the converter's RESPONSE field was ambiguous as well as coupled. The
  guard pins the phrase rather than the schema name.

The rule behind the last two: a general guide must not need a commit when the converter renames a field. Two
fixtures had also recorded opposite principles about that same file — one asserting the mobile guides "stay
converter-free", the other requiring the pointer to exist — and that contradiction is what let the stale line
sit unnoticed. The principle is now written where it is enforced: a pointer to the conversion guide is
allowed wherever a general rule would otherwise contradict it, but a converter response FIELD NAME outside
the conversion guides is not.

---

## 11. Test-suite findings

Converting the assertions off the prose exposed **six tests that could never fail**:

| Test | Why it was vacuous |
|---|---|
| `..._NoComponentTwin_OmitsAdvisoryDiagnostic` | **no assertions at all** — the `constraints` assertion had been deleted and nothing replaced it |
| `..._TwinDeclaredButAbsentFromPage_...` | asserted `NotContain(WebName == "AttachmentList")` on a page with **no** `AttachmentList` |
| 2× `NotContain("NO ROW")`, 1× `NotContain("no title")` | neither string exists **anywhere** in production |
| `AssertReasonCodesAreFromTheClosedVocabulary` predecessor | matched `"multi-data-source"` — prose that no longer exists in any form |
| two `modelConfig` tests | identical arrangements after a dead parameter was removed, while one still promised a warning in its name |

Also removed: a **Blocker** cluster of dead producer state (two `Unavailable` parameters read by nothing, two
unused root-merge locals, `ExcludedComponentsDiagnostics` in full, `SkippedRulesWithoutFilters`) — each
carrying a doc comment claiming the caller surfaces it.

New coverage added for: `parentSource` `"converter"`/`"unknown"`, `ClassifyTwinPayload.NothingToCarry` in
both routes, the re-keyed caption in both directions, the tool's refusal envelope over the real MCP
transport, and the closed operation-key set.

---

## 12. Validation

```
dotnet test --filter "Category=Unit&(Module=Command|Module=McpServer)"   ->  8 172 passed, 0 failed
clio.mcp.e2e MobilePageConversionGuide + PageValidateTool                ->  40 passed (net10.0)
clio-knowledge Clio.Knowledge.Bundle.Tests                               ->  119 passed
WorkspaceTemplateGuidanceDriftTests                                      ->  11 passed
```

0 `error CS`, no new `CLIO*` warnings. Two fixes were verified **non-vacuous** by temporarily restoring the
old code (the declared-empty caption in both directions, and the depth budget — where rewriting the test
exposed that the *old* fixture could not have been testing what it claimed).

---

## 13. Commits

**clio** (16 commits + 1 merge)

```
dd65508e7  refactor!: elementMap becomes a pasteable viewConfigDiff
4378f83c3  refactor!: elementMap becomes operations only; drops move to droppedElements
3e22b85f3  refactor!: elementMap reason becomes a list of {code, params}
c8ff18efd  fix: close the pre-PR review's blockers, including two regressions of my own
daf971517  fix: register a declared caption whose text is empty
e8f1eb9e4  refactor!: remove every prose array from the guide
55fb2994f  refactor: remove the causes behind two diagnostics instead of reporting them
6b737177b  feat!: replace the constraints prose array with typed diagnostics
2992827ad  fix: report a degraded component twin only when one actually degraded
4d6930479  refactor: stop restating how to apply adaptiveLayout and the tab body
0f92fb217  feat: report data-section conflicts as data, one entry per occurrence
123720e6e  feat: make parent provenance total, and drop the constraint that carried half of it
9f8c76336  refactor: report the data-section fallback once, drop the happy-path prose
a909d2777  refactor: drop the constraints that restate structured guide data
9e4f87df1  feat: enforce the unconditional mobile invariants instead of stating them
```

**clio-knowledge** (13 commits + 1 merge) — `libraryVersion` 1.13.67 → 1.13.83, one new article.

Landing after the list above: the commit that added this file, and one per repository closing the guidance
gap described in §10 (a commit cannot carry its own SHA, so they are named rather than listed).

---

## 14. Breaking changes

1. `constraints`, `diagnostics` and `nextSteps` no longer exist on the response.
2. `elementMap` is replaced by `viewConfigDiff`. `webName`, `webType`, `mobileName`, `mobileType`,
   `mobileValues`, `parentSource` and `captionResource` are gone from the entries.
3. `elementMap[].reason` is gone; `droppedElements[].reason` is a list of `{code, params}`.
4. The tool now **fails** where it previously returned a degraded guide (three causes, each naming its own
   fix).
*(No source-side field was renamed in this branch — see §1.3 and §15.)*

`MobilePageConversionGuideModels.cs` carries comment blocks at the removed sites forbidding re-introduction,
with the reasoning.

---

## 15. Remaining work (not in this branch)

Ordered by impact. Items 4, 7, 9 and 10 come from re-auditing this branch against the code AFTER it was
written; several correct or replace what an earlier draft of this section claimed. Two items this list
carried — prose surviving in four sibling collections, and one drop reported twice in two languages — were
DONE in this branch and are described in §5.5 rather than here.

1. **Converted mobile pages are monolingual.** `resourceStrings` is `{key: string}` and
   `ResourceStringHelper.cs:103` writes `["cultureName"] = "en-US"` unconditionally. Measured on a cached
   `Contacts_FormPage`: **195 of 204** source resources carry more than one culture — conversion keeps only
   `en-US`. **Genuine data loss and the largest item left.**

2. **Stage 2 of the binding** — per-mobile-type binding rules in the rules file, after which
   `pendingBindings` disappears and `viewConfigDiff` is 100 % verbatim.

3. **Caption re-keying is unconditional.** `<mobileName>_caption` exists because `update-page` never
   overwrites an existing key, but `MobileTemplateProbe` does not capture the template's resource keys, so a
   real collision cannot be detected and the converter re-keys always.

4. **`AttachmentList` loses its columns** — *narrower than an earlier draft of this section said.* Every
   other list on the real page carries its full column set (`SimilarLeadList` 6, `StageHistoryList` 4,
   `LeadsByCustomerList` and `OpportunitiesByCustomerList` 5 each, `ProductsList` 2). Only `AttachmentList`
   is truncated to one column, and its operation is a **merge** (no `type` in `values`), so the defect lives
   in the twin/merge path, not in list projection generally.

5. **Two merges onto one element** (section 7) — decide whether to coalesce. Confirmed on the OOTB
   `Leads_FormPage`, not only in a fixture: `Tabs` and `CardToggleTabPanel` both merge onto `Tabs`.
   Harmless there only because the second carries no `values`.

6. **Content pruned as identical to the web-template baseline leaves no trace.** `PruneTemplateComponents`
   removes it before the walk, so it is in neither `sourceStructure` nor `viewConfigDiff` nor
   `droppedElements`. This is NOT the same set as inherited chrome, which *is* now traced
   (`drop-inherited-chrome`) — the distinction is worth stating because the two are easy to conflate.

7. **`remove` operation support**, once the converter needs to delete a base-template element. One thing to
   know first: the internal element map still uses `relocate-children`, a word the applier does not have. It
   is filtered out at projection (`ProjectViewConfigDiff`) and reported as
   `drop-container-no-mobile-equivalent`, which is deliberate — but it means the internal and wire
   vocabularies are not the same list, and adding `remove` touches that seam.

8. **Source-side renaming for the mobile→mobile source kind** — 7 remaining `web*` wire names, all still
    present:

    | Current | Proposed |
    |---|---|
    | `containerMap[].web` (`.mobile` stays) | `.source` / `.target` |
    | `componentSuggestions[].primaryWebMerge` | `primarySourceMerge` |
    | `droppedElements[].webName` / `.webType` | `sourceName` / `sourceType` |
    | `webOnlySections` | `sourceOnlySections` |
    | `requestConversions[].webRequest` × 2 | `sourceRequest` |

    *Correction to an earlier draft of this section:* the `sourceType` collision is neither hypothetical nor
    what was described there. The wire ALREADY carries three `sourceType` fields with **two** meanings —
    `componentSuggestions[].sourceType` is a COMPONENT type (`crt.ComboBox`), while `guide.sourceType` and
    the response envelope's `sourceType` are the source PAGE kind (`freedom-web`). Renaming
    `droppedElements[].webType` → `sourceType` is therefore *consistent* with a meaning the wire already has;
    the anomaly is the page-kind field, which should become `sourcePageType`. And the rename reaches AUTHORED
    data: `WebToMobilePageConversionRulesModels.cs` holds five more `"web"` properties in the rules schema,
    so renaming the wire alone splits the vocabulary in half.

9. **`mobileType` is verified against the `environment-superset`** rather than the target catalog.

10. **`resourceStrings` token closure is not a test invariant.** Nested-token collection is covered
    (`WebToMobileConversionServiceTests.cs:3952`) and the whole-map registration rule is pinned (`:8593`),
    but nothing asserts `tokens ⊆ keys` over a whole response — the one check that would make a raw
    `#ResourceString` reaching the device impossible rather than unlikely.

### One item listed here was never true

**Synthesized container names are NOT random.** `StableSuffix` is the first 7 lowercase base36 characters of
SHA-256 over `$"{sourcePage}:{tabName}"`, extended deterministically on collision, carrying a doc comment
that rejects `Guid.NewGuid` by name for exactly this reason. It came from `master` in commit `842ea2574`
(ENG-95573); the plan note this section inherited predates it. A sweep for `Guid.NewGuid`, `DateTime.Now`,
`DateTime.UtcNow` and `Random` across the converter returns **nothing but that comment**, so whole-response
golden-file regression is **unblocked today** — a test to write, not a blocker to clear.

**And one was a design decision, not a gap:** `index` is absent on 149 of 155 entries because
`CompactPositionalIndexes` numbers only the positional (`:top`) siblings of an anchor. Everything else is
appended, and an appended insert has no index to carry. Ordering is implicit *because appending is the
operation.*

---

## 16. Worked examples — real entries, before and after

Every **before** below is copied verbatim from `Leads_FormPage-converter-guide-2026-09-02.json`, the
captured master-shape response every measurement in this document comes from — 27 top-level fields, with
`constraints` and `nextSteps` present and no `parentSource` (that field was both added and replaced inside
this branch). Every **after** is the same entry under the new contract, derived by applying the change, and
the sizes are computed from the two JSON forms rather than estimated. Two examples are marked FIXTURE:
the real page produces nothing of that kind, so they come from the unit-test fixtures instead.

### 16.1 A positional container insert

**Before** — 650 chars

```json
{
  "webName": "ProgressBarContainer",
  "webType": "crt.FlexContainer",
  "operation": "insert",
  "mobileName": "ProgressBarContainer",
  "mobileType": "crt.FlexContainer",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "mobileValues": {
    "type": "crt.FlexContainer",
    "direction": "column",
    "fitContent": true,
    "visible": true,
    "color": "transparent",
    "borderRadius": "none",
    "padding": {
      "top": "none",
      "right": "small",
      "bottom": "none",
      "left": "small"
    },
    "justifyContent": "start",
    "alignItems": "stretch",
    "gap": "medium",
    "wrap": "nowrap",
    "layoutConfig": {
      "row": 1,
      "column": 1,
      "colSpan": 1,
      "rowSpan": 1
    },
    "items": []
  },
  "reason": "container; placed above the mobile Tabs (in MainContainer)"
}
```

**After** — 472 chars (-27%)

```json
{
  "operation": "insert",
  "name": "ProgressBarContainer",
  "parentName": "MainContainer",
  "propertyName": "items",
  "index": 0,
  "values": {
    "type": "crt.FlexContainer",
    "direction": "column",
    "fitContent": true,
    "visible": true,
    "color": "transparent",
    "borderRadius": "none",
    "padding": {
      "top": "none",
      "right": "small",
      "bottom": "none",
      "left": "small"
    },
    "justifyContent": "start",
    "alignItems": "stretch",
    "gap": "medium",
    "wrap": "nowrap",
    "layoutConfig": {
      "row": 1,
      "column": 1,
      "colSpan": 1,
      "rowSpan": 1
    },
    "items": []
  }
}
```

The entry was already 90 % `mobileValues`, so the saving is small — that is the point. What
changed is that the six keys around the payload are now the six the applier reads, and `index: 0` means
what it means to `Insert` rather than being a hint the caller has to translate. The dropped
`reason` was *"container; placed above the mobile Tabs (in MainContainer)"* — `parentName` and `index: 0`
say that, and say it in the form that gets applied.

### 16.2 A data-bound leaf, and the binding the old shape threw away

**Before** — 480 chars

```json
{
  "webName": "LeadDisqualifyReason",
  "webType": "crt.ComboBox",
  "operation": "insert",
  "mobileName": "LeadDisqualifyReason",
  "mobileType": "crt.ComboBox",
  "parentName": "SideAreaProfileFieldFlexContainer",
  "propertyName": "items",
  "mobileValues": {
    "type": "crt.ComboBox",
    "label": "#ResourceString(LeadDisqualifyReason_label)#",
    "labelPosition": "above",
    "listActions": [],
    "showValueAsLink": true,
    "controlActions": [],
    "visible": false,
    "placeholder": "",
    "tooltip": ""
  },
  "reason": "field/leaf; mobile-supported"
}
```

**After** — 342 chars (-29%)

```json
{
  "operation": "insert",
  "name": "LeadDisqualifyReason",
  "parentName": "SideAreaProfileFieldFlexContainer",
  "propertyName": "items",
  "values": {
    "type": "crt.ComboBox",
    "label": "#ResourceString(LeadDisqualifyReason_label)#",
    "labelPosition": "above",
    "listActions": [],
    "showValueAsLink": true,
    "controlActions": [],
    "visible": false,
    "placeholder": "",
    "tooltip": ""
  }
}
```

`reason` here was *"field/leaf; mobile-supported"* — one of the 119 of 155 entries whose reason
said only what `operation` already said.

The more interesting part is what is NOT in either shape: this is a `crt.ComboBox` with **no binding**.
The converter strips `control` / `value` out of the carried values because the mobile binding property is a
type-specific rename, and the master response then said nothing at all about it — the caller was told in
prose to "add the value binding" with no way to know what it had been. The page's own
`viewModelConfig` names the attribute (`LookupAttribute_mfwvuqd`, whose `modelConfig.path` is
`PDS.LeadDisqualifyReason`), so the new response reports it:

```json
"pendingBindings": [
  { "name": "LeadDisqualifyReason",
    "sourceProperty": "control",
    "sourceValue": "$LookupAttribute_mfwvuqd" }
]
```

31 of the 136 inserts need one of these; **zero** carried anything in the captured response. This block is
the one reconstructed example in this section — necessarily, because the field exists to report a value the
old shape had already discarded.

### 16.3 A merge twin that is also a rename

**Before** — 357 chars

```json
{
  "webName": "CardContentWrapper",
  "webType": "crt.GridContainer",
  "operation": "merge",
  "mobileName": "GeneralTabContainer",
  "mobileType": "crt.GridContainer",
  "mobileValues": {
    "adaptive": {
      "small": {
        "columns": [
          "1fr"
        ]
      },
      "medium": {
        "columns": [
          "1fr",
          "1fr"
        ]
      },
      "large": {
        "columns": [
          "1fr",
          "1fr"
        ]
      }
    }
  },
  "reason": "provided by the mobile template (merge into the template's element)."
}
```

**After** — 171 chars (-52%)

```json
{
  "operation": "merge",
  "name": "GeneralTabContainer",
  "values": {
    "adaptive": {
      "small": {
        "columns": [
          "1fr"
        ]
      },
      "medium": {
        "columns": [
          "1fr",
          "1fr"
        ]
      },
      "large": {
        "columns": [
          "1fr",
          "1fr"
        ]
      }
    }
  }
}
```

The rename moves to `nameMap` — `{"CardContentWrapper": "GeneralTabContainer"}` — where it is
stated once instead of on every entry as a `webName`/`mobileName` pair. Five of the 155 entries are
renames, so the map holds five keys.

A merge declares **no `type`**: `mobileType` is gone because the element already exists on the target and
re-declaring its type is how you overwrite something the template owns.

### 16.4 A merge whose prose restated its own values

**Before** — 406 chars

```json
{
  "webName": "Tabs",
  "webType": "crt.TabPanel",
  "operation": "merge",
  "mobileName": "Tabs",
  "mobileType": "crt.TabPanel",
  "mobileValues": {
    "layoutConfig": {
      "column": 1,
      "colSpan": 1,
      "row": 2,
      "rowSpan": 1
    }
  },
  "reason": "provided by the mobile template (merge into the template's element).; moved down 1 row(s): the page inserts 1 element(s) above it, and its parent positions children by layoutConfig rather than by item order"
}
```

**After** — 106 chars (-74%)

```json
{
  "operation": "merge",
  "name": "Tabs",
  "values": {
    "layoutConfig": {
      "column": 1,
      "colSpan": 1,
      "row": 2,
      "rowSpan": 1
    }
  }
}
```

This is the clearest single case in the whole response. The 206-character reason ended with
*"moved down 1 row(s): the page inserts 1 element(s) above it, and its parent positions children by
layoutConfig rather than by item order"* — and `values.layoutConfig.row = 2` **is** that sentence. The
prose was a description of the payload sitting next to it.

### 16.5 The longest reason in the map — 466 characters

**Before** — 828 chars

```json
{
  "webName": "AttachmentList",
  "webType": "crt.FileList",
  "operation": "merge",
  "mobileName": "AttachmentFileList",
  "mobileType": "crt.FileList",
  "mobileValues": {
    "recordColumnName": "Lead",
    "columns": [
      {
        "id": "3fb963b9-dc31-43eb-b899-ecc98131cde8",
        "code": "AttachmentListDS_Name",
        "caption": "#ResourceString(AttachmentListDS_Name)#",
        "dataValueType": 28,
        "width": 200
      }
    ]
  },
  "reason": "Attachments detail. Same component on both sides (crt.FileList) under a different template name, so the page's delta over the web-template baseline — the object-specific link column recordColumnName included — merges onto the template-provided AttachmentFileList; an unchanged property is omitted so the mobile default (e.g. RecordId) stands. — template-provided element — merge the prebuilt mobileValues onto 'AttachmentFileList' by name (do not insert a duplicate)"
}
```

**After** — 258 chars (-69%)

```json
{
  "operation": "merge",
  "name": "AttachmentFileList",
  "values": {
    "recordColumnName": "Lead",
    "columns": [
      {
        "id": "3fb963b9-dc31-43eb-b899-ecc98131cde8",
        "code": "AttachmentListDS_Name",
        "caption": "#ResourceString(AttachmentListDS_Name)#",
        "dataValueType": 28,
        "width": 200
      }
    ]
  }
}
```

Four sentences of reasoning about *why* the merge is shaped as it is, on an entry whose whole
job is "apply these two properties to `AttachmentFileList`". Everything it explained is either in the
payload or in the guidance article.

This entry also shows the defect recorded in §15.4: `columns` carries **one** column, and the merge path is
where that happens.

### 16.6 Five drops, five codes

The five drop shapes the real page produces. `before` is the `elementMap` entry; `after` is the
`droppedElements` entry.

| element | before (chars) | after |
|---|---|---|
| `CancelButton` | 326 | `[{"code":"drop-inherited-chrome","params":{"name":"CancelButton","scope":"MainHeader","target":"FloatingActionButton.menuItems"}}]` |
| `SimilarLeadSearchFilter` | 241 | `[{"code":"drop-excluded-by-rule","params":{"webType":"crt.SearchFilter","hostType":"crt.ExpansionPanel","host":"SimilarLeadExpansionPanel","slot":"tools"}}]` |
| `ExplainProbabilityScoreButton` | 183 | `[{"code":"drop-unsupported-request","params":{"request":"crt.CopilotActionRequest"}}]` |
| `NextSteps` | 123 | `[{"code":"drop-type-not-in-mobile-registry","params":{"webType":"crt.NextSteps"}}]` |
| `OpportunityPlanningToolsContainer` | 163 | `[{"code":"drop-empty-container"}]` |

Full form of the first one:

**Before**

```json
{
  "webName": "CancelButton",
  "webType": "crt.Button",
  "operation": "drop",
  "reason": "action under non-converting scope 'MainHeader'; 'CancelButton' is inherited from the web template (chrome the mobile template provides natively) — not retargeted into FloatingActionButton.menuItems (retargeting would duplicate the native element)"
}
```

**After**

```json
{
  "webName": "CancelButton",
  "webType": "crt.Button",
  "reason": [
    {
      "code": "drop-inherited-chrome",
      "params": {
        "name": "CancelButton",
        "scope": "MainHeader",
        "target": "FloatingActionButton.menuItems"
      }
    }
  ]
}
```

Across all twelve drops the reason text goes from **1 708 to 1 414 characters — only −17 %**, and that
number is worth stating plainly: coding a DROP reason saves almost nothing, because its params carry values
a caller genuinely needs. The saving in this ticket came from deleting `reason` off the 143 operations that
are NOT drops, where it said only what `operation` already said. What the codes bought here is not bytes —
it is that **eleven of these twelve** elements have `componentSuggestions[].category = "DirectMapping"`, so
without a code every one of them reads as conversion loss and invites a re-insert.

### 16.7 An action binding: lost with its element, and lost on its own

**Before** — the chrome case, one of three identical-in-kind entries (221 chars)

```json
{
  "elementName": "SaveButton",
  "binding": "clicked",
  "webRequest": "crt.SaveRecordRequest",
  "reason": "'SaveButton' is inherited from the web template (chrome the mobile template provides natively), which carries its own action"
}
```

**After** — 134 chars

```json
{
  "elementName": "SaveButton",
  "binding": "clicked",
  "webRequest": "crt.SaveRecordRequest",
  "reason": [
    {
      "code": "drop-request-chrome-native"
    }
  ]
}
```

The code carries **no params**: `elementName` and `webRequest` are already on the record, and a param that
repeats a sibling field is the redundancy this whole change removes. The reader is sent to `webRequest` to
decide what to say — `crt.SaveRecordRequest` is the standard request the mobile control provides natively,
so nothing is lost; a `usr.*` request there would mean the page had overridden the button and THAT is lost.

**Before** — the flagged case, ten entries carrying the identical sentence (229 chars each)

```json
{
  "elementName": "SimilarLeadExportDataButton",
  "binding": "clicked",
  "request": "crt.ExportDataGridToExcelRequest",
  "reason": "Request is not in the conversion map (custom or unknown) — verify it exists on mobile before relying on it."
}
```

**After** — 154 chars

```json
{
  "elementName": "SimilarLeadExportDataButton",
  "binding": "clicked",
  "request": "crt.ExportDataGridToExcelRequest",
  "reason": [
    {
      "code": "flag-request-unmapped"
    }
  ]
}
```

Ten copies of one 107-character sentence is what a per-occurrence prose field looks like at scale. The code
also fixes a misreading the sentence invited: this binding was **kept**, not lost, so the caller must not
report loss and must not remove anything.

### 16.8 `captionResource` — 35 keys that were already in `resourceStrings`

**Before**, on `CreateOrderButton`:

```json
"captionResource": {
  "key": "CreateOrderButton_caption",
  "sourceValue": "Create new order"
}
```

and, in the same response:

```json
"resourceStrings": { ..., "CreateOrderButton_caption": "Create new order", ... }
```

All **35 of 35** `captionResource` keys on this page are present in `resourceStrings` with byte-identical
text — measured, not assumed. The per-entry copy is gone and `resourceStrings` is the single source, which
also removes the question a caller had to answer on every entry: register the map, or the entry, or both?

### 16.9 A page business rule (FIXTURE)

`Leads_FormPage` drops no rules, so this is the `ConvertPageBusinessRules_MixedAndOr_DropsRule` fixture.

**Before** — 274 chars

```json
{
  "caption": "Mixed A AND (B OR C)",
  "reason": "Condition mixes AND and OR across nested groups; a mobile page rule supports only a single flat condition group (one logical operator) and cannot represent this without changing when the rule fires — recreate this rule manually."
}
```

**After** — 89 chars (-68%)

```json
{
  "caption": "Mixed A AND (B OR C)",
  "reason": [
    {
      "code": "drop-rule-condition-mixed-and-or"
    }
  ]
}
```

The switch that produced that string had one arm per `PageRuleConditionIssue` value — prose keyed on an
enum, which is a coded field written the long way. `caption` stays, because it is how the developer finds
the rule to recreate; what the code adds is *which* manual fix it needs (split the condition here, choose a
different comparison for `drop-rule-condition-unsupported-comparison`).

### 16.10 A skipped normalization (FIXTURE)

`Leads_FormPage` skips none, so this is `Analyze_PropertyNormalization_ShouldReportAGroupThatOnlySkipped`
— a `crt.IndicatorWidget` binding its whole `config`.

**Before** — 427 chars

```json
{
  "name": "BoundIndicator",
  "type": "crt.IndicatorWidget",
  "properties": [
    "config"
  ],
  "reason": "the element already carries a non-object value at this path — typically a whole-value binding — and a merging rule never overwrites one: replacing it with an object built from the rule alone would destroy the binding and leave the component missing fields it needs, while still appearing normalized. This element keeps its WEB value here"
}
```

**After** — 132 chars (-69%)

```json
{
  "name": "BoundIndicator",
  "type": "crt.IndicatorWidget",
  "properties": [
    "config"
  ],
  "reason": [
    {
      "code": "skip-normalization-path-blocked"
    }
  ]
}
```

Four sentences of reasoning, on every response that skips a branch, about a decision the converter had
already made correctly. The facts stay on the record (`name`, `type`, `properties`, and the group's own
`note` carrying the count); the reasoning is in the guidance article, read once.

### 16.11 What a caller actually pastes

Three consecutive operations from the real diff, `values` elided, in the order they are applied:

```json
"viewConfigDiff": [
  {
    "operation": "insert",
    "name": "ProgressBarContainer",
    "parentName": "MainContainer",
    "propertyName": "items",
    "index": 0,
    "values": {
      "type": "crt.FlexContainer",
      "...": "(elided)"
    }
  },
  {
    "operation": "insert",
    "name": "OverviewTab",
    "parentName": "Tabs",
    "propertyName": "items",
    "index": 1,
    "values": {
      "type": "crt.TabContainer",
      "...": "(elided)"
    }
  },
  {
    "operation": "insert",
    "name": "ProcessingTab",
    "parentName": "Tabs",
    "propertyName": "items",
    "index": 2,
    "values": {
      "type": "crt.TabContainer",
      "...": "(elided)"
    }
  }
]
```

This is the whole deliverable of the change. The same three entries carried **ten, eleven and eleven**
keys in the captured response — `webName`, `webType`, `mobileName`, `mobileType`, a `reason` sentence each,
and `captionResource` on the two tabs — and the caller had to work out which of them the applier wanted and
what the applier calls them. Here there is nothing to work out. (`parentSource` is absent from all three
because it never reached master: it was introduced and then replaced by `unresolvedParents` inside this
branch — see §4.)
