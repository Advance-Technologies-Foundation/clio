# Manual test prompt — writing an activity-result selection (ENG-91853)

The suite in `…-activity-result-dialect-manual-test-cases.md` covers the DEFECT: a formula written where
the designer edits a selection. This one covers the WRITE that closes it, and every case ends in the two
places a unit test cannot reach — **what the designer renders** and **what the runtime does**.

Hand the whole of the next section to an AI session with browser access to a stand.

---

## Prompt

````
You are running a manual test pass on a live Creatio stand. Report PASS or FAIL per case with the
evidence you actually saw — a screenshot or the exact text — never a restatement of the expectation.

## Before you start

PRECONDITION THAT GATES EVERYTHING: the stand's `CrtProcessBuilder` must be **1.6.2.18 or newer**.
Check with `clio list-packages -e <env>` and stop if it is lower — every write case below will be
refused with "Operation 'setFlowResults' is not supported", which is the correct answer on an old
package and proves nothing about this feature. Say so and stop rather than reporting failures.

A conditional flow carries its predicate in ONE OF TWO disjoint slots:
- a FORMULA (`condition`), for a source that enumerates no activity results;
- a SELECTION (`results`), for a source that does.

The designer decides which editor a connector gets by asking the SOURCE element's properties page. When
it opens the selection editor there is **no formula field on that panel at all** — that is the single
most important thing to look at in every designer step below.

WHEN READING A PROCESS BACK: on a versioned process `describe-business-process --process-name X`
resolves to the family ROOT, which is not what the designer edits or the runtime executes. Read
`version` / `isActiveVersion` / `activeVersionName` in every response and address the active schema by
`--process-uid` when they disagree. Record the schema UId you acted on in each case.

WHEN CHECKING RUNTIME, read the RIGHT TABLE. `SysProcessLog` logs the ROOT row only, so it tells you a
process started and nothing about which elements ran. The per-element trace is **`SysProcessElementLog`**
(`SchemaElementUId`, `Caption`, `StatusId`, `StartDate`, `CompleteDate`, `SysProcessId`), and that is the
only thing that proves WHICH branch was taken. Every runtime assertion below means that table.

AND DO NOT READ A PARKED PROCESS AS A HUNG ONE. A root `SysProcessLog` row with no `CompleteDate` is the
NORMAL state of a graph waiting on a human step, and of one waiting for an approval verdict. It is not a
fault, and reading it as one cost a whole round of this suite: an approval that has not been decided yet
looks exactly like a broken engine. Confirm with `SysProcessElementLog` which elements completed, and
with the `Activity` table that the human step really has a record to complete. A process parked on a
human step resumes when you complete that step's `Activity` (odata update its status to Completed).

Do NOT auto-open a browser to "verify" a successful write. Where a case says to look in the designer,
open it deliberately and report what is on screen.

---

### TC-W01 `setFlowResults` on an Approval — the boxes are ticked and the connector is valid

**Preconditions:**
A process with a start event, an `approval` element named `Approve order` configured against any object,
and two end events `EndOk` / `EndNo`, both connected by PLAIN flows. Nothing else.

**Steps:**
1. `modify-business-process` with
   `[{ "op": "setFlowResults", "source": "Approve order", "target": "EndOk", "results": ["Positive"] }]`
2. `describe-business-process` and read the flow `Approve order -> EndOk`.
3. Open the process in the designer and click that connector.
4. Save the schema from the designer without changing anything.

**Expected result:**
* Step 1 succeeds.
* Step 2: `kind` is `conditional`, `results` is `["Positive"]`, `resultsActivity` is `Approve order`,
  `branchesOnActivityResult` is `true`, and `condition` is `null`.
* Step 3 — THE DESIGNER: the panel is headed `What is the result of an element "Approve order"?`,
  lists `Positive`, `Negative`, `Canceled` as checkboxes, **`Positive` is TICKED**, and there is **no
  formula field on the panel**.
* Step 4: the schema saves and the connector is NOT marked invalid. Specifically, the save does not
  raise `Required fields of some elements are not filled in`.

### TC-W02 The same selection declared on the BUILD path, in one step

**Preconditions:** None — this case creates its own process.

**Steps:**
1. `create-business-process` with a descriptor whose Approval branch is declared as
   `{ "source": "Approve order", "target": "EndOk", "kind": "conditional", "results": ["Positive"] }`.
2. Open the process in the designer and click that connector.

**Expected result:**
* The process is created in ONE call and the connector already carries its selection.
* The designer shows the same ticked checkbox list as TC-W01.
* This is the point of the build path: at no moment was a version of this process saved carrying a
  connector the designer would mark invalid. Say explicitly whether that held.

### TC-W03 Runtime — the branch really routes on the chosen result

