Task list required to achieve the goal of the parent research [Research: Add business process generation via AI instructions](https://creatio.atlassian.net/wiki/spaces/TER/pages/4702928908) ([ENG-90883](https://creatio.atlassian.net/browse/ENG-90883)).

> **Tasks 1–15 are tracked in Jira** (ENG-91839 … ENG-91853) — their full description and estimate live in the linked issue. **Tasks 16–35 are not yet ticketed** and keep their full spec below.

## Tasks

1. [ENG-91839](https://creatio.atlassian.net/browse/ENG-91839) — Merge the BP-generation prototype (clio + clioprocessbuilder) into master · Estimate: ~4 days
2. [ENG-91840](https://creatio.atlassian.net/browse/ENG-91840) — Decide how the clioprocessbuilder package is delivered · Estimate: ~2 days
3. [ENG-91841](https://creatio.atlassian.net/browse/ENG-91841) — Define the testing approach for BP generation · Estimate: ~2 days
4. [ENG-91842](https://creatio.atlassian.net/browse/ENG-91842) — Support Data source filters in element parameters · Estimate: ~6 days
5. [ENG-91843](https://creatio.atlassian.net/browse/ENG-91843) — Add and modify process parameters · Estimate: ~3 days
6. [ENG-91844](https://creatio.atlassian.net/browse/ENG-91844) — Implement full parameter mapping (sources + formula authoring) · Estimate: ~3 days
7. [ENG-91845](https://creatio.atlassian.net/browse/ENG-91845) — Create and modify dynamic element parameters ("Connected to") · Estimate: ~2 days
8. [ENG-91846](https://creatio.atlassian.net/browse/ENG-91846) — Perform task element: usability status + AI understanding · Estimate: ~1.5 days
9. [ENG-91847](https://creatio.atlassian.net/browse/ENG-91847) — User task element (generic + custom): usability + end-to-end scenario · Estimate: ~3 days
10. [ENG-91848](https://creatio.atlassian.net/browse/ENG-91848) — Signal start element: usability status + tracked-change columns · Estimate: ~1.5 days
11. [ENG-91849](https://creatio.atlassian.net/browse/ENG-91849) — Timer start element (start by time / schedule) · Estimate: ~2 days
12. [ENG-91850](https://creatio.atlassian.net/browse/ENG-91850) — Read data element: usability status (filters via Task 4) · Estimate: ~2.5 days
13. [ENG-91851](https://creatio.atlassian.net/browse/ENG-91851) — Modify data element: usability status (filters via Task 4) · Estimate: ~2 days
14. [ENG-91852](https://creatio.atlassian.net/browse/ENG-91852) — Initialize and modify process properties · Estimate: ~3 days
15. [ENG-91853](https://creatio.atlassian.net/browse/ENG-91853) — Gateways and flows (conditional / default) + Y auto-layout · Estimate: ~5 days

---

### Task 16 — Delete elements on the diagram (flow reconnection + parameter-usage check)

**Goal:** Deleting an element must also remove its incoming / outgoing flows, **heal the gap** by creating a new flow between the deleted element's predecessor and successor, and — **before deletion** — verify that the element's parameters are **not referenced anywhere** in the process (abort if they are).

**Status:** Partially implemented. `modify-business-process` `removeElement` already removes the element together with its sequence flows and its `schema.Mappings` entries. **Missing:** gap reconnection (predecessor → successor) and the pre-delete parameter-usage check.

#### What works

* **`removeElement`** — removes the element + its incoming / outgoing sequence flows + its mapping entries (verified live).

#### What does NOT work / to add

* **Gap reconnection** — after removing an element that sat between A and B, create a new sequence flow **A → B** so the process stays connected (today the gap is left open). Handle the multi-flow case sensibly (e.g. an element right after a gateway, or with several incoming / outgoing flows).
* **Parameter-usage pre-check** — before deleting, scan the process for references to the element's parameters (in mappings, formulas, conditions, other elements). If any reference exists, **abort with a clear error** that lists where the parameter is used, rather than leaving dangling references.

#### Deliverable

* `removeElement` extended: optional **gap reconnection** (predecessor → successor flow) + a **pre-delete parameter-usage validation** that aborts on references.
* `describe-process` read-back confirming the element is gone, the flow is healed, and no dangling references remain.
* Tests (gap healed; abort-on-usage) + docs + MCP surface updates.

**Reference:** existing `modify-business-process` `removeElement` (removes element + flows + mappings); Task 6 (where element parameters are referenced — mappings / formulas); state doc `spec/process-design-service/process-design-service-state.md`.

---

### Task 17 — Element "Preconfigured page": selection + parameter re-sync

**Goal:** Support the **Preconfigured page** element — select a preconfigured page for the element and **synchronize the element's parameters with that page's parameters**, including the ability to **re-synchronize** after the preconfigured page's parameters have been changed.

**Status:** Not implemented. The element (a user task) can be added generically, but **selecting a preconfigured page** and **syncing / re-syncing its parameters onto the element** is not supported.

#### Scope

* **Select the preconfigured page** on the element (set the page reference).
* **Initial parameter sync** — the page's parameters appear on the element (analogous to `SynchronizeParameters` for user tasks).
* **Re-sync after change** *(the key requirement)* — when the selected preconfigured page's parameters are later modified, re-synchronize the element's parameters with the new set: add new parameters, drop removed ones, and **preserve existing values / mappings** where the parameter still exists.
* Set / map the synced parameters' values (reuse Task 6).

#### Technical challenge

A preconfigured page is referenced by the element, and its parameters drive the element's parameter set. The exact serialization (the page reference + how the parameter sync works for a preconfigured page) must be **captured from a designer-built example** before implementing. The re-sync must be **idempotent** and must keep already-set values / mappings for parameters that still exist after the page changed.

#### Deliverable

* Build / modify support to **select a preconfigured page** on the element and **(re-)sync** its parameters; a dedicated modify op (e.g. `resyncElementParameters`) for "the page changed — refresh the element parameters".
* Server-side serialization + sync logic (verified against a capture).
* `describe-process` read-back of the page reference + the synced parameters.
* Tests + docs + MCP surface updates.

**Reference:** Task 6 (mapping values onto synced parameters), Task 9 (user-task parameter sync model — `SynchronizeParameters`); state doc `spec/process-design-service/process-design-service-state.md`.

---

### Task 18 — Element "Send email"

**Goal:** Support the **Send email** element — configure the sender, recipients (To / Cc / Bcc), subject and body, and/or an email template.

**Status:** Not implemented. The element can be added as a user task generically, but its email configuration is not supported.

#### Scope

* **Sender** — the mailbox / sender address.
* **Recipients** — To / Cc / Bcc, each as a constant address, a process parameter, or a contact / entity column.
* **Subject + body** — plain text or a formula, with macros from process data.
* **Email template** — select a template (and sync its parameters) as an alternative to a manual subject / body.
* **Options** — importance, etc.

#### Technical challenge

Send email is a user task; its parameters (recipients, subject, body, template reference) are set through the value-mapping mechanism (Task 6), and the template path needs parameter sync similar to Task 9 / Task 17. Capture the serialization from a designer-built Send email element before implementing.

#### Deliverable

* A `sendEmail` configuration in the build / modify contract: sender, recipients, subject / body or template, options; values via Task 6.
* Server-side serialization (verified against a capture).
* `describe-process` read-back of the configuration for verification.
* Tests + docs + MCP surface updates.

**Reference:** Task 6 (value mapping / formulas), Task 9 (user-task parameter sync), Task 17 (template / page parameter-sync analogue); state doc `spec/process-design-service/process-design-service-state.md`.

---

### Task 19 — Element "Sub-process": selection + parameter sync

**Goal:** Support the **Sub-process** element (call another business process) and **synchronize the sub-process's parameters with the element's parameters** so the caller can pass inputs and receive outputs — including the ability to re-synchronize after the sub-process's parameters change.

**Status:** Not implemented. There is no sub-process / call-activity element yet.

#### Scope

* **Select the sub-process** (the called process) on the element.
* **Parameter sync** — the called process's parameters (inputs + outputs) appear on the element, analogous to `SynchronizeParameters`.
* **Value mapping** — map values into the input parameters and read back the outputs (reuse Task 6).
* **Re-sync after change** — when the called process's parameters change, re-synchronize the element's parameters (add new, drop removed, preserve existing values / mappings) — like Task 17.

#### Technical challenge

Sub-process is a call-activity (`callActivity` → `SubProcess`); it references the called process schema, whose parameters drive the element's parameter set. Capture the serialization (the called-process reference + parameter sync) from a designer-built example before implementing; the re-sync must be idempotent.

#### Deliverable

* A `subProcess` configuration in the build / modify contract: select the called process + (re-)sync its parameters; input / output value mapping via Task 6.
* Server-side serialization (verified against a capture).
* `describe-process` read-back of the called process reference + the synced parameters.
* Tests + docs + MCP surface updates.

**Reference:** Task 6 (value mapping), Task 9 (user-task parameter sync), Task 17 (re-sync pattern); the `callActivity` → `SubProcess` data-id; state doc `spec/process-design-service/process-design-service-state.md`.

---

### Task 20 — Element "Add data"

**Goal:** Support the **Add data** element (`addDataUserTask`) — create one or more records of a target object with column values.
**Status:** Not implemented. Can be added as a user task generically; its data configuration is not supported.
**Scope:** target object; column values to set (constant / process parameter / formula / entity column); "add from selection" mode + its selection filters; single-record vs from-a-collection.
**Technical:** capture serialization from a designer-built Add data; column values via Task 6; selection filters via Task 4.
**Deliverable:** an `addData` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 4 (selection filters), Task 6 (column values), Task 13 (Modify data — sibling); state doc.

---

### Task 21 — Element "Delete data"

**Goal:** Support the **Delete data** element (`deleteDataUserTask`) — delete the records of a target object matching a filter.
**Status:** Not implemented. Can be added generically; configuration not supported.
**Scope:** target object; which records to delete (filter), or delete the records returned by a preceding Read data.
**Technical:** capture serialization; record filter via Task 4.
**Deliverable:** a `deleteData` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 4 (filters), Task 12 (Read data), Task 13 (Modify data); state doc.

---

### Task 22 — Element "Call web service"

**Goal:** Support the **Web service / Call web service** element (`WebService`) — call a registered web service, pass a request, receive the response.
**Status:** Not implemented.
**Scope:** select the web service + method; request parameters (incl. a composite request body); map inputs; read the response into parameters (incl. a composite response body).
**Technical:** capture serialization; **this is the main driver for the complex / structured parameter types in Task 5** (request / response bodies = composite structures / collections); value mapping via Task 6.
**Deliverable:** a `webService` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** **Task 5** (composite parameter types), Task 6 (value mapping); state doc.

---

### Task 23 — Element "Script task"

**Goal:** Support the **Script task** element (`scriptTask`) — run C# code inside the process.
**Status:** Not implemented.
**Scope:** the C# body; process-parameter access via the interpretable-process **`Get` / `Set`** model; any required process **Methods** / **Usings**.
**Technical:** capture serialization; the AI generates the C# (existing capability) and **must use `Get` / `Set`**, not direct parameter access.
**Deliverable:** a `scriptTask` config (code body) in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 14 (`Get`/`Set`, Methods / Usings), Task 9 (C# generation); the `scriptTask` data-id; state doc.

---

### Task 24 — Element "Formula task"

**Goal:** Support the **Formula task** element (`formulaTask`) — compute a value via a formula and write it to a parameter.
**Status:** Not implemented.
**Scope:** the formula expression; the target parameter.
**Technical:** capture serialization; formula format + allowed functions via Task 6.
**Deliverable:** a `formulaTask` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 6 (formula authoring + allowed functions); the `formulaTask` data-id; state doc.

---

### Task 25 — Element "Approval"

**Goal:** Support the **Approval** element — request an approval (visa) from a user / role and branch on the result (approved / rejected).
**Status:** Not implemented. Can be added generically; configuration not supported.
**Scope:** the object / record under approval; approver (user / role); options (delegation, comment required); result parameters.
**Technical:** capture serialization; values via Task 6; branch on result via Task 15.
**Deliverable:** an `approval` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 6 (value mapping), Task 15 (branch on result); state doc.

---

### Task 26 — Element "Question"

**Goal:** Support the **Question** element — ask the user a question with predefined answer options and branch on the answer.
**Status:** Not implemented.
**Scope:** the question text; the answer options; the result parameter used to branch (with gateways — Task 15).
**Technical:** capture serialization; values via Task 6.
**Deliverable:** a `question` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 6 (values), Task 15 (branch on answer); state doc.

---

### Task 27 — Element "Open edit page"

**Goal:** Support the **Open edit page** element — open a record edit page for the user, with field pre-fill and read-back of edited values.
**Status:** Not implemented.
**Scope:** the page / object; pre-filled column values; read-back of edited values; parameter sync (like Task 17).
**Technical:** capture serialization; parameter sync + values via Task 17 / Task 6.
**Deliverable:** an `openEditPage` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 17 (preconfigured page — sibling), Task 6 (values); state doc.

---

### Task 28 — Element "Auto-generated page"

**Goal:** Support the **Auto-generated page** element — show an auto-generated page with configured fields and read back the user input.
**Status:** Not implemented.
**Scope:** the page field set; parameters synced from the page config; values + read-back.
**Technical:** capture serialization; parameter sync (Task 17 re-sync pattern) + values via Task 6.
**Deliverable:** an `autoGeneratedPage` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 17 (page parameter sync), Task 6 (values); state doc.

---

### Task 29 — Element "Change access rights"

**Goal:** Support the **Change access rights** element (`ChangeAdminRights`) — grant / revoke record permissions for users / roles.
**Status:** Not implemented.
**Scope:** target record / object; operations (read / edit / delete) granted or revoked; grantee (user / role); values via Task 6.
**Technical:** capture serialization.
**Deliverable:** a `changeAccessRights` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 6 (values); state doc.

---

### Task 30 — Element "Link entity to process"

**Goal:** Support the **Link entity to process** element (`LinkEntityToProcess`) — associate an entity record with the running process instance.
**Status:** Not implemented.
**Scope:** the entity / record to link; values via Task 6.
**Technical:** capture serialization.
**Deliverable:** a `linkEntityToProcess` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 6 (values); state doc.

---

### Task 31 — Element "File processing"

**Goal:** Support the **File processing** element (`*FileProcessing`) — work with files / attachments of a record (read / save / etc.).
**Status:** Not implemented.
**Scope:** the file operation; the target record / attachment; values via Task 6 (incl. the Binary / File parameter type from Task 5).
**Technical:** capture serialization.
**Deliverable:** a `fileProcessing` config in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 5 (Binary / File type), Task 6 (values); state doc.

---

### Task 32 — Element "Message start"

**Goal:** Support the **Message start** event (`startEventMessage`) — start a process on an incoming message.
**Status:** Not implemented. Only Simple and Signal start exist (Timer = Task 11).
**Scope:** the message the start listens for.
**Technical:** capture serialization (sibling to Signal start — Task 10).
**Deliverable:** a `messageStart` descriptor in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 10 (Signal start), Task 11 (Timer start); state doc.

---

### Task 33 — Element "Terminate end"

**Goal:** Support the **Terminate end** event — end the whole process immediately (vs the normal End, which ends only its branch).
**Status:** Not implemented. Only the simple End exists (Terminate shares the `endEvent` data-id with a terminate flag).
**Scope:** the terminate flag on the end event.
**Technical:** capture serialization (the terminate distinction).
**Deliverable:** a terminate option on the end element; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** the `endEvent` data-id (simple end & terminate share it); state doc.

---

### Task 34 — Intermediate events (timer / signal / message)

**Goal:** Support **intermediate events** placed mid-flow — timer, signal, and message, in **catch** (wait for) and **throw** (emit) form.
**Status:** Not implemented. Only start events and End exist.
**Scope:** intermediate catch (wait for a timer / signal / message) and intermediate throw (emit a signal / message), with their config.
**Technical:** capture serialization per type; reuse Timer (Task 11) and Signal (Task 10) / Message (Task 32) configuration.
**Deliverable:** intermediate-event descriptors in build / modify; server-side serialization (verified vs capture); `describe-process` read-back; tests + docs + MCP surface.
**Reference:** Task 10 (Signal), Task 11 (Timer), Task 32 (Message); the `intermediateCatchEvent` / `intermediateThrowEvent` data-ids; state doc.

---

### Task 35 — Add example business processes to clio templates

**Goal:** Ship a set of **example business processes** in clio's templates that show how to solve typical cases with a BP. They serve as ready-to-use references / starting points for users **and** as few-shot examples that steer the AI toward correct BP designs.

**Status:** Not implemented. clio has a templates mechanism, but there are no business-process example templates.

#### Scope — example cases to cover

* **Record trigger → task** — Signal start (on modify) → Perform task.
* **Scheduled job** — Timer start → Read data → Modify data.
* **Approval flow** — start → Approval → gateway (approved / rejected) → branches.
* **Web service call** — start → Call web service → map response → Modify data.
* **Custom C# logic** — start (button / signal) → custom User task with C# → End (the end-to-end case from Task 9).
* …extend as more elements land.

#### Deliverable

* Business-process example descriptors added to clio templates (in the `create-business-process` descriptor format), organized by case, each with a short "what it shows / when to use" note.
* Wired into the clio templates surface so they are discoverable (templates list / create-from-template).
* Referenced from the `process-modeling` MCP guidance so the AI uses them as few-shot examples.
* Docs.

#### Note

* Examples must use only **implemented** capabilities (kept current as the element tasks land); clearly mark any example that depends on a not-yet-built element.

**Reference:** clio templates mechanism; `process-modeling` guidance; the `create-business-process` descriptor (sample `spec/process-design-service/process-design-service-sample-descriptor.json`); Task 9 (end-to-end UT + C#); state doc.

---

## Estimates (rough)

**Assumptions.** Working days, 1 person. The **AI writes the code**, so coding time is small; each estimate is dominated by **code review + unit / e2e tests + QA by a tester**, plus capturing the designer serialization for new elements. **Calibration:** the already-built baseline (the "what's done" list in Task 1) took **~3 AI-coding days with no review and no QA** — those review/QA costs are folded into Task 1. Tasks 1–15 estimates are taken from their Jira issues (ENG-91839 … ENG-91853) and match the figures below.

| Task | Jira | Estimate (days) |
| --- | --- | --- |
| 1 — Merge the prototype (review + QA the existing build) | [ENG-91839](https://creatio.atlassian.net/browse/ENG-91839) | 4 |
| 2 — Decide package delivery (+ wiring / rename) | [ENG-91840](https://creatio.atlassian.net/browse/ENG-91840) | 2 |
| 3 — Define the e2e testing approach | [ENG-91841](https://creatio.atlassian.net/browse/ENG-91841) | 2 |
| 4 — Data source filters (cost center) | [ENG-91842](https://creatio.atlassian.net/browse/ENG-91842) | 6 |
| 5 — Add and modify process parameters | [ENG-91843](https://creatio.atlassian.net/browse/ENG-91843) | 3 |
| 6 — Full parameter mapping + formula authoring | [ENG-91844](https://creatio.atlassian.net/browse/ENG-91844) | 3 |
| 7 — Dynamic element parameters ("Connected to") | [ENG-91845](https://creatio.atlassian.net/browse/ENG-91845) | 2 |
| 8 — Perform task: status + AI understanding | [ENG-91846](https://creatio.atlassian.net/browse/ENG-91846) | 1.5 |
| 9 — User task: end-to-end (UT + C#) | [ENG-91847](https://creatio.atlassian.net/browse/ENG-91847) | 3 |
| 10 — Signal start: tracked-change columns | [ENG-91848](https://creatio.atlassian.net/browse/ENG-91848) | 1.5 |
| 11 — Timer start | [ENG-91849](https://creatio.atlassian.net/browse/ENG-91849) | 2 |
| 12 — Read data (config; filters via #4) | [ENG-91850](https://creatio.atlassian.net/browse/ENG-91850) | 2.5 |
| 13 — Modify data | [ENG-91851](https://creatio.atlassian.net/browse/ENG-91851) | 2 |
| 14 — Process properties (versioning / Methods / Usings) | [ENG-91852](https://creatio.atlassian.net/browse/ENG-91852) | 3 |
| 15 — Gateways + flows + Y auto-layout | [ENG-91853](https://creatio.atlassian.net/browse/ENG-91853) | 5 |
| 16 — Delete elements (flow reconnection + parameter-usage check) | — | 2 |
| 17 — Preconfigured page + re-sync | — | 2 |
| 18 — Send email | — | 2 |
| 19 — Sub-process + param sync | — | 2.5 |
| 20 — Add data | — | 1.5 |
| 21 — Delete data | — | 1.5 |
| 22 — Call web service | — | 3 |
| 23 — Script task | — | 2 |
| 24 — Formula task | — | 1.5 |
| 25 — Approval | — | 2 |
| 26 — Question | — | 1.5 |
| 27 — Open edit page | — | 2 |
| 28 — Auto-generated page | — | 2 |
| 29 — Change access rights | — | 1.5 |
| 30 — Link entity to process | — | 1 |
| 31 — File processing | — | 2 |
| 32 — Message start | — | 1 |
| 33 — Terminate end | — | 1 |
| 34 — Intermediate events | — | 3 |
| 35 — Example business processes in clio templates | — | 2 |
| **Total** | | **~79.5 days (≈ 16 weeks)** |

**Subtotals:** infrastructure (1–3) ≈ 8; core parameters / mapping (4–7) ≈ 14; implemented-element status + gaps (8–19) ≈ 29; per-element new (20–34) ≈ 26.5; examples (35) ≈ 2.

**Caveat.** Estimates assume designer serialization can be captured cleanly per element; a hard-to-capture element (e.g. Web service composite bodies, intermediate events) can swing ±50%. Filters (#4) remains the single biggest risk.
