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
	[Description("Reads an existing Creatio process and returns a STRUCTURED graph (elements with runtime type, the specific user-task schema name, the signalStart record-event trigger (entity, on, and changedColumns for a column-restricted 'modified' signal), label and value-bearing parameters (unbound element inputs are omitted — absence does not mean the parameter does not exist); flows with source/target/kind; and process parameters) — not the raw metadata. Element typing comes from the real object model server-side (universal, incl. custom user tasks); each parameter carries its direction and isResult, and parameter values carry their source (Mapping/ConstValue/Script) and expression. An element parameter is usable as a mapping SOURCE (an output) when isResult=true OR direction=Out — most user-task outputs come back isResult=true with direction=Variable, so detect outputs by isResult, not by direction alone. Identify the process by exactly one of process-name / process-uid / process-caption. Pair with get-guidance name=process-modeling to explain it. Requires the ProcessDesignService (clioprocessbuilder) package on the target environment.")]
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
