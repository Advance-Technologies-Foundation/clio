# ADR: Send email attachments (ENG-95985)

**Status**: Accepted. The owner agreed Q1-Q20 on 2026-10-28 (decisions file of the analysis); this ADR records the
five that shape the architecture (Q1, Q4, Q6, Q8, Q11) and names the related agreed decisions Q9, Q16 and Q20.
**Date**: 2026-10-28
**Jira**: ENG-95985 Send email attachments (Story; epic ENG-92704 Create BP via AI Toolkit)
**Spec**: [spec-eng-95985-send-email-attachments.md](../prd/spec-eng-95985-send-email-attachments.md)
**Story**: [1](../stories/story-eng-95985-send-email-attachments-1.md)
**Test plan**: [tp-eng-95985-send-email-attachments.md](../test-plans/tp-eng-95985-send-email-attachments.md)
**Analysis**: [`spec/eng-95985-send-email-attachments/`](../eng-95985-send-email-attachments/README.md); the
decisions file argues every question below with its source anchors, the design file holds components C1-C10 (C8
dropped) and K1-K12.

Path aliases: `PB/` = crt-process-builder `packages/CrtProcessBuilder/Files/src/cs/`; `PD/` = PackageStore
`CrtProcessDesigner/branches/7.8.0/Schemas/`; `TS/` = core `TSBpm/Src/Lib/`; `CLIO/` and `KB/` = the clio and
clio-knowledge repository roots. Options are compared on clean-slate cost and constraints; the prototype mentioned in
the refinement comment is not an argument for or against any option (owner rule).

---

## Context

The package builds a Send email element through one applier on every route (build, `addElement`, `setElement`,
`PB/Design/ProcessBuildHandler.cs`, `PB/Operations/ElementOperations.cs`). It has no member for attachments, and clio
refuses `email.attachments` pre-send. The designer stores one row per attachment as an `In` collection parameter
`Attachments<N>` with one nested FileLocator item `Attachments<N>FileLocator`, stamped with the process schema; the
run time finds rows by name and stamp and reads the item. The Process file element already has a `files` source
planner (`PB/FileProcessing/ProcessFileProcessingBinder.cs`) that classifies a collection, a collection item, a single
File and an expression and refuses nested sources, and a mapping service in which `ApplyMapping`, `ApplyBlockMapping`
and `BindCollection` plan in one method (`ProcessMappingService.Plan`), with `fromBlock` as the only switch between the
raw and the block form. `BindCollectionItems` does not plan there (`PB/Mappings/ProcessMappingService.cs:89-117`); its
only caller is the process-parameter mirror (`PB/Parameters/ProcessParameterService.cs:1099`), whose target is a
process parameter, so only the from-slot source guard covers it.

## Decisions

### D1 (Q1). The contract is `email.attachments: [entry]`, entry = the `fileProcessing.files` shape

One entry per designer row: `processParameter`, `sourceElement` + `sourceElementParameter`, `expression`, and a
declared-but-refused `value`. The agent already knows this vocabulary from the Process file element, and the planner
is shared (component C2, landed in slice A: `PlanFiles` / `FilesBinding` / `FileItemOf` lifted into
`FileSourcePlanner` behind `IFileSourcePlanner`, `Plan(schema, element, source, wording, userConnection)` with a
`FileSourceWording` (label, member path and the two consequence sentences), returning a data `FileSourcePlan`; the
target names and the write route stay with the caller, and the DI registration lands with its first container
consumer, the Send email applier).

Rejected:
- **B. `email.attachments: ["OF1", ...]` (element names only).** Shorter, but cannot express a process parameter, an
  item path or an expression, and needs a default output per element kind.
- **C. No block: `addMapping` onto `Attachments<N>`.** The slot does not exist until something creates it, so a
  slot-creation operation is needed anyway, and every AC5 refusal would land in the generic mapping service.
- **D. Another member name (`files`).** Avoids the clash with `fileProcessing.attachments` (a SOURCE group of the
  Process file element), but the AC, the designer label and the run-time prefix all say "attachments". The clash is
  solved by one sentence in each guidance article.

Consequences: new `[DataMember(Name = "attachments")]` on `SendEmailDescriptor` and `DescribeSendEmailInfo`; clio's
`ProcessBlockKeyCheck` gains the key and a child key set; the floor rises. An older package drops the unknown member
with success, which the floor and clio's read-back check (K7) exist for.

