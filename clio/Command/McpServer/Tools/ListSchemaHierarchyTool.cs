using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ListSchemaHierarchyTool(
	ListSchemaHierarchyCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ListSchemaHierarchyOptions>(command, logger, commandResolver) {

	internal const string ToolName = "list-schema-hierarchy";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"List the full package hierarchy of a client unit schema by name (the base schema plus all replacing schemas), each with " +
		"package, maintainer, InstallType, is-base and is-client-editable. " +
		"Use in Classic->Freedom migration to enumerate a schema's full hierarchy (read each entry with get-classic-schema-by-uid) " +
		"and to find the client-editable package to write Freedom artifacts into. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ListSchemaHierarchyResponse ListHierarchy(
		[Description("Parameters: schema-name (required); manager-name (optional, default ClientUnitSchemaManager); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ListSchemaHierarchyArgs args) {
		ListSchemaHierarchyOptions options = new() {
			SchemaName = args.SchemaName,
			ManagerName = string.IsNullOrWhiteSpace(args.ManagerName) ? "ClientUnitSchemaManager" : args.ManagerName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(() => {
			ListSchemaHierarchyCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ListSchemaHierarchyCommand>(options);
			}
			catch (Exception ex) {
				return new ListSchemaHierarchyResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryListHierarchy(options, out ListSchemaHierarchyResponse response);
			if (!response.Success && !string.IsNullOrEmpty(response.Error))
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			return response;
		});
	}
}

public sealed record ListSchemaHierarchyArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Client unit schema name shared across all schemas, e.g. 'ContractPageV2'")]
	[property: Required]
	string SchemaName
) {
	[JsonPropertyName("manager-name")]
	[Description("Optional SysSchema.ManagerName filter (default ClientUnitSchemaManager).")]
	public string? ManagerName { get; init; }

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	public string? EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	[Description(McpToolDescriptions.Uri)]
	public string? Uri { get; init; }

	[JsonPropertyName("login")]
	[Description(McpToolDescriptions.Login)]
	public string? Login { get; init; }

	[JsonPropertyName("password")]
	[Description(McpToolDescriptions.Password)]
	public string? Password { get; init; }
}
