using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP surface of <c>import-schema</c>.
/// </summary>
[McpServerToolType]
public class ImportSchemaTool(
	ImportSchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<ImportSchemaOptions>(command, logger, commandResolver) {

	internal const string ImportSchemaToolName = "import-schema";

	/// <summary>
	/// Writes a schema bundle into a package of a Creatio environment.
	/// </summary>
	/// <param name="args">Import parameters.</param>
	/// <returns>The command result, naming the action taken.</returns>
	[McpServerTool(Name = ImportSchemaToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Imports a schema bundle produced by `export-schema` into a package of a Creatio
				 environment, creating or REPLACING exactly one schema.

				 The schema keeps its original UId, so the target holds the same schema rather than a
				 divergent copy.

				 Safety:
				 - `dry-run: true` reports what would happen (create / replace / new layer) and writes
				 nothing. Prefer it first on any environment you care about.
				 - When the schema name is already owned by a DIFFERENT package, the import is refused and
				 names that package, because creating a second layer is sometimes intended and sometimes the
				 duplicate-key defect this feature exists to avoid. Pass `allow-new-layer: true` to do it
				 deliberately.

				 The schema is saved but not built: run `compile-configuration` when it carries source
				 code, and `update-db-structure` when it changes the database structure.

				 Requires cliogate 2.0.0.46 or newer on the environment.
				 """)]
	public CommandExecutionResult ImportSchema(
		[Description("Import schema parameters")] [Required] ImportSchemaArgs args
	) {
		ImportSchemaOptions options = new() {
			Path = args.Path,
			PackageName = args.PackageName,
			DryRun = args.DryRun ?? false,
			AllowNewLayer = args.AllowNewLayer ?? false,
			Environment = args.EnvironmentName
		};
		return InternalExecute<ImportSchemaCommand>(options);
	}
}

/// <summary>
/// Arguments of the <c>import-schema</c> MCP tool.
/// </summary>
public record ImportSchemaArgs(
	[property: JsonPropertyName("path")]
	[Description("Bundle folder produced by export-schema, or its schema-data.json")]
	[Required]
	string Path,

	[property: JsonPropertyName("package-name")]
	[Description("Target package that will own the imported schema")]
	[Required]
	string PackageName,

	[property: JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("dry-run")]
	[Description("Report the planned action (create / replace / new layer) and write nothing. Default: false")]
	bool? DryRun = null,

	[property: JsonPropertyName("allow-new-layer")]
	[Description("Proceed when the schema name is already owned by a different package. Default: false")]
	bool? AllowNewLayer = null
);
