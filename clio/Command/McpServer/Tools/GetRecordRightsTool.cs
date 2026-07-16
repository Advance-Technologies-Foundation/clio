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
public sealed class GetRecordRightsTool(
	GetRecordRightsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetRecordRightsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-record-rights";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("See who has access to a record or dashboard — read the record-level access rights of a single Creatio record. " +
		"Target it with entity + record-id (for a client-unit schema/dashboard, pass entity=SysSchemaAdminUnit and record-id=the schema UId). " +
		"Returns each grant as operation (read/edit/delete) / level (granted/delegated) -> grantee.")]
	public GetRecordRightsResponse GetRecordRights(
		[Description("Parameters: environment-name, entity, record-id (all required).")]
		[Required]
		GetRecordRightsArgs args) {
		try {
			GetRecordRightsOptions options = new() {
				Environment = args.EnvironmentName,
				Entity = args.Entity,
				RecordId = args.RecordId
			};
			CommandExecutionResult result = InternalExecute<GetRecordRightsCommand>(options);
			return new GetRecordRightsResponse {
				Success = result.ExitCode == 0,
				Error = result.ExitCode == 0 ? null : ResolveMessages(result),
				Output = result.ExitCode == 0 ? ResolveMessages(result) : null
			};
		} catch (Exception ex) {
			return new GetRecordRightsResponse {
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

public sealed record GetRecordRightsArgs(
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
	string RecordId
);

public sealed class GetRecordRightsResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("output")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Output { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Error { get; init; }
}
