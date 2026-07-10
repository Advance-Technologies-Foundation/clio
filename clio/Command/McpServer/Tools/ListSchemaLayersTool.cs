using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ListSchemaLayersTool(
	ListSchemaLayersCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ListSchemaLayersOptions>(command, logger, commandResolver) {

	internal const string ToolName = "list-schema-layers";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"List every package layer of a client unit schema by name (base + all replacing layers), each with " +
		"package, maintainer, InstallType, is-base and is-client-editable. " +
		"Use in Classic->Freedom migration to enumerate a schema's layers (read each with get-classic-schema by UId) " +
		"and to find the client-editable package to write Freedom artifacts into. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ListSchemaLayersResponse ListLayers(
		[Description("Parameters: schema-name (required); manager-name (optional, default ClientUnitSchemaManager); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ListSchemaLayersArgs args) {
		ListSchemaLayersOptions options = new() {
			SchemaName = args.SchemaName,
			ManagerName = string.IsNullOrWhiteSpace(args.ManagerName) ? "ClientUnitSchemaManager" : args.ManagerName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(() => {
			ListSchemaLayersCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ListSchemaLayersCommand>(options);
			}
			catch (Exception ex) {
				return new ListSchemaLayersResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryListLayers(options, out ListSchemaLayersResponse response);
			return response;
		});
	}
}

public sealed record ListSchemaLayersArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Client unit schema name shared across all layers, e.g. 'ContractPageV2'")]
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
