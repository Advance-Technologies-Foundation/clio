using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for the <c>describe-process</c> command — reads an existing process into a
/// structured graph the agent can narrate (the inverse of generation). Read-only, environment-sensitive.
/// </summary>
[McpServerToolType]
public sealed class DescribeProcessTool(
	DescribeProcessCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<DescribeProcessOptions>(command, logger, commandResolver) {

	/// <summary>Stable MCP tool name.</summary>
	internal const string ToolName = "describe-process";

	/// <summary>
	/// Reads the identified process and returns its structured graph (elements, flows, parameters).
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Reads an existing Creatio process and returns a STRUCTURED graph (elements with type/label/parameters, flows with source/target/kind, and process parameters) — not the raw metadata. Identify the process by exactly one of process-code / process-uid / process-caption. Pair with get-guidance name=process-modeling to explain it. (v1 does not decode filter/mapping expressions.)")]
	public CommandExecutionResult DescribeProcess(
		[Description("describe-process parameters")]
		[Required]
		DescribeProcessArgs args) {
		DescribeProcessOptions options = new() {
			ProcessCode = args.ProcessCode,
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
/// MCP arguments for the <c>describe-process</c> tool. Provide exactly one of
/// <c>process-code</c> / <c>process-uid</c> / <c>process-caption</c>.
/// </summary>
public sealed record DescribeProcessArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("process-code")]
	[property: Description("Process code (schema Name), e.g. UsrProcess_493d4c9. Provide exactly one identity.")]
	string ProcessCode,

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
