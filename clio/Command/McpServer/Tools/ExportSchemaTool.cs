using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP surface of <c>export-schema</c>.
/// </summary>
[McpServerToolType]
public class ExportSchemaTool(
	ExportSchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ExportSchemaOptions>(command, logger, commandResolver) {

	internal const string ExportSchemaToolName = "export-schema";

	/// <summary>
	/// Exports one schema into a bundle folder on disk.
	/// </summary>
	/// <param name="args">Export parameters.</param>
	/// <returns>The command result, naming the bundle folder that was written.</returns>
	// ReadOnly=false even though nothing on the ENVIRONMENT is touched: the command always writes a bundle
	// folder on the local disk, and the durable-invocation gate keys silent execution on ReadOnlyHint. This
	// matches get-schema / get-client-unit-schema / get-page, which read remotely and write locally too.
	[McpServerTool(Name = ExportSchemaToolName, ReadOnly = false, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("""
				 Exports a SINGLE schema from a Creatio environment into a bundle folder that
				 `import-schema` can apply to another environment.

				 Use this instead of `pull-pkg` when the change is confined to one schema: a package
				 carries every customization in it, so installing one to deliver a one-schema fix can
				 overwrite unrelated work that exists only on the target.

				 Works for every schema kind the platform can export, including addons (business rules,
				 related pages) which have no other read surface.

				 A schema name is unique only per package AND schema manager, so a name that matches more
				 than one layer is REFUSED with every candidate listed as `'package' (manager)`. Re-run with
				 `package-name` when the candidates differ by package, or with `manager-name` when they all
				 live in the same package — the refusal message names the one that applies. Nothing on the
				 ENVIRONMENT is changed, but the command does write a bundle folder on the local disk, which
				 is why it is not annotated as read-only.

				 Requires cliogate 2.0.0.46 or newer on the environment.
				 """)]
	public CommandExecutionResult ExportSchema(
		[Description("Export schema parameters")] [Required] ExportSchemaArgs args
	) {
		ExportSchemaOptions options = new() {
			SchemaName = args.SchemaName,
			PackageName = args.PackageName,
			ManagerName = args.ManagerName,
			Destination = args.Destination,
			Environment = args.EnvironmentName
		};
		return InternalExecute<ExportSchemaCommand>(options);
	}
}

/// <summary>
/// Arguments of the <c>export-schema</c> MCP tool.
/// </summary>
public record ExportSchemaArgs(
	[property: JsonPropertyName("schema-name")]
	[Description("Name of the schema to export")]
	[Required]
	string SchemaName,

	[property: JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[Description("Package that owns the schema layer to export. Required when the name exists in several packages.")]
	string PackageName = null,

	[property: JsonPropertyName("manager-name")]
	[Description("Schema manager to narrow the lookup to, for example AddonSchemaManager")]
	string ManagerName = null,

	[property: JsonPropertyName("destination")]
	[Description("Directory that will receive the bundle folder. Default: the workspace root of the current directory, or the current directory itself when there is no workspace above it.")]
	string Destination = null
);
