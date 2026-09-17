using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;
using System.Collections.Generic;
using System.Text.Json;

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

	/// <summary>The canonical field list echoed back when an unknown argument key is refused (ENG-98566).</summary>
	internal const string ValidArgsHint = "Valid: environment-name, process-name, culture, uri, login, password.";

	/// <summary>
	/// Refusal for a call whose whole argument object is absent (ENG-98566, Sonar S2259).
	/// </summary>
	/// <remarks>
	/// Stays inline rather than moving into the shared helper: it has to run before any field can be read,
	/// and that is what lets the analyser prove no field read is reached with null (csharpsquid:S2259). See
	/// <see cref="McpToolArgumentSupport.BuildUnknownArgumentError"/>.
	/// </remarks>
	internal const string NullArgsError = "args is required: the call carried no argument object. " + ValidArgsHint;

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Resolve a Creatio business process by its code (schema Name) OR its display caption. " +
		"A caption is shared by every version of a process — each version is a "
		+ "separate schema with its own code but the same caption — so a caption resolves to the ACTIVE "
		+ "version, the one the runtime executes; a caption matching several distinct processes, or one "
		+ "whose active version cannot be established, is refused with the candidate codes. Returns " +
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
		if (args is null) {
			return new GetProcessSignatureResponse { Success = false, Error = NullArgsError };
		}

		// The only unknown-key defence this tool has; the helper's docs say why. ENG-98566.
		string argumentError = McpToolArgumentSupport.BuildUnknownArgumentError(
			args.ExtensionData, ValidArgsHint);
		if (!string.IsNullOrWhiteSpace(argumentError)) {
			return new GetProcessSignatureResponse { Success = false, Error = argumentError };
		}

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
	string? Culture = null,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName = null,

	[property: JsonPropertyName("uri")]
	[property: Description("Creatio base URI (emergency fallback only; prefer environment-name)")]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password = null
) {

	/// <summary>
	/// Overflow bag for top-level keys the SDK could not bind to a declared argument (ENG-98566).
	/// Inspected by the tool so a mis-keyed argument is named back to the caller; a bag that is
	/// never read is the failure mode, not the fix.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
