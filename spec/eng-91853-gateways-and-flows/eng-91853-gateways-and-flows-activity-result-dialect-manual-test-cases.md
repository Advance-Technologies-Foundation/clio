# Activity-result branch dialect — manual test cases

Scoped to ENG-91853, whose subject is conditional and default flows. This is a SEPARATE TC sequence
from the gateway suite's TC-01–TC-18 and from the flow-label follow-up's TC-01–TC-10 in the earlier
comments, numbered TC-01–TC-12 to match this file.

**Not yet executed.** Written from the stand reproduction plus the 7.8.0 platform and designer sources
and the clio + CrtProcessBuilder sources. TC-01 has a live reproduction on the dev stand; TC-02–TC-04
need their fixtures built.

**Test case:**

**Conditions:** Stand `http://<dev-stand>:40001` (registered in clio as `Creatio`), logged
in as Supervisor. `CrtProcessBuilder` installed (`install-process-builder`). Reference process
`UsrOrder_Handle` ("New order handling") in package `Custom`, whose element `Approve order` is an
Approval with outgoing flows `Approved` → `Enter delivery details` (conditional, formula condition)
and `Cannot be fulfilled` → `Send cannot fulfil notice to customer`.

TC-01..TC-06 and TC-10 establish WHICH connectors take the designer's result-selection editor.
TC-07..TC-09 cover surrounding behaviour that must not regress. TC-11..TC-12 cover the separate
`validate-process-graph` argument defect.

**ON A VERSIONED PROCESS THE NAME IS NOT THE IDENTITY, and this suite creates versions.** Each saved
version is a separate schema (`<name><PackageName><n>`), and `describe-business-process
--process-name X` resolves that name against ONE schema — the root, version 0 — while the designer
edits, and the runtime executes, whichever is active. A designer save can leave the root behind. So
before trusting any graph, read `version` / `isActiveVersion` / `activeVersionName` in the response,
address the active schema by `--process-uid` or by its versioned name, and RECORD the schema UId you
actually measured in the run identity block. The 2026-09-15 run measured version 0 throughout and
reported TC-07/TC-09 as defects on that basis; both CONFIRM on the active version. See
`docs/knowledge/ProcessModel/describe-business-process-reads-the-base-version-not-the-active-one.md`.

---

### `TC-01` Approval connector — the formula is never shown, and this always happens

**Preconditions:**
`UsrOrder_Handle` exists on the stand, its `Approved` flow carrying a formula condition and no result
selection.

**Steps:**
1. Run `describe-business-process` for `UsrOrder_Handle` with `include: flows`.
2. Open `UsrOrder_Handle` in the process designer.
3. Select the connector labelled `Approved` between `Approve order` and `Enter delivery details`.
4. Read the connector's properties panel.
5. Switch the properties panel to its other page mode and read it again.
6. Save the process schema.

**Expected result:**
* Step 1 reports the flow as `kind: "conditional"` with a `condition` text and
  `branchesOnActivityResult: false`.
* Step 4 shows the panel headed `What is the result of an element "Approve order"?` listing the three
  final `VisaStatus` values — Canceled, Negative, Positive — with every checkbox cleared.
* The condition text from step 1 appears nowhere in the panel, in either page mode.
* The connector is marked invalid on the diagram.
* Step 6 raises `Required fields of some elements are not filled in`, naming that connector.

---

### `TC-02` Perform task connector — same editor when the activity category has results

**Preconditions:**
A process with a Perform task whose `ActivityCategory` has at least one `ActivityCategoryResultEntry`,
and one outgoing conditional flow carrying a formula condition and no result selection.

**Steps:**
1. Open the process in the designer.
2. Select the conditional connector leaving the Perform task.
3. Read the properties panel.

**Expected result:**
* The panel is the result-selection checkbox list, not a formula field.
* The listed values are the activity results of that element's category.
* Every checkbox is cleared and the connector is marked invalid.

---

### `TC-03` Preconfigured page connector — completing buttons are the result set

**Preconditions:**
A process with a Preconfigured page element carrying at least one completing button that closes the
page, and one outgoing conditional flow with a formula condition and no result selection.

