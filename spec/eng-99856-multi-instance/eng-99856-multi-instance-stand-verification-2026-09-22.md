# ENG-99856 — stand verification, 2026-09-22

The four questions no unit test can reach, answered once on a running stand. This discharges PRD
assumptions **A-02, A-03, A-04, A-05** (story 11).

## Run context

| | |
|---|---|
| Environment | `Creatio` — `http://d_krestov_n.tscrm.com:40001` |
| Platform | .NET Framework (`IsNetCore: false`) |
| Package under test | `CrtProcessBuilder` **1.6.6.0** (verified with `list-packages -e Creatio`) |
| clio commit | `6d8eb5c90` (branch `feature/ENG-99856-multi-instance`) |
| package commit | `34b671d` (branch `feature/ENG-99856-multi-instance`) |
| Signed in as | `Supervisor` |
| Date | 2026-09-22, ~06:31–07:10 (stand clock, UTC) |

**Every schema write went one at a time.** No write was issued while another was in flight, and no write
was retried in parallel. No stand cleanup was performed — the ENG-92707 residue is untouched, and so are
the fixtures below.

## Fixtures built for this run

All ten were built by `create-business-process`, dispatched through `clio-run` (the tool is
destructive, so the direct call is refused by design and the executor is the supported route).

| Process | Schema UId | What it is for |
|---|---|---|
| `UsrEng99856Callee` | `80CE1E0D-9027-4DBE-887E-23E223D7C860` | callee: `ItemName` In / `Echoed` Out, formula `[#ItemName#] + " ok"` |
| `UsrEng99856Caller` | `44A705E8-030E-45B0-A652-914E523AC99B` | **A-02 / A-03** — readData → multi-instance sub-process, Sequential |
| `UsrEng99856CallerParallel` | `2FB0B87B-3DFD-4821-B164-5439EEAA5BD6` | **A-04** — the same, Parallel + `useBackgroundMode: true` |
| `UsrEng99856CallerBranch` | `7BD7D4F3-4AA6-44E6-BE68-E36477713279` | **AC-02 / AC-03** — branches on the three counters |
| `UsrEng99856CalleeProbe` | `23743BC6-F1D6-413A-B490-17D759599820` | probe callee: branches on the value it was handed |
| `UsrEng99856CallerOutput` | `964FE356-7DB9-4A9B-9934-C568553A0D37` | **AC-02** — feeds its OUTPUT collection into a second iterator |
| `UsrEng99856CallerSingle` | `0715DDB2-4152-4CBF-92A7-FA399FFEA860` | **A-05** — single-instance baseline |
| `UsrEng99856CallerHand` | `51B1E8A2-9DEE-4FDB-AD67-DB59CA829D71` | **A-05** — single-instance **with a collection available to map** |
| `UsrEng99856CallerParked` | `9ADB1615-3F51-4DA3-9BD8-0495D326ACC1` | superseded — see *What did not work* |
| `UsrEng99856CallerCounters` | `F3A28C8C-2931-4444-871E-FD3D25D85DD1` | superseded — see *What did not work* |

---

## A-02 — does the process designer open and render an element the applier built? **CONFIRMED**

`UsrEng99856Caller` opened at
`…/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/44a705e8-030e-45b0-a652-914e523ac99b`.

The diagram rendered — `StartEvent1`, `Read three contacts`, `Call the callee once per contact`,
`EndEvent1` — and the sub-process element carries the BPMN sequential multi-instance marker.

The decisive part is not the diagram, it is the **properties panel**, because that is where the client
resolves all five multi-instance parameters while loading localizable values. Selecting the element
loaded a panel titled **"Sub-process (Call activity)"** carrying:

```
Which process to run?          ENG-99856 multi-instance callee
Execution mode                 Sequential
Continue execution on errors   (unchecked)
Process parameters
  Input record collection
    Hide nested parameters
      ItemName                 [#Read three contacts.Collection of records:Full name#]
Run current and the following elements in the background   (unchecked)
```

Two things follow from the last parameter line, and they are worth separating:

