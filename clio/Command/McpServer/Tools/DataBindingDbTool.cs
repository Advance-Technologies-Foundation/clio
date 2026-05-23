using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for creating a DB-first package data binding.
/// </summary>
public class CreateDataBindingDbTool(
	CreateDataBindingDbCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateDataBindingDbOptions>(command, logger, commandResolver) {
	internal const string CreateDataBindingDbToolName = "create-data-binding-db";

		[Description("Creates a DB-first package data binding by saving data directly to the remote Creatio database.")]
	public CommandExecutionResult CreateDataBindingDb(
		[Description("Parameters: environment-name, package-name, schema-name (all required); binding-name, rows (optional)")]
		[Required]
		CreateDataBindingDbRunArgs args) {
		CreateDataBindingDbOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			SchemaName = args.SchemaName,
			BindingName = args.BindingName,
			RowsJson = args.RowsJson
		};
		return InternalExecute<CreateDataBindingDbCommand>(options);
	}
}

/// <summary>
/// MCP tool for upserting a single row in a remote DB-first data binding.
/// </summary>
public class UpsertDataBindingRowDbTool(
	UpsertDataBindingRowDbCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<UpsertDataBindingRowDbOptions>(command, logger, commandResolver) {
	/// <summary>Legacy MCP tool name retained for ToolContractGetTool documentation; the entry now lives on <see cref="DataBindingRowDbTool"/>.</summary>
	internal const string UpsertDataBindingRowDbToolName = "upsert-data-binding-row-db";

	public CommandExecutionResult UpsertDataBindingRowDb(
		[Description("Parameters: environment-name, package-name, binding-name, values (all required)")]
		[Required]
		UpsertDataBindingRowDbArgs args) {
		UpsertDataBindingRowDbOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			BindingName = args.BindingName,
			ValuesJson = args.ValuesJson
		};
		return InternalExecute<UpsertDataBindingRowDbCommand>(options);
	}
}

/// <summary>
/// MCP tool for removing a row from a remote DB-first data binding.
/// </summary>
public class RemoveDataBindingRowDbTool(
	RemoveDataBindingRowDbCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<RemoveDataBindingRowDbOptions>(command, logger, commandResolver) {
	/// <summary>Legacy MCP tool name retained for ToolContractGetTool documentation; the entry now lives on <see cref="DataBindingRowDbTool"/>.</summary>
	internal const string RemoveDataBindingRowDbToolName = "remove-data-binding-row-db";

	public CommandExecutionResult RemoveDataBindingRowDb(
		[Description("Parameters: environment-name, package-name, binding-name, key-value (all required)")]
		[Required]
		RemoveDataBindingRowDbArgs args) {
		RemoveDataBindingRowDbOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			BindingName = args.BindingName,
			KeyValue = args.KeyValue
		};
		return InternalExecute<RemoveDataBindingRowDbCommand>(options);
	}
}

/// <summary>
/// Arguments for the <c>create-data-binding-db</c> MCP tool.
/// </summary>
public sealed record CreateDataBindingDbRunArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the remote environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Entity schema name for the binding")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Optional binding folder name; defaults to <schema>")]
	string? BindingName = null,

	[property: JsonPropertyName("rows")]
	[property: Description("Optional JSON array of row objects, each with a 'values' key containing column name-value pairs")]
	string? RowsJson = null
) : ClioRunArgs;

/// <summary>
/// Arguments for the <c>upsert-data-binding-row-db</c> MCP tool.
/// </summary>
public sealed record UpsertDataBindingRowDbArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the remote environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Binding folder name")]
	[property: Required]
	string BindingName,

	[property: JsonPropertyName("values")]
	[property: Description("Row values as JSON object keyed by column name")]
	[property: Required]
	string ValuesJson
);

/// <summary>
/// Arguments for the <c>remove-data-binding-row-db</c> MCP tool.
/// </summary>
public sealed record RemoveDataBindingRowDbArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the remote environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Binding folder name")]
	[property: Required]
	string BindingName,

	[property: JsonPropertyName("key-value")]
	[property: Description("Primary-key value of the row to remove")]
	[property: Required]
	string KeyValue
);
