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

	[McpServerTool(Name = CreateDataBindingDbToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates a DB-first package data binding by saving data directly to the remote Creatio database.")]
	public CommandExecutionResult CreateDataBindingDb(
		[Description("Parameters: environment-name, package-name, schema-name (all required); binding-name, rows (optional)")]
		[Required]
		CreateDataBindingDbArgs args) {
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
	internal const string UpsertDataBindingRowDbToolName = "upsert-data-binding-row-db";

	[McpServerTool(Name = UpsertDataBindingRowDbToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Upserts a single row in a remote DB-first data binding.")]
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
	internal const string RemoveDataBindingRowDbToolName = "remove-data-binding-row-db";

	[McpServerTool(Name = RemoveDataBindingRowDbToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Removes a row from a remote DB-first data binding by primary-key value, and deletes the package schema data record when no bound rows remain.")]
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
///     MCP surface for <see cref="ReadDataBindingDbCommand" />: reports which columns a DB-first binding ships.
/// </summary>
public class ReadDataBindingDbTool(
	ReadDataBindingDbCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ReadDataBindingDbOptions>(command, logger, commandResolver) {

	internal const string ReadDataBindingDbToolName = "read-data-binding-db";

	/// <summary>
	///     Reads a binding's shipped column set from the environment.
	/// </summary>
	[McpServerTool(Name = ReadDataBindingDbToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description(
		"Reports what a DB-first package data binding ACTUALLY ships: its entity schema, row count, the exact set "
		+ "of bound columns, and each row's values. A binding ships only the columns it was created with, and that "
		+ "projection is the transfer contract — reading the LIVE record proves nothing about it, so this is the "
		+ "check that matters before calling a navigation or seed-data change done. Use it instead of exporting the "
		+ "package and parsing Data/<binding>/data.json. Note it lists localizable columns (for example a workplace "
		+ "Name) inline, whereas the package export splits them into a Localization folder — same binding, one list.")]
	public CommandExecutionResult ReadDataBindingDb(
		[Description("Parameters: environment-name, package-name, binding-name (all required)")]
		[Required]
		ReadDataBindingDbArgs args){
		ReadDataBindingDbOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			BindingName = args.BindingName
		};
		return InternalExecute<ReadDataBindingDbCommand>(options);
	}

}

/// <summary>
///     Arguments for the <c>read-data-binding-db</c> MCP tool.
/// </summary>
public sealed record ReadDataBindingDbArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the remote environment")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Binding folder name, i.e. the SysPackageSchemaData.Name")]
	[property: Required]
	string BindingName
);

/// <summary>
/// Arguments for the <c>create-data-binding-db</c> MCP tool.
/// </summary>
public sealed record CreateDataBindingDbArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
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
);

/// <summary>
/// Arguments for the <c>upsert-data-binding-row-db</c> MCP tool.
/// </summary>
public sealed record UpsertDataBindingRowDbArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description(McpToolDescriptions.EnvironmentName)]
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
	[property: Description(McpToolDescriptions.EnvironmentName)]
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