**Steps:**
1. Open the process in the designer.
2. Select the conditional connector leaving the Preconfigured page element.
3. Read the properties panel.

**Expected result:**
* The panel lists the element's page-closing buttons as the selectable results.
* Every checkbox is cleared and the connector is marked invalid.

---

### `TC-04` Open edit page WITH the result list enabled — the editor appears

**Preconditions:**
A process with an Open edit page element whose `resultsByColumn` is enabled and points at a lookup
column, and one outgoing conditional flow with a formula condition and no result selection.

**Steps:**
1. Open the process in the designer.
2. Select the conditional connector leaving the Open edit page element.
3. Read the properties panel.

**Expected result:**
* The panel is the result-selection checkbox list, populated from the lookup column's referenced
  object.
* Every checkbox is cleared and the connector is marked invalid.

---

### `TC-05` Open edit page WITHOUT the result list — the formula editor appears

**Preconditions:**
The TC-04 process with `resultsByColumn` switched off (`enabled: false`), and a conditional flow
leaving that element with a formula condition.

**Steps:**
1. Open the process in the designer.
2. Select the conditional connector leaving the Open edit page element.
3. Read the properties panel.

**Expected result:**
* The panel shows the FORMULA field, not a checkbox list.
* The stored condition text is displayed and editable.
* The connector is not marked invalid.

---

### `TC-06` A gateway between the activity and the branch does not escape the editor

**Preconditions:**
A process where an Approval or Perform task is followed by a plain sequence flow into an exclusive
gateway, and the gateway's outgoing conditional flow carries a formula condition with no result
selection.

**Steps:**
1. Open the process in the designer.
2. Select the conditional connector leaving the GATEWAY.
3. Read the properties panel.

**Expected result:**
* The panel is still the result-selection checkbox list, headed with the ACTIVITY's caption, not the
  gateway's.
* The connector is marked invalid.
* Inserting a gateway is therefore not a workaround; guidance must not present it as one.

---

### `TC-06b` TWO chained gateways DO escape the editor

**Preconditions:**
A process where an Approval or Perform task feeds a plain sequence flow into an exclusive gateway,
that gateway feeds a plain sequence flow into a SECOND exclusive gateway, and the second gateway has
an outgoing conditional flow with a formula condition and no result selection.

**Steps:**

1. Open the process in the designer.
2. Select the conditional connector leaving the SECOND gateway.
3. Read the properties panel.

**Expected result:**

* The panel shows the FORMULA field, not a checkbox list.
* The connector is not marked invalid.
* The walk back to the activity is therefore one hop only. This is the boundary of TC-06 and the only
  known topology that escapes; it is not a remedy for a connector that already carries a selection,
  which is resolved by the stored UId and keeps its editor regardless of topology (TC-07).

---

### `TC-07` A connector that already carries a result selection is refused by clio

**Preconditions:**
A process with a conditional flow whose result selection was made in the designer (one or more
checkboxes ticked, schema saved).

**Steps:**
1. Run `describe-business-process` for that process with `include: flows`.
2. Call `modify-business-process` with `setFlowCondition` against that flow, passing any valid
   condition.
3. Call `modify-business-process` with `setFlow` against the same flow, passing `kind: "sequence"`.
4. Repeat step 3 on a second fixture whose source's ONLY outgoing flow is that conditional one, or
   which carries a second conditional sibling.

**Expected result:**
* Step 1 reports `branchesOnActivityResult: true` for that flow.
* Step 2 is refused, with a message saying the flow branches on the preceding activity's result and a
  condition set here would be stored and then ignored at run time.
* Step 3 is refused, and WHICH refusal you get depends on the source's topology. Both are correct:
  * the source has other outgoing flows and this is its last CONDITIONAL one — the STRUCTURAL guard
    fires first, naming "the last conditional branch leaving `<element>`". `Approve order` on
    `UsrOrder_Handle` is exactly this shape (one conditional beside one plain), so it always answers
    this way and the selection-aware message is unreachable there.
  * otherwise — the SELECTION-aware refusal, saying the selection would be discarded with no way back.
  The order is deliberate: `ProcessGraphBuilder.SetFlow` tests `WouldDropTheLastBranch` before the
  selection check, because losing the synthesized exclusive gateway is the worse outcome. Do NOT read
  the structural message as "the selection is not protected" — step 4 is what proves it is.
