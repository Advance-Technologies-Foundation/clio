# ENG-91853 follow-up — flow LABELS on the diagram

Parked on 2026-09-07 by the owner, to be implemented in a separate session, on this branch
(`feature/ENG-91853-flow-labels`), as a separate pull request **opened only after the main ENG-91853
PRs merge**. Nothing here is implemented yet. Everything below is measured, not assumed.

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

The name in the key is the flow's `Name` — which is exactly why the rename introduced in 1.4.0.66
matters here: **re-deriving a flow's Name moves the key its label is stored under.** Check what a
re-kind does to an EXISTING label before shipping the field; `CarryOperatorState` already CLONES
`Caption` across a re-kind (and the comment there explains why a plain assignment would alias the
`LocalizableString`), so the value survives the object swap — the open question is the resource key.

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
  coverage, and the bundled archive (next version after whatever the main PR merges — 1.4.0.67 if it
  merges at .66).
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
