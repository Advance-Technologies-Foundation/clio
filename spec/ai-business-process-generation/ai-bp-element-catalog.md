# Creatio Process element catalog (for AI process design)

> Authoritative element reference for the AI knowledge base (subtask 1). Sources:
> live Creatio 8.3 designer palette (`data-id`s), Academy 8.x docs, the QA repo
> `C:\Projects\Cucumber\bpms` (setup-card field codes), and clio `Schema.cs` `ManagerMap`.
> `data-id` = the diagram-js element type. It is the vocabulary for `validate-process-graph`
> (the node `type`) and for reasoning about / reading back processes (`describe-business-process`
> returns the runtime class plus the round-trippable `buildType`). It is **not** a UI action — the
> shipped flow builds processes declaratively via the backend MCP tools.
> Feeds `ProcessModelingGuidanceResource.cs` (MCP guidance) and the connection validator
> (`validate-process-graph`).

## How the AI should use this

The shipped flow is **declarative**: you describe the process (elements + flows + parameters +
mappings) as a JSON descriptor and the backend builds and saves it in one call. You never drive the
designer, place nodes, or configure setup cards by hand — the server-side `ProcessDesignService`
owns all metadata serialization and layout is automatic.

1. Map the user's plain-language intent → the right element kind(s) below. Use the `data-id` and the
   setup-card field codes to reason about which element fits and what configuration it implies.
2. `validate-process-graph` — pre-flight the planned graph (nodes by `data-id` + flows) against the
   connection rules R1–R17 (see `ai-bp-connection-rules.md`); fix every error-severity finding before
   building.
3. `list-user-tasks` — pick the exact `userTaskName`(s) for the activities you plan to build.
4. `create-business-process` — pass the JSON descriptor (`{ name, caption, packageName, elements[],
   flows[], parameters[], mappings[] }`); the process is built **and** saved in one call, with
   automatic left-to-right layout (do not set positions).
5. Iterate / verify — `modify-business-process` for ordered edits (addElement / removeElement /
   addFlow / removeFlow / addParameter / addMapping), `describe-business-process` to read the result
   back as a structured graph.

> The `data-id` strings in the tables below are the validate/read-back vocabulary. To **build**, map
> them to the `create-business-process` descriptor `type` token (e.g. events `startEvent` /
> `signalStart` / `endEvent`; a user/system task → `type:"userTask"` with a `userTaskName` from
> `list-user-tasks`, e.g. Perform task = `performTask`/`ActivityUserTask`, Read data =
> `readData`/`ReadDataUserTask`). The full token mapping (build `type` ↔ describe `buildType` ↔
> validate `data-id`) is published in `clio/docs/commands/describe-business-process.md` →
> "Element type vocabulary".

---

## System actions (palette group "System actions" / `create-serviceTask`)

