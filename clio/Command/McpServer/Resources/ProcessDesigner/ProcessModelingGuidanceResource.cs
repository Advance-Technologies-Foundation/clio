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
			  (aliases `readData`->ReadDataUserTask, `performTask`->ActivityUserTask).
			- Sequence flows; process-level parameters (with an optional constant default value); element-parameter mappings.
			- NOT yet buildable: gateways, conditional/default flows, timer/message start, intermediate events,
			  sub-process, Read-data filters/column config. Use the catalog below to reason about a solution and to
			  READ existing processes (`describe-business-process`); don't expect to build those types in this increment.

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
			- To convert an EXISTING process to start on a record event, use `modify-business-process`:
			  removeElement the current start, addElement a `signalStart`, addFlow signalStart -> (first activity).

			== Build recipe (intent -> running process) ==
			1. Translate the request into a graph: one start event, the activities, the sequence flows, one or
			   more end events; plus process parameters and the value mappings between them.
			2. (recommended) `validate-process-graph(graph)` -> fix every error-severity finding.
			3. `list-user-tasks` -> pick the exact `userTaskName`(s) for your activities.
			4. `create-business-process(descriptor)` -> builds + saves in one call (layout is automatic).
			5. Verify: `describe-business-process` (element types, user-task names, parameter sources + direction, the signal trigger) /
			   `generate-process-model` / `execute-esq` (VwProcessLib by caption).
			6. Change it later with `modify-business-process` (ops: addElement / removeElement / addFlow / removeFlow /
			   addParameter / addMapping / setParameter / removeParameter — same parameter/mapping shapes as a build).
			- File-design-mode caveat: on an FSD stand a built process is saved to the file system (the designer
			  sees it) but is NOT runtime-active until it is loaded FS->DB and published — so a signal won't
			  physically fire yet.

			== Element catalog (data-id -> label -> purpose) ==
			(The `data-id` strings below are the vocabulary for `validate-process-graph` and for reasoning about /
			reading processes. To BUILD, map them to the create-business-process `type` + `userTaskName`: events
			`startEvent`/`startEventSignal`->`signalStart`/`endEvent`; a user/system task -> `type:"userTask"` with
			`userTaskName` from list-user-tasks, e.g. Perform task = `performTask`/ActivityUserTask, Read data =
			`readData`/ReadDataUserTask.)
			System actions (palette group "System actions"):
			- `readDataUserTask`  Read data    — read first record / aggregate / count / collection of an object.
			    Setup fields: DataReadMode, EntitySchemaSelect (object), filters, SortByColumn_N, ColumnSelectMode.
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
			  Parameter-to-parameter mappings require COMPATIBLE TYPES: source and target in the same data-value-type
			  group (text↔text, number↔number, …; for a lookup the same reference object) — exactly what the visual
			  designer allows; incompatible types are rejected. `processParameter` flows a process input into the
			  field (the server builds the correct reference); `expression` is a C#-like formula, e.g.
			  `[#SysVariable.CurrentUserContact#]`, `[#System variable.Current date and time#].AddDays(3)`.
			- Reference syntax when an expression must read another element's output: `[#ElementName.PropertyPath#]`
			  (e.g. `[#Read data.First item.Id#]`). Formulas are strictly typed (convert with `.ToString()` etc.).

			== Connection rules R1–R17 (validate-process-graph enforces the structural subset: R1–R3, R7,
			   R9–R15, R17; R4–R6, R8 and R16 are semantic or not yet enforced — verify those yourself) ==
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
