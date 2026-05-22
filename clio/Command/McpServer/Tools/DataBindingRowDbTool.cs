using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Consolidated MCP tool that upserts or removes a single row in a remote DB-first data binding.
/// Folds the legacy <c>upsert-data-binding-row-db</c> and <c>remove-data-binding-row-db</c> tools.
/// </summary>
[McpServerToolType]
public sealed class DataBindingRowDbTool(
	UpsertDataBindingRowDbTool upsertTool,
	RemoveDataBindingRowDbTool removeTool) {

	internal const string ToolName = "data-binding-row-db";
	internal const string ActionUpsert = "upsert";
	internal const string ActionRemove = "remove";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description("Upserts or removes a single row in a remote DB-first data binding. action='upsert' uses `values`; action='remove' uses `key-value` and also deletes the package schema data record when no bound rows remain.")]
	public CommandExecutionResult Apply(
		[Description("data-binding-row-db parameters")] [Required] DataBindingRowDbArgs args) {
		CommandExecutionResult actionError = CommandExecutionResult.ValidateExactlyOneMode(
			"action", args.Action, ActionUpsert, ActionRemove);
		if (actionError != null) {
			return actionError;
		}

		if (string.Equals(args.Action, ActionUpsert, StringComparison.OrdinalIgnoreCase)) {
			CommandExecutionResult missing = CommandExecutionResult.ValidateRequiredForMode(
				"values", args.ValuesJson, ActionUpsert);
			if (missing != null) {
				return missing;
			}
			return upsertTool.UpsertDataBindingRowDb(new UpsertDataBindingRowDbArgs(
				args.EnvironmentName!,
				args.PackageName!,
				args.BindingName!,
				args.ValuesJson!));
		}

		CommandExecutionResult missingKey = CommandExecutionResult.ValidateRequiredForMode(
			"key-value", args.KeyValue, ActionRemove);
		if (missingKey != null) {
			return missingKey;
		}
		return removeTool.RemoveDataBindingRowDb(new RemoveDataBindingRowDbArgs(
			args.EnvironmentName!,
			args.PackageName!,
			args.BindingName!,
			args.KeyValue!));
	}
}

/// <summary>
/// Arguments for the consolidated <c>data-binding-row-db</c> MCP tool.
/// </summary>
public sealed record DataBindingRowDbArgs(
	[property: JsonPropertyName("action")]
	[property: Description("Discriminator: 'upsert' or 'remove'.")]
	[property: Required]
	string Action,

	[property: JsonPropertyName("environment-name")]
	[property: Description("Creatio environment name.")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name on the remote environment.")]
	[property: Required]
	string PackageName,

	[property: JsonPropertyName("binding-name")]
	[property: Description("Binding folder name.")]
	[property: Required]
	string BindingName,

	[property: JsonPropertyName("values")]
	[property: Description("Required when action='upsert'. Row values as JSON object keyed by column name.")]
	string? ValuesJson = null,

	[property: JsonPropertyName("key-value")]
	[property: Description("Required when action='remove'. Primary-key value of the row to remove.")]
	string? KeyValue = null
);
