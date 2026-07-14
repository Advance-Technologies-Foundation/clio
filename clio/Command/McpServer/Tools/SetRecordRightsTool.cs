using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Command.RecordRights;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SetRecordRightsTool(
	SetRecordRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SetRecordRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "set-record-rights";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Change who can access a record or dashboard — grant or revoke a record-level access right on a single Creatio record (DESTRUCTIVE — " +
		"changes only the specified grant; other grants are untouched). " +
		"Target it with entity + record-id (for a client-unit schema/dashboard, pass entity=SysSchemaAdminUnit and record-id=the schema UId). " +
		"Specify grantee (SysAdminUnit GUID), operation (read/edit/delete), level (granted/delegated, default granted), and revoke to remove.")]
	public SetRecordRightsResponse SetRecordRights(
		[Description("Parameters: environment-name, entity, record-id, grantee, operation (all required); level, revoke.")]
		[Required]
		SetRecordRightsArgs args) {
		try {
			SetRecordRightsOptions options = new() {
				Environment = args.EnvironmentName,
				Entity = args.Entity,
				RecordId = args.RecordId,
				Grantee = args.Grantee,
				Operation = args.Operation,
				Level = string.IsNullOrWhiteSpace(args.Level) ? "granted" : args.Level,
				Revoke = args.Revoke ?? false,
				// --confirm is a CLI-only interactive gate; on MCP the Destructive flag is the safety mechanism,
				// so confirm the apply here (the command otherwise refuses in a non-interactive run).
				Confirm = true
			};
			lock (CommandExecutionSyncRoot) {
				CommandExecutionResult result = InternalExecute<SetRecordRightsCommand>(options);
				return new SetRecordRightsResponse {
					Success = result.ExitCode == 0,
					Output = result.ExitCode == 0 ? ResolveMessages(result) : null,
					Error = result.ExitCode == 0 ? null : ResolveMessages(result)
				};
			}
		} catch (Exception ex) {
			return new SetRecordRightsResponse {
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

public sealed record SetRecordRightsArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("entity")]
	[property: Description("Entity schema name of the target record (use with record-id). For a client-unit schema/dashboard, pass SysSchemaAdminUnit.")]
	[property: Required]
	string Entity,

	[property: JsonPropertyName("record-id")]
	[property: Description("Primary column value (record id) of the target record (use with entity). For a client-unit schema/dashboard, the schema UId.")]
	[property: Required]
	string RecordId,

	[property: JsonPropertyName("grantee")]
	[property: Description("Grantee SysAdminUnit GUID.")]
	[property: Required]
	string Grantee,

	[property: JsonPropertyName("operation")]
	[property: Description("Operation to grant or revoke: read | edit | delete.")]
	[property: Required]
	string Operation,

	[property: JsonPropertyName("level")]
	[property: Description("Right level for a grant: granted (default) | delegated.")]
	string? Level = null,

	[property: JsonPropertyName("revoke")]
	[property: Description("Revoke (remove) the right instead of granting it.")]
	bool? Revoke = null
);

public sealed class SetRecordRightsResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("output")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Output { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }
}
