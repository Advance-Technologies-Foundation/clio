using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ResolveMigrationUnitTool(
	ResolveMigrationUnitCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ResolveMigrationUnitOptions>(command, logger, commandResolver) {

	internal const string ToolName = "resolve-migration-unit";

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Resolve the page-role graph of an entity for a Classic->Freedom migration: its Classic sections, " +
		"edit pages (including per-type/typed pages) and add mini pages, each classified classic vs freedom. " +
		"One level only — recurse into detail entities by calling this per detail entity; details-on-page and " +
		"Freedom counterparts are read from the page body by the merge module, not here. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ResolveMigrationUnitResponse Resolve(
		[Description("Parameters: entity-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		ResolveMigrationUnitArgs args) {
		ResolveMigrationUnitOptions options = new() {
			EntityName = args.EntityName,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(() => {
			ResolveMigrationUnitCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ResolveMigrationUnitCommand>(options);
			}
			catch (Exception ex) {
				return new ResolveMigrationUnitResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryResolve(options, out ResolveMigrationUnitResponse response);
			if (!response.Success && !string.IsNullOrEmpty(response.Error))
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			return response;
		});
	}
}

public sealed record ResolveMigrationUnitArgs(
	[property: JsonPropertyName("entity-name")]
	[property: Description("Entity schema name, e.g. 'Contract' or 'SupportUnit'")]
	[property: Required]
	string EntityName
) {
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
