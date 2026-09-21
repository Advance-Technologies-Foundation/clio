using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.ObjectRights;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetObjectRightsTool(
	GetObjectRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetObjectRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-object-rights";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Check whether the external (portal) audience already has object operation access to an object AND every entity connected to it through a lookup column. " +
		"Read-only companion of set-object-rights: use it to grant only where needed and to verify a grant. " +
		"For the root object and each connected lookup object it reports whether All external users has read/create/edit, and lists the objects that still need a grant " +
		"(this mirrors the Freedom UI designer's red \"objects not available to external users\" notification). " +
		"An object that is not administered by operation permissions is reported as available to all (no grant needed).")]
	public GetObjectRightsResponse GetObjectRights(
		[Description("Parameters: environment-name, entity-schema-name (both required).")]
		[Required]
		GetObjectRightsArgs args) {
		try {
			GetObjectRightsOptions options = new() {
				Environment = args.EnvironmentName,
				EntitySchemaName = args.EntitySchemaName
			};
			CommandExecutionResult result = InternalExecute<GetObjectRightsCommand>(options);
			return new GetObjectRightsResponse {
				Success = result.ExitCode == 0,
				Output = result.ExitCode == 0 ? ResolveMessages(result) : null,
				Error = result.ExitCode == 0 ? null : ResolveMessages(result)
			};
		} catch (Exception ex) {
			return new GetObjectRightsResponse {
				Success = false,
				Error = SensitiveErrorTextRedactor.Redact(ex.Message)
			};
		}
	}

	private static string ResolveMessages(CommandExecutionResult result) {
		string[] messages = result.Output
			.Select(message => message.Value?.ToString())
			.Where(message => !string.IsNullOrWhiteSpace(message))
			.ToArray();
		return messages.Length > 0 ? string.Join("\n", messages) : null;
	}
}

public sealed record GetObjectRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Root object (entity schema) name to check; its connected lookup objects are checked too.")]
	[property: Required]
	string EntitySchemaName
);

public sealed class GetObjectRightsResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("output")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Output { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }
}
