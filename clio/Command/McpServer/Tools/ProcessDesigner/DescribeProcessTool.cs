using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool surface for the <c>describe-business-process</c> command — reads an existing process into a
/// structured graph the agent can narrate (the inverse of generation). Read-only, environment-sensitive.
/// </summary>
[McpServerToolType]
public sealed class DescribeProcessTool(
	DescribeProcessCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<DescribeProcessOptions>(command, logger, commandResolver) {

	/// <summary>Stable MCP tool name.</summary>
	internal const string ToolName = "describe-business-process";

	/// <summary>
	/// Reads the identified process and returns its structured graph (elements, flows, parameters).
	/// </summary>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Reads an existing Creatio process and returns a STRUCTURED graph (elements with runtime type, the specific user-task schema name, the signalStart record-event trigger (entity, on, and changedColumns for a column-restricted 'modified' signal), a sendEmail element's email configuration (mode, sender, subject, hasBody plus the body itself — the HTML with its process macros DECODED back into [[param:Name]]/[[element:Element.Output(.Column)]] author form so you can read it and (on a modify) edit it in place, but NULL on a package that predates this feature (older builds report only hasBody), and a macro whose UIds no longer resolve is left as the raw <img> token (best-effort decode); the decode/resolve round-trip is NOT guaranteed — a parameter renamed or removed since, or literal [[…]] a human typed into the designer, is echoed here yet REJECTED when written back, so a pure read-modify-write can fail on a body you never semantically changed, importance, ignoreErrors, to/cc/bcc recipients with their value sources, and the manual-mode performer) which round-trips into a create/modify email block with ONE exception: a FORMULA subject is reported for visibility only and is refused if you feed it back through email.subject — set that through addMapping with an expression source instead, a readData element's data configuration (source, mode, columns, sort — mode 'first' round-trips into a create/modify readData block; 'collection'/'function' report a designer-made mode; on a designer-made element a linked-object column cannot be expressed and is omitted from columns, so that list can be narrower than what the element really reads; sort is the EFFECTIVE PRIMARY entry — the one the runtime's ORDER BY ranks first — and a designer-made element can carry further ACTIVE secondary entries that are not reported, while a sort write replaces the whole stored order, so do not write a described block back to a multi-sort element expecting its secondaries to survive), a changeData element's data configuration (source + column-value assignments; source is null when the element's target object is set by a formula/mapping rather than a constant — the block is still reported, and retargeting such an element needs an explicit source. A CONSTANT always reads back in value, INCLUDING one the write path would refuse (a non-text or empty constant — a date/lookup/numeric column holding a raw string is a live run-time fault, so it stays visible rather than being blanked), and feeding that row back is refused naming the column; a parameter or element-output binding is DECODED back to processParameter / sourceElement + sourceElementParameter so the block re-applies in another process (a decoded sourceElement still obeys the create-time rule that its element must appear EARLIER in elements[], and describe emits stored order, so a described block may need reordering before it re-creates); a recognized binding that fails the assignment type check reads back as its raw [#…#] expression; and a stored source this API cannot re-apply — a disabled row, a legacy designer or entity mapping, a system value/setting, or an item whose packed value is missing — reads back as its COLUMN ALONE and is refused if fed back), a Change access rights element's accessRights block (object + objectSchemaUId — object is null when the stored UId resolves to no entity — considerTimeInFilter, and the add/remove permission entries with their operations, level (granted entries only: permit/delegate/restrict) and grantee decoded into the wire shape: a role/employee grantee carries the stored formula in role/contact with the stored caption in display (an echoed [#Lookup…#] macro re-applies as written; a legacy element-parameter formula is reported truthfully but is process-local), a selectedEmployees filter decodes when stored in the modern format (a legacy FilterEdit value reports the entry without its filter), a stored-but-undecodable collection reports as an EMPTY array, but addUnreadable/removeUnreadable then say how many entries could NOT be reported (-1 when the collection itself did not decode), so a lossy read is no longer indistinguishable from a genuinely empty one — so never build a replacement from a read whose count is non-zero: a supplied add/remove REPLACES the stored collection and would delete the entries the read-back never saw: omit the field instead to keep whatever is there, and the legacy allRolesAndUsers kind is reported truthfully but refused if written back) the element-level useBackgroundMode flag, label and value-bearing parameters (unbound element inputs are omitted — absence does not mean the parameter does not exist); flows with source/target/kind; and process parameters) — not the raw metadata. Element typing comes from the real object model server-side (universal, incl. custom user tasks); each parameter carries its direction and isResult, and parameter values carry their source (Mapping/ConstValue/Script) and expression. A value that the designer RENDERS differently also carries valueDisplay — for a Lookup constant the referenced record's name (Call) beside the bare record id in value, for a mapping the source parameter's caption; value alone is what round-trips back into addMapping/setParameter, valueDisplay is read-only and re-derived on write, and it is absent both on an older CrtProcessBuilder and whenever the environment could not name the record (which does not mean the value is wrong). An element parameter is usable as a mapping SOURCE (an output) when isResult=true OR direction=Out — most user-task outputs come back isResult=true with direction=Variable, so detect outputs by isResult, not by direction alone. Each element also carries its BOUND 'Connected to' links as connections[] — which records the Activity it creates is attached to. Every entry gives both the raw persisted macro (value) AND a decoded source in exactly the shape setConnections accepts ({recordId, referenceSchema} | {processParameter} | {sourceElement, sourceElementParameter} | {expression}), so you can feed it straight back without translating a platform metapath — with four exceptions: a legacy fixed-record connection whose stored macro names a different entity than its column, where you must drop referenceSchema to re-apply it; a connection whose stored value is not a [#...#] macro at all (check source), which comes back as expression and is refused on re-apply; a stored macro from a family that cannot hold a record id (DateValue, DateTimeValue, TimeValue, BooleanValue), likewise refused; and a [#SysVariable...#] whose name does not resolve on THIS environment or cannot hold a record id, which unlike the others depends on where you are — a current CrtProcessBuilder checks the name against the platform vocabulary and an older one does not, so the same read-back can re-apply on one environment and be refused on another; a macro this build does not recognise degrades to expression rather than breaking the read. UNBOUND connections are deliberately absent — the platform leaves those behind in bulk, so absence does NOT mean the column cannot be connected; the whole array is also absent when the host entity cannot be resolved on that environment, or the connection registry cannot be read — both degrade to absent rather than failing the read, so absence never means verified-empty. registered=false means the value IS written at run time but the connection is ignored by the record page's connections detail, Next Steps, email auto-relation rules and quick-add, and is normally absent from the designer's connections block too — the one exception being the designer's client-appended Project column, which it shows without a registry row. A user-task element additionally carries deprecated (its user-task schema is retired by the platform — reported only, no operation refuses one) and writesConnectionsAtRuntime, where FALSE is the answer that matters: it marks a process whose connections persist, compile and run green while writing nothing. FALSE has two causes, fixed differently — the user task's runtime never writes connections (the CallUserTask, EmailUserTask, SendEmailUserTask and ReadDataUserTask schemas — NOT the sendEmail element type, which maps to the supported EmailTemplateUserTask), which you fix by changing the element kind; or this element's activity-creation gate is shut, which you fix by setting CreateActivity to a constant true — or, on a Send email element, by switching it to manual send, which creates the activity unconditionally and needs no CreateActivity write at all — and setConnections is refused either way, with the refusal saying which. null means not established: not a user task, a user-task schema that does not resolve, or a user task outside the supported six — all three are also refused, so null is no licence either. Identify the process by exactly one of process-name / process-uid / process-caption. Pair with get-guidance name=process-modeling to explain it. Requires the ProcessDesignService (CrtProcessBuilder) package on the target environment; install it with install-process-builder.")]
	public CommandExecutionResult DescribeProcess(
		[Description("describe-business-process parameters")]
		[Required]
		DescribeProcessArgs args) {
		DescribeProcessOptions options = new() {
			ProcessName = args.ProcessName,
			ProcessUid = args.ProcessUid,
			ProcessCaption = args.ProcessCaption,
			Culture = args.Culture ?? "en-US",
			Environment = args.EnvironmentName
		};
		try {
			return InternalExecute<DescribeProcessCommand>(options);
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)]);
		}
	}
}

/// <summary>
/// MCP arguments for the <c>describe-business-process</c> tool. Provide exactly one of
/// <c>process-name</c> / <c>process-uid</c> / <c>process-caption</c>.
/// </summary>
public sealed record DescribeProcessArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("process-name")]
	[property: Description("Process code (schema Name), e.g. UsrProcess_493d4c9. Provide exactly one identity.")]
	string? ProcessName = null,

	[property: JsonPropertyName("process-uid")]
	[property: Description("Process UId (GUID). Provide exactly one identity.")]
	string? ProcessUid = null,

	[property: JsonPropertyName("process-caption")]
	[property: Description("Process caption (display name). Provide exactly one identity.")]
	string? ProcessCaption = null,

	[property: JsonPropertyName("culture")]
	[property: Description("Optional culture used to resolve localized captions (default en-US).")]
	string? Culture = null
);
