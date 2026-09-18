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
public sealed class SetObjectRightsTool(
	SetObjectRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetObjectRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-object-rights";

	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Grant object operation + record permissions to an object AND every entity connected to it through a lookup column, " +
		"making it available to the external (portal) audience (DESTRUCTIVE — changes access rights). " +
		"Object-level analog of set-record-rights. Unlike set-record-rights it takes no grantee/operation: " +
		"the platform service is coarse — pass only the root object via entity-schema-name and the server resolves the connected lookup objects itself. " +
		"This wraps the Freedom UI designer's \"Update object permissions now\" action. " +
		"It does NOT change column permissions. The call is asynchronous (rights are recalculated) and may take tens of seconds.")]
	public SetObjectRightsResponse SetObjectRights(
		[Description("Parameters: environment-name, entity-schema-name (both required).")]
		[Required]
		SetObjectRightsArgs args) {
		try {
			SetObjectRightsOptions options = new() {
				Environment = args.EnvironmentName,
				EntitySchemaName = args.EntitySchemaName,
				// --confirm is a CLI-only interactive gate; on MCP the Destructive flag is the safety mechanism,
				// so confirm the apply here (the command otherwise refuses in a non-interactive run).
				Confirm = true
			};
			CommandExecutionResult result = InternalExecute<SetObjectRightsCommand>(options);
			return new SetObjectRightsResponse {
				Success = result.ExitCode == 0,
				Output = result.ExitCode == 0 ? ResolveMessages(result) : null,
				Error = result.ExitCode == 0 ? null : ResolveMessages(result)
			};
		} catch (Exception ex) {
			return new SetObjectRightsResponse {
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

public sealed record SetObjectRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity-schema-name")]
	[property: Description("Root object (entity schema) name whose access is granted; connected lookup objects are resolved server-side.")]
	[property: Required]
	string EntitySchemaName
);

public sealed class SetObjectRightsResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("output")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Output { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }
}
