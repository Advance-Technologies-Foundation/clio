# ENG-91853 follow-up — flow LABELS on the diagram

Parked on 2026-09-07 by the owner, to be implemented in a separate session, on this branch
(`feature/ENG-91853-flow-labels`), as a separate pull request. **That gate is now open: all three ENG-91853
PRs merged on 2026-09-08** (package `4a000d035`, knowledge `8a21fd9c4` released as 1.13.99, clio
`ddff7cc62`). Nothing here is implemented yet. Everything below is measured, not assumed.

## The gap

A flow clio builds has no diagram label, so a two-branch gateway renders as two identical unlabelled
arrows and a reader cannot tell which branch is which without opening each one's properties. The
chain is entirely on our side, three links:

1. **The descriptor has no field.** `flows[]` takes `source`, `target`, `kind`, `condition`. Nothing
   else. `process-naming`'s N10 already records the label as the ONE thing missing from `flows[]`,
   and says explicitly that ENG-91853 did not add it.
2. **The builder never assigns one.** `NewFlowOfKind` in
   `packages/CrtProcessBuilder/Files/src/cs/Graph/ProcessGraphBuilder.cs` sets `ConditionExpression`,
   `ManagerItemUId` and `VisualType` and no `Caption`. An ELEMENT does get one, on line 131:
   `element.Caption = descriptor.Caption ?? name`.
3. **So nothing writes the resource entry** the designer reads the label from.

**Second half of the gap, and it was a surprise:** `describe-business-process` does not report a
flow's caption either. A flow comes back with exactly
`['branchesOnActivityResult', 'kind', 'name', 'source', 'target']`. So on a designer-authored process
that HAS labels, an agent cannot read them, cannot preserve them, and cannot know they exist.

## Where a flow's label actually lives

Not in `metadata.json`. It is a `LocalizableString`, and it is stored in the package's resource file:

```
<pkg>/branches/<ver>/Resources/<SchemaName>.Process/resource.<culture>.xml

<Item Name="BaseElements.<FlowName>.Caption" Value="No updates available" />
```

Worked example, verified on the stand: `PushNotificationAboutAppUpdateAvailableProcess`
(schemaUId `736340ba-0084-4819-b083-2e91b019d64b`, package `CrtBase`) carries
`BaseElements.ConditionalSequenceFlow1.Caption` = `No updates available`, and the designer draws that
text centred on the connector.

> **ANSWERED on 2026-09-08: the trap is benign and needs none of the three remedies this paragraph proposes.** A re-kind re-materialises the resource row under the new name and leaves nothing under the old one. Measured through the `FreeTheDefaultSlot()` route specifically. Do not implement the guard the paragraph below invites - see the end of this file, and `docs/knowledge/platform/a-flow-rekind-does-not-orphan-its-label.md`.

**THE ONE TRAP, and it is invisible from either side alone.** The name in the key is the flow's
`Name`, and CrtProcessBuilder re-derives a flow's Name on a re-kind (shipped in 1.4.0.66). **The rename and the label live in the
same key, so a label written before a re-kind is orphaned by it** — the row stays in the resource
file under the old key while the flow now looks for a new one. Nothing in the rename code mentions
labels, because they did not exist when it was written, and nothing in the label code will mention
the rename unless someone puts it there. That is the whole reason this paragraph exists.

`CarryOperatorState` already CLONES `Caption` across a re-kind (its comment explains why a plain
assignment would alias the `LocalizableString`), so the in-memory VALUE survives the object swap.
The open question is only the resource key, and it has to be answered before the field ships: either
the rename carries the resource row with it, or a re-kind must not rename a flow that carries a
label, or the label is stored somewhere the Name does not address.

## How much of a norm this is

Measured over the whole shipped 7.8.0 corpus, 1710 schemas containing flows
(`flow-caption-corpus-scan.py`, committed beside this brief and runnable as
`python flow-caption-corpus-scan.py <PackageStore>`; the scan cross-references flow names from
`metadata.json` against the per-schema resource file):

