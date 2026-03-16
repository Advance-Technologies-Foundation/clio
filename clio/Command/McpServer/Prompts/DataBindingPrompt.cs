using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command.McpServer.Tools;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for data-binding MCP tools.
/// </summary>
[McpServerPromptType, Description("Prompts for creating and mutating package data bindings")]
public static class DataBindingPrompt {
	/// <summary>
	/// Builds a prompt for creating or regenerating a data binding.
	/// </summary>
	[McpServerPrompt(Name = CreateDataBindingTool.CreateDataBindingToolName), Description("Prompt to create a package data binding")]
	public static string CreateDataBinding(
		[Required] [Description("Target package name")] string packageName,
		[Required] [Description("Entity schema name")] string schemaName,
		[Required] [Description("Absolute workspace path")] string workspacePath,
		[Description("Optional binding folder name")] string? bindingName = null,
		[Description("Optional descriptor install type")] int installType = 0,
		[Description("Optional values JSON")] string? values = null,
		[Description("Optional localizations JSON")] string? localizations = null,
		[Description("Optional Creatio environment name for non-templated schemas")] string? environmentName = null) =>
		$"""
		 Use clio mcp server `{CreateDataBindingTool.CreateDataBindingToolName}` to create or regenerate a data binding
		 for schema `{schemaName}` in package `{packageName}`.
		 Pass `workspace-path` `{workspacePath}` exactly as provided.
		 If `{schemaName}` is covered by a built-in offline template such as `SysSettings` or `SysModule`, omit `environment-name`.
		 Otherwise pass `environment-name` `{environmentName ?? "<required for non-templated schema>"}` exactly as provided.
		 Use `binding-name` `{bindingName ?? "<default>"}` and `install-type` `{installType}`.
		 Use `values` `{values ?? "<not provided>"}` when an initial row should be created. If that payload omits the
		 GUID primary key column or sets it to null, the tool generates it automatically. For image-content columns,
		 `values` may contain either an existing base64 string or a local file path inside the workspace that clio
		 will encode. For `SysModule.IconBackground`, only the predefined 16-color palette is allowed. Use
		 `localizations` `{localizations ?? "<not provided>"}` when localized row values must be written too.
		 """;

	/// <summary>
	/// Builds a prompt for adding or replacing a data-binding row.
	/// </summary>
	[McpServerPrompt(Name = AddDataBindingRowTool.AddDataBindingRowToolName), Description("Prompt to add or replace a data-binding row")]
	public static string AddDataBindingRow(
		[Required] [Description("Target package name")] string packageName,
		[Required] [Description("Binding folder name")] string bindingName,
		[Required] [Description("Absolute workspace path")] string workspacePath,
		[Required] [Description("Row values JSON")] string values,
		[Description("Optional localizations JSON")] string? localizations = null) =>
		$"""
		 Use clio mcp server `{AddDataBindingRowTool.AddDataBindingRowToolName}` to add or replace a row in binding
		 `{bindingName}` under package `{packageName}`.
		 Pass `workspace-path` `{workspacePath}` exactly as provided and use `values` `{values}`.
		 If that payload omits the GUID primary key column or sets it to null, the tool generates it automatically.
		 For image-content columns, `values` may contain either an existing base64 string or a local file path inside
		 the workspace that clio will encode. For `SysModule.IconBackground`, only the predefined 16-color palette
		 is allowed.
		 Use `localizations` `{localizations ?? "<not provided>"}` only when localized row values must also be updated.
		 """;

	/// <summary>
	/// Builds a prompt for removing a data-binding row.
	/// </summary>
	[McpServerPrompt(Name = RemoveDataBindingRowTool.RemoveDataBindingRowToolName), Description("Prompt to remove a data-binding row")]
	public static string RemoveDataBindingRow(
		[Required] [Description("Target package name")] string packageName,
		[Required] [Description("Binding folder name")] string bindingName,
		[Required] [Description("Absolute workspace path")] string workspacePath,
		[Required] [Description("Primary-key value")] string keyValue) =>
		$"""
		 Use clio mcp server `{RemoveDataBindingRowTool.RemoveDataBindingRowToolName}` to remove the row with primary key
		 `{keyValue}` from binding `{bindingName}` under package `{packageName}`.
		 Pass `workspace-path` `{workspacePath}` exactly as provided.
		 """;
}