**Preconditions:** TC-W01's process, with `Positive` selecting `EndOk` and a second branch selecting
`Negative` for `EndNo`. Both arms should do something observable (a Perform task each, say).

**Steps:**
1. Start the process against a record.
2. Approve the visa.
3. Read `SysProcessElementLog` for that run — NOT `SysProcessLog`, which carries the root row only.
4. Repeat from step 1 with a fresh record, and REJECT the visa instead.

**Expected result:**
* Approving executes the `EndOk` arm and NOT the `EndNo` arm.
* Rejecting executes the `EndNo` arm and NOT the `EndOk` arm.
* Neither run executes both. If both arms ran, the branch is a parallel split and the case FAILS —
  report which flows the log shows.

### TC-W04 All THREE outcomes, because a two-way split drops one

**Preconditions:** TC-W03's process.

**Steps:**
1. Start the process, then CANCEL the approval rather than approving or rejecting it.
2. Read `SysProcessElementLog`.
3. Now add a third branch with `results: ["Canceled"]` to its own end, and repeat.

**Expected result:**
* Before step 3, the canceled run takes NEITHER branch — report exactly what the process did (it should
  reach no end event through those arms). This is the case a two-way Approved/Rejected split loses.
* After step 3, the canceled run takes the third arm.

### TC-W05 `results` CLEARS a stored condition, and the designer stops showing one

**Preconditions:** An Approval branch that ALREADY carries a formula condition. You cannot create one:
writing a formula there is refused now, and the designer never offered the option. So this case needs
LEGACY content — a process written before the refusal existed, which is what the clear is for. If the
stand has none, report the case as NOT RUN and say why rather than arranging something else.

**Steps:**
1. `describe-business-process` and note `condition` is non-null.
2. `setFlowResults` on that same flow with `["Positive"]`.
3. `describe-business-process` again.
4. Open the connector in the designer.

**Expected result:**
* Step 3: `condition` is now `null` and `results` is `["Positive"]`. The expression is GONE, not parked
  beside the selection.
* Step 4: the checkbox list, `Positive` ticked, no formula field and no residue of the old expression
  anywhere on the panel.

### TC-W06 A condition onto a connector that ALREADY carries a selection is refused

**Preconditions:** TC-W01's process, after its selection is written.

**Steps:**
1. `modify-business-process` with `setFlowCondition` on that same flow, condition `1 > 0`.
2. `describe-business-process`.

**Expected result:**
* Step 1 is REFUSED, and the message says the flow branches on the preceding activity's result rather
  than on a formula, and tells you to clear the selection in the designer first.
* Step 2: the selection is UNCHANGED. A refusal must not be a partial write.

### TC-W07 An unknown caption is refused WITH the set the element offers

**Preconditions:** TC-W01's process.

**Steps:**
1. `setFlowResults` with `results: ["Approved"]` — a plausible-looking caption that does not exist.

**Expected result:**
* REFUSED, and the message LISTS what the element actually offers: `Canceled`, `Negative`, `Positive`.
* This refusal is the only way to discover an element's results — no read API returns them — so check
  the list is complete and spelled as the designer spells it.

### TC-W08 Two sibling branches may not claim the same result

**Preconditions:** TC-W01's process, with `Positive` already selecting `EndOk`.

**Steps:**
1. `setFlowResults` on `Approve order -> EndNo` with `results: ["Positive"]`.
2. Open the FIRST connector in the designer, then the second.

**Expected result:**
* Step 1 is REFUSED, naming the sibling that already selects it.
* Step 2: the designer does not offer `Positive` on the second connector at all — it hides a result a
  sibling has taken. The refusal exists because the runtime would otherwise start BOTH arms.

### TC-W09 Send email is refused, and the designer shows why

**Preconditions:** A process with a `sendEmail` element branching two ways.

**Steps:**
1. `setFlowResults` on one of its outgoing flows with any caption.
2. Open that connector in the designer.

**Expected result:**
* Step 1 is REFUSED — the element does not offer result values through this surface, and the message
  points at `condition` as the dialect to use.
* Step 2 — THE POINT OF THE CASE: the designer shows a **formula field** on that connector, not a
  checkbox list. This is the one element whose server-side schema declares results while its properties
  page offers no selection editor, and the refusal exists precisely so a selection is not written
  somewhere the designer would erase it on open.

### TC-W10 A connector leaving a GATEWAY — keyed on the activity BEHIND it

**Preconditions:** A process where an Approval feeds an exclusive gateway by a PLAIN flow, and the
gateway has a `default` branch plus one more outgoing flow. Out of a deciding gateway a flow can only be
conditional or default — there is no plain flow to convert later, so declare the branch with its
selection in the same call.

