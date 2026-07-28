using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools.ProcessDesigner;

/// <summary>
/// MCP tool surface for the <c>get-process-signature</c> command.
/// Returns the parameter signature (codes, types, direction) of a Creatio business process so an
/// agent can author a <c>crt.RunBusinessProcessRequest</c> button config with correct parameter codes.
/// </summary>
[McpServerToolType]
public sealed class GetProcessSignatureTool(
	GetProcessSignatureCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetProcessSignatureOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-process-signature";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Resolve a Creatio business process by its code (schema Name) OR its display caption and return " +
		"its parameter signature: per parameter " +
		"the CODE (name), caption, CLR type, dataValueTypeId, direction, and lookup reference schema. " +
		"Use this BEFORE authoring a run-process button (crt.RunBusinessProcessRequest): the parameter " +
		"CODE — not the caption — must be the key in processParameters / parameterMappings / " +
		"recordIdProcessParameterName, otherwise the platform silently drops the value. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public GetProcessSignatureResponse GetProcessSignature(
		[Description("Parameters: process-name (required, the process CODE/schema name); culture (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		GetProcessSignatureArgs args) {
		GetProcessSignatureOptions options = new() {
			ProcessName = args.ProcessName,
			Culture = args.Culture ?? "en-US",
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			GetProcessSignatureCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetProcessSignatureCommand>(options);
			}
			catch (Exception ex) {
				return new GetProcessSignatureResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryGetSignature(options, out GetProcessSignatureResponse response);
			return response;
		});
	}
}

/// <summary>
/// MCP arguments for the <c>get-process-signature</c> tool.
/// </summary>
public sealed record GetProcessSignatureArgs(
	[property: JsonPropertyName("process-name")]
	[property: Description("Process code (schema Name), e.g. 'UsrProcess_e629820', OR the display "
		+ "caption shown in the process designer, e.g. 'Business process 1'. Pass whatever the user "
		+ "named; the tool resolves both and echoes the resolved processCode.")]
	[property: Required]
	string ProcessName,

	[property: JsonPropertyName("culture")]
	[property: Description("Optional culture used to resolve localized parameter captions (default en-US)")]
	string? Culture,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName,

	[property: JsonPropertyName("uri")]
	[property: Description("Creatio base URI (emergency fallback only; prefer environment-name)")]
	string? Uri,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password
);