| data-id | Label (EN/RU) | Purpose / when to use | Key setup-card fields | Output(s) |
|---|---|---|---|---|
| `readDataUserTask` | Read data / Читать данные | Fetch field values, an aggregate, a count, or a collection of records from an object. | `DataReadMode` (Read first record / Calculate function / Read count / Read collection), `EntitySchemaSelect` (object), filters, `SortByColumn_N`+`SortingOrderDirection_N`, `ColumnSelectMode` (all / selected), selected columns | first-record fields / function result / count / **collection** |
| `addDataUserTask` | Add data / Добавить данные | Create record(s) in the background (no UI, bypasses user perms). | object, mode (Add one record / Add selection), field-value mapping, selection filters | **new record `Id`** (one-record mode returns only Id → chain a Read data for other fields) |
| `changeDataUserTask` | Modify data / Изменить данные | Bulk-update existing records (same values to all matched). | object, filter (which records), field→value mappings | — |
| `deleteDataUserTask` | Delete data / Удалить данные | Delete record(s) regardless of user perms. | object, filter (which records) | — |
| `formulaTask` | Formula / Формула | Compute a value (math/string/date/bool) into an output param. | the formula expression (`ResultParameterMetaPath` + formula editor), output type | computed value |
| `changeAdminRightsUserTask` | Change access rights | Grant/revoke record permissions. | object, filter, users/roles, permission type (Read/Edit/Delete), action (Grant/Revoke), level | — |
| `webService` | Call web service | Call a registered web service and use the response. | service, method, timeout, request params, response params | `Success` (bool), `Http status code`, response params |
| `scriptTask` | Script task / Задание-сценарий | Custom C# logic (code ends `return true;`; needs publication). | C# body; `Get<T>/Set<T>("element.param")` | per code |
| `callActivity` | Sub-process (Call activity) | Run another process; reuse/decompose; multi-instance over a collection. | target process (must start with **Simple start**), execution method (Sequential/Parallel), parameter mapping (in/out/bi-dir) | mapped out params |
| `linkEntityToProcessUserTask` | Connect process to object | Link the running instance to a record (shows in record's Process log). | `EntitySchemaId` (object), `EntityId` (record) | — |
| `userTask` | User task (generic) | Generic task referencing a user-task schema with in/out params. | `Schema`, input `Value`s, `OutputResult` | schema outputs |
| `mLDataPredictionUserTask` | Predict data | ML prediction into a parameter. | model, input mapping | prediction |
| `searchDuplicatesUserTask` | Find and merge duplicates | Dedup logic. | object, rules | — |
| `objectFileProcessingUserTask` | Process file | File handling. | file source/params | — |
| `eventSubProcessExpanded` | Event sub-process | Embedded event-triggered sub-flow (has its own start event). | inner start event config | — |

## User actions (palette group "User actions" / `create-userTask`)

| data-id | Label | Purpose / when to use | Key setup-card fields | Result drives branching? |
|---|---|---|---|---|
| `activityUserTask` | Perform task / Выполнить задачу | Create an activity (to-do/meeting) for a user. | `caption`, `ActivityCategory` (required), `Who performs`, `StartIn`, `Duration`, `InformationOnStep`, `Recommendation` | yes (activity result) |
| `userQuestionUserTask` | User dialog / Вопрос пользователю | Ask the user to pick option(s). | `caption`, dialog text, `Who performs`, answer options, default, single (→Exclusive gw) / multiple (→Inclusive gw) | **yes** |
| `openEditPageUserTask` | Open edit page | Open a record page to add/edit. | which page, editing mode (Add/Edit), record Id / default values, `Who performs`, completion condition | yes (column results) |
| `autoGeneratedPageUserTask` | Auto-generated page | Dynamic form of fields + buttons. | page title, `Who performs`, buttons (each may emit a signal), page items | **yes** (clicked button) |
| `preconfiguredPageUserTask` | Pre-configured page | Open a custom Freedom/Classic UI page. | which page, `Who performs`, page parameters, completing buttons | yes |
| `emailTemplateUserTask` | Send email / Отправить e-mail | Send email (auto) or open pre-filled email (manual). | `From`, `Recepient`/`CopyRecepient`/`BlindCopyRecepient`, `Subject`, custom/template message, `Body`, `Importance`, `IsIgnoreErrors`, send mode | `Task Id`, errors flag |
| `approvalUserTask` | Approval / Визирование | Create an approval, assign approver, branch on outcome. | approval object, record Id, approver (Employee/Manager/Role), delegate flag, notify options | **yes** (approved/rejected) |

## Events

| Element | data-id (`codeDiagramJS`) | Trigger / role | Key config |
|---|---|---|---|
| Simple start | `startEvent` | Started by user/run action. | — |
| Signal start | `startEventSignal` | Auto-start on record add/modify/delete (object mode) or a broadcast custom signal. | `SignalType`, object + `EntityChangeType` (Add/Modify/Delete) **or** custom signal value |
| Start timer | `startEventTimer` | Schedule/CRON. | start time, frequency (Once/Min/Hour/Day/Week/Month/Year/CRON), timezone |
| Start message | `startEventMessage` | Directed message receipt. | message def |
| Intermediate catch (message/signal/timer) | `intermediateCatchEventMessage`/`...Signal`/`...Timer` | Pause until message/signal/timer; place **before** the element to run after. | `SignalType`, `SignalValue`, object/record, `EntityChangeType`, `ExpectChanges` |
| Throw signal | `intermediateThrowEvent` | Broadcast a signal to **all active processes**; place **after** the trigger. | `MessageValue` (signal name) |
| Throw message | `intermediateThrowEventMessage` | Directed message. | message def |
| End / Terminate | `endEvent` | End a path; **Terminate** kills the whole instance (all parallel branches). | — |

## Gateways

| Gateway | data-id | Diverge | Converge | Flow types |
|---|---|---|---|---|
| Exclusive (OR) | `exclusiveGateway` | exactly ONE path | first arrival, no sync | conditional + **required default** |
| Parallel (AND) | `parallelGateway` | ALL paths | **waits for all** | sequence only (no conditions) |
| Inclusive (OR) | `inclusiveGateway` | every true condition (≥1) | sync active branches | conditional + **required default** |
| Exclusive event-based | `eventBasedGateway` | first event wins (race) | — | sequence only; each → an intermediate **catch** event |

## Flows

These are the conceptual flow kinds you express **declaratively** in the descriptor's `flows[]`
(each `{ source, target }`), not UI actions. `validate-process-graph` records the kind per edge as
`flowKind ∈ sequence|conditional|default`.

| Flow | Flow kind / `data-id` | Rule |
|---|---|---|
| Sequence flow | `sequence` (default) | execution order; **multiple outgoing = implicit parallel split** |
| Conditional flow | `conditionalConnection` | only from a **gateway** or an **activity**; activates when condition true |
| Default flow | `defaultConnection` | legal only if **≥1 conditional flow** leaves the same element; fallback path |

> Buildability note: only `sequence` flows are buildable in this increment. Conditional and default
> flows are valid for reasoning / read-back and are recognized by `validate-process-graph`, but
> `create-business-process` does not build them yet (see the buildability caveat in the guidance
> resource and the ADR).

Invalid connections are caught **before** the build by `validate-process-graph` (R1–R17): it returns
structured findings `{ severity, ruleId, message, node/edge }` so the agent fixes the graph up front.
There is no live designer check — the validator is the pre-flight authority.

## Parameters / mapping / formulas (summary — full detail in the guidance resource)
- **Parameters**: process-level (Parameters tab) vs element-level; types Text/Integer/Decimal/
  Boolean/Lookup/Date-Time/Currency/Collection/Id; direction Input (set before) / Output (set
  during) / bi-directional (sub-process). Value sources: constant, system setting/variable,
  formula, or another parameter (= mapping).
- **Mapping**: value-picker → Process parameter → source element → source parameter. Reference
  syntax `[#ElementName.PropertyPath#]`, e.g. `[#Read data.First item.Id#]`,
  `[#Read products.First element of the resulting collection.Name#]`, `[#ProcessParam#]`.
  Add data → returns only `Id` → chain Read data for other fields.
- **Formulas**: C#-like, strictly typed (convert with `.ToString()` etc.); editor tabs Process
  elements / Process parameters / System settings / Lookup / System variables / Functions /
  Date and time. Examples: `"text " + [#param#]`, `[#System variable.Current date and time#].AddDays(3)`,
  `([#A#] - [#B#]).TotalHours`. A single string-literal formula renders as a plain constant.