* Step 4 produces the selection-aware refusal.
* The stored selection is unchanged after every refusal.

---

### `TC-08` The formula branch still executes at run time

**Preconditions:**
`UsrOrder_Handle` is the active version. The `Approved` connector still carries its formula condition
and an empty result selection. A user in the `Sales team` role can approve.

**Steps:**
1. Create an Order whose amount exceeds the process's approval threshold (10 000).
2. Wait for the process to start and raise the approval on that Order.
3. Approve the visa positively.
4. Open the Order's process log / the running process instance.

**Expected result:**
* The process takes the `Approved` branch and starts `Enter delivery details`.
* `Send cannot fulfil notice to customer` is not started.
* No process error is logged — the defect is design-time only and does not affect execution.

---

### `TC-09` Completing the selection in the designer makes the connector valid

**Preconditions:**
The state left by TC-01 — formula present, selection empty, connector invalid.

**Steps:**
1. Open the connector's properties panel in the designer.
2. Tick `Positive`.
3. Save the process schema.
4. Run `describe-business-process` with `include: flows`.

**Expected result:**
* The connector is no longer marked invalid and the schema saves without the
  `Required fields of some elements are not filled in` message.
* The connector's caption on the diagram becomes the selected result's caption.
* Step 4 reports `branchesOnActivityResult: true` for that flow.
* A subsequent `setFlowCondition` against that flow is now refused, per TC-07.

---

### `TC-10` An element with no result parameter is unaffected

**Preconditions:**
A process with a conditional flow leaving a Read data, Change data or Send email element, or an
exclusive gateway fed only by such elements, with a formula condition.

**Steps:**
1. Open the process in the designer.
2. Select the conditional connector.
3. Read the properties panel.

**Expected result:**
* The panel shows the FORMULA field with the stored condition text, editable.
* The connector is not marked invalid.
* No checkbox list is offered.

---

### `TC-11` validate-process-graph must not answer about a graph it was not given

**Preconditions:**
`CrtProcessBuilder` installed on environment `Creatio`.

**Steps:**
1. Call `validate-process-graph` with `environment-name` plus an argument the tool does not declare,
   for example `process-name: UsrOrder_Handle`, and no `nodes` / `edges`.
2. Call `validate-process-graph` with `environment-name` only.
3. Compare the two responses.

**Expected result:**
* Step 1 does not report `R3 Process has no start event` — that finding is about a graph the tool
  never received.
* Step 1 names the unrecognized argument, the way `McpToolArgumentSupport.BuildLegacyAliasError` does
  for other tools.
* Step 2 states that no graph was supplied rather than reporting a rule violation.
* Steps 1 and 2 are distinguishable: an unknown argument must not be answered as if it were a
  well-formed call with an empty graph.

---

### `TC-12` validate-process-graph still validates a supplied graph

**Preconditions:**
As TC-11.

**Steps:**
1. Call `validate-process-graph` with `environment-name`, a `nodes` array containing a `startEvent`, a
   `userTask` and an `endEvent`, and the `edges` connecting them in order.
2. Call it again with the `startEvent` node removed.

**Expected result:**
* Step 1 returns `has-errors: false` with no findings.
* Step 2 returns `R3 Process has no start event`.
* The rule still fires on a real graph that genuinely lacks a start event.

---

## Gaps to close before these are final

* **TC-01..TC-06 assert the CURRENT behaviour**, which is the defect. Whether the fix makes clio
  REFUSE the write or merely raise a NOTICE is an open decision; until it is made, no case here can
  state what clio should answer at write time. Add that case once the decision is recorded.
* **TC-02 / TC-03 / TC-04 need their fixtures built.** Only the Approval case (TC-01) has a
  reproduction on the reference stand today.
* **The designer's save behaviour** is asserted as "raises the message". Whether it also BLOCKS the
  save was not measured; measure it before promising it.
