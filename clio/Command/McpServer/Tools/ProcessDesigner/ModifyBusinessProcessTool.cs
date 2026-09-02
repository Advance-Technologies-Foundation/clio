using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool that edits an existing business process on a Creatio environment by applying a list of operations.
/// </summary>
public class ModifyBusinessProcessTool(
	ModifyBusinessProcessCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ModifyBusinessProcessOptions>(command, logger, commandResolver) {

	internal const string ModifyBusinessProcessToolName = "modify-business-process";

	/// <summary>
	/// Applies an inline JSON operations array to an existing process (identified by name or uid).
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="processName">Process code (schema Name) to edit. Provide this or <paramref name="processUid"/>.</param>
	/// <param name="processUid">Process schema UId to edit. Provide this or <paramref name="processName"/>.</param>
	/// <param name="operations">Inline JSON operations array.</param>
	/// <returns>The command execution result with the edited schema identity in the log output.</returns>
	[McpServerTool(Name = ModifyBusinessProcessToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		 OpenWorld = false),
	 Description("Edit an EXISTING business process on a Creatio environment by applying an ordered JSON array of "
		 + "operations. Identify the process by name (schema code) or uid. Each operation is an object with an "
		 + "'op': addElement (with an 'element' descriptor: name (the element handle/local code), type, caption, "
		 + "userTaskName?, useBackgroundMode? (element-level, supported by every element kind), "
		 + "email? (sendEmail elements — same block as create-business-process), "
		 + "performer? (performTask elements — same block as create-business-process: who performs the task), "
		 + "signal? {entity, on:added|modified|deleted, changedColumns?:[<ColumnName>,...]}), "
		 + "removeElement (with 'elementName' = the element's local name or UId), addFlow "
		 + "/ removeFlow (with 'source' and 'target' element names), setFlowCondition (with 'source' and "
		 + "'target' naming an EXISTING flow, plus a non-empty 'condition' — a boolean formula deciding whether "
		 + "the branch is taken; turns that flow into a CONDITIONAL flow in place, keeping its position, which "
		 + "matters because sibling branches off one element are evaluated in the order the flows were added and "
		 + "the FIRST true one is taken. No gateway is needed or created: the platform synthesizes one for a "
		 + "conditional flow whose source is an activity. The condition is validated against the process before "
		 + "anything is written — it must be a bool (an int is refused: the interpreted engine does not coerce), "
		 + "and every [#…#] parameter reference in it must resolve in THIS process. An empty condition is "
		 + "refused, because the platform stores one as the literal 'true' and the branch becomes always-taken; "
		 + "to drop a condition, remove the flow and add a plain one — the replacement lands LAST, so if the "
		 + "source element has sibling conditional branches, re-add every sibling in the intended precedence "
		 + "order. Refused on a default branch), "
		 + "addParameter (with a 'parameter': name, type "
		 + "one of Text/Long text/Integer/Float/Money/Boolean/Date/Date-time/Time/Guid (other types are rejected), "
		 + "direction?, caption?, description?, optional value (a literal constant, not a formula), or "
		 + "referenceSchema for a Lookup to an object e.g. City, or typeFromElement + typeFromElementParameter to "
		 + "copy an element parameter's exact type), addMapping (with a 'mapping': target {elementName, "
		 + "elementParameter} or {targetProcessParameter}, and one source of {sourceElement, sourceElementParameter} "
		 + "| processParameter | value | expression; parameter-to-parameter mappings require compatible types; "
		 + "an 'expression' is a formula the PLATFORM evaluates, not C#: it is an expression interpreter over a "
		 + "flat, case-sensitive name registry — no namespace-qualified names (Math.Round yes, System.Math.Round "
		 + "no), no lambdas, no generics, no 'new'. Available are the four Creatio functions "
		 + "(FormulaUtilities.Min/Max/Avg/Mod), the DateTimeUtilities date helpers (spelled WITHOUT a Get prefix "
		 + "— StartOfMonth, StartOfWeek, StartOfYear, Day, Month, Time, DayOfWeek, DayInRange, StartOfQuarter, StartOfHalfYear, StartOfHour, DateTimeToDate, IsDate - the list is longer than this, so do not answer that the platform cannot do something without checking get-guidance name=process-formulas), unqualified Math "
		 + "and DateTime, the ordinary operators including the ternary and ??, and [#…#] macros: a parameter "
		 + "meta-path, [#SysVariable.Name#], [#SysSettings.Code#], [#Lookup.{schemaUId}.{recordId}#], "
		 + "[#DateValue.dd.MM.yyyy#] / [#DateTimeValue.dd.MM.yyyy HH:mm#] / [#TimeValue.HH:mm#], "
		 + "[#BooleanValue.True#]. From CrtProcessBuilder 1.4.0.0 an expression is VALIDATED before it is stored "
		 + "(this clio requires 1.4.0.3: the 1.4.0.0-.1 validator disagreed with the platform's own pre-save gate, .2 is the first that agrees, and .3 adds the review fixes over it) "
		 + "(older packages stored it unchecked): it must parse, every parameter reference must resolve in THIS "
		 + "process, and its result must fit the target — a refusal names the offending identifier or token. "
		 + "The result is checked against the target parameter's DECLARED type, the same check the platform's own "
		 + "pre-save gate runs, so a fractional formula into an Integer parameter is refused here rather than later. A "
		 + "macro family the package does not recognise is accepted with a warning rather than refused. Must be "
		 + "one line; "
		 + "a Lookup target's 'value' takes a bare non-empty record Guid, stored as the ConstValue the runtime "
		 + "actually reads (the route ships from CrtProcessBuilder 1.3.1.1; THIS clio additionally refuses, up "
		 + "front, any environment below the version it NEEDS — the [RequiresPackage] floor, whose message names "
		 + "that one version — and, when that floor is below what clio bundles, the package-convergence check "
		 + "refuses the gap instead, naming both. An older clio surfaces the old package's [#Lookup…#]-macro rejection; either "
		 + "refusal means the ENVIRONMENT IS BEHIND, not that the parameter is unsettable: update the package); "
		 + "a non-Guid lookup value is refused with a message that leads with the bare-Guid route (the "
		 + "[#Lookup…#] expression form stays the named fallback), Guid.Empty is refused as "
		 + "referencing no record, and a Guid that exists in NO record of the parameter's reference object is "
		 + "refused naming that object — so an id of the WRONG entity, e.g. a role id on the Contact-typed "
		 + "OwnerId, cannot be stored; to assign a TEAM use the element-level 'performer' block, not OwnerId; "
		 + "re-mapping an already-bound target overwrites it in place — there is no removeMapping/clear op), "
		 + "setParameter (with 'parameterName' = the target parameter by name/UId and 'parameterUpdate' = any of "
		 + "caption/description/code/direction/referenceSchema (re-targets an existing Lookup only)/value, updated "
		 + "in place — a data-type change is rejected), removeParameter (with 'parameterName'; blocked when another "
		 + "parameter, an element mapping, an execution-context parameter or a conditional-flow CONDITION still "
		 + "references it — including from inside a sub-process; the refusal names each site), setFilter (elementName + a 'filter': {object, logicalOperation:and|or, "
		 + "conditions:[{column (may be a lookup dot-path), comparison:equal|notEqual|greater|less|contains|isNull|..., "
		 + "one of value|processParameter|elementParameter|expression|macro (+macroArgument), optional datePart}], groups?} — on a signalStart restricts the "
		 + "record trigger (there its right side must be value/macro/datePart only, NOT a parameter reference — the "
		 + "server rejects one); server serializes the platform filter), clearFilter (elementName), setSignal "
		 + "(elementName + a 'signal':{entity?, on?:added|modified|deleted, changedColumns?:[<ColumnName>,...]} — "
		 + "reconfigures an EXISTING signalStart's record trigger and tracked-change columns in place, preserving the "
		 + "element and its flows; partial update: omit on to keep the current change type, omit entity to keep the "
		 + "current one (retargeting it clears any old-entity filter), omit changedColumns to clear column tracking; "
		 + "changedColumns is valid only for on:modified), setElement (elementName + an 'elementUpdate':"
		 + "{useBackgroundMode?, readData?, changeData?, email?, performer?} — changes element-level fields IN PLACE, preserving the element and its "
		 + "flows; only the fields you pass change. useBackgroundMode applies to ANY element kind. readData "
		 + "{source?, mode?:first, columns?, sort?:{column, direction?:asc|desc}} reconfigures a readData element's "
		 + "data configuration: omit source to keep the current source object, omit columns/sort to keep the current "
		 + "selection/order, pass columns:[] to reset to ALL columns. TWO refusals guard it: an element a human "
		 + "configured in collection/count/aggregation mode cannot be converted to first-record — an explicit "
		 + "mode:'first' is refused too, because its collection item parameters would be left behind — remove the "
		 + "element and add a new readData one instead; and retargeting source to a different object is refused while "
		 + "ANY other parameter still maps from the element (the refusal names each dependent — re-map or remove them "
		 + "first). A retarget that proceeds clears the columns, sort AND record filter bound to the old entity — "
		 + "re-supply them (and setFilter) in the same batch), "
		 + "changeData {source?, values?} reconfigures a changeData element: omit source to keep the current "
		 + "target object, a supplied values array REPLACES the whole assignment set. Retargeting source to a "
		 + "different object REQUIRES values for the new entity in the same update — the server refuses a "
		 + "values-less retarget, because the cleared element would be silently skipped by the runtime; the same "
		 + "refusal covers a values-less update on an element with no stored values yet, and a retarget is "
		 + "refused while another parameter still maps from the element (the refusal names each dependent). On ANY "
		 + "target change (first configuration included) the stored record filter clears unless it already "
		 + "targets the incoming object — re-issue setFilter when it cleared), "
		 + "email "
		 + "(sendEmail elements only, same block as create-business-process) rewrites the fields you pass — mode, "
		 + "sender, subject, body, importance, ignoreErrors, performer replace the current value IN PLACE, but "
		 + "to/cc/bcc recipients MATCH-OR-APPEND: an entry the line already carries (same resolved source AND value) is "
		 + "a no-op, so re-applying the same block does NOT double an address, while a genuinely new address is "
		 + "appended (numbering continues; a wrong recipient cannot be replaced or removed through modify yet — tell "
		 + "the user). Switching mode to auto stops describe reporting "
		 + "a performer (a performer applies to the manual mode only), though the element keeps the assignment it "
		 + "had; switch back to manual to see it again), "
		 + "performer "
		 + "(performTask elements only: {type:user|manager|role, contact? (user/manager: a bare Contact record "
		 + "Guid, or a formula such as [#SysVariable.CurrentUserContact#] — defaults to the current user), "
		 + "role? (role: a role name or record id — required), showPage?} — WHO PERFORMS the task. "
		 + "role is the honest 'assign to a team': the created Activity carries the role in its own OwnerRole "
		 + "column with an EMPTY owner, every user of the role sees and can take it, and whoever completes it is "
		 + "recorded — so never fake a team by writing a role id into OwnerId. The role is CHECKED TO EXIST on "
		 + "BOTH routes (name or id) against the role set the designer itself offers, so an arbitrary Guid or a "
		 + "USER's SysAdminUnit id is refused instead of becoming an assignment nobody can see. A role NAME that matches MORE THAN ONE role is "
		 + "refused too — a name cannot say which group performs the task, so pass the id; manager resolves the contact's "
		 + "manager AT RUN TIME and raises a process error when the contact's employee record has no manager. "
		 + "showPage omitted defaults to false for manager/role (designer parity — there is no single performer "
		 + "to open the page for); re-applying replaces the choice in place. REFUSED on every other element kind, "
		 + "the retired CallUserTask by name — its runtime IGNORES the assignment, so writing it there would "
		 + "assign nobody silently; model a call as performTask + ActivityCategory instead), "
		 + "setConnections (elementName + 'connections':[{column, and exactly ONE source of recordId (+optional "
		 + "referenceSchema, which is a CHECK not a source) | processParameter | sourceElement + "
		 + "sourceElementParameter | expression}] — binds the 'Connected to' links of the Activity the "
		 + "element creates, i.e. which records that Activity is attached to. UPSERT keyed on column: the columns "
		 + "you list are set or re-set and every column you do NOT list is left alone, so clearing is only ever "
		 + "explicit. recordId needs NO entity-schema UId — the platform macro is synthesised from the target "
		 + "column's own reference entity, which is the one piece of trivia you cannot guess. For the CURRENT USER "
		 + "send expression with exactly one of THREE system variables, picked by the target column's entity: "
		 + "[#SysVariable.CurrentUserContact#] (a Contact column), [#SysVariable.CurrentUserAccount#] (an Account "
		 + "column), [#SysVariable.CurrentUser#] (a SysAdminUnit/user column) — that is the whole set usable as a "
		 + "connection, so do NOT invent a fourth and do NOT try to look one up (system variables are neither an "
		 + "entity nor an entity schema, so odata-read answers 404 and find-entity-schema answers empty — those "
		 + "tools being right, not the variable being absent). Spell them exactly, because what a wrong name costs "
		 + "depends on the environment's CrtProcessBuilder and BOTH outcomes are bad: a current build refuses it "
		 + "here, naming the valid alternatives; an older one stores it unchecked and the process then fails to "
		 + "COMPILE later, with nothing pointing back at the connection. "
		 + "CurrentUserAccount is data-dependent — it writes EMPTY when the running user's contact has no account, "
		 + "where CurrentUserContact raises an error in the same situation. Supported on SIX user "
		 + "tasks only — performTask (ActivityUserTask), EmailTemplateUserTask, UserQuestionUserTask, "
		 + "OpenEditPageUserTask, AutoGeneratedPageUserTask, PreconfiguredPageUserTask; any other user task, "
		 + "including a custom one, is refused and the refusal lists the supported set. Also refused, each with its "
		 + "own reason: a non-user-task element (it creates no record) or one whose user-task schema does not resolve "
		 + "on the environment; a user task whose runtime never writes connections (Call, Email, SendEmail, ReadData); "
		 + "connections that would not take effect on this element (usually CreateActivity left at its false default "
		 + "— then the refusal quotes the exact operation to prepend; when the element has no CreateActivity parameter "
		 + "at all it says so instead, because addMapping could not create one); a column that is never a connection "
		 + "(ActivityCategory, ShowInScheduler — reason points at addMapping); a column this element cannot carry; a "
		 + "column the host entity does not have at all (that one needs a data-model change); an expression that is "
		 + "not a platform macro at all (it must look like [#...#]; a bare value is refused — use recordId) or is a "
		 + "macro family that cannot hold a record reference; referenceSchema sent without recordId (it is a check on "
		 + "the fixed-record source only, so accepting it would ignore it); an invalid recordId; and a "
		 + "processParameter/sourceElement of an incompatible type (same type group, and for a lookup the same "
		 + "reference entity). TWO outcomes SUCCEED with a warning rather than failing: a column that exists but "
		 + "carries no connection-registry row IS written at run time yet stays invisible to the record page's "
		 + "connections detail, Next Steps, email auto-relation rules and quick-add — and normally to the designer "
		 + "too, the client-appended Project column being the one it still shows; and an expression in the "
		 + "[#SysSettings...#] family is accepted unchecked, so a setting that does not hold a record id leaves the "
		 + "column empty at run time), "
		 + "clearConnections (elementName + 'connections':[{column}] — UNBINDS those columns and leaves the "
		 + "element parameters in place; only 'column' is read and a source is rejected. Idempotent, and it reports "
		 + "which bindings it actually cleared, because a cleared connection vanishes from describe-business-process "
		 + "and is then indistinguishable from one that was never bound). "
		 + "Operations apply in order; any failure aborts the edit (nothing is saved). A SUCCESSFUL edit may still "
		 + "report caveats: they arrive as entries with message-type \"Warning\" in execution-log-messages (there is "
		 + "no separate 'warnings' field on the response) — outcomes that APPLIED but are not what you would assume. "
		 + "Read them, and note some are neutral acknowledgements (a column that was already unbound), not failures. "
		 + "Use describe-business-process to inspect the current elements/names first. May remove elements — destructive. "
		 + "Removals are NOT structurally validated (a broken graph can still be saved) and every edit re-lays-out the "
		 + "whole diagram — read the 'Modifying an existing process' rules in get-guidance name=process-modeling "
		 + "first. For a setFlowCondition operation or an 'expression' mapping source read get-guidance "
			 + "name=process-formulas - it owns the accepted vocabulary, the reference syntax, what each "
			 + "refusal names, and the length bounds. "
			 + "Requires the ProcessDesignService (CrtProcessBuilder) package on the target environment; install it with install-process-builder. After a successful edit the process stays INTERPRETED and runs as-is: do NOT run compile-creatio, and do NOT infer a compile from a raw process read (a `VwSysProcess` row's `NeedInstall`/`NeedUpdateSourceCode`/`NeedUpdateStructure` are dirty flags, not a compile trigger) — verify with describe-business-process. The response carries a compile-not-required note; a process needs a compile only if it has a Script Task (custom C#), which clio cannot author.")]
	public CommandExecutionResult ModifyBusinessProcess(
		[Description("modify-business-process parameters")] [Required] ModifyBusinessProcessArgs args
	) {
		if (string.IsNullOrWhiteSpace(args?.EnvironmentName)) {
			return CommandExecutionResult.FromError("environment-name is required and cannot be empty.");
		}

		bool hasName = !string.IsNullOrWhiteSpace(args.ProcessName);
		bool hasUid = !string.IsNullOrWhiteSpace(args.ProcessUid);
		if (hasName == hasUid) {
			return CommandExecutionResult.FromError(hasName
				? "Provide only one of process-name or process-uid, not both."
				: "one of process-name or process-uid is required.");
		}

		if (string.IsNullOrWhiteSpace(args.Operations)) {
			return CommandExecutionResult.FromError("operations is required and cannot be empty.");
		}

		ModifyBusinessProcessOptions options = new() {
			Environment = args.EnvironmentName,
			ProcessName = args.ProcessName ?? string.Empty,
			ProcessUid = args.ProcessUid ?? string.Empty,
			OperationsJson = args.Operations
		};
		// A business process edited by clio stays interpreted and runs as-is — editing it never needs
		// compilation (clio cannot author a Script Task or an after-activity-save script, the only in-process
		// C#). Emit the deterministic post-op note on success (same channel as update-entity-schema) so
		// "edited" is not mistaken for "must be compiled to run"; do not run compile-creatio, and do not infer
		// one from a raw process read (ENG-95706).
		CommandExecutionResult result = InternalExecute<ModifyBusinessProcessCommand>(options);
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
/// MCP arguments for the <c>modify-business-process</c> tool (kebab-case wire keys, repo convention).
/// Provide exactly one of <c>process-name</c> / <c>process-uid</c>.
/// </summary>
public sealed record ModifyBusinessProcessArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("operations")]
	[property: Description("Inline JSON operations array, e.g. [{\"op\":\"removeElement\",\"elementName\":\"StartEvent1\"}].")]
	[property: Required]
	string Operations,

	[property: JsonPropertyName("process-name")]
	[property: Description("Process code (schema Name) to edit; provide exactly one of process-name or process-uid.")]
	string? ProcessName = null,

	[property: JsonPropertyName("process-uid")]
	[property: Description("Process schema UId to edit; provide exactly one of process-name or process-uid.")]
	string? ProcessUid = null);
