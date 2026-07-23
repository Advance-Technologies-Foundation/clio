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
	// false in line with the schema-read family (get-client-unit-schema, sql-schema-get), which likewise
	// accept an arbitrary output-file and write it verbatim while staying non-destructive. Only the DEFAULT
	// (no output-file) path is confined to the resolved anchor with a format-validated schema-name; an
	// explicit output-file is written as given — the same trade as the sibling schema-get tools, NOT
	// get-page/PageFileWriter (which always re-nests every write under an anchor).
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
		// This tool is environment-sensitive (environment-name/uri/login/password), so it must run under the
		// PER-TENANT execution lock. ExecuteResolved keys the lock on the resolved tenant and marks the session
		// container in-use for the whole multi-round-trip bundle assembly (ENG-93208), instead of the
		// environment-less ExecuteWithCleanLog overload which keys on the shared fallback — that would serialize
		// independent tenants and leave the resolved IApplicationClient/HttpClient evictable mid-call.
		// ExecuteResolved also centralizes the resolution-failure redaction that used to be hand-rolled here.
		return ExecuteResolved<GetClassicMigrationBundleCommand, GetClassicMigrationBundleResponse>(
			options,
			resolvedCommand => {
				resolvedCommand.TryAssembleBundle(options, out GetClassicMigrationBundleResponse response);
				if (!string.IsNullOrEmpty(response?.Error)) {
					// The command's inner error can carry an HTTP/DataService message with the environment
					// URI/host; redact before it lands in the MCP transcript (parity with ExecuteResolved's
					// resolution-failure redaction).
					response.Error = SensitiveErrorTextRedactor.Redact(response.Error);
				}
				return response;
			},
			error => new GetClassicMigrationBundleResponse { Success = false, Error = error });
	}
}

/// <summary>
/// Arguments of the <c>get-classic-migration-bundle</c> MCP tool. Derives from
/// <see cref="ConnectionArgsBase"/> for the shared connection surface (environment-name / uri / login /
/// password) but deliberately NOT from <see cref="SchemaGetBaseArgs"/>: that base describes
/// <c>output-file</c> as an optional schema-body sink, while here it is the manifest destination and the
/// manifest is always written, so <c>output-file</c> is declared locally with its own semantics.
/// </summary>
public sealed record GetClassicMigrationBundleArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Classic client-unit (page) schema name to assemble the bundle for, e.g. 'ContactPageV2'")]
	[property: Required]
	string SchemaName,
	[property: JsonPropertyName("entity")]
	[property: Description("Entity schema name (optional; inferred from the page body when omitted). Drives entityColumns/columnTitles.")]
	string Entity = null
) : ConnectionArgsBase {

	[JsonPropertyName("output-file")]
	[Description("Manifest output path (absolute path recommended). Default: <workspace-root>/.clio-migration/" +
		"<schema>/manifest.json. The manifest is always written; the response reports the absolute path.")]
	public string OutputFile { get; init; }
}
