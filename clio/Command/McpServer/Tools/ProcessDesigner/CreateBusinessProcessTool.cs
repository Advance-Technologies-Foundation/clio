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
		 + "({name (the element handle/local code), type:startEvent|signalStart|endEvent|userTask|sendEmail|exclusiveGateway|parallelGateway "
		 + "(aliases readData/performTask), caption, userTaskName?, "
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
		 + "be conditional or default, a lone unconditional one is written as the default branch, and there is at "
		 + "most one default per element; out of a parallelGateway, which starts every branch, all flows are "
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
		 + "validation failed' and nothing is created. On CrtProcessBuilder this clio requires 1.4.0.60, which is "
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
		 + "matching records, add filter:{object, logicalOperation:and|or, conditions:[{column (entity column name, may be a lookup dot-path like Account.Code), comparison:equal|notEqual|greater|less|contains|isNull|..., one of value|macro (+macroArgument), optional datePart}], groups?} to the signalStart element. A signalStart filter's right side must be a constant/macro/datePart — NOT a process/element parameter (the signal is evaluated before the process instance exists; the server rejects a parameter reference here). The server serializes the platform filter; never hand-write filter JSON. Read get-guidance name=process-modeling FIRST — the full descriptor contract (buildable slice, filter condition + datePart/macro vocabulary, date/time DEFAULT-value macro rules and the Lookup bare-Guid default rule, mapping type-compatibility groups, formula policy, FSD caveat). For an `expression` mapping source or a conditional-flow condition read get-guidance name=process-formulas — it owns the accepted vocabulary, the reference syntax, what each refusal names, and the length bound. Use list-user-tasks to discover valid userTaskName values. Requires the ProcessDesignService (CrtProcessBuilder) package on the target environment; install it with install-process-builder. After a successful create the process is INTERPRETED and runs as-is: do NOT run compile-creatio, and do NOT infer a compile from a raw process read (a `VwSysProcess` row's `NeedInstall`/`NeedUpdateSourceCode`/`NeedUpdateStructure` are dirty flags, not a compile trigger) — verify with describe-business-process. The response carries a compile-not-required note; a process needs a compile only if it has a Script Task (custom C#), which clio cannot author.")]
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
	[property: Description("Inline JSON process descriptor (name, caption, packageName, elements[], flows[], "
		+ "parameters[], mappings[]).")]
	[property: Required]
	string Descriptor,

	[property: JsonPropertyName("package-name")]
	[property: Description("Optional package name that overrides the descriptor's packageName.")]
	string? PackageName = null);