1. The designer resolved the **dotted per-item source** the applier wrote and renders it in the
   designer's *own* notation — naming the source element, its collection output and the column. The
   metapath is therefore the shape the designer itself produces, not merely one the server tolerates.
2. No exception. The page was reloaded with console capture active before the element was selected, and
   the console carried no error or exception through load and through the panel opening.

**A-02 confirmed.** Nothing routes back to story 3.

## A-03 — does it iterate? **CONFIRMED**

`UsrEng99856Caller` was started through `ProcessEngineService.svc/UsrEng99856Caller/Execute`. The stand
holds 14 contacts and the read is `top 3`, so **N = 3**.

`SysProcessLog` — one caller run produced **three** callee runs, all `Completed`:

```
ENG-99856 multi-instance callee    06:31:32  06:31:32  Completed
ENG-99856 multi-instance callee    06:31:32  06:31:32  Completed
ENG-99856 multi-instance callee    06:31:32  06:31:32  Completed
ENG-99856 multi-instance caller …  06:31:32  06:31:32  Completed
```

`SysProcessElementLog` for the caller run (`ec1877e4-caa3-4054-8399-af2cd55967df`) — one iterator
gateway wrapping three sub-process rows:

```
Started                  Completed                Ms      Element
2026-09-22 06:31:32.739  2026-09-22 06:31:32.743    4.0   ProcessSchemaUserTask      (Read three contacts)
2026-09-22 06:31:32.750  2026-09-22 06:31:32.856  105.5   FlowIteratorGateway        (multi-instance)
2026-09-22 06:31:32.750  2026-09-22 06:31:32.767   17.1   ProcessSchemaSubProcess    iteration 1
2026-09-22 06:31:32.777  2026-09-22 06:31:32.814   37.0   ProcessSchemaSubProcess    iteration 2
2026-09-22 06:31:32.823  2026-09-22 06:31:32.837   14.0   ProcessSchemaSubProcess    iteration 3
2026-09-22 06:31:32.856  2026-09-22 06:31:32.856    0.0   ProcessSchemaTerminateEvent
```

Sequential is strictly sequential here: each iteration starts after the previous one completes.

### The counters (AC-02, AC-03)

`UsrEng99856CallerBranch` carries three conditional flows off the multi-instance element, each leading
to a distinctly captioned Perform task. The element log records **which** branch was taken, and that
record is persistent, so the counters are read from the log rather than from memory.

The run took the arm captioned **`COUNTERS completed=3 total=3 terminated=0`**, whose condition is

```
[#SubProcess1.CompletedIterationsCount#] == 3 &&
[#SubProcess1.TotalIterationsCount#]     == 3 &&
[#SubProcess1.TerminatedIterationsCount#] == 0
```

so all three counters landed, with `CompletedIterationsCount = 3`.

**The reading is taken after the element completed**, which is the only moment it means anything.
`FlowJoinIteratorGateway` uses `CompletedIterationsCount` as the parallel barrier's arrival counter
while the element is running, and the End token then overwrites it with
`TotalIterationsCount − FailedIterationsCount`
(`ProcessInstanceCollectionParametersDataWriter.ActualizeResultParameters`). A mid-run reading is a
barrier count, not an iteration count. Here the condition is evaluated on the flow *leaving* the
element, which is after the overwrite. **AC-03 satisfied.**

### The output collection (AC-02)

`UsrEng99856CallerOutput` chains two multi-instance elements: the first iterates the readData
collection, the second iterates **the first element's `OutputRecordCollection`**, calling
`UsrEng99856CalleeProbe`. The second element's iteration count therefore *is* the number of items in
the output collection.

```
Read three contacts              ProcessSchemaUserTask      Completed
Call the callee once per contact FlowIteratorGateway        Completed
Call the callee once per contact ProcessSchemaSubProcess    Completed   ×3
Iterate the OUTPUT collection    FlowIteratorGateway        Completed
Iterate the OUTPUT collection    ProcessSchemaSubProcess    Completed   ×3
EndEvent1                        ProcessSchemaTerminateEvent Completed
```