**Steps:**
1. Declare the gateway's non-default branch with `kind: "conditional"` and `results: ["Positive"]`.
2. `describe-business-process` and read that flow.
3. Open the connector in the designer.
4. Repeat the whole case with TWO chained gateways between the Approval and the branch.

**Expected result:**
* Step 1 succeeds.
* Step 2: `resultsActivity` names the **Approval**, NOT the gateway. This is the field a caller must not
  assume — writing the selection back onto the gateway would change which activity decides the branch.
* Step 3: the checkbox list, `Positive` ticked. The designer walks one hop back through the gateway and
  so does the tool.
* Step 4: `results` is REFUSED and the designer shows a formula field. One hop, no recursion, on both
  sides — the limit is the designer's and the tool matches it rather than being cleverer.

> **Step 3 VERIFIED** on schemaUId `5f533dd1-5dd4-48bd-92f7-2e27497d99b7`, by a human opening the
> connector after two automated attempts failed. Panel "Conditional flow", headed *"What is the result
> of an element 'Approve order'?"* — the APPROVAL's name, not the gateway's — with Positive ticked.
> That heading is the second piece of evidence in the same view: it is rendered from the resolved
> activity, so it confirms the designer followed the map key one hop back rather than reading the
> flow's own source.
>
> Why this step cannot be substituted by the API ones, in case a future run is tempted to skip it:
> every other step proves we WROTE the map we intended. This is the only one that proves the designer
> can READ it when the key is not the flow's source — getProcessActivityBySelectedResults resolves the
> element BY the map key, a different path through the client than the one TC-W02 covers.
>
> AUTOMATION HAZARD, recorded so the next run does not lose time to it: this gateway's outgoing
> connector carries no caption in the accessibility tree BEFORE selection, so find-by-label has nothing
> to grab and coordinate clicks land on the canvas. The Approve—EndOk/EndNo connectors do not behave
> this way. If it resists again, have a human open it rather than spending stand time on coordinates.
>
> OBSERVED IN THE SAME SCREENSHOT, worth knowing: the canvas draws a **`Positive` caption on the
> connector line itself**, so a gateway-keyed selection is legible from the diagram without opening the
> panel. The toolkit does not write that caption — `ApplySelection` never touches Caption or Label —
> so it is rendered by the designer from the selection. Do not mistake it for a stored `label`, and do
> not assume writing a `label` would replace it.

### TC-W13 A formula on a result-enumerating connector is REFUSED

**Preconditions:** TC-W01's process, with the `EndOk` branch still plain.

**Steps:**
1. `setFlowCondition` on `Approve order -> EndOk` with `1 > 0`.
2. In the designer, select that connector and use **Change type — Conditional flow**.

**Expected result:**
* Step 1 is REFUSED, and the refusal names the deciding activity and lists `Canceled`, `Negative`,
  `Positive`.
* Step 2 — the evidence the refusal rests on: the designer's own action offers the checkbox list and
  **no formula option at all**. There is no UI path to attach a raw condition to this connector, which is
  why writing one is refused rather than permitted with a warning.
* If step 1 SUCCEEDS that is a regression — report it as a BLOCKER, and check what the connector then
  looks like in the designer. It should open as an empty panel, which is the state the refusal prevents.

### TC-W11 Read back, write back, nothing moves

**Preconditions:** TC-W01's process.

**Steps:**
1. `describe-business-process` and take the `results` array verbatim.
2. `setFlowResults` on the same flow with exactly that array.
3. `describe-business-process` again, and open the connector in the designer.

**Expected result:**
* Step 2 succeeds and step 3 is byte-identical to step 1 — same captions, same order.
* The designer shows the same ticks. A caller must be able to preserve a selection it did not mean to
  change.

### TC-W12 The other result-enumerating elements

Repeat TC-W01 (write, describe, designer) for each of these, noting that each must first be CONFIGURED
into offering results:
* a `performTask` whose activity category carries result entries;
* a `preconfiguredPage` with at least one completing button;
* an `openEditPage` with the result list enabled AND its column set.

**Expected result:**
* Each behaves as TC-W01 — ticked boxes, no formula field, valid connector.
* The captions differ per element and come from that element's own configuration. Record what each one
  offered, since the refusal in TC-W07 is the only way to learn them.
* An `openEditPage` with the result list OFF must instead show a FORMULA field and refuse the
  selection. Include that as the negative half.

---

## Reporting

For every case give: the schema UId you acted on, the exact refusal text where one was expected, and a
screenshot of the designer panel wherever the case names one. For runtime cases give the
`SysProcessElementLog` rows showing which elements ran, by Caption — a `SysProcessLog` row is not an
answer to "which branch was taken", and quoting one as if it were is how this suite produced a false
engine-fault report.

If any case shows the designer rendering a formula field where these expectations say checkboxes, stop
and report it as a BLOCKER — that is the original defect reappearing from the other direction.
````
