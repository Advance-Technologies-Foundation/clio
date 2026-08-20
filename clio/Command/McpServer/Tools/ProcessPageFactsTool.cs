using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class ProcessPageFactsTool(
	ProcessPageFactsCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ProcessPageFactsOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-process-page-facts";
	private const string SchemaNameParam = "schema-name";

	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["schemaName"] = SchemaNameParam,
		["pageName"] = SchemaNameParam,
		["page-name"] = SchemaNameParam,
		["page"] = SchemaNameParam,
		["name"] = SchemaNameParam,
		["environmentName"] = "environment-name"
	};

	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Read the facts a Pre-configured page process element needs about a Freedom UI page: the buttons that can "
		+ "complete the page (with the caption the process designer records) and the page-scoped entity data "
		+ "sources. Pass these verbatim into the process descriptor's preconfiguredPage.buttons / .dataSources — "
		+ "they are page FACTS, not choices, and cannot be derived server-side because a page inherits buttons "
		+ "from its template chain. Choosing WHICH candidates complete the page is still yours. Fails for a "
		+ "Classic UI page, which completes through its own page-designer buttons instead.")]
	public ProcessPageFactsResponse GetProcessPageFacts(
		[Description("Parameters: schema-name (required), culture (optional, default en-US); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] ProcessPageFactsArgs args) {
		string? legacyAliasError = GetLegacyAliasError(args);
		if (!string.IsNullOrWhiteSpace(legacyAliasError)) {
			return new ProcessPageFactsResponse { Success = false, Error = legacyAliasError };
		}
		if (string.IsNullOrWhiteSpace(args.SchemaName)) {
			return new ProcessPageFactsResponse { Success = false, Error = "schema-name is required." };
		}
		ProcessPageFactsOptions options = new() {
			SchemaName = args.SchemaName,
			Culture = args.Culture,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			ProcessPageFactsCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<ProcessPageFactsCommand>(options);
			} catch (Exception ex) {
				return new ProcessPageFactsResponse {
					Success = false,
					Error = SensitiveErrorTextRedactor.Redact(ex.Message)
				};
			}
			resolvedCommand.TryGetFacts(options, out ProcessPageFactsResponse response);
			return response;
		});
	}

	private static string? GetLegacyAliasError(ProcessPageFactsArgs args) {
		return McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, string.Empty,
			"Valid: schema-name, culture, environment-name, uri, login, password.");
	}

}

/// <summary>Arguments for <see cref="ProcessPageFactsTool"/>.</summary>
public sealed class ProcessPageFactsArgs {

	[JsonPropertyName("schema-name")]
	[Description("Freedom UI page schema name, e.g. 'UsrMyApp_FormPage'.")]
	public string? SchemaName { get; set; }

	[JsonPropertyName("culture")]
	[Description("Culture used to resolve resource-backed button captions. Default en-US.")]
	public string? Culture { get; set; }

	[JsonPropertyName("environment-name")]
	[Description("Registered clio environment name. Preferred.")]
	public string? EnvironmentName { get; set; }

	[JsonPropertyName("uri")]
	[Description("Direct Creatio URL; emergency/bootstrap fallback. Prefer environment-name.")]
	public string? Uri { get; set; }

	[JsonPropertyName("login")]
	[Description("Direct login paired with uri; fallback only.")]
	public string? Login { get; set; }

	[JsonPropertyName("password")]
	[Description("Direct password paired with uri; fallback only.")]
	public string? Password { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; set; }

}
