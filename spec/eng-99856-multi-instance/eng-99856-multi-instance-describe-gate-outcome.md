# ENG-99856 — the describe gate: outcome

Story 1's deliverable. It answers one question — *why does `describe-business-process` return no
`itemProperties` for a multi-instance element's collections?* — and the answer selects the shape of
stories 9, 10 and 12 and the disposition of AC-15.

**Story**: [story-eng-99856-multi-instance-1.md](../stories/story-eng-99856-multi-instance-1.md) ·
**ADR**: [adr-eng-99856-multi-instance.md](../adr/adr-eng-99856-multi-instance.md) — Decision 0 ·
**Test plan**: [tp-eng-99856-multi-instance.md](../test-plans/tp-eng-99856-multi-instance.md) — C1, §4 ·
**Written**: 2026-09-21

---

## The outcome

> ## OUTCOME 1 — a cold instance reports them.
>
> `describe-business-process` **does** report a multi-instance element's `itemProperties`, in full, with
> the counts the stored metadata carries. Measured on a worker process whose whole request history is
> known, and reproduced on a second read.

### AC-15 disposition

> **AC-15 is met as written (4 and 6).**

No renegotiation is needed, and **no `calleeContract` member is added**. Nobody has to sign anything off;
the Outcome-2 branch of the gate table is closed unused.

Consequences, applying story 1's own gate table:

| | |
|---|---|
| **Story 10** takes its Outcome-1 shape | a regression test pinning nested reporting through the **real** describe path, plus a documented statement that a described `itemProperties` reflects the instance the server holds |
| **Story 9** is unblocked | the `multiInstanceOptions` read block lands beside a projection that already works |
| **Story 12** is unblocked | nothing about the rebundle depends on a contingent describe member |
| **The `calleeContract` view** | dropped from scope — per the ADR's own gate table and story 1's, where it is the Outcome-2 fallback. Note the ADR states **D0-b unconditionally** in its decision list, on a rationale that does not depend on the gate ("different facts with different truth conditions"). This document resolves that ambiguity in favour of the gate tables; an owner who meant D0-b unconditionally should say so |

---

## What was measured

Both instruments were run. Neither asserts AC-15 by itself; together they settle it.

### Environment, quoted with the reading

| | |
|---|---|
| Environment | `Creatio` — `http://d_krestov_n.tscrm.com:40001`, `.NET Framework 4.8.9337.0`, MSSql, IIS site `Creatio`, app pool `Creatio` |
| Package | `CrtProcessBuilder` **1.6.3.31** (`clio list-packages -e Creatio`) — the same version the reference measurement quoted |
| clio | built from `feature/ENG-99856-multi-instance` **`635ca3ac9`**. **Byte-identical describe path to the reference measurement's clio**, verified rather than assumed: `git diff --stat 01c8b677a 635ca3ac9 -- . ':(exclude)spec' ':(exclude)docs'` is **empty**, and `01c8b677a` is the exact master commit the reference measurement quoted |
| Package repo | `crt-process-builder` `feature/ENG-99856-multi-instance` `cdeeaf9` (tests only) |
| Worker process | PID **21896**, started **2026-09-21T19:35:51Z** — started by this session's own first request after an idle-timeout, so the pool was **cold** |
| Read 1 | 2026-09-21T19:43:26Z → 19:43:38Z |
| Read 2 | 2026-09-21T19:45:03Z, **same** worker process |
| Concurrency | strictly sequential. No schema write was issued against this environment in this worker's lifetime; the only prior requests were `get-info` and three read-only `sql` selects |

### Instrument A — the capability pin (no stand)

`tests/UnitTests/CrtProcessBuilder.Tests/ProcessParameterServiceItemPropertiesTests.cs`, committed on the
package branch. **GREEN**, three cases:

- `ToDescribeParameter_ShouldReportNestedItemProperties_WhenParameterCarriesThem` — a parameter the
  platform **deserialized** (written through `WriteMetaData`, read through `ReadMetaData`) reports its two
  item properties by name.
- `ToDescribeParameter_ShouldReportItemPropertiesAtDepthThree_WhenTheShapeIsNested` — nesting survives the
  projection to depth three.