| flow kind | flows | labelled | share |
|---|---|---|---|
| **conditional** | 1405 | 1193 | **84.9 %** |
| default | 757 | 193 | 25.5 % |
| sequence | 7599 | 50 | 0.7 % |
| all | 9761 | 1436 | 14.7 % |

Read it as two facts, not one average: for a CONDITIONAL flow a label is the norm, and for a plain
sequence flow its absence is the norm. So the field is not a nicety for the branch case, and it is
not wanted at all for the ordinary case.

**Shipped labels are business phrases, not condition text.** Samples, verbatim: `Information
received`, `Create order`, `User Not Found`, `Proceed to order`, `If job does not exist`,
`Prediction enabled` / `Prediction disabled`, `Closed with negative result`, `no record found`,
`Distribute later`, `Complete`, `Default flow`, and plain `Yes` / `No`. Whatever guidance this ships
with should say that: a label names the OUTCOME in the reader's language, and repeating the
expression is the thing to avoid.

## Surfaces a complete change has to touch

Four, plus the archive:

- **package** — a `label` on `flows[]` (build path) and on `addFlow`; assignment of `Caption`;
  reporting it in the describe contract; an operation to set/clear it on the modify path. Extending
  `setFlow` with a `label` field is the natural home — it already takes `source`/`target`/`kind`/
  `condition` — but check the interaction with the Name re-derivation above first.
- **clio** — tool descriptions for create/modify, `docs/McpCapabilityMap.md`, `clio.mcp.e2e`
  coverage, and the bundled archive (the next version after what master now ships, which is
  **1.6.0.6** — so plan on 1.6.0.7. Read it out of the DESCRIPTOR rather than off this line: the
  number moved eight times during the main ticket because `main` kept claiming the next one).
- **clio-knowledge** — `process-naming` N10 says the label is missing and must stop saying so;
  `process-branch-conditions` should carry the style rule and the 85% figure. `libraryVersion` +
  sequence bump, as always.
- **`docs/knowledge/`** — the resource-key fact above is exactly a record: the code does not say it,
  and the failure mode is silent (a caption set with no resource row simply does not render). Write
  it in the same PR.

## Verify before building

> **ANSWERED on 2026-09-08, and the answer was yes.** The paragraph below is kept as written because it is the reason the spike was done first; read "Measured 2026-09-08" at the end of this file for what it found. Nothing here turned out to be rework.

The load-bearing assumption is that assigning `ProcessSchemaSequenceFlow.Caption` from our build
path produces the resource row and a rendered label. It is plausible — the element path already
does this and element captions render — but it has NOT been demonstrated for a flow. Do the minimal
spike first: accept `label`, assign `Caption`, rebundle, deploy, open the process in the designer,
look. If it does not render, everything above is rework.

## Unrelated find, worth carrying to the autolayout task

That same shipped process routes its back edge ABOVE the row of elements:
`m230,184 L230,88 L742,88 L742,184`. The deferred autolayout defect is that our back edge is drawn on
top of the forward flow; the platform's own content shows the intended answer is to lift it onto a
free row rather than to move the elements.

## Master moved under this brief on 2026-09-07

The guidance library SPLIT its process guide set while this was parked, so two paths named above have
moved and one target changed:

- what `create-business-process` can build today, and the element catalog, left `process-modeling`
  for a new **`process-element-catalog`** — that is where a label field has to be declared buildable,
  and where the ENG-91853 slice (gateway ELEMENTS, all three flow kinds) was carried during the merge;
- `process-data-source-filters`, `process-task-category` and `process-task-performer` were split out
  as well, so the set is 14 articles now;
- `process-naming` N10 is still the place that says the label is the ONE thing missing from `flows[]`,
  and still the place that has to stop saying it.

