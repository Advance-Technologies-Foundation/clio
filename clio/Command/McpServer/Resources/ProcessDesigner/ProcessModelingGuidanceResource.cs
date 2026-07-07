using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources.ProcessDesigner;

/// <summary>
/// Provides canonical AI-facing guidance for designing Creatio business processes (BPMN) through clio MCP:
/// the element catalog, the connection rules (R1–R17), and the declarative build recipe
/// (create-business-process / modify-business-process / describe-business-process / list-user-tasks).
/// </summary>
[McpServerResourceType]
[FeatureToggle("process-designer")]
public sealed class ProcessModelingGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/process-modeling";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for AI-driven Creatio business process modeling.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "process-modeling-guidance")]
	[Description("Returns canonical MCP guidance for designing Creatio business processes: the declarative build recipe (create-business-process / modify-business-process / list-user-tasks / describe-business-process), the BPMN element catalog, and connection rules R1-R17.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
			clio MCP process-modeling guide — design Creatio business processes (BPMN)

			== How clio builds processes (read first) ==
			- clio makes no LLM call. You own the intent->BPMN translation: decide which elements the process
			  needs, their parameters, and how they connect. The server-side ProcessDesignService package owns
			  metadata serialization — you NEVER hand-author process metadata, filters, or column mappings.
			- The build is DECLARATIVE: you describe the process (elements + flows + parameters + mappings) and
			  clio builds + saves it in one call. Diagram layout is automatic (start leftmost, end rightmost, no
			  overlap) — do not set positions.
			- Tools:
			  * list-user-tasks         — the user-task palette (name + uid); pass a name as `userTaskName`.
			  * create-business-process — build a NEW process from a JSON descriptor, and save it.
			  * modify-business-process — edit an EXISTING process by an ordered list of operations.
			  * describe-business-process        — read a process back as a structured graph (verify / explain).
			  * validate-process-graph  — pre-check a planned graph against the connection rules R1-R17.

			== What you can build today (create-business-process) ==
			- Events: `startEvent` (Simple start), `signalStart` (record signal: add/modify/delete), `endEvent`.
			- Activities: `userTask` referencing any task from list-user-tasks via `userTaskName`
			  (aliases `readData`->ReadDataUserTask, `performTask`->ActivityUserTask). CAVEAT: `readData` (and the
			  other data-operation tasks) PLACES an UNCONFIGURED element — its source object, filters, and columns
			  cannot be set yet, so the step does nothing useful until a human configures it in the designer. Say so
			  when you use it; do not present the result as a working data operation.
			- Sequence flows; process-level parameters (with an optional constant default value); element-parameter mappings.
			- A data source `filter` on a `signalStart` to restrict WHICH records fire the trigger (see the
			  "Data source filters" section below).
			- NOT yet buildable: gateways, conditional/default flows, timer/message start, intermediate events,
			  sub-process, the Read/Add/Modify/Delete-data target object + read config (so a `filter` on a data
			  task is serialized but not end-to-end usable yet — only the `signalStart` filter is). Use the catalog
			  below to reason about a solution and to READ existing processes (`describe-business-process`); don't
			  expect to build those types in this increment.

			== Descriptor (create-business-process) ==
			{
			  "name": "UsrSchemaCode", "caption": "Title", "packageName": "Custom",
			  "elements": [
			    { "name": "Start1", "type": "startEvent" },
			    { "name": "task1",  "type": "performTask", "caption": "..." },
			    { "name": "End1",   "type": "endEvent" }
			  ],
			  "flows":      [ { "source": "Start1", "target": "task1" }, { "source": "task1", "target": "End1" } ],
			  "parameters": [ { "name": "MyText", "type": "Text", "direction": "In", "caption": "..." } ],
			  "mappings":   [ { "elementName": "task1", "elementParameter": "<ParamName>", "processParameter": "MyText" } ]
			}
			- `name` is the local element handle (the schema element Name, a string code) used by flows
			  (`source`/`target`) and mappings (`elementName`). Creatio identifies an element by this Name plus a
			  UId GUID; the platform reserves "Id" for the GUID, so the handle is `name`, not `id`. A `userTask`
			  element auto-carries the task's parameters; map values into them with `mappings`. For a record trigger
			  use `signalStart` (next section).

			== Trigger a process on a record event ("run on save" of a page/record) — READ THIS ==
			- When the goal is "run a process when a record is saved / added / changed / deleted" (e.g. on a page
			  like UsrXxx_FormPage), that is a PROCESS trigger, NOT page logic. Make the process START with a
			  Signal start element bound to the object. Do NOT add a client-side save handler
			  (`crt.SaveRecordRequest` / any page handler) to launch a process on save — that is the wrong tool and
			  a fragile workaround. The signal start is the platform-native, declarative trigger.
			- Build it with `create-business-process`. The start element is:
			    { "name": "Start1", "type": "signalStart", "signal": { "entity": "<EntityName>", "on": "modified" } }
			  then the activity (e.g. a Perform task / `performTask` that shows a Task), then an `endEvent`,
			  wired Start1 -> activity -> end. (`entity` is the page's object, e.g. UsrTestRunButton.)
			- `on` is a SINGLE event: "added" | "modified" | "deleted" (the designer has no combined
			  "added or modified"). "On save" of a record edited on a page = "modified"; a brand-new record = "added".
			- To fire the trigger ONLY for records matching a condition (e.g. only when Name = "Start"), add a
			  `filter` to the signalStart element (full shape in "Data source filters" below):
			    { "id": "Start1", "type": "signalStart",
			      "signal": { "entity": "UsrTestRunButton", "on": "modified" },
			      "filter": { "object": "UsrTestRunButton",
			        "conditions": [ { "column": "UsrName", "comparison": "equal", "value": "Start" } ] } }
			  Use the entity COLUMN name (here `UsrName`), not the field caption ("Name").
			- To convert an EXISTING process to start on a record event, use `modify-business-process`:
			  removeElement the current start, addElement a `signalStart`, addFlow signalStart -> (first activity).

			== Data source filters (signalStart trigger condition) ==
			- A `filter` declares, high-level, WHICH records a filtered element acts on. The server serializes it to
			  the platform Terrasoft.FilterGroup — you NEVER hand-write the escaped filter JSON.
			- Usable today on a `signalStart` (restrict the record trigger). Shape:
			    "filter": {
			      "object": "<EntityName>",        // root object; defaults to the signal entity if omitted
			      "logicalOperation": "and",       // "and" (default) | "or"
			      "conditions": [
			        { "column": "UsrName",      "comparison": "equal", "value": "Start" },
			        { "column": "Account.Code", "comparison": "equal", "value": "1" }   // dot-path traverses a lookup
			      ],
			      "groups": [                       // optional nested groups, each with its own logicalOperation
			        { "logicalOperation": "or", "conditions": [ /* ... */ ] }
			      ]
			    }
			- `column` is the entity COLUMN name (e.g. `UsrName`, not the caption "Name") and may be a dot-path
			  through lookups (`Account.Code`, `Account.Owner.Name`); the server resolves the column type from the
			  object's schema (so you don't supply types).
			- `comparison`: equal (default) | notEqual | greater | greaterOrEqual | less | lessOrEqual | contains |
			  notContains | startWith | notStartWith | endWith | notEndWith | isNull | isNotNull.
			- The right-hand value of a condition is exactly ONE of: `value` (a constant as a string — the server
			  types it by the column; for a Date/DateTime/Time column pass ISO-8601, e.g. `2026-05-01` or
			  `2026-05-01T12:00:00Z`), `processParameter` (a process parameter by name), `elementParameter`
			  ({ elementId, parameter } — another element's output), `expression` (a raw token). isNull/isNotNull
			  take none.
			- Groups nest to any depth: A AND (B OR C) = conditions:[A] + groups:[{ "logicalOperation":"or",
			  conditions:[B, C] }].
			- A `filter` on a data task (Read/Add/Modify/Delete data) is serialized too, but those tasks' target
			  object / read config is not buildable yet, so data-task filters are NOT end-to-end usable in this
			  increment — use the signalStart filter.
			- On an EXISTING process, set/clear a filter via `modify-business-process` ops `setFilter`
			  ({ op:"setFilter", elementName, filter }) and `clearFilter` ({ op:"clearFilter", elementName }).
			  `setFilter` REPLACES the element's whole filter (there is no add-one-condition op); to add a condition,
			  read the current filter first (below) and send the complete new filter.
			- `describe-business-process` reads a filter back: an element carries a decoded `filter` (the same
			  object / logicalOperation / conditions / groups shape) when it has one, so you can inspect it or
			  round-trip it into a `setFilter`. A parameter reference comes back as its raw meta-path `expression`.

			== Build recipe (intent -> running process) ==
			1. Translate the request into a graph: one start event, the activities, the sequence flows, one or
			   more end events; plus process parameters and the value mappings between them.
			2. (recommended) `validate-process-graph(graph)` -> fix every error-severity finding.
			3. `list-user-tasks` -> pick the exact `userTaskName`(s) for your activities.
			4. `create-business-process(descriptor)` -> builds + saves in one call (layout is automatic).
			5. Verify: `describe-business-process` (element types, user-task names, parameter sources + direction + isResult
			   — an output you can map FROM has `isResult:true` or `direction:"Out"`; the signal trigger) /
			   `generate-process-model` / `execute-esq` (VwProcessLib by caption).
			6. Change it later with `modify-business-process` (ops: addElement / removeElement / addFlow / removeFlow /
			   addParameter / addMapping / setParameter / removeParameter / setFilter / clearFilter — same parameter/mapping/filter shapes as a build).
			- File-design-mode caveat: on an FSD stand a built process is saved to the file system (the designer
			  sees it) but is NOT runtime-active until it is loaded FS->DB and published — so a signal won't
			  physically fire yet.

			== Modifying an existing process — safety rules (modify-business-process) ==
			- ALWAYS `describe-business-process` first, and re-describe after the edit to verify the result.
			- The modify path runs NO structural validation (only the create path validates the graph):
			  `removeElement` / `removeFlow` can leave the process unreachable or with dangling paths and the save
			  still succeeds. `removeElement` also CASCADES — it deletes every flow touching the element and the
			  mappings TARGETING it, but does NOT re-join the flow across the gap, and mappings/values READING the
			  removed element's outputs may survive as dangling references. Add the bridging `addFlow` in the same
			  operations array, then re-describe and clean up any leftover references to the removed element.
			- Before removals, run `validate-process-graph` on the graph AS IT WILL BE after your operations
			  (describe output + your planned ops applied), and confirm destructive removals with the user.
			- If describe shows constructs the builder cannot create (gateways, conditional/default flows,
			  sub-process, timer/message/intermediate events), they survive a save untouched as data — but you CAN
			  still remove or rewire them by name and nothing will warn you. Treat such a process as high-risk:
			  prefer additive edits, do not remove or rewire those elements, and tell the user what you left alone.
			- Every modify re-applies the automatic layout to the WHOLE diagram: a hand-arranged multi-lane or
			  branched diagram is flattened into generated left-to-right rows (process data intact, manual layout
			  lost). Warn the user before editing a process with a curated diagram.

			== Element catalog (data-id -> label -> purpose) ==
			(The `data-id` strings below are the vocabulary for `validate-process-graph` and for reasoning about /
			reading processes. To BUILD, map them to the create-business-process `type` + `userTaskName`: events
			`startEvent`/`startEventSignal`->`signalStart`/`endEvent`; a user/system task -> `type:"userTask"` with
			`userTaskName` from list-user-tasks, e.g. Perform task = `performTask`/ActivityUserTask, Read data =
			`readData`/ReadDataUserTask.)
			System actions (palette group "System actions"):
			- `readDataUserTask`  Read data    — read first record / aggregate / count / collection of an object.
			    Setup fields: DataReadMode, EntitySchemaSelect (object), filters, SortByColumn_N, ColumnSelectMode
			    (designer-only for now — the builder cannot set them; a built Read data lands unconfigured).
			- `addDataUserTask`   Add data     — create record(s) in background; one-record mode returns only the Id.
			- `changeDataUserTask` Modify data — bulk-update matched records (same values to all).
			- `deleteDataUserTask` Delete data — delete matched records.
			- `formulaTask`       Formula      — compute a value (math/string/date/bool) into an output param.
			- `scriptTask`        Script task  — custom C# (ends with `return true;`; needs publication).
			- `webService`        Call web service — call a registered service; outputs Success + Http status code.
			- `callActivity`      Sub-process  — run another process (must start with a Simple start); multi-instance over a collection.
			- `userTask`/`*UserTask` — user/system tasks (Perform task, Open edit page, Send email, Approval, etc.).
			User actions: `activityUserTask` Perform task, `userQuestionUserTask` User dialog,
			  `openEditPageUserTask` Open edit page, `autoGeneratedPageUserTask` Auto-generated page,
			  `preconfiguredPageUserTask` Pre-configured page, `emailTemplateUserTask` Send email, `approvalUserTask` Approval.
			Events: `startEvent` Simple start, `startEventSignal` Signal start (record add/modify/delete or custom
			  signal), `startEventTimer` Start timer (schedule/CRON), `startEventMessage` Start message, intermediate
			  catch/throw (`intermediateCatchEvent*`/`intermediateThrowEvent*`), `endEvent` End/Terminate.
			Gateways: `exclusiveGateway` (OR), `parallelGateway` (AND), `inclusiveGateway` (OR), `eventBasedGateway`.
			Flows: sequence (default `connect`), conditional (setup -> conditionalConnection), default (setup -> defaultConnection).

			== Parameters / mapping / formulas ==
			- Process parameters (`parameters[]`): { name, type (Text/Long text/Integer/Float/Money/Boolean/Date/Date-time/Time/Guid/Lookup),
			  direction (In/Out/Variable/Internal), caption, description, or referenceSchema = an object name (e.g. City) to make
			  it a Lookup to that object }, and an optional value (a constant default). A user-task element's own parameters come from the task. The same shape is
			  used by modify-business-process `addParameter`. To create a process parameter that mirrors an element parameter's EXACT type (e.g. expose a user-task OUTPUT for mapping with NO conversion), set `typeFromElement` + `typeFromElementParameter` instead of `type`/`referenceSchema` — the data value type (and lookup reference object) is copied verbatim. Edit a parameter with `setParameter` (parameterName + parameterUpdate: any of caption/description/code/direction/referenceSchema/value, applied in place — the UId and its references are preserved; a data-type change is rejected) and remove it with `removeParameter` (parameterName; blocked when another parameter's value or an element mapping still references it). Supported types: Text, Long text, Integer, Float, Money, Boolean, Date, Date-time, Time, Guid, and Lookup — other types (composite / entity / file / ...) are not supported yet.
			- Mappings (`mappings[]`): bind a TARGET parameter to a SOURCE.
			  TARGET — `elementName` + `elementParameter` (an element input) OR `targetProcessParameter`
			  (a process parameter, e.g. expose an element's OUTPUT as a process output).
			  SOURCE — exactly ONE of: `sourceElement` + `sourceElementParameter` (another element's OUTPUT parameter) |
			  processParameter (a process parameter by name) | value (a constant) | expression (a raw formula).
			  Identifying an OUTPUT for `sourceElementParameter`: in `describe-business-process` output an element parameter
			  is usable as a mapping source when `isResult: true` OR `direction: "Out"`. Most user-task outputs come back as
			  `isResult: true` with `direction: "Variable"` (the platform reports element params as Variable), so detect
			  outputs by `isResult`, NOT by `direction` alone.
			  Parameter-to-parameter mappings require COMPATIBLE TYPES: source and target in the same data-value-type
			  group (text↔text, number↔number, …; for a lookup the same reference object) — exactly what the visual
			  designer allows; incompatible types are rejected. `processParameter` flows a process input into the
			  field (the server builds the correct reference); `expression` is a raw C#-like formula passed through UNVALIDATED — the backend (unlike the visual designer) does NOT check it, so a wrong token / function / type fails only at RUNTIME. Do NOT invent or guess formulas: formula-authoring guidance (token format + the allowed function set) is not available yet. Prefer `value` / `processParameter` / `sourceElement`; use `expression` ONLY with a formula you already know is correct (user-supplied, or copied verbatim from an existing process via describe-business-process), e.g.
			  `[#SysVariable.CurrentUserContact#]`, `[#SysVariable.CurrentDateTime#].AddDays(3)`.
			- Date / Date-time / Time DEFAULT VALUES are the ONE formula you may author (an EXCEPTION to the
			  "don't invent formulas" rule): the designer stores a date/time constant as a formula macro (a Script
			  source), NOT a plain `value` (a `ConstValue`). Set it via `expression` — for a process-parameter
			  default, a mapping with `targetProcessParameter` + `expression`. The inner format is FIXED (NOT ISO,
			  NOT locale): `dd.MM.yyyy` and 24-hour `HH:mm`.
			  Date → `[#DateValue.dd.MM.yyyy#]` (e.g. `[#DateValue.03.07.2026#]`);
			  Date-time → `[#DateTimeValue.dd.MM.yyyy HH:mm#]` (e.g. `[#DateTimeValue.03.07.2026 02:15#]`);
			  Time → `[#TimeValue.HH:mm#]` (e.g. `[#TimeValue.12:20#]`). A LOOKUP default is the same kind of macro — set via `expression` as `[#Lookup.{referenceObjectSchemaUId}.{recordId}#]` (both are GUIDs: the referenced OBJECT's schema UId, NOT its name, and the chosen RECORD's Id, e.g. `[#Lookup.5ca90b6a-…(City object).1548d3d2-…(a City record)#]`). You cannot guess these ids — copy the token from an existing process (`describe-business-process`) or resolve the object/record ids first; a bare record id as `value` will NOT work.
			- To read another element's output, PREFER the structured `sourceElement` + `sourceElementParameter` mapping (above) — the server builds the correct reference. Do NOT hand-write an element-output reference —
			  in the saved metadata it is a server-generated UId meta-path (`[#...[Element:{uid}].[Parameter:{uid}].[EntityColumn:{uid}]#]`), NOT a friendly `Element.Property` path, so you cannot author it — ALWAYS use `sourceElement`. Formulas are strictly typed (convert with `.ToString()` etc.).

			== Connection rules R1–R17 (validate-process-graph enforces the structural subset: R1–R3, R7,
			   R9–R15, R17; R4–R6, R8 and R16 are semantic or not yet enforced — verify those yourself.
			   Validation pass ≠ buildable: the rules cover the FULL catalog incl. gateways and conditional
			   flows, but only the "What you can build today" slice above can actually be built) ==
			R1  Start event: no incoming flow; exactly one outgoing.
			R2  End event: no outgoing flow; one or more incoming.
			R3  Exactly one top-level start event; every path reaches an end event.
			R4  Terminate end kills the whole instance; Simple end ends only its path.
			R5  Start triggers: Simple=user/run; Signal(object)=record add/modify/delete; custom signal=broadcast; message=directed; timer=schedule/CRON.
			R6  Diverging gateway: 1 in, >=2 out. Converging gateway: >=2 in, 1 out.
			R7  Exclusive(OR) diverge: conditional flows + exactly one default; one path taken. Converge: first arrival, no sync.
			R8  Parallel(AND) diverge: all out fire, plain sequence flows only. Converge: waits for all incoming.
			R9  Inclusive(OR) diverge: conditional flows + required default; >=1 path. Converge: syncs active branches.
			R10 Event-based gateway: each outgoing sequence flow leads directly to an intermediate catch event; first event wins.
			R11 Parallel and event-based gateways must not carry conditional/default flows.
			R12 Sequence flow: target runs after source. Multiple outgoing sequence flows = implicit parallel split.
			R13 Conditional flow originates only from a gateway or an activity.
			R14 Default flow is legal only if >=1 conditional flow leaves the same element; diverging Exclusive/Inclusive require a default.
			R15 No orphan/unreachable nodes; every flow needs a valid source and target.
			R16 Sub-process (callActivity) target must begin with a Simple start; collection mapping => multi-instance.
			R17 (advisory) Add data one-record mode outputs only Id; chain a Read data for other fields.

			Quick can/can't (source -> target via sequence flow): start->{activity,gateway,intermediate,end} ok,
			never ->start (R1); end is a sink, never a source (R2); event-based gateway out must hit a catch event (R10).
			"""
	};
}