- `ToDescribeParameter_ShouldReportNullItemProperties_WhenTheParameterCarriesNone` — the counter-metric: a
  parameter with none reports `null`, so "no `itemProperties`" and "an empty shape" are not confusable.

So the projection at `Files/src/cs/Parameters/ProcessParameterService.cs:152-154` is not the problem, and a
code defect is excluded before the stand is consulted. The test stays committed either way, as AC-01 asks.

One harness fact worth keeping, because it reproduces the very symptom under investigation: driving the
platform's `JsonDataReader` with `Read()` **and** `ReadInto()` before `ReadMetaData` descends one level too
far, and every field — including `ItemProperties` — comes back at its default. A single `Read()` is correct,
which is the shape `MetaDataSerializer.Deserialize` uses. Each test therefore asserts its **arrange** before
it asserts the projection.

### Instrument B1 (AC-03) — the stored blob, read directly

`SysSchema.MetaData` for `ExpireLicenseNotificationProcess` (schema UId `1893ce0c-d743-4999-bf79-999e874737eb`),
read straight out of the environment's own database (`Data Source=localhost;Initial Catalog=Creatio`):
**157 303 bytes of plain JSON** — the column is not compressed, it is fed straight to the metadata
serializer.

It carries **two** multi-instance sub-process elements, not one:

| element | `BP6` keys | parameter | `L1` | `L12` | `L18` entries |
|---|---|---|---|---|---|
| `SubProcess2` | `JE2 JE3 JE5 JE6 JE7` | `InputRecordCollection` | `651ec16f…` (CompositeObjectList) | 0 (In) | **4** |
| | | `OutputRecordCollection` | `651ec16f…` | 1 (Out) | **6** |
| | | the three counters | `6b6b74e2…` (Integer) | 1 (Out) | — |
| `PushExpiredLicensesNotificationSubProcess` | `JE2 JE3 JE5 JE6 JE7` | `InputRecordCollection` | `651ec16f…` | 0 | **10** |
| | | `OutputRecordCollection` | `651ec16f…` | 1 | **10** |
| | | the three counters | `6b6b74e2…` | 1 | — |

**The 4 and 6 read from the shipped file are exactly what this environment stores.** The second candidate
cause the ADR left open — "a stored blob that differs from the file the counts were read from" — is
therefore **eliminated by measurement**, not by argument.

Two incidental confirmations of platform-facts §2 fall out of the same read: `SubProcess2` serialises its
`BP2` in **client** order (input, output, then counters) while `PushExpiredLicensesNotificationSubProcess`
uses **server** order (counters, input, output) — in one file, in one process. A tool must not key on
position.

### Instrument B2 (AC-02) — the cold describe read

One `describe-business-process` on `ExpireLicenseNotificationProcess`, sequential, against the cold worker.
`exit-code: 0`, correlation id `3a307896378a`.

**Five parameters report `itemProperties`, and the counts match the stored blob exactly:**

```
SubProcess2                                InputRecordCollection    itemProperties = 4
SubProcess2                                OutputRecordCollection   itemProperties = 6
PushExpiredLicensesNotificationSubProcess  InputRecordCollection    itemProperties = 10
PushExpiredLicensesNotificationSubProcess  OutputRecordCollection   itemProperties = 10
<process-level>                            CheckedLicenses          itemProperties = 4
```

