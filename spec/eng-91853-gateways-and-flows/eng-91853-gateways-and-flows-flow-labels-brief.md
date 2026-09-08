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
(`scan_flow_captions.py` in the session scratchpad; the scan cross-references flow names from
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
