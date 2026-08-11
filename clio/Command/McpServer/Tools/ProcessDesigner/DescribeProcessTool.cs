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
[FeatureToggle("process-designer")]
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
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Reads an existing Creatio process and returns a STRUCTURED graph (elements with runtime type, the specific user-task schema name, the signalStart record-event trigger (entity, on, and changedColumns for a column-restricted 'modified' signal), the element-level useBackgroundMode flag, label and value-bearing parameters (unbound element inputs are omitted — absence does not mean the parameter does not exist); flows with source/target/kind; and process parameters) — not the raw metadata. Element typing comes from the real object model server-side (universal, incl. custom user tasks); each parameter carries its direction and isResult, and parameter values carry their source (Mapping/ConstValue/Script) and expression. An element parameter is usable as a mapping SOURCE (an output) when isResult=true OR direction=Out — most user-task outputs come back isResult=true with direction=Variable, so detect outputs by isResult, not by direction alone. Each element also carries its BOUND 'Connected to' links as connections[] — which records the Activity it creates is attached to. Every entry gives both the raw persisted macro (value) AND a decoded source in exactly the shape setConnections accepts ({recordId, referenceSchema} | {processParameter} | {sourceElement, sourceElementParameter} | {expression}), so you can feed it straight back without translating a platform metapath; a macro this build does not recognise degrades to expression rather than breaking the read. UNBOUND connections are deliberately absent — the platform leaves those behind in bulk, so absence does NOT mean the column cannot be connected. registered=false means the value IS written at run time but the connection is invisible in the designer and ignored by the record page's connections detail, Next Steps, email auto-relation rules and quick-add. A user-task element additionally carries deprecated (its user-task schema is retired by the platform) and writesConnectionsAtRuntime, where FALSE is the answer that matters: it marks a process whose connections persist, compile and run green while writing nothing. null means not established, not false. Identify the process by exactly one of process-name / process-uid / process-caption. Pair with get-guidance name=process-modeling to explain it. Requires the ProcessDesignService (CrtProcessBuilder) package on the target environment; install it with install-process-builder.")]
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
	string ProcessName,

	[property: JsonPropertyName("process-uid")]
	[property: Description("Process UId (GUID). Provide exactly one identity.")]
	string ProcessUid,

	[property: JsonPropertyName("process-caption")]
	[property: Description("Process caption (display name). Provide exactly one identity.")]
	string ProcessCaption,

	[property: JsonPropertyName("culture")]
	[property: Description("Optional culture used to resolve localized captions (default en-US).")]
	string Culture
);
