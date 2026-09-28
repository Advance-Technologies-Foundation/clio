using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP adapter for <c>localize-page</c>: writes the captions of one Freedom UI page in one culture, or only
/// reports their coverage, through <see cref="ILocalizePageService"/> resolved for the target environment.
/// </summary>
[McpServerToolType]
public sealed class LocalizePageTool(
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<LocalizePageOptions>(null, logger, commandResolver) {

	internal const string ToolName = "localize-page";

	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["schemaName"] = "schema-name",
		["schema_name"] = "schema-name",
		["environmentName"] = "environment-name",
		["environment_name"] = "environment-name"
	};

	private const string ValidArgumentNames =
		"Valid: schema-name, culture, resources, caption, output-directory, environment-name, uri, login, password.";

	/// <summary>
	/// Translates the captions of one page into one culture, or reports coverage when nothing is supplied.
	/// </summary>
	/// <param name="args">Tool arguments.</param>
	/// <returns>The <c>localize-page</c> result, identical to the CLI output.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	// SharedFileResource is .clio-pages: after a save the command refreshes the get-page conflict baseline
	// under .clio-pages/{schema}/meta.json, which two clio processes could otherwise race.
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.ClioPages)]
	[Description("Translate the captions of ONE Freedom UI page into ONE additional culture without changing en-US or any other culture. " +
		"Read get-guidance name=page-schema-resources before translating a page. " +
		"Writes the supplied `resources` (existing key -> value in `culture`) and the page title `caption` in that culture only; " +
		"the same call twice leaves the same state and the second one reports saved:false. " +
		"Report-only when both `resources` and `caption` are omitted: nothing is saved and `coverage` lists the keys still missing in the culture. " +
		"An unknown key fails the whole call and nothing is saved; register new keys with update-page first. " +
		"A culture absent from the environment fails before any write and names the Languages section; an inactive culture is written with a warning. " +
		"Data-source-bound field labels are entity column captions: translate them with the entity tools' title-localizations, not here.")]
	public LocalizePageResponse LocalizePage(
		[Description("schema-name, culture (required); resources, caption, output-directory (optional); environment-name preferred; uri/login/password fallback only. " +
			"Optional under credential passthrough — omit environment-name/uri/login/password so the header-supplied tenant is used; " +
			"supplying any of them together with an active passthrough header is rejected, not silently honored.")]
		[Required] LocalizePageArgs args) {
		string? legacyAliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".", ValidArgumentNames);
		if (!string.IsNullOrWhiteSpace(legacyAliasError)) {
			return Failure(args.SchemaName, legacyAliasError);
		}
		LocalizePageOptions options = BuildOptions(args);
		return ExecuteWithCleanLog(options, () => {
			try {
				ILocalizePageService service = ResolveFromCallContainer<ILocalizePageService>(options);
				return service.Localize(options);
			} catch (Exception ex) {
				return Failure(options.SchemaName, SensitiveErrorTextRedactor.Redact(ex.Message));
			}
		});
	}

	/// <summary>
	/// Maps the MCP arguments onto the command options. Internal so the mapping can be asserted directly.
	/// </summary>
	internal static LocalizePageOptions BuildOptions(LocalizePageArgs args) =>
		new() {
			SchemaName = args.SchemaName,
			Culture = args.Culture,
			Resources = args.Resources,
			Caption = args.Caption,
			OutputDirectory = args.OutputDirectory,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

	private static LocalizePageResponse Failure(string? schemaName, string error) =>
		new() {
			Success = false,
			SchemaName = schemaName,
			Error = error
		};
}

/// <summary>
/// Arguments for the <c>localize-page</c> MCP tool.
/// </summary>
public sealed record LocalizePageArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Freedom UI page schema name, for example 'UsrMyApp_FormPage'.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("culture")]
	[property: Description("Target culture, for example 'es-ES'. Must exist in the environment's Languages section (SysCulture); matched case-insensitively.")]
	[property: Required]
	string Culture,

	[property: JsonPropertyName("resources")]
	[property: Description("Optional JSON object string mapping EXISTING resource keys (as get-page shows them) to their value in `culture`, for example '{\"UsrTitle_caption\":\"Titulo\"}'. Omit together with `caption` for a report-only call.")]
	string? Resources = null,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional page title in `culture`.")]
	string? Caption = null,

	[property: JsonPropertyName("output-directory")]
	[property: Description("Optional. Directory that anchors the .clio-pages baseline lookup - pass the same value that was passed to get-page when it differs from the auto-detected workspace root, so the baseline is refreshed after the save. Does not change where the page is saved.")]
	string? OutputDirectory = null,

	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	string? EnvironmentName = null,

	[property: JsonPropertyName("uri")]
	[property: Description(McpToolDescriptions.Uri)]
	string? Uri = null,

	[property: JsonPropertyName("login")]
	[property: Description(McpToolDescriptions.Login)]
	string? Login = null,

	[property: JsonPropertyName("password")]
	[property: Description(McpToolDescriptions.Password)]
	string? Password = null
) {
	/// <summary>
	/// Overflow bag for request fields that do not bind to a declared kebab-case parameter, used to reject
	/// camelCase aliases such as <c>schemaName</c> instead of silently ignoring them.
	/// </summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