### D2 (Q4). Modify replaces the list

A sent `attachments` list becomes the element's attachments. Slots whose stored binding equals an entry's planned
binding (resolved meta paths, one-to-one) are kept as they are; the rest are removed after a dependency check
(`IProcessDependencySnapshot.FindDependents`, refusing while something reads the slot, as the designer's
`canRemoveParameter` does); unmatched entries take new slots, numbered lowest-free from 1 over every parameter of the
element (Q5). `attachments` absent = untouched; `[]` removes every slot. Describe returns the same list (D5), so
describe -> edit -> send back is the modify recipe, idempotent by construction.

Rejected:
- **B. Match-or-append, like recipients.** No removal path through the tool; the refinement called this out as
  "discovered late last time".
- **C. Append plus a new `removeAttachment` op.** A new op token costs bytes in the 19,000 B short-form bundle and the
  op drift tests.

Risk carried: an agent may send only the new item. Mitigations: the removal notice names every removed entry in the
write shape (one call restores it), clio's read-back says how many were removed, the modify description says "to add
one, send the described list plus the new one", and the guide says to describe immediately before sending (modify has
no concurrency token).

### D3 (Q6). Refuse what the build can see; warn on flow reachability

Refused, each message naming `email.attachments[<i>]` and the source: (i) an entry with no source (or more than one,
or `value`); (ii) an unresolvable element, parameter or item; (iii) a source that holds no files; (iv) a nested
collection; (v) on the build path, a source element declared after the Send email element in `elements[]` (Q7: the
existing forward-reference sentence, run on PLANNED names so item-only references are covered). A WARNING, on build
and on modify, when the source cannot reach the Send email element along sequence flows (the existing breadth-first
`ProcessGraphBuilder.Traverse`): at run time a source that has not run yields no files and no error (traced; EM10).

Rejected:
- **A. The refusals only.** Leaves the commonest silent failure (a source wired after the email) without a word.
- **B. A flow REFUSAL.** Cheap over `Traverse`, but wrong as a refusal: a source on another gateway branch can reach
  the email and still not run, and a loop can make an "after" source run first, so reachability is a hint, not a proof.

### D4 (Q8, with Q10). Every non-block mapping onto a slot is refused; mapping FROM a slot is refused