`SubProcess2`'s block reads `{"process": "ChecksLicensesForNotificationProcess", "processUId":
"5905d1ab-20b1-43f5-ab65-e9cc65652e01", "multiInstance": true, "inSync": false}` — the same block the
reference measurement reported. Read `inSync: false` as nothing at all here: `MirrorsCallee` compares only
**root** parameter names, and a multi-instance element's callee names live one level down inside the
collections, so it is false by construction for every such element. It is not evidence that anything is
stale.

The items themselves, which is what makes this a contract and not a count:

| `InputRecordCollection` | type | direction | | `OutputRecordCollection` | type | direction |
|---|---|---|---|---|---|---|
| `LicenseProductName` | LongText | Variable | | `LicenseProductName` | LongText | Variable |
| `LicensePackage` | Lookup | Variable | | `LicensePackage` | Lookup | Variable |
| `ExpireDate` | Date | **In** | | `NextExpireDay` | DateTime | Variable |
| `NextExpireDay` | DateTime | Variable | | `IsNotEnoughLicenses` | Boolean | **Out** |
| | | | | `AvailableLicensesCount` | Integer | **Out** |
| | | | | `LicensedUsersCount` | Integer | **Out** |

That direction distribution is `FillCollectionParameters`' routing, visible in live data: the three
`Variable` items appear on **both** sides (the XOR'd twins), the one `In` item only on the input side, the
three `Out` items only on the output side. It confirms platform-facts §1.6 and §3 from the wire rather than
from source.

**The two readings agree on everything except the field under investigation** — and that is worth stating
precisely, because it is easy to over-read. The reference measurement recorded "exactly the five root
parameters — three `Integer/Out` counters, `InputRecordCollection` `CompositeObjectList/In` with its
`Script` mapping, `OutputRecordCollection` `CompositeObjectList/Out` — and `subProcess: { multiInstance:
true, inSync: false, process: "ChecksLicensesForNotificationProcess" }`". Today's read reproduces that
**field for field**, down to the three counters' `source: None` and the input collection's `Script` value
`[#[IsOwnerSchema:false].[IsSchema:false].[Element:{d2a1c1a5-…}]…#]` (the block also carries
`processCaption: "Checks licenses for notification"`, which the reference quote omitted).

So the difference is not a different process, element, element version or serialization — only the nested
collection content. **What that agreement does NOT do is discriminate between the candidate causes.** A
compiled instance would reproduce every one of those fields too: the generators exclude `ItemProperties`
from the parameter initializers they emit but do **not** exclude `SourceValue`
(`ProcessSchemaGenerator.cs:1725`, `:1783` list `DataValueType`, `Manager`, `Caption`, `Position`,
`ContainerItemIndex`, `ManagerItemUId`, `ItemProperties`, `BackgroundModePriority` — and no `SourceValue`).
The agreement narrows *what* differs; it says nothing about *why*.

### Instrument B3 — the control the story did not ask for

A **second** describe, in the **same** worker process, 85 seconds later: byte-identical `itemProperties`
reporting, all five parameters, same counts.

This matters because the most economical version of the stale-cache story is "describe caches a flattened
instance as a side effect of its own read" — `LoadForDescribe` really does reach a factory that mutates
`managerItem.Instance`. **It does not happen.** Describe is idempotent here, which removes the one
self-inflicted mechanism and keeps the cause outside the read path.

---

## Why the reference measurement disagreed, and what is NOT established

The reference measurement (2026-09-21, same stand, same package version 1.6.3.31, clio `01c8b677a`)
reported `itemProperties` on **none** of them. Today's reads disagree with it completely. The code is not
the difference: the package version is identical, and today's clio is master's describe path. So the
difference is a property of the **worker process** each read reached.

**AC-06 asks which earlier operation flattens the cached instance. It is not established, and the
mechanism the ADR named cannot be the whole answer.** The reasoning is short and worth keeping:

- The ADR's mechanism is `SynchronizeParametersInternal`, reached from
  `ProcessSchemaSubProcess.SchemaUId`'s setter. It operates on an **activity's** `Parameters`, and it is
  weaker than "it empties the item properties" suggests. Read line by line
  (`Terrasoft.Core/Process/ProcessSchemaActivity.cs:373-395`): `:378-379` take **clones** of the two
  collections (`GetClonedInputCollectionParameter` `:453-456`, `GetClonedOutputCollectionParameter`
  `:448-451`); `:387-388` clear the **clones'** `ItemProperties`; `:390` `FillCollectionParameters`
  **refills them** from the synchronized `Parameters`; `:393-394` attach the clones. What mutates the
  cached graph in place is `Parameters.Clear()` at `:385` and `:391` — not the two lines usually cited.
  The rebuild does not leave a collection empty; it rebuilds it.

  That correction sharpens the residual rather than closing it: the collections come back empty **only if
  `SynchronizeParameters()` at `:389` left `Parameters` empty** — which is what happens when the callee
  does not resolve. That is a testable proposition, and a better lead than "unknown".
- The reference measurement also reported no `itemProperties` on the **process-level** `CheckedLicenses`,
  which is a parameter of `ProcessSchema` itself. No element rebuild touches it.
