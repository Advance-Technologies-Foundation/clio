using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that adds, replaces, or removes a row in an existing local package data
/// binding. Folds the legacy <c>add-data-binding-row</c> and <c>remove-data-binding-row</c> tools.
/// </summary>
[McpServerToolType]
public sealed class DataBindingRowTool(
	AddDataBindingRowTool addTool,
	RemoveDataBindingRowTool removeTool) {

	internal const string ToolName = "data-binding-row";
	internal const string ActionAdd = "add";
	internal const string ActionRemove = "remove";

		[Description("Adds, replaces, or removes a row in an existing local package data binding. action='add' uses `values` (+ optional `localizations`); action='remove' uses `key-value`.")]
	public CommandExecutionResult Apply(
		[Description("data-binding-row parameters")] [Required] DataBindingRowRunArgs args) {
		CommandExecutionResult actionError = CommandExecutionResult.ValidateExactlyOneMode(
			"action", args.Action, ActionAdd, ActionRemove);
		if (actionError != null) {
			return actionError;
		}

		if (string.Equals(args.Action, ActionAdd, StringComparison.OrdinalIgnoreCase)) {
			CommandExecutionResult missing = CommandExecutionResult.ValidateRequiredForMode(
				"values", args.ValuesJson, ActionAdd);
			if (missing != null) {
				return missing;
			}
			return addTool.AddDataBindingRow(new AddDataBindingRowArgs(
				args.PackageName!,
				args.BindingName!,
				args.WorkspacePath!,
				args.ValuesJson!,
				args.LocalizationsJson));
		}

		CommandExecutionResult missingKey = CommandExecutionResult.ValidateRequiredForMode(
			"key-value", args.KeyValue, ActionRemove);
		if (missingKey != null) {
			return missingKey;
		}
		return removeTool.RemoveDataBindingRow(new RemoveDataBindingRowArgs(
			args.PackageName!,
			args.BindingName!,
			args.WorkspacePath!,
			args.KeyValue!));
	}
}

/// <summary>
/// Arguments for the consolidated <c>data-binding-row</c> MCP tool.
/// </summary>
public sealed record DataBindingRowRunArgs(
	[property: JsonPropertyName("action")]
	[property: Description("Discriminator: 'add' adds or replaces a row; 'remove' removes a row by primary key.")]
	[property: Required]
	string Action,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name inside the workspace.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Binding folder name under package Data.")]
	[property: Required]
	string BindingName,

	[property: JsonPropertyName("workspace-path")]
	[property: Description("Absolute path to the local workspace.")]
	[property: Required]
	string WorkspacePath,

	[property: JsonPropertyName("values")]
	[property: Description("Required when action='add'. JSON object keyed by column name for the row to add or replace.")]
	string? ValuesJson = null,

	[property: JsonPropertyName("localizations")]
	[property: Description("Optional when action='add'. JSON object keyed by culture then column name.")]
	string? LocalizationsJson = null,

	[property: JsonPropertyName("key-value")]
	[property: Description("Required when action='remove'. Primary-key value of the row to remove.")]
	string? KeyValue = null
) : ClioRunArgs;
