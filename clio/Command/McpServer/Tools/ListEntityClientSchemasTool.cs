using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ListEntityClientSchemasTool(
	ListEntityClientSchemasCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ListEntityClientSchemasOptions>(command, logger, commandResolver) {

	internal const string ToolName = "list-entity-client-schemas";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Resolve the page-role graph of an entity for a Classic->Freedom migration: its Classic sections, " +
		"edit pages (including per-type/typed pages) and add mini pages, each classified classic, freedom, or unknown. " +
		"One level only — recurse into detail entities by calling this per detail entity; details-on-page and " +
		"Freedom counterparts are read from the page body by the merge module, not here. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ListEntityClientSchemasResponse Resolve(
		[Description("Parameters: entity-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ListEntityClientSchemasArgs args) {
		return ExecuteWithCleanLog(() => {
			if (args is null) {
				return new ListEntityClientSchemasResponse { Success = false, Error = "args is required" };
			}
			ListEntityClientSchemasOptions options = new() {
				EntityName = args.EntityName,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			ListEntityClientSchemasCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ListEntityClientSchemasCommand>(options);
			}
			catch (Exception ex) {
				return new ListEntityClientSchemasResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryResolve(options, out ListEntityClientSchemasResponse response);
			if (!response.Success && !string.IsNullOrEmpty(response.Error))
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			return response;
		});
	}
}

public sealed record ListEntityClientSchemasArgs(
	[property: JsonPropertyName("entity-name")]
	[property: Description("Entity schema name, e.g. 'Contract' or 'SupportUnit'")]
	[property: Required]
	string EntityName
) : ConnectionArgsBase;
