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
	/// The guard below reads <c>args.ExtensionData</c> and every check after it reads a real field, so
	/// exactly one place may decide what a null <c>args</c> means - and it is this one. An <c>args?.</c>
	/// on the first line followed by an unconditional dereference on the next READS as null-safe while
	/// only moving the NullReferenceException three lines down, where it escapes as a raw transport
	/// fault instead of an answer. Hard to reach behind [Required] and the SDK missing-parameter error,
	/// but "hard to reach" is not the same as handled.
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

		// ENG-98566. This tool is LONG-TAIL - absent from McpCoreToolProfile - so McpToolErrorFilter's
		// unknown-key classifier never runs on it in ANY payload shape: TryRefuseCallArgumentsCore bails at
		// TryGetToolMethod because MatchedPrimitive is null for a tool that is not advertised. Even a
		// RESIDENT tool is only classified in the FLAT shape - an already-wrapped {"args":{...}} call is
		// passed through untouched. So the overflow bag plus this check is the ONLY thing standing between
		// a mis-keyed call and a plausible success. Do not delete it because the normalizer exists.
		string argumentError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArgsHint);
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
	public Dictionary<string, JsonElement> ExtensionData { get; init; }
}