- One mechanism that empties an element's collections therefore cannot explain a reading in which a
  process-level collection was empty too. Whatever the earlier worker held, it was **globally** without
  item properties, not an element that had been rebuilt.

**The compiled instance is the hypothesis that fits best, and eliminating it takes more than the sentence
this document first carried.** It has to be said properly, because a compiled instance is globally without
item properties *by construction* — which is exactly the shape the `CheckedLicenses` evidence demands:

- The generators strip `ItemProperties` from **every** parameter initializer they emit — process-level at
  `ProcessSchemaGenerator.cs:1637` and `ProcessSchemaGeneratorNew.cs:1852`, element parameters at
  `ProcessSchemaGenerator.cs:1725`, `:1783` and `ProcessSchemaGeneratorNew.cs:2057`, each passing
  `nameof(ProcessSchemaParameter.ItemProperties)` into `GeneratorUtilities.GenerateProperties`'s exclusion
  list, which really does skip them (`GeneratorUtilities.cs:531-537`).

The first version of this document eliminated the hypothesis by observing that neither generator file so
much as names `MultiInstanceOptions`. **That argument does not hold.** `GenerateProperties` emits
*reflectively* over `metaItemType.GetProperties()` against a deny-list, so a property need never appear in
generator source to be emitted; `MultiInstanceOptions` is `[MetaTypeProperty]`-decorated and writable
(`ProcessSchemaActivity.cs:121-122`) and is **not** in the element path's deny-list, which is only
`ManagerItem, Manager, Parameters, ImageList, ImageName, ParentSchema`
(`WriteSchemaContainer`, `ProcessSchemaGenerator.cs:1515-1516`).

The step that actually carries it is the one the ADR gives and this document had dropped: `GenerateValue`
handles `Color`, `Point`, `Size` and a short list of platform types and otherwise falls through to
`value.ToString()` (`GeneratorUtilities.cs:490`; `GenerateExtendedTypeValue`,
`ProcessSchemaGenerator.cs:111-130`). `ProcessSchemaMultiInstanceOptions` is a plain `MetaItem` with **no
`ToString` override** (`ProcessSchemaMultiInstanceOptions.cs:25`), so emitting it would write the literal
type name into a property initializer and the generated schema would not compile. A compiled instance
carrying `MultiInstanceOptions` therefore cannot exist — which is what makes `multiInstance: true` in the
reference reading decisive.

**And that is a dependency worth naming out loud:** the elimination rests on a single field of the very
reading this document calls unreproducible. It is the strongest available argument, not a closed one.

**A second named candidate, and it has the scope the evidence demands.**
`Terrasoft.Core.ServiceModel/Designers/Mappers/DtoToSchema/SchemaParametersDtoApplier.cs:104-107`:

```csharp
private void ApplyNestedParameters(List<SchemaParameterDto> parameters, T parameter) {
    if (parameters == null || parameters.Count == 0) {
        parameter.ItemProperties.Clear();
        return;
    }
```

That runs generically over parameter kind, so it can flatten a **process-level** parameter — exactly the
reach the `CheckedLicenses` evidence requires and which no activity rebuild has. A save whose DTO omitted
the nested parameters would produce the observed shape across the whole schema. Note the scope this
widens: the ADR's F6 counted "only two lines in all of `Terrasoft.Core`", which is true as stated — this
one is in `Terrasoft.Core.ServiceModel`. The earlier negative was therefore stated more broadly than the
search behind it. **This is the next thing to check**, not a residual unknown.

The other hypothesis, **labelled as a hypothesis because nothing here proves it**: the reference
measurement was taken minutes after `CrtProcessBuilder` was pushed from 1.6.3.14 to 1.6.3.31, and on a
`.NET Framework` environment a package install does not by itself replace the assembly a running worker is
serving. `list-packages` reads the database row, not the serving assembly — which is exactly the trap the
parent ticket's own guidance records ("It does NOT prove WHICH build is serving: on an upgrade a stale
assembly that still answers passes"). A worker mid-reload is a global state, which is the shape the
`CheckedLicenses` evidence demands. Against it: the projection landed at package version **1.6.1.9**
(`crt-process-builder` `a6d2ded`, 2026-09-11), so even the superseded 1.6.3.14 assembly carried it — the
hypothesis needs the worker to have been serving something older still, or to have been in a state where
the package's service was answering from a partially reloaded configuration. That is not proven, and this
document does not claim it.

