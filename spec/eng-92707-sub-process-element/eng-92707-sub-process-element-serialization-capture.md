# ENG-92707 — Sub-process element: serialization capture

AC4 of the ticket is *"Server serialization matches a designer-built capture."* This document is that
capture.

It is mined from the **shipped 7.8.0 corpus** rather than from one hand-built designer example, for the
reason the ENG-91853 and ENG-95891 captures give: one example cannot separate *what the designer always
writes* from *what that one example happened to have*. A single-example capture on a live stand **was taken on 2026-09-17** (V1) at CrtProcessBuilder 1.6.3.12 - and is diffed against this document in DQ-33: five keys match, `BK15.GT1` and
`BL8` differ, and whether that satisfies AC-4 is the owner's open call.

**Method.** `grep -rl --include=metadata.json "ProcessSchemaSubProcess"` over
`C:/Projects/PackageStore` (1 099 package roots), then a per-occurrence window scan. Measured
2026-09-12 against the working copies as checked out.

| Measure | Value |
|---|---|
| `metadata.json` files containing at least one sub-process element | **262** |
| `Terrasoft.Core.Process.ProcessSchemaSubProcess` occurrences | **420** |
| Distinct package roots | **73** |
| Occurrences carrying the palette UId `49eafdbb-…` | **417 / 420** |
| Occurrences whose window carries `CK5` (`UseLastSchemaVersion = true`) | **3** |
| Occurrences whose window carries `CK1` (`TriggeredByEvent = true`) | **5** |
| `BN2` (size) `"69;55"` | 309 |
| `BN2` (size) `"70;56"` | 105 |
| `BN2` other (oversized — expanded/event containers) | 6 |

A parallel, independently-run corpus scan during the same analysis reported 416 call-activity elements
across 260 schemas, 388 of them in 249 distinct *process* schemas = **14.9 % of the 1 671 process
schemas in the store**, plus 25 in DCM case schemas and 3 inside a page schema's embedded process; and
a split of 340 same-package versus 72 cross-package calls, 29 of the cross-package ones targeting a
package that is **not** a direct `DependsOn` of the caller. The two counts agree to within the slicing
method, and the base figures (files, elements, packages, the 61 multi-instance elements, the one element
with no `CK4`) were reproduced again by a second verification pass on 2026-09-13. Treat the *derived*
splits — same-package vs cross-package, the business motives, the dead mappings — as single-sourced.

The headline: **this is a mainstream element, not a curiosity.** Any rule the builder enforces has 420
shipped counter-examples available to test it against.

### The two provenance stamps, measured

Re-measured 2026-09-14 by parsing each element object rather than scanning text, over 416 elements and
1 650 element parameters:

| Claim | Measurement |
|---|---|
| A synced element parameter's `A3` is the **callee's** schema UId | 1 321 match. Of the 329 that do not, **293 sit on multi-instance elements** — their collection and counter parameters are caller-created, so the mismatch is correct there. On the single-instance path: **1 321 / 1 357 = 97.3 %**, and all 36 exceptions are one element in `LeadFinance`. |
| A caller-written value stamps `L8.GS5` with the **caller's** schema UId | Of 629 parameters carrying a value, **581 (92.4 %)** do. 47 stamp the callee — the dead mappings both code generators skip — and 1 is neither. |

This is the corpus oracle plan D5 rests on, and it is also why D9 refuses a multi-instance element
rather than trying to stamp it: there, the rule genuinely does not apply.

---

## 1. Element identity keys