`libraryVersion` is **1.13.99** on master and released as such (2026-09-08 12:59Z). The clio-side pin
`clio.tests/Command/McpServer/Fixtures/curated-knowledge-names.json` already carries all 14 names, so
adding a label needs no new article and no new name — only content changes and a version bump.

## Refreshed 2026-09-08, after the main ticket merged

Three numbers in this brief had gone stale and are corrected above: the archive to plan on is
**1.6.0.7** (master ships 1.6.0.6, not 1.4.0.69), the guidance library is **1.13.99** and released,
and the "opened only after the main PRs merge" gate is now open.

Two things to know before starting, neither of which is in the text above:

- **This branch carries the brief and `flow-caption-corpus-scan.py`, and master does not.** They
  exist nowhere else, so start from this branch rather than from master, or they are invisible.
- **The re-kind trap named above got sharper during the main ticket, and in a way that matters
  here.** `FlowKindRules.FreeTheDefaultSlot()` now tells a caller to free a gateway's default slot
  with `setFlow` + `kind: conditional` on the EXISTING default — that is a re-kind, on a flow a
  designer is most likely to have labelled, since 85% of conditional flows carry one. So the remedy
  this ticket shipped routes callers straight through the orphaning path. Whatever answer the resource
  key gets has to cover that route specifically, and there is a test to extend rather than write:
  `AddFlow_SecondDefaultOffADecidingGateway_NamesARemedyThatWorks` already executes it.

Everything else in this brief was re-read against master at `ddff7cc62` and still holds.

## Measured 2026-09-08 — the spike answered both open questions, and both answers were favourable

Everything in this section is a measurement on `Creatio` (`<dev-stand>:40001`, .NET Framework, MSSQL),
against `CrtProcessBuilder` **1.6.0.7** for the spike and **1.6.0.8** for the shipped field. The spike
process is `UsrBPFlowLabelSpike1` (schemaUId `619e63b4-0432-41d1-8092-126649dd18a4`, package `Custom`).

### 1. The load-bearing assumption HOLDS. A flow Caption renders.

`flows[].label` → `ProcessSchemaSequenceFlow.Caption` → a resource row → a label drawn on the
connector. All three kinds, in one process, on the first attempt:

| flow | key written | drawn |
|---|---|---|
| `SequenceFlow_StartSpike_Threshold` | `BaseElements.SequenceFlow_StartSpike_Threshold.Caption` | `Amount known` |
| `ConditionalFlow_Threshold_EndHigh` | `BaseElements.ConditionalFlow_Threshold_EndHigh.Caption` | `Above the threshold` |
| `DefaultFlow_Threshold_EndLow` | `BaseElements.DefaultFlow_Threshold_EndLow.Caption` | `Everything else` |

