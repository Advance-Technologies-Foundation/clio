using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for creating package data bindings from runtime schemas.
/// </summary>
public class CreateDataBindingTool(
	CreateDataBindingCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateDataBindingOptions>(command, logger, commandResolver) {
	internal const string CreateDataBindingToolName = "create-data-binding";

	[McpServerTool(Name = CreateDataBindingToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Creates or regenerates a package data binding from a runtime entity schema.")]
	public CommandExecutionResult CreateDataBinding(
		[Description("create-data-binding parameters")]
		[Required]
		CreateDataBindingArgs args) {
		CreateDataBindingOptions options = new() {
			Environment = args.EnvironmentName,
			PackageName = args.PackageName,
			SchemaName = args.SchemaName,
			BindingName = args.BindingName,
			InstallType = args.InstallType ?? 0,
			ValuesJson = args.ValuesJson,
			LocalizationsJson = args.LocalizationsJson,
			WorkspacePath = DataBindingToolPathValidator.ValidateWorkspacePath(args.WorkspacePath)
		};
		return InternalExecute<CreateDataBindingCommand>(options);
	}
}

/// <summary>
/// MCP tool for adding or replacing a row inside an existing binding.
/// </summary>
public class AddDataBindingRowTool(AddDataBindingRowCommand command, ILogger logger)
	: BaseTool<AddDataBindingRowOptions>(command, logger) {
	internal const string AddDataBindingRowToolName = "add-data-binding-row";

	[McpServerTool(Name = AddDataBindingRowToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Adds or replaces a row in an existing local package data binding.")]
	public CommandExecutionResult AddDataBindingRow(
		[Description("add-data-binding-row parameters")]
		[Required]
		AddDataBindingRowArgs args) {
		AddDataBindingRowOptions options = new() {
			PackageName = args.PackageName,
			BindingName = args.BindingName,
			ValuesJson = args.ValuesJson,
			LocalizationsJson = args.LocalizationsJson,
			WorkspacePath = DataBindingToolPathValidator.ValidateWorkspacePath(args.WorkspacePath)
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// MCP tool for removing a row from an existing binding.
/// </summary>
public class RemoveDataBindingRowTool(RemoveDataBindingRowCommand command, ILogger logger)
	: BaseTool<RemoveDataBindingRowOptions>(command, logger) {
	internal const string RemoveDataBindingRowToolName = "remove-data-binding-row";

	[McpServerTool(Name = RemoveDataBindingRowToolName, ReadOnly = false, Destructive = true, Idempotent = false,
		OpenWorld = false)]
	[Description("Removes a row from an existing local package data binding by primary-key value.")]
	public CommandExecutionResult RemoveDataBindingRow(
		[Description("remove-data-binding-row parameters")]
		[Required]
		RemoveDataBindingRowArgs args) {
		RemoveDataBindingRowOptions options = new() {
			PackageName = args.PackageName,
			BindingName = args.BindingName,
			KeyValue = args.KeyValue,
			WorkspacePath = DataBindingToolPathValidator.ValidateWorkspacePath(args.WorkspacePath)
		};
		return InternalExecute(options);
	}
}

internal static class DataBindingToolPathValidator {
	internal static string ValidateWorkspacePath(string workspacePath) {
		if (string.IsNullOrWhiteSpace(workspacePath)) {
			throw new InvalidOperationException("workspace-path is required.");
		}

		if (!Path.IsPathRooted(workspacePath)) {
			throw new InvalidOperationException($"Workspace path must be absolute: {workspacePath}");
		}

		if (workspacePath.StartsWith(@"\\", StringComparison.Ordinal)) {
			throw new InvalidOperationException($"Workspace path must be a local absolute path: {workspacePath}");
		}

		string fullPath = Path.GetFullPath(workspacePath);
		if (!Directory.Exists(fullPath)) {
			throw new InvalidOperationException($"Workspace path not found: {fullPath}");
		}

		return fullPath;
	}
}

/// <summary>
/// Arguments for the <c>create-data-binding</c> MCP tool.
/// </summary>
public sealed record CreateDataBindingArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name used to fetch the runtime entity schema")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name inside the workspace")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("schema-name")]
	[property: Description("Entity schema name used to build the binding descriptor")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("workspace-path")]
	[property: Description("Absolute path to the local workspace")]
	[property: Required]
	string WorkspacePath,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Optional binding folder name; defaults to <schema>")]
	string? BindingName = null,

	[property: JsonPropertyName("install-type")]
	[property: Description("Optional descriptor install type; defaults to 0")]
	int? InstallType = null,

	[property: JsonPropertyName("values")]
	[property: Description("Optional JSON object keyed by column name for the initial row. If the GUID primary key is omitted or null, create-data-binding generates it automatically.")]
	string? ValuesJson = null,

	[property: JsonPropertyName("localizations")]
	[property: Description("Optional JSON object keyed by culture then column name")]
	string? LocalizationsJson = null
);

/// <summary>
/// Arguments for the <c>add-data-binding-row</c> MCP tool.
/// </summary>
public sealed record AddDataBindingRowArgs(
	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name inside the workspace")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Binding folder name under package Data")]
	[property: Required]
	string BindingName,

	[property: JsonPropertyName("workspace-path")]
	[property: Description("Absolute path to the local workspace")]
	[property: Required]
	string WorkspacePath,

	[property: JsonPropertyName("values")]
	[property: Description("JSON object keyed by column name for the row to add or replace. If the GUID primary key is omitted or null, add-data-binding-row generates it automatically.")]
	[property: Required]
	string ValuesJson,

	[property: JsonPropertyName("localizations")]
	[property: Description("Optional JSON object keyed by culture then column name")]
	string? LocalizationsJson = null
);

/// <summary>
/// Arguments for the <c>remove-data-binding-row</c> MCP tool.
/// </summary>
public sealed record RemoveDataBindingRowArgs(
	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name inside the workspace")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Binding folder name under package Data")]
	[property: Required]
	string BindingName,

	[property: JsonPropertyName("workspace-path")]
	[property: Description("Absolute path to the local workspace")]
	[property: Required]
	string WorkspacePath,

	[property: JsonPropertyName("key-value")]
	[property: Description("Primary-key value of the row to remove")]
	[property: Required]
	string KeyValue
);
