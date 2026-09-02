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
	[McpServerTool(Name = CreateBusinessProcessToolName, ReadOnly = false, Destructive = false, Idempotent = false,
		 OpenWorld = false),
	 Description("Build a business process on a Creatio environment from a declarative JSON descriptor. The "
		 + "descriptor is an object with: name (schema code), caption, packageName, elements[] "
		 + "({name (the element handle/local code), type:startEvent|signalStart|endEvent|userTask|sendEmail|approval "
		 + "(aliases readData/performTask), caption, userTaskName?, "
		 + "approval? (approval elements only — the designer's Approval element, which requests a visa on a record: "
		 + "{object:<EntityName> (required on a first configuration — the object whose record goes for approval, "
		 + "resolved by NAME server-side), recordId:{exactly ONE of recordId (a fixed record: its GUID, or the "
		 + "[#Lookup...#] macro describe reports, and it MUST be a record of 'object' — a foreign id is refused "
		 + "because the designer renders that field blank and the next human save wipes the element) | "
		 + "processParameter | sourceElement + sourceElementParameter} (required on a first configuration), "
		 + "purpose? (the text shown to the approver; omitted writes the platform default 'Approval required', which "
		 + "is what the designer itself persists), "
		 + "approver:{type:user|manager|role, employee?:<user name, id or [#SysVariable.CurrentUser#]> (types "
		 + "'user' and 'manager' only — for 'manager' it names the employee WHOSE manager approves; omitted takes "
		 + "the designer's own default, the current user), role?:<role name or id> (type 'role' only)} (required "
		 + "on a first configuration — WHO approves; it is NOT defaulted, because making whoever ran the build the "
		 + "approver routes real approvals to somebody nobody chose) — the user or role is checked to exist, an "
		 + "ambiguous NAME is refused (pass the id), and setting one type CLEARS the other's field, exactly as the "
		 + "designer does, allowDelegation?:bool, "
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
		 + "no approval settings. ONE deliberate gap: BRANCHING on the outcome is not buildable (gateways and "
		 + "conditional flows are not supported yet), so the approved/rejected/canceled result cannot be routed — "
		 + "the outcome is readable as the element's ResultParameter output. Formula and system-setting value "
		 + "sources are not offered. get-guidance name=process-approval owns this block's full contract), "
		 + "readData? (readData elements only: {source:<EntityName> (required), mode?:first (the only supported "
		 + "mode — the first record of the sorted selection; collection/count/aggregation are planned), "
		 + "columns?:[<ColumnName>,...] (TOP-LEVEL column names only — omit or pass [] to read ALL columns; a "
		 + "dot-separated path into a linked object like Owner.Name is rejected, read the whole record instead), "
		 + "sort?:{column, direction?:asc|desc}} "
		 + "— configures WHAT the element reads; WHICH records qualify is the element's separate filter block. NOTE: "
		 + "the read record's individual COLUMN values are NOT yet referenceable downstream — the element's only "
		 + "output parameter is ResultEntity (the whole record); do not build mappings, changeData values or filters "
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
		 + "useBackgroundMode? (element-level: every element supports it; true runs it asynchronously via the "
		 + "background scheduler — omit to keep the element kind's default, e.g. a signalStart defaults to true), signal?, "
		 + "filter?}), flows[] ({source, target} of "
		 + "element names), parameters[] ({name, type (a supported scalar or Lookup — other types rejected), "
		 + "referenceSchema? (object name, e.g. City — makes it a Lookup), direction, caption, description?, "
		 + "value? (a literal constant default — not a formula)}; or "
		 + "typeFromElement + typeFromElementParameter to copy an element parameter's exact type), "
		 + "and mappings[] (bind a target to a source — target is {elementName, "
		 + "elementParameter} (an element input) or {targetProcessParameter} (a process parameter, e.g. expose an "
		 + "element output as a process output); source is exactly one of {sourceElement, sourceElementParameter} "
		 + "(another element's output), processParameter, value, or expression; parameter-to-parameter mappings "
		 + "require compatible types; a Lookup target's 'value' takes a bare non-empty record Guid, stored as the "
		 + "ConstValue the runtime actually reads (the route needs CrtProcessBuilder 1.4.6.0, the version this clio bundles; THIS clio "
		 + "additionally refuses any environment older than the version it BUNDLES — up front, via the "
		 + "package-convergence message naming both versions — while an older clio surfaces the old package's "
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
		 + "matching records, add filter:{object, logicalOperation:and|or, conditions:[{column (entity column name, may be a lookup dot-path like Account.Code), comparison:equal|notEqual|greater|less|contains|isNull|..., one of value|macro (+macroArgument), optional datePart}], groups?} to the signalStart element. A signalStart filter's right side must be a constant/macro/datePart — NOT a process/element parameter (the signal is evaluated before the process instance exists; the server rejects a parameter reference here). The server serializes the platform filter; never hand-write filter JSON. Read get-guidance name=process-modeling FIRST — the buildable slice and the descriptor contract; the filter condition + datePart/macro vocabulary now lives in get-guidance name=process-data-elements, and the date/time DEFAULT-value macro rules and the Lookup bare-Guid default rule, mapping type-compatibility groups, formula policy, FSD caveat). Use list-user-tasks to discover valid userTaskName values. Requires the ProcessDesignService (CrtProcessBuilder) package on the target environment; install it with install-process-builder. After a successful create the process is INTERPRETED and runs as-is: do NOT run compile-creatio, and do NOT infer a compile from a raw process read (a `VwSysProcess` row's `NeedInstall`/`NeedUpdateSourceCode`/`NeedUpdateStructure` are dirty flags, not a compile trigger) — verify with describe-business-process. The response carries a compile-not-required note; a process needs a compile only if it has a Script Task (custom C#), which clio cannot author.")]
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
	[property: Description("Inline JSON process descriptor (name, caption, packageName, elements[], flows[], "
		+ "parameters[], mappings[]).")]
	[property: Required]
	string Descriptor,

	[property: JsonPropertyName("package-name")]
	[property: Description("Optional package name that overrides the descriptor's packageName.")]
	string? PackageName = null);