**Three items — one per completed iteration.**

The probe callee branches on the value it received (`[#ItemName#].EndsWith(" ok")`), and across the
three probe instances the element log holds:

```
PROBE the echo landed    3
PROBE the value is empty or wrong    0
```

That is the stronger result, and it is worth stating plainly: the per-item value was really delivered,
twice over. The first element's `InputRecordCollection.ItemName` received a contact name (the callee's
formula could only produce `"… ok"` from a non-empty input), and the second element's
`InputRecordCollection.ItemName ← SubProcess1.OutputRecordCollection.Echoed` carried it back out. Both
are dotted per-item paths written by this feature, and both bound at **run time** — which is what the
`ContainerUId` backfill in `ProcessSchemaElementLocator.DescendItemProperties` exists to guarantee. A
design-time-only resolution would have produced three empty values and a `PROBE the value is empty or
wrong` count of 3.

**A-03 confirmed.** The contract is not merely writable; the feature does what it says at run time.

## A-04 — `Parallel` together with `useBackgroundMode` **CONFIRMED, and it does not overlap**

`UsrEng99856CallerParallel` is the same graph with `executionMode: "Parallel"` and
`useBackgroundMode: true` on the element — the shape 39 of the 61 shipped multi-instance elements are
in, so the majority shape.

```
Started                  Completed                Ms       Element
2026-09-22 06:34:35.286  2026-09-22 06:34:35.291     4.2   ProcessSchemaUserTask
2026-09-22 06:34:35.301  2026-09-22 06:34:36.582  1282.1   FlowIteratorGateway
2026-09-22 06:34:35.586  2026-09-22 06:34:35.633    47.5   ProcessSchemaSubProcess  iteration 1
2026-09-22 06:34:35.948  2026-09-22 06:34:35.966    17.7   ProcessSchemaSubProcess  iteration 2
2026-09-22 06:34:36.252  2026-09-22 06:34:36.275    22.9   ProcessSchemaSubProcess  iteration 3
2026-09-22 06:34:36.584  2026-09-22 06:34:36.584     0.0   ProcessSchemaTerminateEvent
```

**The iterations still do not overlap.** Each one starts well after the previous finished, spaced about
300 ms apart — the latency of the background job queue rather than the work. Nothing reordered: the
three ran in input order.

The measured cost of the majority shape on this stand is **1282 ms against 105 ms** for Sequential, a
factor of twelve, for the same three iterations. `Parallel` here buys the *absence of an ordering
guarantee*, not concurrency: `FlowVisitor` drives a single-threaded FIFO queue, and the
`FlowBackgroundToken` that `UseBackgroundMode` inserts moves each iteration onto the scheduler instead
of running it inline. On this stand the scheduler drained them one at a time.

Stated as a limit, because one stand is one stand: this says the majority shape **can** serialise, not
that it always does. It is enough to refuse the inference that `Parallel` means concurrent, which is
the inference that would have been made without the measurement.

**A-04 confirmed** — the majority shape runs, completes, and preserves order.

## A-05 — can a multi-instance element be built **by hand** in this designer? **CONFIRMED**

> **This section was rewritten on 2026-09-22, after the first run recorded "not reached".** The
> gesture failed for a reason that had nothing to do with multi-instance, and a one-variable
> experiment then completed it. Both the original finding and the correction are kept below,
> because the reason the first attempt failed is the whole value of the story.

Both gates at `process-subprocess-schema.js:201` are **open** on this stand:

```js
getIsMultiInstanceSupported: function() {
    return Terrasoft.Features.getIsEnabled("UseMultiInstanceSubProcess") && !this.parentSchema.useForceCompile;
}
```

- `Terrasoft.Features.getIsEnabled("UseMultiInstanceSubProcess")` evaluates **`true`**, measured inside
  the running designer as the signed-in user. The feature is per role, and `AdminUnitFeatureState`
  carries `FeatureState = 1` for both *All employees* and *All external users*.