The keys are exactly the ones the corpus scan predicted, and the designer draws all three centred on
their connectors — verified visually and in the DOM (each label is a `div.foreign-text` inside the
designer's canvas SVG, positioned on its connector). So nothing above this line was rework.

The designer URL, which cost more time than the spike itself:
`/0/Nui/ViewModule.aspx?vm=SchemaDesigner#process/<schemaUId>`. Recorded in
`docs/knowledge/platform/the-process-designer-has-one-url-and-guessing-wedges-the-shell.md`, together
with the DOM query that reads the labels back without a screenshot.

### 2. THE TRAP IS BENIGN. A re-kind does not orphan the label.

This was the question the brief said "has to be answered before the field ships", and the answer is
that neither of the three remedies it listed is needed. Driven through the exact route
`FlowKindRules.FreeTheDefaultSlot()` prescribes — `setFlow kind: conditional` on the existing default:

```
before   BaseElements.DefaultFlow_Threshold_EndLow.Caption     = Everything else
after    BaseElements.ConditionalFlow_Threshold_EndLow.Caption = Everything else
         (no row left under the old key — exactly three rows in the schema, one per flow)
```

Two mechanisms make it work and neither mentions the other: `CarryOperatorState` already CLONES
`Caption` onto the replacement object, and the platform re-materialises the whole resource set from
the object graph at save time — so the row is not "moved", it is rewritten under whatever name the
flow has when the schema is saved, and the old key is simply never written again. Recorded in
`docs/knowledge/platform/a-flow-rekind-does-not-orphan-its-label.md`, including the warning against
the guard this trap invites: refusing a re-kind on a labelled flow would break the shipped remedy on
the flows most likely to be labelled.

### 3. Two more behaviours, measured because the contract now promises them

- **An empty label CLEARS.** `setFlow` with `label: ""` deleted the row outright rather than leaving
  it blank, and `describe` then reports `label: null`. So "omitted keeps, empty clears" is a real
  distinction on the server and not just in clio's parsing.
- **A relabel on a kind NO-OP lands.** `setFlow` with the kind the flow already has takes the
  early return in `SetFlow`; the label is applied before it returns. Measured: `Amount known` →
  `Amount confirmed` on an unchanged `sequence` flow. Without that line the operation would have
  reported success and written nothing, which is the only way to relabel a flow at all — `kind` is
  mandatory on `setFlow`.

### 4. describe DID report nothing, and now reports the label

Confirmed on 1.6.0.7 before the change: a flow came back with exactly
`branchesOnActivityResult, condition, kind, name, source, target`. On 1.6.0.8 it carries `label`.
clio's `DescribedFlow` gained a TYPED property rather than relying on its `[JsonExtensionData]` bag,
because the post-write guard reads it by name.

### What shipped, and where the version landed

**SUPERSEDED — do not run a provenance check against the pair below.** The archive named here was the
FIRST cut; the branch has rebundled several times since. The shipped pair is whatever
`ExpectedArchiveVersion` / `ExpectedProducingCommit` pin in
`clio.tests/Common/BundledProcessBuilderPackageTests.cs`, and those are the only values the
staleness and tag controls may be run against. Kept as a record of the first cut and why it was
numbered as it was:

The archive was **1.6.0.8**, not the 1.6.0.7 this brief planned on: 1.6.0.7 was spent on the spike and
installed on the stand, so re-cutting under it would have reached that environment as "already
converged" — the exact trap `rebundle-process-builder.ps1` documents. Package commit
`98b1a8c` (the spike is `92c1b0f`, kept as its own commit because it is the measurement, not the
feature).

`[RequiresPackage]` stayed at **1.6.0.3** deliberately. A label is an optional field whose absence is
detectable after the fact, so it gets the read-back warning the sendEmail body macros already
established (`FlowLabelExpectation`) rather than a floor that would refuse every build and edit on an
environment one archive behind. The reasoning is in the comment block above
`CreateBusinessProcessOptions`, next to the rule it looks like an exception to.

### Two things fixed on the way, both of which blocked the canonical procedure

- `rebundle-process-builder.ps1` still ran the package tests from `tests/CrtProcessBuilder/` — the
  path they were moved out of in package commit `3f791d7`. It failed as
  `MSBUILD : error MSB1009: Project file does not exist`, which reads as a broken checkout. The
  script now DISCOVERS the project and refuses unless it finds exactly one, because zero matches
  would have skipped the suite and shipped the archive anyway.
- Both `.application/net-framework` junctions in the package checkout were dangling (the local
  Creatio core moved to `.devenv/repos/core`), and they fail differently — `core-bin` takes the whole
  build down, `bin` takes only the test project down with one MSB3245, AFTER the package's own
  `Build succeeded`. The existing knowledge record covered only the first; it now covers both and
  names the new core location.

### Still open, and NOT part of this change

The autolayout defect this brief noted in passing is untouched: our back edge is still drawn over the
forward flow rather than lifted onto a free row. The shipped example
(`PushNotificationAboutAppUpdateAvailableProcess`, `m230,184 L230,88 L742,88 L742,184`) remains the
reference for what the intended answer looks like.
