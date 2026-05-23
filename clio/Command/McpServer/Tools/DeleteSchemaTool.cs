using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class DeleteSchemaTool(
	DeleteSchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver) : BaseTool<DeleteSchemaOptions>(command, logger, commandResolver) {

	internal const string DeleteSchemaToolName = "delete-schema";
	[McpServerTool(Name = DeleteSchemaToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("""
				 Deletes a schema from a Creatio environment.

				 Two modes:
				 - workspace (default): schema must belong to a package listed in the supplied workspace path.
				   Uses WorkspaceExplorerService.svc/GetWorkspaceItems + Delete.
				 - remote (`remote: true`): deletes the schema directly from the target environment by name,
				   bypassing workspace validation. Supply only `schema-name` and `environment-name`.

				 Supports any workspace item type, including entity, client unit, source code, process,
				 DCM, process user task, campaign, service, addon, Copilot intent, localization schemas,
				 as well as SQL scripts, data bindings, and assemblies.

				 This operation is destructive and cannot be undone.
				 """)]
	public CommandExecutionResult DeleteSchema(
		[Description("Delete schema parameters")] [Required] DeleteSchemaArgs args
	) {
		DeleteSchemaOptions options = new() {
			SchemaName = args.SchemaName,
			Environment = args.EnvironmentName,
			WorkspacePath = args.WorkspacePath,
			Remote = args.Remote ?? false
		};
		return InternalExecute<DeleteSchemaCommand>(options);
	}
}

public record DeleteSchemaArgs(
	[property:JsonPropertyName("schema-name")]
	[Description("Schema name to delete")]
	[Required]
	string SchemaName,

	[property:JsonPropertyName("environment-name")]
	[Description("Creatio environment name")]
	[Required]
	string EnvironmentName,

	[property:JsonPropertyName("workspace-path")]
	[Description("Absolute path to the local workspace that owns the schema. Required when `remote` is false.")]
	string WorkspacePath,

	[property:JsonPropertyName("remote")]
	[Description("If true, delete the schema directly from the remote environment by name, no workspace required. Default: false")]
	bool? Remote
);