- `UseForceCompile` serialises as metadata key `BK31` and is omitted when false
  (`ProcessSchema.cs:1493`). It is **absent** from `UsrEng99856CallerHand`, from `UsrEng99856Caller`,
  and from the shipped `ExpireLicenseNotificationProcess` alike — so it is false for all three, and the
  embedded-schema exclusion does not apply to an ordinary process.

**There is no dedicated control.** The conversion is a side effect of mapping a collection, wired in
`ProcessFlowElementPropertiesPage.js`:

```js
t.on("collectionMappingSet",   this._convertElementToMultiInstanceMode, this);
t.on("collectionMappingReset", this._convertElementToSingleInstanceMode, this);
```

So the hand gesture is: open a single-instance sub-process element and give one of its parameters a
collection-valued source. That was attempted on `UsrEng99856CallerHand`, which carries a Read data
element in `collection` mode beside a single-instance sub-process.

**The first attempt failed, and the failure was the finding.** The element's panel offers `ItemName`
and `Echoed` with *Select value*; the value picker offers *Process parameter / System setting /
Formula*; choosing *Process parameter* opens the **Select parameter** dialog, which lists
`Read three contacts` under PROCESS ELEMENTS and then reports, stably:

> There are no parameters of required type

The *Formula* dialog on the same element lists only "Resulting collection" (`ResultEntityCollection`)
and never "Collection of records" (`ResultCompositeObjectList`) with its columns. Beside a Read data
element built **in the designer**, both dialogs show the opposite — "Collection of records" and its
columns, and not "Resulting collection". The visibility is exactly inverted, which is what pointed at
the Read data element rather than at anything multi-instance.

### Root cause — a Read data defect, not a multi-instance one

`ReadDataConfigBinder.WriteMode` sets `IsResult = true` on **both** collection outputs. The platform
sets it on neither:

| Read data element in collection mode | parameters with `IsResult = true` |
|---|---|
| built in the designer (`UsrProcess_d32c1e8`) | **0** |
| shipped `ExpireLicenseNotificationProcess` (two elements) | **0** and **0** |
| built by clio | **2** |

and the designer client throws on more than one
(`parametrized-process-schema-element.js:334`, `getResultParameter` →
`Terrasoft.InvalidObjectState`).

### The experiment that completed the gesture

A package was cut with those two `plan.Outputs.Add` calls removed and **nothing else**, deployed as
1.6.6.1, and `UsrEng99856HandExp` created with it from the same descriptor. On the same stand, in the
same designer session:

- the *Formula* dialog lists **Collection of records → Full name**;
- the *Select parameter* dialog lists the same, instead of "There are no parameters of required type";
- selecting *Full name* **converts the element to multi-instance on the spot** — the `≡` marker
  appears, the panel grows `Execution mode = Sequential`, and the binding reads
  `[#Read three contacts.Collection of records:Full name#]`.