In `ProcessMappingService.Plan`, in a NEW `if (!fromBlock)` block placed first, before `ResolveTargetParameter`
(`PB/Mappings/ProcessMappingService.cs:135`), a target on a Send email element (found by the name-only owner lookup
`ResolveTargetOwner`, `:580-586`) whose root parameter name satisfies the guard predicate
`EmailAttachmentSlot.IsReservedName` (`^Attachments\d+$`, case-insensitive, whatever the stamp, so near-misses are
caught) is refused, naming `email.attachments`. It keys on the NAME, so it also covers a build `mappings[]` entry onto
a slot that does not exist yet, a foreign slot and the item. `IProcessMappingService.BindCollection` gains a block form
(the applier's collection writes plan with `fromBlock = true`). A slot or a slot's item as a SOURCE is refused ("only
receives files"; Q10) beside `EnsureNotReadFromAFilesInput`, which runs right after `ResolveStructuredSource` on both
paths (`:110`, `:705`); it sits there, not inside `ResolveStructuredSource`, because that method is private static and
the Send email identity needs a `UserConnection`. This also covers `BindCollectionItems`, whose target is a process
parameter and never a slot. `TakesFiles` does not change.
Precedent: a raw mapping onto a block-owned Process file parameter is already refused, naming the block member.

Rejected:
- **B. Widen `CollectionBindingRules.TakesFiles`** so the pairing rule binds the item and the fileless-source rule
  refuses. `TakesFiles` is reached by every structured mapping of every element and by every describe; PU-42 stays
  green only with a narrow predicate. Larger blast radius, two routes with two sets of checks.
- For Q10, **leaving it to the platform**: the pre-save gate refuses an element `In` parameter as a source
  (`InvalidParameterMappingDirection`) only while `Feature-UseVerificationOfProcessParameterDirection` is on, with a
  less helpful message.

Consequence: the shared mapping service changes, so gate 1 and gate 2 run the full 3-lens fan-out for it, and a
PU-42 mirror test pins that every other `Attachments`-named parameter behaves as before.

### D5 (Q11). Describe returns `email.attachments[]` in the write shape

Each entry is decoded from the stored formulas with the decoder `fileProcessing.files` uses
(`FileProcessingBlockDescriber.DecodeValueSource`) and carries read-back members `slot`, `bindingLevels` (`both` |
`outerOnly` | `itemOnly` | `none`) and `issues` (only when a stored shape would fail or surprise at run time: outer-only,
item-only on a collection item, different sources, stale parent, nothing bound, foreign stamp, unscoped source,
multi-instance element). Order by slot number; null when there is no slot. The generic element `parameters[]`
projection does NOT decode a slot into `sourceElement` / `processParameter` names, because those would be re-sent as
`addMapping`, which D4 refuses. Two predicates, each defined once on `EmailAttachmentSlot`: `IsSlot` is what the run
time reads (top level, case-sensitive `^Attachments\d+$`, `CreatedInSchemaUId` = this process) and drives the
`email.attachments` read and write (find, replace, the describe list); `IsReservedName` is the guard (a root parameter
name matching `^Attachments\d+$` case-insensitively on a Send email element, whatever its stamp) and drives both the D4
raw-mapping guards and this non-decoding, so describe never offers names that a write refuses. clio types
`DescribedEmail.Attachments` and keeps the extension bag.

Rejected:
- **B. Only the generic element `parameters[]`.** Not re-sendable, so it cannot be the modify recipe D2 relies on,
  and it is not the evidence AC1 asks for.

## Related agreed decisions

- **Q9 - no build-time check of `UseProcessEmailAttachments`.** The toggle is a rollout switch shipped on in 7.17.1,
  force-updated on for "All employees" and "All external users" by every install, and never switched off by any
  package or ticket. A check would cost a per-user reader seam for a state that does not occur in practice and could
  only see the building user. The guide carries one troubleshooting line instead. (Changed from the first
  recommendation, a warning notice.)
- **Q16 - `email.attachments` on a multi-instance Send email is refused.** The designer can convert a Send email to
  multi-instance; the conversion moves every root parameter, the slots included, into the item properties of
  `InputRecordCollection`, where the run time's top-level slot search does not look (traced). Detected by
  `MultiInstanceOptions` on the activity (not `CollectionBindingRules.IsMultiInstance`, which answers for a Sub-process
  only); describe reports such an element as found, with an issue. The per-recipient pattern stays a multi-instance
  Sub-process, documented only if EM8 passes. A `removeElement` dependency guard for a source is proposed as a
  follow-up only if EM11 shows that the platform saves a dangling reference green (a silent break). (Changed from the
  first recommendation.)
- **Q20 - an unscoped Process file source is refused.** Object attachments with no record scope and no selecting
  filter, and Generated report with no selecting filter, are refused as attachment sources, through the existing
  ENG-92719 File processing element D16 machinery: the applier tracks every Process file source in the request's `IFileProcessingFilterLedger`,
  `ObjectFileProcessingEmptyFilterRule` also refuses an unscoped element that a Send email slot reads whatever its
  action, and `FileProcessingFilterLedger.EnsureSelectingFilters` judges at the END of the request, so operation order
  in a batch does not matter. Reason: the run time reads attachment files with rights disabled and sends them outside
  the system, and the designer has no such check.

## Consequences

- New package components: C1 contract, C2 shared planner (refactor first, behaviour-neutral), C3 slot model and writer,
  C4 `ApplyAttachments`, C5 raw-route guards, C6 order checks, C7 describe (refactor + new), C9 comment fixes, C10
  capture doc. Every behaviour class gets an interface and a DI registration.
- clio K1-K12: rebundle, key check, floors, descriptions (paid by swaps, Q12), prompts, typed describe, read-back
  checks, capability map, curated re-pin, MCP e2e, knowledge records; MCP resources and `clio/tpl` reviewed only.
  ClioRing consumes none of these tools' contracts beyond the index entry, whose shape does not change.
- clio-knowledge: a new article `process-send-email-attachments`, routed from `send-email.md`, `files.md`,
  `element-catalog.md`, `process-modeling.md` and `routing.md`.
- Residual risk: nothing yet proves that the platform's pre-save gate and the designer's files control accept a
  server-built slot; the STOP gate (EM2 + EM3) after slice B decides before the larger slices.
