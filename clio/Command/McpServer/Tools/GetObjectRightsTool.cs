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

/// <summary>MCP surface of <c>get-object-rights</c>: reads the per-role object operation permissions of an object.</summary>
[McpServerToolType]
public sealed class GetObjectRightsTool(
	GetObjectRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetObjectRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-object-rights";

	internal const string ValidArguments = "Valid: environment-name, entity-schema-name, grantee, include-connected.";

	// The call is bounded by the MCP read deadline (120 s by default). One read gets one attempt of at most 30 s, and
	// the listing stops once 90 s are spent, so the answer — with what was read — arrives before the deadline instead
	// of being lost to it (a hang would otherwise cost the 100 s request timeout three times over).
	internal const int McpReadTimeoutMilliseconds = 30_000;

	internal static readonly TimeSpan McpReadBudget = TimeSpan.FromSeconds(90);

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		// With include-connected the read makes several sequential round-trips per object. The worker is not killed
		// at the default budget, but as a ReadOnly tool the call is still bounded by the MCP read-response deadline
		// (120 s by default, CLIO_MCP_READ_DEADLINE_SECONDS); an object with many lookups can reach it (ENG-100407).
		BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Read OBJECT operation permissions — who may read/create/edit/delete a whole entity (the SysEntitySchemaOperationRight / \"Object permissions\" layer). " +
		"Read-only companion of set-object-rights. Reports, per object, every role's row in PRIORITY order with its [position] (0 is the highest; a user in several roles gets the highest matching row, and a row with no operations denies them); pass grantee to show that role's row and the rows above it (every row when it has none, or when the object is not administered). " +
		"include-connected also reports the root object's own lookup objects (security/system objects are skipped) — the discovery step before deciding, per object, what to change with set-object-rights; each read gets one attempt of at most 30 s, and the listing stops after 90 s or a read that times out, naming the objects not read — then read them one by one. The output is facts only, with no coverage verdict. Fails (success=false) when the root object cannot be read; a connected object that cannot be read is reported with a warning. " +
		"An object not administered by operation permissions is available to all INTERNAL users; external users reach it only through an explicit grant; every row listed for it applies once operation permissions are turned on. " +
		"Unknown or misspelled argument names are refused.")]
	public ObjectRightsToolResponse GetObjectRights(
		[Description("Parameters: environment-name, entity-schema-name (required); grantee, include-connected (optional).")]
		[Required]
		GetObjectRightsArgs args) {
		// Long-tail (clio-run): the serializer would silently drop a misspelled key — e.g. "grantee-id" — and the
		// read would then answer for EVERY role instead of the one asked about. Refuse it instead.
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, McpToolArgumentSupport.EnvironmentNameAliases, ".", ValidArguments);
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ObjectRightsToolResponse.FromValidationError(aliasError);
		}
		try {
			GetObjectRightsOptions options = new() {
				Environment = args.EnvironmentName,
				EntitySchemaName = args.EntitySchemaName,
				Grantee = args.Grantee,
				IncludeConnected = args.IncludeConnected ?? false,
				TimeOut = McpReadTimeoutMilliseconds,
				MaxAttempts = 1,
				ReadBudget = McpReadBudget
			};
			return ObjectRightsToolResponse.From(InternalExecute<GetObjectRightsCommand>(options));
		} catch (Exception ex) {
			return ObjectRightsToolResponse.FromError(ex);
		}
	}
}

/// <summary>Arguments of the <c>get-object-rights</c> MCP tool.</summary>
public sealed record GetObjectRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Object (entity schema) name to read.")]
	[property: Required]
	string EntitySchemaName,

	[property: JsonPropertyName("grantee")]
	[property: Description("Optional SysAdminUnit id (role or user): show its row and the rows above it, which decide first (every row when it has none, or when the object is not administered). Omit to list every row.")]
	string Grantee = null,

	[property: JsonPropertyName("include-connected")]
	[property: Description("Also read the root object's own lookup objects, skipping security/system objects (default false).")]
	bool? IncludeConnected = null
) {
	/// <summary>Overflow bag for unknown JSON fields; a non-empty bag refuses the call.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
