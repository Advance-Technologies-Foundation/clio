using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class GetClassicMigrationBundleTool(
	GetClassicMigrationBundleCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetClassicMigrationBundleOptions>(command, logger, commandResolver) {

	internal const string ToolName = "get-classic-migration-bundle";

	// ReadOnly=false: the tool's whole purpose is a local file write (the manifest). Destructive stays
	// false in line with the schema-read family: the default path is confined to the resolved anchor
	// (schema-name is format-validated), matching the get-page/PageFileWriter trade.
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Assemble a Classic->Freedom migration bundle for a classic page schema and WRITE it to disk as a manifest " +
		"the migration engine (migrate.mjs) folds: the whole replacing-schema layer chain (base->top) + the " +
		"parent-template seed + resolution inputs (entityColumns/columnTitles/resources). The response returns the " +
		"ABSOLUTE manifest file path and a small summary — the layer bodies are written to the file, NOT returned, " +
		"so they never enter the caller's context. Prefer `environment-name`; keep direct connection args for fallback only.")]
	public GetClassicMigrationBundleResponse GetBundle(
		[Description("Parameters: schema-name (required, the classic page); entity (optional); output-file (optional); environment-name preferred.")]
		[Required]
		GetClassicMigrationBundleArgs args) {
		return ExecuteWithCleanLog(() => {
			if (args is null) {
				return new GetClassicMigrationBundleResponse { Success = false, Error = "args is required" };
			}
			GetClassicMigrationBundleOptions options = new() {
				SchemaName = args.SchemaName,
				Entity = args.Entity,
				OutputFile = args.OutputFile,
				Environment = args.EnvironmentName,
				Uri = args.Uri,
				Login = args.Login,
				Password = args.Password
			};
			GetClassicMigrationBundleCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetClassicMigrationBundleCommand>(options);
			}
			catch (Exception ex) {
				return new GetClassicMigrationBundleResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);
			if (!string.IsNullOrEmpty(response?.Error)) {
				// The command's inner error can carry an HTTP/DataService message with the environment URI/host;
				// redact before it lands in the MCP transcript (parity with the resolution-failure path above).
				response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
			}
			return response;
		});
	}
}

/// <summary>
/// Arguments of the <c>get-classic-migration-bundle</c> MCP tool. Deliberately NOT derived from
/// <see cref="SchemaGetBaseArgs"/>: that base describes <c>output-file</c> as an optional schema-body
/// sink, while here it is the manifest destination and the manifest is always written.
/// </summary>
public sealed record GetClassicMigrationBundleArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Classic client-unit (page) schema name to assemble the bundle for, e.g. 'ContactPageV2'")]
	[property: Required]
	string SchemaName,
	[property: JsonPropertyName("entity")]
	[property: Description("Entity schema name (optional; inferred from the page body when omitted). Drives entityColumns/columnTitles.")]
	string Entity = null
) {

	[JsonPropertyName("output-file")]
	[Description("Manifest output path (absolute path recommended). Default: <workspace-root>/.clio-migration/" +
		"<schema>/manifest.json. The manifest is always written; the response reports the absolute path.")]
	public string OutputFile { get; init; }

	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	public string EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	[Description(McpToolDescriptions.Uri)]
	public string Uri { get; init; }

	[JsonPropertyName("login")]
	[Description(McpToolDescriptions.Login)]
	public string Login { get; init; }

	[JsonPropertyName("password")]
	[Description(McpToolDescriptions.Password)]
	public string Password { get; init; }
}
