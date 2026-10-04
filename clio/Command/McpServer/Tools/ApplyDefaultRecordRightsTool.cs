using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.ObjectRights;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP surface of <c>apply-default-record-rights</c>: starts the platform's "Update record permissions" for ONE object,
/// once. Destructive and NOT idempotent: every call starts a run. The host's approval of the call is the confirmation.
/// </summary>
[McpServerToolType]
public sealed class ApplyDefaultRecordRightsTool(
	ApplyDefaultRecordRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ApplyDefaultRecordRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "apply-default-record-rights";

	internal const string ValidArguments = "Valid: environment-name, entity-schema-name, wait, timeout-seconds.";

	// Each request gets one attempt of at most 25 s and the whole call — the read, the record count, the launch and the
	// wait — 100 s, so the answer arrives before the caller's deadline. A run that is still going then is reported as
	// such, with its process id; it is not a failure.
	internal const int McpRequestTimeoutMilliseconds = 25_000;

	internal static readonly TimeSpan McpCallBudget = TimeSpan.FromSeconds(100);

	internal const int McpDefaultWaitSeconds = 60;

	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Apply an object's current default record rules to its EXISTING records — the platform's \"Update record permissions\" (ObjectRecordRightsActualizationProcess) (DESTRUCTIVE, NOT idempotent: every call starts a run). " +
		"Run it ONLY when the user asked for it: after enabling record permissions or changing rules with set-default-record-rights, ASK the user whether and when to apply them, stating the record count and that the run is heavy on large tables; never run it on your own. " +
		"The run deletes the record rights that came from default rules and applies the current rules to every existing record; rights granted by hand (set-record-rights) stay. Refused when the object's record permissions are OFF. " +
		"wait (default true) polls the run until it completes or fails, up to timeout-seconds (default 60, at most about 90 s on MCP); a run still going then is reported as 'still running' with its process id — not a failure: do NOT start it again, check SysProcessLog (Id = the process id) later. Fails (success=false) when the run could not be started or ended in error. " +
		"Unknown or misspelled argument names are REFUSED before any read or write.")]
	public ObjectRightsToolResponse ApplyDefaultRecordRights(
		[Description("Parameters: environment-name, entity-schema-name (required); wait, timeout-seconds (optional).")]
		[Required]
		ApplyDefaultRecordRightsArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ObjectRightsToolResponse.FromValidationError(aliasError);
		}
		try {
			return ObjectRightsToolResponse.From(InternalExecute<ApplyDefaultRecordRightsCommand>(BuildOptions(args)));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}

	/// <summary>The command options an MCP call runs with.</summary>
	/// <param name="args">The call's arguments.</param>
	/// <returns>The options, with the MCP request limits.</returns>
	internal static ApplyDefaultRecordRightsOptions BuildOptions(ApplyDefaultRecordRightsArgs args) => new() {
		Environment = args.EnvironmentName,
		EntitySchemaName = args.EntitySchemaName,
		Wait = args.Wait ?? true,
		TimeoutSeconds = args.TimeoutSeconds ?? McpDefaultWaitSeconds,
		// MCP cannot prompt anyone: the host's approval of this call is the confirmation.
		Confirm = true,
		TimeOut = McpRequestTimeoutMilliseconds,
		MaxAttempts = 1,
		CallBudget = McpCallBudget
	};
}

/// <summary>Arguments of the <c>apply-default-record-rights</c> MCP tool.</summary>
public sealed record ApplyDefaultRecordRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("The one object (entity schema) whose existing records get the current default record rules.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("wait")]
	[property: Description("Wait for the run to end (default true); false starts it and returns the process id.")]
	bool? Wait = null,

	[property: JsonPropertyName("timeout-seconds")]
	[property: Description("How long to wait for the run, in seconds (default 60; the call itself ends after about 100 s). Past it the run keeps going and is reported as still running.")]
	int? TimeoutSeconds = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call before any read or write.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