| Key | Meaning | Written when |
|---|---|---|
| `BL1` | CLR type — `Terrasoft.Core.Process.ProcessSchemaSubProcess` | always |
| `UId` | element UId | always |
| `A2` | `Name` (`SubProcess1`, `SubProcess2`, …) | always |
| `A3` / `A4` | `CreatedInSchemaUId` / `ModifiedInSchemaUId` — **the CALLER schema** | always |
| `A5` | caption resource manager UId | when captioned |
| `IL2` | `ContainerUId` — the lane, or a container element | always |
| `BL3` | position, `"x;y"` | always |
| `BL7` | `ManagerItemUId` = `49eafdbb-a89e-4bdf-a29d-7f17b1670a45` | always (417/420) |
| `BN2` | size, `"w;h"` — `69;55` or the older `70;56` | always |
| `BO2` / `BO3` | element booleans (logging / serialize-to-DB family) | when true |
| `BP2` | the element's parameter collection | always (68 of 416 are empty) |
| **`CK4`** | **`SchemaUId` — the CALLED process** | omitted when `Guid.Empty` |
| `CK5` | `UseLastSchemaVersion` | omitted when false — effectively never written |
| `CK1` | `TriggeredByEvent` | omitted when false |
| `CK2` / `CK3` | `FlowElements` / `Artifacts` | **always**, `[]` for a call activity |

## 2. Element-parameter keys (`BP2` entries)

| Key | Meaning | Note |
|---|---|---|
| `UId` | element parameter UId | **freshly generated** — never equal to the callee's parameter UId |
| `A2` | parameter `Name` | the runtime binding key (see platform reference, §6) |
| `A3` / `A4` | `CreatedInSchemaUId` / `ModifiedInSchemaUId` — **the CALLEE schema** | the provenance half of the value-survival rule |
| `IL2` | `ContainerUId` = the element's UId | |
| `L1` | `DataValueTypeUId` | |
| `L8` | `SourceValue` object | `GS1` = `Source` enum, `GS2` = `Value`, `GS5` = `ModifiedInSchemaUId` |
| `L12` | `Direction` | **absent means `Variable` (2), not `In`** |
| `L18` | `ItemProperties` — the item schema of a collection parameter | |

`GS1` takes `ProcessSchemaParameterValueSource`: `1` = `ConstValue`, `3` = `Script`. A mapped value
from another element is `GS1:3` with a `[# … [Element:{uid}].[Parameter:{uid}] … #]` meta-path in
`GS2`.

## 3. Mapping-row keys (caller schema's `BK15` collection)

| Key | Meaning |
|---|---|
| `A2` | `Name` — **the element's name**, which is how rows are attributed to an element |
| `A3` / `A4` | the **caller** schema |
| `GT2` | `TargetMetaPath` — `[Element:{elementUId}].[Parameter:{elementParameterUId}]` |
| `GT3` | `TargetUId` — the element parameter |
| `GT4` | `SourceSchemaUId` — **the callee** |
| `GT5` | `SourceParameterUId` — the callee's parameter |
| `GT1` | `Source` — a `ProcessSchemaParameterValue` snapshot. **Populated in 1 578 of the 1 672 rows (94.4 %); empty `{}` in 94; absent in none.** Measured 2026-09-18, after a single hand-built designer example came out empty and — because this table recorded the key without its OCCUPANCY — briefly read as the authoritative shape. A key's presence in this capture says it exists, not how often it is filled; where that distinction can decide a parity question, measure it. |

One row per synced parameter, **including nested collection items**.

---

## 4. Worked example — `BulkFileManagement / RunFileCleanup`

Caller schema `d4aa9448-1c4c-48de-aae7-3a8aca009498`; callee `b2aae6b3-7f1e-46ed-908e-2e13c521a4d2`;
element `SubProcess1` `1ea89d86-1364-42b5-bf4b-15163eee0fdf`. Six mapping rows named `SubProcess1`.
The element block, its parameters and one mapping row are quoted verbatim in the
[platform reference §4](eng-92707-sub-process-element-platform-reference.md).

The three facts this one example proves, and which the corpus then generalises:

1. **`CK4` is written after `BP2`.** Serialization order follows `WriteMetaData`; a writer that emits
   the element and its parameters in a different order still round-trips, but a byte-parity diff will
   not.
2. **The two provenance stamps point in opposite directions.** Element parameter `A3`/`A4` = the
   callee; a caller-written value's `L8.GS5` = the caller. This is the whole of the value-survival
   rule in §3.5 of the platform reference, visible in the file.