**What this means operationally is settled even though the mechanism is not**: a describe reading of
`itemProperties` reflects the instance the server is holding at that moment, and an environment that has
just had a package pushed is not a sound place to measure one. Re-measure on a worker whose history you
know. Story 10's Outcome-1 deliverable is exactly the sentence that has to be written into the tool text.

### D0-a, restated

**Describe will not invalidate the manager cache.** A read that evicts a shared, process-lifetime cached
object graph has a write's blast radius, and it would be paid by every other caller on the environment to
harden one read. Instrument B3 also removes the motive: the read is already stable across repetitions
within a worker lifetime. The instability is between worker processes, and the fix for that is to know
which worker you are asking, not to make every read destructive.

---

## Reproduction

Sequential, one at a time, against an environment nobody is writing to.

```bash
# 1 - the stored blob (AC-03). Plain JSON, not compressed.
#     Data Source=localhost;Initial Catalog=Creatio;Integrated Security=true
#     SELECT TOP 1 MetaData FROM SysSchema WHERE Name = 'ExpireLicenseNotificationProcess'
dotnet clio.dll sql "SELECT Name, LEN(MetaData) AS Bytes FROM SysSchema WHERE Name = 'ExpireLicenseNotificationProcess'" -e Creatio
```

```bash
# 2 - the describe read (AC-02). describe-business-process has NO CLI verb: drive `mcp-server`
#     over stdio JSON-RPC with the arguments nested under an `args` key.
#     tools/call -> {"name":"describe-business-process","arguments":{"args":{
#         "environment-name":"Creatio","process-name":"ExpireLicenseNotificationProcess"}}}
dotnet clio.dll mcp-server
```

The graph arrives inside `result.content[0].text` → `execution-log-messages[]` → the one message whose
value parses as an object carrying `elements`. `content[1]` is the executor's "prefer `clio-run`" advisory,
not part of the answer.

Check the worker's identity before trusting a reading:

```powershell
Get-Process -Name w3wp | Select-Object Id, StartTime
```

## Blockers encountered

**The app pool was not cycled deliberately.** `appcmd recycle apppool` was refused by this session's
permission layer. It was not needed: the worker process had been started by this session's own first
request after an idle timeout, PID and start time were recorded, and every request made in its lifetime is
known and read-only. AC-02's substance — a cold pool, a schema untouched by any write in that process
lifetime, and the pool state recorded at read time — is met. Per AC-ERR no warm-pool reading was
substituted for a cold one and no outcome was guessed; the reading reported above **is** the cold one, and
the warm second read is reported separately as the control it is.

## AC roll-up

| AC | State |
|---|---|
| AC-01 — the committed capability pin | **met** — three cases, green, committed |
| AC-02 — one sequential read on a cold pool, fully attributed | **met** — environment, package version, clio commit, worker PID/start, timestamps, element and parameter names |
| AC-03 — the stored `SysSchema.MetaData` read directly, with counts | **met** — 4 and 6, beside the shipped file's 4 and 6 |
| AC-04 — the gate names exactly one outcome | **met** — Outcome 1 |
| AC-05 — the AC-15 disposition in one sentence | **met** — met as written (4 and 6); no `calleeContract` member, nothing for the owner to sign off |
| AC-06 — the flattening operation, and D0-a restated | **met on the AC's own terms; partly met on a stricter reading.** AC-06's antecedent is "Given Outcome 1 **with a stale-cache cause**", and this document argues the cause is probably *not* a stale cache — the self-inflicted cache mechanism is removed by measurement. On that antecedent only D0-a is owed, and it is restated. Read unconditionally the first clause is unmet: the operation is not identified, the ADR's named mechanism is shown insufficient, and two named candidates are left for whoever continues |
| AC-07 — production code untouched | **met** — the package diff is three test files |
| AC-ERR | **one of its two triggers DID fire** — the app pool could not be cycled (the session's permission layer refused `appcmd recycle`). Nothing was substituted for it: the worker was independently cold, and its PID, start time and complete read-only request history are recorded. The stand was reachable and the reading was taken, so the story did not stop |
