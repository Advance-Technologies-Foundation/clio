using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that builds a business process on a Creatio environment from a declarative JSON descriptor.
/// </summary>
public class CreateBusinessProcessTool(
	CreateBusinessProcessCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<CreateBusinessProcessOptions>(command, logger, commandResolver) {

	internal const string CreateBusinessProcessToolName = "create-business-process";

	/// <summary>
	/// Builds a business process from an inline JSON descriptor on the specified environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="descriptor">Inline JSON process descriptor.</param>
	/// <param name="packageName">Optional package name that overrides the descriptor's <c>packageName</c>.</param>
	/// <returns>The command execution result with the created schema identity in the log output.</returns>
	[McpServerTool(Name = CreateBusinessProcessToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		 OpenWorld = false),
	 Description("BEFORE CALLING with an accessRights block: that block changes who can read, edit or delete LIVE records. Show the user the target object, the element record filter that decides WHICH records are affected, and every grantee with its operations and level - calling out level:delegate as onward re-sharing, level:restrict as the platform Deny level, which is DESTRUCTIVE rather than inert: it DOWNGRADES an existing Allow row for that grantee to Deny, and on a fresh insert denies the two operations you did not name, so it deserves the same confirmation as a remove, a remove entry as a revoke, and a supplied add/remove as a REPLACEMENT that drops every entry it does not restate - and get an explicit yes. The element has no output parameters, so nothing at run time will report what it did. "
		 + "Build a business process on a Creatio environment from a declarative JSON descriptor. The "
		 + "descriptor is an object with: name (schema code), caption, packageName, elements[] "
		 + "({name (the element handle/local code), type:startEvent|signalStart|endEvent|userTask|sendEmail|approval|exclusiveGateway|parallelGateway|"
		 + "openEditPage|preconfiguredPage (aliases readData/changeData/changeAccessRights/performTask), caption, userTaskName?, "
		 + "approval? (approval elements only — the designer's Approval element, which requests a visa on a record: "
		 + "{object:<EntityName> (required on a first configuration — the object whose record goes for approval, "
		 + "resolved by NAME server-side), recordId:{exactly ONE of recordId (a fixed record: its GUID, or the "
		 + "[#Lookup...#] macro describe reports, and it MUST be a record of 'object' — a foreign id is refused "
		 + "because the designer renders that field blank and the next human save wipes the element) | "
		 + "processParameter | sourceElement + sourceElementParameter} (required on a first configuration), "
		 + "purpose? (the text shown to the approver; omitting it writes the platform default 'Approval required' ONLY on an element that has none yet — on one that already carries a purpose it is left alone, so a partial update cannot silently replace what the approver reads, which "
		 + "is what the designer itself persists), "
		 + "approver:{type:user|manager|role, employee?:<user name, id or [#SysVariable.CurrentUser#]> (types "
		 + "'user' and 'manager' only — for 'manager' it names the employee WHOSE manager approves; OMITTING it is "
		 + "a partial update, not a default: 'user' and 'manager' share one stored field, so an element that "
		 + "already names somebody keeps them — which is what makes {type:'manager'} alone mean 'that person's "
		 + "manager approves instead' — and only an element naming nobody yet falls back to the current user), "
		 + "role?:<role name or id> (type 'role' only)} (required "
		 + "on a first configuration — WHO approves; it is NOT defaulted, because making whoever ran the build the "
		 + "approver routes real approvals to somebody nobody chose) — the user or role is checked to exist, an "
		 + "ambiguous NAME is refused (pass the id), and switching TO or FROM 'role' clears the other branch's "
		 + "field, exactly as the designer does, allowDelegation?:bool, "
		 + "notifyApprover?:{emailTemplate:<TemplateName or id>} (presence switches 'Notify that approval is "
		 + "required' ON — the flag and its template are written together because the runtime gates the send on the "
		 + "flag), notifyAuthor?:{emailTemplate:<TemplateName or id>, recipient:{exactly ONE of value | "
		 + "processParameter}} (presence switches 'Notify about the approval result' ON), ignoreEmailErrors?:bool "
		 + "(already true by default on the platform)}. EITHER notification is REFUSED without an emailTemplate, "
		 + "and notifyAuthor is ALSO refused without a recipient, unless the element already carries one (so {} is "
		 + "how you switch a notification back on using what it already has). Both refusals guard the same silent "
		 + "failure: the runtime checks neither before sending and ignores email errors by default, so either gap "
		 + "yields an element that reports the notification as configured and never sends. On 'recipient' note "
		 + "that AUTHOR is a misnomer from the designer's caption — the runtime does NOT resolve the process or "
		 + "record author; it reads only the address this field writes, and sends nothing when it is empty. THREE parameters are DERIVED from 'object' server-side and never accepted as input — the visa "
		 + "schema, its master column and the section — with the platform's SysApproval fallback when the object has "
		 + "no approval settings. BRANCHING on the outcome takes a SECOND call, not a gateway: this build path "
		 + "refuses flows[].kind, so build the outgoing flows plain and then give each its condition with "
		 + "modify-business-process setFlowCondition, reading the verdict from the element's ResultParameter as "
		 + "[#[Element:{elementUid}].[Parameter:{parameterUid}]#]. The outcome set is THREE values — a two-way "
		 + "Approved/Rejected split drops the canceled case. Formula and system-setting value "
		 + "sources are not offered. get-guidance name=process-approval owns this block's full contract), "
		 + "openEditPage? (openEditPage elements only — the \"Open edit page\" element, which shows a record's edit "
		 + "page to a user. It is the DEFAULT choice whenever a user fills in COLUMNS of a record; of the other two "
		 + "page elements, Auto-generated page is NOT buildable here and Pre-configured page IS (see preconfiguredPage "
		 + "below); choosing between them is your decision, not a question for the user (asking which OBJECT or COLUMN is meant is fine): "
		 + "{page:<PageSchemaName> (required at create, e.g. AccountPageV2 or Accounts_FormPage; pageSchemaUId? is "
		 + "the escape hatch when the name is unknown) — the target OBJECT and, for a typed object, the RECORD TYPE "
		 + "are DERIVED from it. Only a page REGISTERED ON A SECTION is accepted; discover valid ones with "
		 + "list-entity-client-schemas for the object and PREFER a kind:freedom entry where the environment offers "
		 + "one. recordType? is an optional CHECK, not a selector: a mismatch is refused naming the registered type. "
		 + "editMode:add|edit (required at create) — 'add' unlocks defaultValues, 'edit' REQUIRES recordId; supplying "
		 + "the other mode's field is refused. defaultValues? (add mode only): [{column, and exactly ONE of value (a "
		 + "TEXT constant, non-empty) | processParameter | sourceElement + sourceElementParameter (an EARLIER "
		 + "element's output) | expression (a raw macro — how a LOOKUP value is set: "
		 + "[#Lookup.{objectSchemaUId}.{recordId}#])}]; an EMPTY array means 'no pre-filled values' and on a modify "
		 + "CLEARS the stored set, which omitting the field never does. recordId? (edit mode only, required there): "
		 + "exactly one of value (a fixed record Id) | processParameter | sourceElement + sourceElementParameter "
		 + "(e.g. a signalStart element's RecordId) | expression. recommendation? is the text shown on the opened "
		 + "page — required by the designer, defaults to the element caption, SINGLE line (a line break is refused; "
		 + "for a value from the process use addMapping against the Recommendation parameter). hint? is the extra "
		 + "information behind the page's info button. completion?:{mode:onSave|onConditions} — onSave is the "
		 + "default; onConditions REQUIRES the element's filter in the same request and vice versa, and a filter "
		 + "whose condition group is EMPTY counts as no conditions. performer?:{type:user|manager|role, contact? "
		 + "(user/manager: a contact formula, defaulting to the current user), role? (a SysAdminUnit record id or a "
		 + "role NAME resolved for you; an unknown name is refused), showPage?} is 'Who performs the task?' with "
		 + "'Show page automatically'. Omitting it leaves the step unassigned, which does NOT mean nobody gets it — "
		 + "the platform runs an unassigned step for the CURRENT USER. showPage follows the performer at create (true "
		 + "for user or none, false for manager/role) and an explicit true on manager/role is refused. "
		 + "logActivity?:{enabled? (supplying the block turns it ON unless you pass false; OMITTING it does NOT turn "
		 + "it off - the platform materializes the user-task schema's own default onto a new element, measured ON "
		 + "with a 5-minute duration on a 10.1.628 core, so send enabled:false when the request wants no "
		 + "activity), "
		 + "startIn?/duration?/remindIn? (each {value, unit:minutes|hours|days|weeks|months} — the unit is REQUIRED "
		 + "with a non-zero value and is stored WITH it), showInCalendar?, priority? (an ActivityPriority lookup NAME "
		 + "such as 'Medium', or its record id; an unknown one is refused)} makes the step create an Activity record "
		 + "and also makes this element's 'Connected to' links effective at run time; scheduling fields with "
		 + "enabled:false are refused. resultsByColumn?:{enabled?, column} is 'Create a list of results by column' — "
		 + "one result per value of a LOOKUP column of the page's object (another kind is refused), column REQUIRED "
		 + "unless enabled:false. TELL THE USER before building one: the CONDITIONAL flows that route those results "
		 + "are not buildable here, so the list is created but branches only after a human wires the flows. "
		 + "get-guidance name=process-modeling carries the reasoning behind each of these rules and what each refusal "
		 + "is protecting}), "
		 + "readData? (readData elements only: {source:<EntityName> (required), mode?:first|count|aggregation "
		 + "(first = the first record of the sorted selection → ResultEntity; count = how many match → ResultCount, "
		 + "takes NO columns and NO sort; aggregation = sum|avg|min|max of a column → REQUIRES aggregation:{function, "
		 + "column}, output picked by the column's type: Integer → ResultIntegerFunction, Float/Money → "
		 + "ResultFloatFunction, Date/Date-time/Time (min/max only) → ResultDateTimeFunction; any other column type is "
		 + "refused because the runtime would write NO result; sort and columns are refused for "
		 + "count/aggregation — an EMPTY columns array included, since 'read all columns' is as meaningless there as "
		 + "a selection and the runtime ignores both; the designer's collection mode is NOT buildable yet, "
		 + "ENG-96504, and is refused naming this set), "
		 + "columns?:[<ColumnName>,...] (TOP-LEVEL column names only — omit or pass [] to read ALL columns, in the "
		 + "first-record mode only; a "
		 + "dot-separated path into a linked object like Owner.Name is rejected, read the whole record instead), "
		 + "sort?:{column, direction?:asc|desc}} "
		 + "— configures WHAT the element reads; WHICH records qualify is the element's separate filter block. NOTE: "
		 + "the read record's individual COLUMN values are NOT yet referenceable downstream — a first-record element's "
		 + "only output parameter is ResultEntity (the whole record) and a count/aggregation element's is its scalar; do not build mappings, changeData values or filters "
		 + "that reference the read element's column outputs, they fail with 'element has no parameter' — the ONE place a read element's column IS addressable is a Send email body macro (see body below), which CAN drill a read element's output column), "
		 + "changeData? (changeData elements only: {source:<EntityName> (required), values:[{column, and exactly ONE "
		 + "of value (a plain constant — TEXT columns ONLY and non-empty: the platform stores it as the raw string "
		 + "and the runtime reads every non-text column typed, so a date/lookup/numeric constant is REFUSED at "
		 + "build — assign those via processParameter/sourceElement or an expression macro such as [#DateValue.…#] "
		 + "/ [#Lookup.…#]) | processParameter | sourceElement + sourceElementParameter | expression}] (required, "
		 + "one entry per column)} — configures WHAT the element updates; WHICH records is the element's filter block "
		 + "(effectively mandatory — the runtime refuses to update with an empty filter; to target one record, filter "
		 + "on Id against a process parameter or a trigger output such as a signalStart element's RecordId — NOT a "
		 + "preceding readData element's column outputs, see the readData NOTE), "
		 + "accessRights? (changeAccessRights elements only — the Change access rights/ChangeAdminRightsUserTask element: grant/revoke record permissions: {object:<EntityName> (required; the object MUST use record permissions — one with AdministratedByRecords off is REFUSED, because the element's runtime silently does NOTHING there and has no output to say so), considerTimeInFilter?, add?/remove? (permission entries, each {operations:[read|edit|delete] (at least one), level?:permit|delegate|restrict (ADD entries only — permit is the default, delegate = the grantee may share the right onward, and restrict writes the platform Deny level and is DESTRUCTIVE, not inert: UseDenyRecordRights gates only record positioning, never whether a right row is written. Against a grantee who already holds Allow it DOWNGRADES that row to Deny, and a fresh insert writes one row per operation - the one you named at your level and the OTHER TWO at Deny - so operations:['read'] denies edit and delete as well. Prefer a remove entry to take access away; a level on a remove entry is refused), grantee:{type:role|employee|selectedEmployees, role? (a role name, record id or [#Lookup…#] macro — checked against the platform role view; an ambiguous name is refused, pass the id), contact? (a contact record id or an echoed [#Lookup…#] macro — both checked to exist — or any other formula stored verbatim, e.g. a process-parameter meta-path; contact NAMES are deliberately not resolved), filter? (selectedEmployees only: a filter block over Contact — 'object' must be Contact or omitted; NOTE the runtime evaluates it with record rights DISABLED)}})} — a fourth grantee type allRolesAndUsers takes no payload and is valid on a REMOVE entry ONLY, at most one per collection: it strips EVERY rights row for its operations, which is how the shipped approval processes lock a record for approval. It is NOT interchangeable with a role entry on a remove — that drops one role's row and leaves individual grants standing, so the record does not lock. Refused on add. WHICH records get the permissions is the element's separate record filter, MANDATORY in practice: with NO filter the runtime runs UNFILTERED and applies the change to EVERY record of the object (its query has record permissions disabled, so that is every row in the table) — target one record by filtering Id against a process parameter or a trigger output. The element has NO output parameters — nothing downstream can branch on whether rights were applied. TWO configurations build green and then silently do NOTHING at run time: a record filter that IS PRESENT but carries no conditions (the runtime takes its 'filters empty' exit; the current package also refuses this one at build), and add/remove both empty (not refused; an accessRights block carrying only 'object' is one). The total ABSENCE of a record filter is the OPPOSITE state - it acts on EVERY record, not none. The PLATFORM neither refuses nor warns that one, but clio's post-operation read-back DOES warn on it, so treat that warning as the signal. A revoke in either state removes no permission and still reports success, so a successful build is NOT evidence the element will do anything — check the filter and the entries yourself. clio also reads the saved process back and WARNS when the accessRights block did not land at all (a deployed CrtProcessBuilder that predates this element discards it and still answers success); if you see that warning, or one saying the read-back could not be obtained, do not report a grant or revoke as applied), "
		 + "email? (sendEmail elements only — the Send email/EmailTemplateUserTask element, CUSTOM MESSAGE only, no "
		 + "email templates: {mode?:auto|manual (how the email is sent; the designer requires a sender for auto), "
		 + "sender? (a MailboxSyncSettings record id, or a sender email address configured on the environment), "
		 + "subject? (plain constant text ONLY — a [#...#] macro here is REFUSED, because this route stores a constant "
		 + "and the runtime would send the macro text verbatim; for a formula subject use addMapping with an "
		 + "'expression' source against the element's Subject parameter, NOT a 'value' source, which stores a constant "
		 + "and reproduces exactly what the refusal prevents), "
		 + "body? (the HTML custom message; to insert PROCESS DATA use friendly macros the server resolves BY NAME "
		 + "into the platform's image tokens — no UID needed: [[param:<Name>]] (a whole process parameter), "
		 + "[[element:<ElementName>.<OutputParameter>]] (a whole element output, e.g. a readData element's "
		 + "ResultEntity), [[element:<ElementName>.<OutputParameter>.<Column>]] (ONE direct column of that output "
		 + "record — a process parameter can only be inserted WHOLE, Creatio has no column drill on a bare parameter, "
		 + "so read a record with a data element to use its columns); an unknown parameter/element/column is REJECTED "
		 + "naming what was missing (column names are matched case-sensitively), so do not guess: on a CREATE the exact "
		 + "names come from THIS descriptor's own parameters[]/elements[] you declare here (there is no process to describe yet — describe-business-process is the MODIFY path); a whole raw platform image token, OR a bare [#…#] formula, written by hand passes through unchanged (the escape hatch), while {{…}} is NOT clio macro syntax (that is the content designer's editable template fields) and is left alone; a "
		 + "server-built body reopens in the designer's Content designer as an editable block — verified on a stand; a CrtProcessBuilder that predates this feature does NOT resolve the macros and stores the text verbatim, so clio warns after the build when the read-back shows the body did not land — update the package (install-process-builder) if you see that warning), "
		 + "bodyFormat?:html, to?/cc?/bcc? (recipient arrays; each entry sets EXACTLY ONE of {value (a constant "
		 + "address) | processParameter (the recipient mirrors that parameter's type — a Contact-lookup parameter is "
		 + "resolved to the contact's email at send time) | expression (a raw formula macro; add "
		 + "referenceSchema:<ObjectName> when it references a record, e.g. a fixed Contact)}), "
		 + "importance?:none|normal|high|low (the designer labels normal as \"Medium\"), ignoreErrors?, "
		 + "performer? (manual mode only — who performs the task: "
		 + "{type:user|manager|role, contact? (a formula; defaults to the current user's contact), role? (a "
		 + "SysAdminUnit role name or record id, required for type:role), showPage?})}), "
		 + "performer? (performTask elements only — WHO PERFORMS the task, the same {type:user|manager|role, "
		 + "contact?, role?, showPage?} block as email.performer but TOP-LEVEL on the element: role is the honest "
		 + "'assign to a team' — the created Activity carries the role in its own OwnerRole column with an EMPTY "
		 + "owner, every user of the role sees and can take it, so never fake a team by writing a role id into the "
		 + "OwnerId parameter. The role is CHECKED TO EXIST whether you pass a name or an id, against the same role "
		 + "set the designer's picker offers — so a typo'd name, an arbitrary Guid and a USER's own SysAdminUnit id "
		 + "are all refused rather than stored as an assignment nobody can see (OwnerRole does not control "
		 + "integrity, so nothing downstream would report it); A role NAME that matches MORE THAN ONE role is "
		 + "refused too — a name cannot say which group performs the task, so pass the id; manager resolves the contact's manager AT RUN TIME "
		 + "(process error when no manager exists); showPage omitted defaults to false for manager/role, mirroring the designer; REFUSED on other "
		 + "element kinds — the retired CallUserTask by name, whose runtime ignores the assignment: model a call "
		 + "as performTask + the Call ActivityCategory), "
		 + "preconfiguredPage? (preconfiguredPage elements only — shows a Freedom UI page to a user and "
		 + "resumes when the user presses a completing button: {page:<Freedom UI page schema name> (REQUIRED "
		 + "— the server NEVER creates a page, and both an unknown page and a Classic UI page are refused), "
		 + "buttons:[{name (the buttons view-element name on the page), caption?, event?:clicked, validate?}] "
		 + "(REQUIRED on a build — at least one, and NOT defaulted for you: an element with no completing "
		 + "button saves green and then hangs forever at run time), "
		 + "dataSources?:[{name, entitySchemaName}], "
		 + "performer? ({type:user|manager|role, contact?, role?, showPage?} — omit the whole block and the "
		 + "CURRENT USER is used; omit showPage and the page is STILL shown automatically because that is the "
		 + "task default, so do not send showPage:true to be sure — it is accepted only for type:user anyway), "
		 + "recommendation? (a single line — a line break is rejected)}). "
		 + "The page's buttons and data sources are FACTS, not values you may invent: a page inherits its "
		 + "buttons from its template chain and the server cannot see them, so call get-process-page-facts "
		 + "FIRST and pass its entries through unchanged. Both are CHECKED here and a name the page does not "
		 + "have is refused, because neither one fails visibly: an invented button is never matched, and an "
		 + "invented data source makes the completing button abandon the completion silently — the page stays "
		 + "open with no error and the instance never leaves 'Running'. The page's PARAMETERS are deliberately absent from "
		 + "the descriptor — the server reads them off the page itself and copies them onto the element, so "
		 + "do not declare them. "
		 + "useBackgroundMode? (element-level: every element supports it; true runs it asynchronously via the "
		 + "background scheduler — omit to keep the element kind's default, e.g. a signalStart defaults to true), signal?, "
		 + "filter?}), flows[] ({source, target, kind?, condition?} of "
		 + "element names; kind is sequence (default) | conditional | default, and a conditional flow REQUIRES a "
		 + "condition — a boolean formula, validated by the platform at the pre-save gate. REFERENCE A PARAMETER BY "
		 + "NAME here: [#Amount#] for a process parameter and [#ElementName.ParameterName#] for an element's "
		 + "output. This path is the one place that accepts a name, and it has to - the platform evaluates a "
		 + "condition through a UId meta-path, and on create those UIds do not exist yet, because the parameters "
		 + "and elements are made by this same call. The server expands the name once everything exists. A name "
		 + "that resolves to nothing is refused up front, naming the flow and listing what does exist. "
		 + "[#SysSettings.Code<Type>#], [#Lookup.Schema.Record#] and an already-written meta-path are passed "
		 + "through untouched. On the MODIFY path there is no expansion and none is needed: the UIds exist by "
		 + "then and describe-business-process reports them. "
		 + "FLOW ORDER IS BRANCH PRECEDENCE — "
		 + "sibling conditions are evaluated in the order you list them and the FIRST true one is taken, and "
		 + "nothing else encodes that. Out of a gateway that CHOOSES (exclusiveGateway) every outgoing flow must "
		 + "be conditional or default, an unconditional one is written as the default branch whenever that gateway "
		 + "has no default YET - in any order you declare them - and a SECOND unconditional one is refused because "
		 + "there is at most one default per element; out of a parallelGateway, which starts every branch, all flows are "
		 + "plain), parameters[] ({name, type (a supported scalar or Lookup — other types rejected), "
		 + "referenceSchema? (object name, e.g. City — makes it a Lookup), direction, caption, description?, "
		 + "value? (a literal constant default — not a formula)}; or "
		 + "typeFromElement + typeFromElementParameter to copy an element parameter's exact type), "
		 + "and mappings[] (bind a target to a source; an entry's keys are FLAT, not nested under a 'target' "
		 + "object — the target is either 'elementName' + "
		 + "'elementParameter' (an element input) or 'targetProcessParameter' (a process parameter, e.g. expose an "
		 + "element output as a process output); source is exactly one of {sourceElement, sourceElementParameter} "
		 + "(another element's output), processParameter, value, or expression. An 'expression' is a FORMULA, "
		 + "validated by the PLATFORM at the pre-save gate — so a bad one aborts the whole build with 'Process "
		 + "validation failed' and nothing is created. On CrtProcessBuilder this clio requires 1.6.0.3, which is "
		 + "NOT where that collapse happened: 1.4.0.41 is where the PACKAGE stopped validating formulas a second "
		 + "time and the platform's gate became the only one, and .44 is simply the first archive carrying that "
		 + "AND the ENG-96325 lookup-constant contract. Below .41 a refused formula still fails, "
		 + "with the package's own wording. The floor is 1.4.0.60 rather than .44 for a different KIND of "
		 + "reason. flows[].kind and the two gateway type tokens arrive in .58, which accepts them; what .60 adds "
		 + "is the by-name condition expansion, and below it a flows[].condition can only reference a system "
		 + "setting - 88% of real conditions name a "
		 + "parameter, which needs the server-side expansion. That expansion reaches 65% of them, not all: a "
		 + "condition on a COLUMN of a read record ([Element].[Parameter].[EntityColumn], 242 of the 487 "
		 + "element-output conditions in the shipped product) has a third segment the name form cannot say, "
		 + "and still belongs to the modify step. The capability, not the wording of a refusal, is what this "
		 + "floor buys. "
		 + "(Shared with modify-business-process. A conditional branch IS built here, through flows[].kind and "
		 + "flows[].condition above; what cannot be built here is a branch on an activity RESULT.) "
		 + "The formula itself: ONE line, "
		 + "its result must fit the target's "
		 + "DECLARED type (an Integer target refuses a fractional result), every [#…#] parameter reference must "
		 + "resolve in THIS process, every macro family must be one a converter resolves where you used it, names "
		 + "resolve through a flat case-sensitive registry (Math.Round yes, "
		 + "System.Math.Round no), and a parameter is referenced by its UId meta-path - EXCEPT in a "
		 + "flows[].condition on THIS call, where you write the NAME and the server expands it, because on "
		 + "create the UIds do not exist yet (see flows[].condition above). Everywhere else - a mapping, a "
		 + "filter, a condition on the modify path - it is the meta-path. A refusal "
		 + "always names the parameter. The character index comes with a PARSE fault only, so do not wait for "
		 + "one on a type mismatch ('Cannot convert type X to Y') or an unknown identifier ('Parameter X not "
		 + "found') - the two commonest faults, and both already name what to fix. When the expression IS "
		 + "quoted, it is quoted as the platform's own converter left it - a parameter reference by "
		 + "the parameter NAME, a fractional literal with an 'm' appended - not as you wrote it. An "
		 + "unresolvable [#…#] parameter reference is not in this family at all: it names the reference "
		 + "and the remedy instead ('which is not in this process. Add the parameter first, or correct the "
		 + "reference.') - the sentence 1.4.0.42 introduced, and the reason this floor is what it is. See "
		 + "modify-business-process for the full vocabulary; parameter-to-parameter mappings "
		 + "require compatible types; a Lookup target's 'value' takes a bare non-empty record Guid, stored as the "
		 + "ConstValue the runtime actually reads - and, from 1.4.0.40, the referenced record's NAME is resolved into the parameter's display value, which is what the designer renders, so 'Task category' shows Call rather than a Guid and describe reports it as valueDisplay beside the unchanged bare-Guid value. An already-composed [#Lookup.{objectUId}.{recordId}#] is ALSO accepted on a Lookup target and decoded to that bare id, so a value echoed back from describe re-submits unchanged; the same name resolution applies to a Lookup process parameter's DEFAULT set through addParameter / setParameter 'value', but the macro DECODE does NOT - that is the mapping route only, so a [#Lookup...#] written into a parameter default is stored as text and never resolved. (the route ships from CrtProcessBuilder 1.3.1.1; THIS clio "
		 + "additionally refuses, up front, any environment below the version THIS clio needs (the "
		 + "[RequiresPackage] floor, whose message names that one version); when the floor is below what clio "
		 + "bundles, the package-convergence check refuses the gap between them instead, naming both — while "
		 + "an older clio surfaces the old package's "
		 + "[#Lookup…#]-macro rejection; either refusal means the environment is behind, not that the parameter "
		 + "is unsettable), while a non-Guid lookup value is refused with a "
		 + "bare-Guid-first message (the [#Lookup…#] expression form stays the named fallback) and Guid.Empty "
		 + "as referencing no record. "
		 + "To run the process when a record "
		 + "is saved/added/changed, use a "
		 + "signalStart element with signal:{entity:<EntityName>, on:added|modified|deleted (one event), "
		 + "changedColumns?:[<ColumnName>,...]} instead of a page save handler. changedColumns restricts an "
		 + "on:modified trigger to fire ONLY when one of those column values changes (column names on the "
		 + "trigger entity; valid only for on:modified; omit for any-change). To fire that trigger only for "
		 + "matching records, add filter:{object, logicalOperation:and|or, conditions:[{column (entity column name, may be a lookup dot-path like Account.Code), comparison:equal|notEqual|greater|less|contains|isNull|..., one of value|macro (+macroArgument), optional datePart}], groups?} to the signalStart element. A signalStart filter's right side must be a constant/macro/datePart — NOT a process/element parameter (the signal is evaluated before the process instance exists; the server rejects a parameter reference here). The server serializes the platform filter; never hand-write filter JSON. Read get-guidance name=process-modeling FIRST — the full descriptor contract (buildable slice, filter condition + datePart/macro vocabulary, date/time DEFAULT-value macro rules and the Lookup bare-Guid default rule, mapping type-compatibility groups, formula policy, FSD caveat). For the accessRights block read get-guidance name=process-access-rights - it owns the permission entries, the grantee kinds, and the two configurations that build green and change nothing. For an `expression` mapping source or a conditional-flow condition read get-guidance name=process-formulas — it owns the accepted vocabulary, the reference syntax, what each refusal names, and the length bound. Use list-user-tasks to discover valid userTaskName values. Requires the ProcessDesignService (CrtProcessBuilder) package on the target environment; install it with install-process-builder. After a successful create the process is INTERPRETED and runs as-is: do NOT run compile-creatio, and do NOT infer a compile from a raw process read (a `VwSysProcess` row's `NeedInstall`/`NeedUpdateSourceCode`/`NeedUpdateStructure` are dirty flags, not a compile trigger) — verify with describe-business-process. The response carries a compile-not-required note; a process needs a compile only if it has a Script Task (custom C#), which clio cannot author. " + "A SUCCESSFUL build can still report caveats, and they arrive as message-type \"Warning\" entries "
		 + "in execution-log-messages — there is no separate warnings field on the response, so looking for "
		 + "one and finding nothing is not evidence there were none. Read them: a Pre-configured page whose "
		 + "referenced page could not be loaded is built and SAVED carrying none of that page's parameters, "
		 + "so anything you meant to map onto them is simply not there.")]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	public CommandExecutionResult CreateBusinessProcess(
		[Description("create-business-process parameters")] [Required] CreateBusinessProcessArgs args
	) {
		if (string.IsNullOrWhiteSpace(args?.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		if (string.IsNullOrWhiteSpace(args.Descriptor)) {
			return CommandExecutionResult.FromError("descriptor is required and cannot be empty.");
		}

		CreateBusinessProcessOptions options = new() {
			Environment = args.EnvironmentName,
			DescriptorJson = args.Descriptor,
			PackageName = args.PackageName ?? string.Empty
		};
		// A business process built by clio is interpreted and runs as-is — it never needs compilation
		// (clio cannot author a Script Task or an after-activity-save script, the only in-process C#).
		// Emit the deterministic post-op note on success (same channel as update-entity-schema / create-page)
		// so "created" is not mistaken for "must be compiled to run" — the note is the one reply the caller
		// cannot skip. Do NOT run compile-creatio, and do not infer one from a raw process read (ENG-95706).
		CommandExecutionResult result = InternalExecute<CreateBusinessProcessCommand>(options);
		if (result.ExitCode != 0) {
			return result;
		}
		// Append (not clobber) so a command-set success note is preserved (mirrors PageCreateTool).
		return result with {
			Note = string.IsNullOrWhiteSpace(result.Note)
				? CommandExecutionResult.CompileNotRequiredNote
				: result.Note + " " + CommandExecutionResult.CompileNotRequiredNote
		};
	}
}

/// <summary>
/// MCP arguments for the <c>create-business-process</c> tool (kebab-case wire keys, repo convention).
/// </summary>
public sealed record CreateBusinessProcessArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("descriptor")]
	[property: Description("The process descriptor (name, caption, packageName, elements[], flows[], "
		+ "parameters[], mappings[]) SERIALIZED AS A JSON STRING - not a nested object. Passing a real object "
		+ "fails with \"Cannot get the value of a token type 'StartObject' as a string\".")]
	[property: Required]
	string Descriptor,

	[property: JsonPropertyName("package-name")]
	[property: Description("Optional package name that overrides the descriptor's packageName.")]
	string? PackageName = null);