3. **The binding lives in the mapping rows, not in the parameters.** Nothing in `BP2` names the
   callee's parameter. Delete `BK15` and the element still compiles, still runs, and silently
   re-creates its parameters on the next design-time read.

---

## 5. Generation drift in the corpus — what a writer must tolerate

These were reported by the corpus-mining pass and are single-sourced; each is cheap to re-check and
each changes what a diff tool may assert.

* **Two sizes.** `69;55` (309) and `70;56` (105). `Layout.TaskWidthPx` / `TaskHeightPx` in
  CrtProcessBuilder is `69 x 55`, so the modern value is already the default.
* **7.7-era encoding.** 203 parameter values encode an absent string as the **literal four characters**
  `"null"` (`GS4`, `BL6`, `BL9`, `L2..L5`), and use the obsolete `BL4` instead of `IL2` for
  `ContainerUId`.
* **Two legitimate encodings of the same value.** `GS1:3` with a raw literal in `GS2` (old generation)
  versus `GS1:3` with a macro (current).
* **Real dead mappings ship.** 48 parameter values across 8 elements carry a `[# … #]` expression whose
  `GS5` is the **callee's** UId rather than the caller's — which both code generators silently skip.
  These are shipped processes that violate the rule the platform itself enforces.
* **68 of 416 elements have an empty `BP2`**, one element has **no `CK4` at all**, and **4 elements
  point `CK4` at a schema that does not exist in the package store.** Nothing refuses any of these.
* **Multi-instance elements do not mirror the callee at all.** 61 elements (14.7 %) are multi-instance
  — a count reproduced independently by both research passes, and the basis of the Blocker in plan D9;
  their
  `BP2` holds exactly `InputRecordCollection` + `OutputRecordCollection` (whose `L18` mirrors the
  callee) plus three counter parameters.
* **The element is not always a direct child of the schema's `BK4`.** It also appears inside a
  container's `CK2` (an event sub-process region), so a flat scan of `BK4` undercounts.

---

## 5a. What the live stand adds (2026-09-14)

Read-only `describe-business-process` calls against shipped callers on `http://d_krestov_n.tscrm.com:40001`
(CrtProcessBuilder 1.6.2.10):

* The palette UId `49eafdbb-a89e-4bdf-a29d-7f17b1670a45` is reported live as `managerItemUId`, matching
  the corpus.
* `buildType` comes back **`null`** for a sub-process element — no handler claims it — while a start
  event on the same process reports `"startevent"`. That null is the read-back gap in one field.
* The element's reported key set carries **no called-process reference**:
  `accessRights, caption, changeData, managerItemUId, name, parameters, position, readData, type, uid,
  useBackgroundMode`.
* The **synced parameters already come back**, with `direction`, `isResult`, `source` and the
  `[# … #]` meta-path `value`. Every reported `direction` on that element was `Variable`, consistent
  with the corpus rule that an absent `L12` means `Variable`.

One discrepancy left open: the corpus records 9 parameters on that element and describe reported 7. It
was not chased — the stand's copy of the schema may not be the 7.8.0 one. Do not quote either number as
a describe-completeness claim without re-checking.

---

## 6. What this capture does NOT settle

* The **byte-level order and defaulting** of a freshly built element as opposed to a designer-saved
  one. That needs the live capture in plan step V1: build one element with CrtProcessBuilder, build the
  equivalent in the designer on the same stand, `clio pull-pkg` both and diff the `CK*` / `BP2` /
  `BK15` keys.
* Whether the designer externalises a sub-process element's **caption** into schema resources under a
  key the builder must also write. The caption is auto-generated from the callee's display name on
  every retarget (platform reference §5), so this is a live question, not a formality — see
  `docs/knowledge/platform/parameter-displayvalue-lives-in-the-schema-resources.md` and
  `a-duplicate-schema-resource-key-overwrites-silently.md`.
* Anything about the **multi-instance** serialization beyond the shape named above. It is out of scope
  for this ticket (plan, D9).