**Verdict: confirmed** — a multi-instance element can be built by hand on this stand. Both gates are
open, the route is "map a collection into one of the element's parameters", and the only thing that
blocked it was clio's own Read data element. Tracked as
[ENG-99967](https://creatio.atlassian.net/browse/ENG-99967); the ticket also carries the second defect
the fix exposes (`describe` reports a Read data element's collections only when they are flagged, so it
has never reported them for a designer-built element).

### Where that goes (AC-07)

The PRD's consequence for a refuted A-05 is "no E2E happy path, and story 13 says so explicitly". That
consequence **does not apply here**, and the reason is worth writing down rather than leaving implicit:
story 13's fixture was always going to be built by clio, and this run proves clio can build one
(A-02), that it runs and iterates correctly (A-03), and that the designer opens and renders it
(A-02). The hand-built baseline was a convenience, not the fixture.

What story 13 must carry is the narrower statement: **there is no hand-built control group**, so a
future divergence between a clio-built element and a designer-built one cannot be caught by comparing
against one on this stand. The divergence found below is an instance of exactly that risk.

---

## Two by-products worth keeping

### 1. The designer turns background mode ON at every conversion; clio does not

`ProcessFlowElementPropertiesPage._convertElementToMultiInstanceMode` ends with

```js
this.set("useBackgroundMode", true);
```

and its de-conversion counterpart sets it back to `false` and resets `backgroundModePriority` to
`Inherited`. So a human who converts an element in the designer *always* gets background mode, without
asking for it.

The element this feature builds does not: `describe-business-process` reports `useBackgroundMode:
false` on `UsrEng99856Caller.SubProcess1`.

This also explains the PRD's own observation that 39 of 61 shipped multi-instance elements carry
background mode — that is not 39 deliberate choices, it is the designer's default arriving with the
conversion.

The owner's ruling on this feature was *"должно работать как в дизайнере"*. This is a divergence from
the designer that no unit test would have surfaced, and it is a decision rather than a defect: matching
the designer means every clio conversion silently costs an order of magnitude in wall-clock time (see
A-04), which is not obviously what a caller asking for multi-instance wants. **Raised, not resolved.**

### 2. The input collection binds from `ResultCompositeObjectList`, never `ResultEntityCollection`

A Read data element in `collection` mode exposes two collection outputs. Only one is type-compatible
with a multi-instance element's `InputRecordCollection`:

| Parameter | Data value type UId | |
|---|---|---|
| `ResultEntityCollection` | `51fb23ba-3eb2-11e2-b7d5-b0c76188709b` | **not** compatible |
| `ResultCompositeObjectList` | `651ec16f-d140-46db-b9e2-825c985a8ac2` | compatible |
| `InputRecordCollection` (multi-instance) | `651ec16f-d140-46db-b9e2-825c985a8ac2` | — |

The first attempt at the fixture mapped `ResultEntityCollection` and was refused by this feature's own
type check — correctly, and with a message that named the incompatibility. The shipped
`ExpireLicenseNotificationProcess` confirms the intended shape: its multi-instance
`InputRecordCollection` sources from `ResultCompositeObjectList`, and each of its item properties
sources from one of that collection's own items.

This belongs in the guidance for the feature, since a caller writing the mapping by hand will reach for
the more obvious name first.

## What did not work, and why it is recorded

Two fixtures are superseded and are left on the stand rather than deleted, because the reason they
failed is a platform fact that will be rediscovered otherwise.

**A completed element's parameter values are gone.** `SysProcessElementData.PropertiesData` is plain
JSON (not a compressed blob), but the row exists only while the element is live: after the
multi-instance element completed, the parked process `UsrEng99856CallerParked` held exactly one element
data row, for the Perform task it was parked on, and nothing for the sub-process element.

**And a process parameter's value is not persisted either.** `UsrEng99856CallerCounters` captures the
three counters into a `Variable` process parameter with a formula task — the formula ran and completed
— and then parks. `SysProcessData.PropertiesData` for that parked instance is 545 bytes of pure
structure (`"HM5": []`, `"HM12": {}`), with no trace of the captured string.

So there is no SQL route to a running process's parameter values. Branching on them and reading the
element log is the route that works, and it is what `UsrEng99856CallerBranch` does.

(A parked process is `Running` with no `CompleteDate` — parked, not hung.)

## Verdicts

| Assumption | Verdict | Evidence |
|---|---|---|
| **A-02** designer opens and renders an applier-built element | **confirmed** | panel "Sub-process (Call activity)" fully rendered; per-item source shown in the designer's own notation; no console exception |
| **A-03** it iterates; output collection fills; counters land | **confirmed** | 3 sub-process element-log rows; 3 callee process runs; second iterator ran 3× over the output collection; counter branch `completed=3 total=3 terminated=0`; 3/3 probes saw the delivered value |
| **A-04** `Parallel` + `useBackgroundMode` | **confirmed, with a finding** | runs and completes in input order; iterations do **not** overlap; 1282 ms vs 105 ms sequential |
| **A-05** a multi-instance element can be hand-built here | **confirmed** | both gates measured open; the gesture completed once clio stopped flagging the Read data collection outputs (ENG-99967) — the element converted in the designer with `Execution mode = Sequential` and the per-item binding |
