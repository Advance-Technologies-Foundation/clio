# Backend designer — manual QA test cases

> Manual test cases for the backend BP designer (`clioprocessbuilder` package / `ProcessDesignService`,
> surfaced in clio as verbs + MCP tools). Pinned to `cli-process-builder@52df22b` +
> clio [PR #715](https://github.com/Advance-Technologies-Foundation/clio/pull/715).
> Capability matrix source:
> [Backend designer — capability status](https://creatio.atlassian.net/wiki/spaces/TER/pages/4769087495).
>
> Covers **only capabilities implemented in the build** (status `Implemented` / `Partial`).
> `Not impl.` and `In progress (PR #6 / ENG-91842)` are out of scope — for those, only
> negative cases are provided ("must be cleanly rejected").

## Methodology (how the tester runs the cases)

Each case is an **end-to-end scenario driven through the AI agent**, not a direct CLI call:

1. **Tester** gives the AI a business task in natural language (the "Business task for AI" field).
2. **AI** forms and executes calls through clio MCP (`create-business-process`,
   `modify-business-process`, `describe-business-process`, `list-user-tasks`,
   `validate-process-graph`, `get-process-signature`).
3. **Tester** opens the result **in the visual process designer** in Creatio
   and checks the structure (elements, flows, parameters, trigger).
4. **Tester** **runs** the process and checks the **runtime behavior**
   (signal firing, Activity creation, process log).
5. The result is compared against "Expected result" and "Must NOT work".

The tester **does not write the JSON descriptor by hand** — that is the AI's job. The tester
verifies that the AI understood the business task correctly and that the produced artifact is
correct and functional.

## Stand requirements

- **Preferably a DB-mode stand** (e.g. 10.1.10, MSSQL, non-FSM, IIS): the built process lands
  in the DB immediately, codeless changes are live without a restart — runtime can be checked without publish.
- On an **FSD stand**, the built process exists only in the file system until `FS→DB load + publish`,
  so **runtime cases (firing/execution) are NOT performed on FSD** — structural checks only.
- The environment is registered in clio (`list-environments`), and the clio MCP server is connected to it.
- The target package (`Custom` or a dedicated one) is unlocked and writable.

## Priority legend

- **P1** — implemented, but runtime/UI never verified (or a Gap) — highest risk.
- **P2** — Implemented/Partial with a Partial mark in the Verified columns.
- **P3** — Partial tasks (code exists, functionality incomplete).
- **P4** — atomicity, layout, negative cases (behavior "implemented = rejects correctly").

## Case template

Each case contains: ID · priority · covered capability · preconditions ·
**business task for AI** · expected clio MCP calls · designer check ·
runtime check · expected result · must NOT work (known gaps).

---

# Group A — Building processes (`create-business-process`)

## TC-A-01 — Simple process: manual start → user task → end `[P4]`

- **Capability:** Simple start · Sequence flow · User task · End event · Build process.
- **Preconditions:** stand ready, package `Custom` available.
- **Business task for AI:**
  > "Create a business process `QA Simple Task`: started manually, runs a single user task
  > 'Process the request', then ends."
- **Expected clio MCP calls:** `create-business-process` with a descriptor
  `startEvent → userTask/performTask → endEvent`, sequence flows.
- **Designer check:**
  1. Open process `QA Simple Task` in the designer.
  2. Three elements in a line: Simple start → task → End.
  3. Flows are plain sequence flows, no breaks.
  4. The process opens with no validation errors.
- **Runtime check:** run the process manually → it reaches completion, the process log
  shows the task executing and the instance ending correctly.
- **Expected result:** the process is saved as a normal schema, structure matches the task.
- **Must NOT work:** —

## TC-A-02 — Signal start (added) → Perform task → End, runtime check `[P1/Tier2]`

- **Capability:** Signal start (added) · Perform task (ActivityUserTask) · runtime firing.
- **Business task for AI:**
  > "Build a process: when a new Lead is created — create a task 'Call the lead back'
  > with High priority and a 30-minute duration. Then end."
- **Expected clio MCP calls:** `create-business-process` with a `startEventSignal`
  (object = Lead, EntityChangeType = Add) → `activityUserTask` (constants: priority/duration) → `endEvent`.
- **Designer check:**
  1. Trigger = "Signal", object Lead, event "Record added".
  2. The Perform task has duration / priority set as constants.
- **Runtime check:**
  1. Create a new Lead record.
  2. The process **starts automatically**; the process log shows execution.
  3. An **Activity is created** with the given priority and duration.
- **Expected result:** the signal fires on a real event, the task executes, the Activity is created.
- **Must NOT work:** performer/owner, "Connected to" (linking the Activity to the lead), subject — not built;
  the process fires for **every** Lead (no filter).

## TC-A-03 — Signal start (modified), runtime check `[P1]`

- **Capability:** Signal start (modified) · runtime.
- **Business task for AI:**
  > "Create a process: when a contact is modified, create a task 'Review contact changes'."
- **Designer check:** trigger = Signal, object Contact, event "Record modified".
- **Runtime check:** modify any field of an existing contact → the process starts, the Activity is created.
- **Must NOT work:** restricting to specific changed columns (tracked-change) — NOT built;
  "modified" fires on any field change.

## TC-A-04 — Signal start (deleted), runtime check `[P1 — never verified before]`

- **Capability:** Signal start (deleted) · runtime (only added & modified confirmed previously).
- **Business task for AI:**
  > "Create a process: when an Activity record is deleted, run the user task 'Record the deletion'."
- **Designer check:** trigger = Signal, event "Record deleted".
- **Runtime check:** delete a record of the trigger object → the **process must start** and run the task.
- **Expected result:** firing on `deleted` confirmed at runtime (not verified before).
- **Must NOT work:** —

## TC-A-05 — Process parameters of all scalar types `[P2 — DB-mode Partial]`

- **Capability:** Scalar process parameter types (DB-mode Verified = Partial).
- **Business task for AI:**
  > "Create a process `QA Scalar Params` with parameters: text, long text, integer,
  > float, money, yes/no, date, date-time, time, identifier (Guid). Started manually,
  > one user task, end."
- **Expected clio MCP calls:** `create-business-process` with `parameters[]`:
  ShortText, Long text, Integer, Float2, Currency, Boolean, Date, DateTime, Time, Guid.
- **Designer check:** open the process parameters panel — **every** parameter is present
  and shown with the **correct platform data type**.
- **Runtime check:** (optional) set values and confirm the types are accepted.
- **Expected result:** all 10 scalar types map correctly to platform types.
- **Must NOT work:** Composite / Entity / Entity collection / Binary-File / Color / Image / Enum — not built.

## TC-A-06 — Lookup process parameter `[P2]`

- **Capability:** Lookup parameter (`referenceSchema`).
- **Business task for AI:**
  > "Create a process with a lookup parameter 'City' (reference to the City object). Manual start."
- **Designer check:** the parameter is shown as a lookup to the City object.
- **Must NOT work:** —

## TC-A-07 — Value sources: constant / parameter reference / raw formula `[P2]`

- **Capability:** Constant · Process-parameter reference · Raw formula passthrough (Partial, not validated by the backend).
- **Business task for AI:**
  > "Create a process with a text parameter `Greeting`. In a user task: one field — constant 'Hello',
  > the second — the value of parameter `Greeting`, the third — a formula (use a correct formula)."
- **Designer check:**
  1. Constant field = "Hello".
  2. The reference field resolves correctly to parameter `Greeting` (matched by internal id).
  3. The formula field contains the given expression.
- **Runtime check:** run → the values are substituted.
- **Must NOT work (important to record):** the backend does **not** validate the raw formula — provide a
  **deliberately broken** formula, confirm that `create` accepts it and the error surfaces **only at runtime**
  (unlike the visual designer, which blocks broken formulas). Record the wording of the error.

## TC-A-08 — User task: auto-sync of fixed parameters `[P3]`

- **Capability:** User task (generic/custom) · Fixed auto-synced element params.
- **Business task for AI:**
  > "Create a process with a user task referencing <a chosen palette task with input parameters>,
  > and set the values of its input parameters."
- **Designer check:** the task's input parameters **auto-attached** to the element, values are set.
- **Must NOT work:** non-parameter options (e.g. "Run … in background") — not set, stay at defaults.

## TC-A-09 — End event: type vs the visual designer `[P1 — legacy type, runtime not verified]`

- **Capability:** End event (Partial; the backend builds a deprecated legacy variant — flagged on PR #715).
- **Business task for AI:** any process with an explicit end (TC-A-01 can be reused).
- **Designer check:** compare the type of the built end-element with the one the designer creates
  when an end is added manually. Record the type divergence.
- **Runtime check:** run → the instance **ends correctly** (does not hang or crash),
  despite the legacy type.
- **Expected result:** the end behavior is equivalent, the type is legacy (known technical debt).

---

# Group B — Modifying processes (`modify-business-process`)

## TC-B-01 — Add a step to an existing process + relayout `[P4 — Tier2]`

- **Capability:** Add element + sequence flow · X-axis auto-layout (overwrites positions).
- **Preconditions:** a process with 3+ elements exists (TC-A-01 works).
- **Business task for AI:**
  > "Add one more user task 'Notify the manager' to `QA Simple Task`, between the task and the end."
- **Expected clio MCP calls:** `modify-business-process` (addElement + addFlow, reconnection).
- **Designer check:** the new element is inserted, flows are correct, the process opens.
- **Expected result / record:** the **whole diagram is re-laid-out** (X-axis auto-layout
  runs over all nodes on every save) — manual positioning is lost, but data is intact.
  This is known behavior; important to confirm and document as a warning to the user.
- **Must NOT work:** the added element is an "empty shell" (cannot be configured for data operations).

## TC-B-02 — Removing an element / connection (Gap) `[P1 — UI = Gap]`

- **Capability:** Remove element / connection (Partial; skips the designer's safety checks).
- **Preconditions:** a process with 3+ elements and mappings (e.g. a chain with parameters).
- **Business task for AI:**
  > "Remove the middle user task from `QA Simple Task`."
- **Designer check (critical):**
  1. The process opens with no errors.
  2. **No** dangling references to the removed element.
  3. The flow is **rejoined** across the gap (or explicitly record that it is NOT rejoined — a known Gap).
  4. Mappings: no references to the removed element's UId remain; adjacent ones are **not over-deleted**
     (removeElement cleans mappings by string-matching the UId — risk of missing/over-deleting).
- **Expected result:** record the actual Gap behavior; on a complex process, removal may
  leave incorrect bindings — document as a risk.
- **Must NOT work:** safe removal with rejoining, as in the designer.

## TC-B-03 — Atomic rollback on error `[P4]`

- **Capability:** Modify process — all-or-nothing.
- **Business task for AI:**
  > "Add a user task to `QA Simple Task`, then perform a deliberately invalid operation
  > (e.g. add an unsupported gateway) as a single batch of changes."
- **Expected clio MCP calls:** `modify-business-process` with an operation list where a middle/last op is invalid.
- **Designer check:** the process is **unchanged** — not even the valid operations from the same batch were applied.
- **Expected result:** the whole edit is rolled back, the original process is intact, the error is clear (not a raw exception).

## TC-B-04 — Swap start type Simple ⇄ Signal `[P4 — Tier1]`

- **Capability:** Swap start (Simple ⇄ Signal) via modify.
- **Business task for AI:**
  > "Change `QA Simple Task` so it starts not manually, but when a Lead is created."
- **Designer check:** the trigger changed to Signal (Lead / Add).
- **Runtime check:** create a Lead → the process starts.
- **Must NOT work:** adding a filter on the signal, tracked-change columns — not built.

## TC-B-05 — Add a process parameter to an existing process `[P2]`

- **Capability:** Add a process parameter.
- **Business task for AI:**
  > "Add an integer parameter `RetryCount` to `QA Simple Task`."
- **Designer check:** the parameter appears with type Integer.
- **Must NOT work:** setting a default value; changing/removing an existing parameter; changing the type.

---

# Group C — Read / validate / signature

## TC-C-01 — `describe-business-process` on a simple process `[Tier1]`

- **Capability:** Read back a process · Element type/task/position · Start trigger ·
  Parameter values & bindings · Connection types.
- **Business task for AI:**
  > "Describe `QA Simple Task`: which elements, flows, trigger, parameters."
- **Check:** all elements returned (id/name/caption/type/position), flows with `kind`,
  trigger (object+event), process parameters and values. Cross-check with the designer.

## TC-C-02 — `describe-business-process` on a complex existing process `[P4 — semantic blindness]`

- **Capability:** Read back on a process with gateways / sub-process / conditional flows
  (round-trips as data; semantics not decoded).
- **Preconditions:** a real complex process exists (gateways, sub-process, filters, conditional flow).
- **Business task for AI:**
  > "Explain what `<complex process>` does."
- **Check (critical):**
  1. `describe` **does not choke** on gateways/sub-process, lists them with runtime types
     (`ProcessSchemaExclusiveGateway`, `ProcessSchemaSubProcess`, etc.).
  2. Conditional/default flows are read with the correct `kind`.
  3. The AI **does not invent** semantics: gateway conditions, filters, sub-process target, timer schedule —
     must be **absent** from the description, not wrongly guessed.
- **Expected result:** structurally correct, semantically "blind" — confirm the AI honestly
  reports the limitation rather than misleading.

## TC-C-03 — `get-process-signature` (gps) `[P1 — never verified anywhere]`

- **Capability:** Read a process signature (Implemented; Verified in all columns = "—").
- **Preconditions:** a process with input and output parameters of various types.
- **Business task for AI:**
  > "Show the signature of `<process with in/out parameters>` — its inputs and outputs."
- **Check:**
  1. Returns codes, types, direction (In/Out) of each parameter.
  2. Cross-check with the real process signature in the designer.
  3. Edge cases: process with no parameters → empty signature; non-existent process → a clear error.
- **Expected result:** the signature is correct (the first verification of this command at all).

## TC-C-04 — `list-user-tasks` `[P4]`

- **Capability:** List palette user tasks (~23 types, including custom).
- **Business task for AI:**
  > "Which task types are available in the designer palette on this stand?"
- **Check:** the catalog is returned (~23 built-in + the stand's custom tasks). If a custom user task
  was added to the stand — it is present in the list.

## TC-C-05 — `validate-process-graph` and the divergence from the builder `[P4 — fork]`

- **Capability:** Graph validation (R1–R17), validate-vs-build fork.
- **Business task for AI:**
  > "Validate the graph of a process with an exclusive gateway and two conditional flows before building."
- **Check:**
  1. The validator correctly applies BPMN rules R1–R17 (one start, no dangling/loop, gateway arity, reachability).
  2. **Record the fork:** the validator **accepts** gateways/conditional flows the builder
     **cannot** build → the subsequent `create-business-process` rejects them. This is a known divergence.

---

# Group D — Negative cases (implemented = rejects correctly)

> Goal: confirm that unsupported requests yield a **clear error**, not a raw exception,
> and that the AI honestly tells the user about the limitation.

## TC-D-01 — Branching request (gateway) `[P4]`

- **Business task for AI:**
  > "Create a process: if the deal amount > 1000 — request approval, otherwise auto-approve."
- **Expected result:** the builder **rejects** the gateway/conditional flow with an explicit message;
  the AI explains that branching is not supported yet. No broken process is created.

## TC-D-02 — Timer / message start request `[P4]`

- **Business task for AI:**
  > "Create a process that runs every morning at 9:00."
- **Expected result:** timer start is not built — a clear error/refusal. Same for message start.

## TC-D-03 — Signal filter / data-operation config request `[P4]`

- **Business task for AI:**
  > "Create a process: when a Lead is modified **only in status New**, read the related contacts."
- **Expected result:** the signal filter and Read data config are **not built**
  (cost-center, ENG-91842/PR #6 not merged). Read data may be placed as an empty shell,
  but cannot be configured. The AI clearly reports the gap.

## TC-D-04 — Default value on a process parameter request `[P4]`

- **Business task for AI:**
  > "Add a parameter `RetryCount` with a default value of 3."
- **Expected result:** the parameter is created, but the **default value is not set** — cleanly rejected/ignored
  with a notice (marked "n/a — verified" on DB-mode).

## TC-D-05 — Combined signal events request `[P4]`

- **Business task for AI:**
  > "Start the process when a contact is created **or** modified."
- **Expected result:** only **one** trigger event is built; "added OR modified" together — rejected/unsupported.

---

# Group E — Layout and swimlanes

## TC-E-01 — X-axis auto-layout (longest-path) `[Tier1]`

- **Capability:** X-axis longest-path layout.
- **Business task for AI:**
  > "Create a linear process of 5 user tasks in a row."
- **Designer check:** elements are laid out left-to-right by flow order, no overlaps,
  start leftmost, end rightmost.

## TC-E-02 — Lane-aware placement `[P2 — FSD = No]`

- **Capability:** Lane-aware placement (Partial; lands in the first lane if no lane is named).
- **Business task for AI:**
  > "Create a process with two swimlanes and place the task in the second lane."
- **Designer check:**
  1. With an explicitly named lane — the element is in the right lane.
  2. Without a lane name — the element lands in the **first** lane (record the behavior).
- **Must NOT work:** branch-aware Y-layout (needed for gateways) — not built.

---

# Coverage summary against the capability matrix

| Case | Capability (status) | Priority |
|------|----------------------|----------|
| TC-A-01 | Simple start / sequence / user task / end (Impl) | P4 |
| TC-A-02 | Signal start added + Perform task, runtime (Partial) | P1/Tier2 |
| TC-A-03 | Signal start modified, runtime (Partial) | P1 |
| TC-A-04 | Signal start **deleted**, runtime (Partial) | P1 |
| TC-A-05 | Scalar parameter types (Impl, DB Partial) | P2 |
| TC-A-06 | Lookup parameter (Impl) | P2 |
| TC-A-07 | Constant / param-ref / **raw formula** (Impl/Partial) | P2 |
| TC-A-08 | User task auto-synced params (Partial) | P3 |
| TC-A-09 | **End event** legacy type, runtime (Partial) | P1 |
| TC-B-01 | Add element + **relayout** (Partial) | P4 |
| TC-B-02 | **Remove element/connection** (Partial, Gap) | P1 |
| TC-B-03 | Atomic rollback (Impl) | P4 |
| TC-B-04 | Swap Simple⇄Signal (Impl) | P4 |
| TC-B-05 | Add process parameter (Impl) | P2 |
| TC-C-01 | describe-business-process simple (Impl) | Tier1 |
| TC-C-02 | describe-business-process complex, semantics (Impl/Partial) | P4 |
| TC-C-03 | **get-process-signature** (Impl, never verified) | P1 |
| TC-C-04 | list-user-tasks (Impl) | P4 |
| TC-C-05 | validate-process-graph + fork (Impl/Partial) | P4 |
| TC-D-01..05 | Negative: gateway / timer / filter / default / combined | P4 |
| TC-E-01 | X-axis layout (Impl) | Tier1 |
| TC-E-02 | Lane-aware placement (Partial, FSD=No) | P2 |

> **Re-verify policy:** re-check after server PRs merge. Open front —
> PR #6 (data-source-filters / ENG-91842): after merge, add positive cases for
> signal filters and data operations (currently in Group D as negative).
