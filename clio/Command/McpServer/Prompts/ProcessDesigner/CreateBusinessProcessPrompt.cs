using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt helpers for building a business process on a Creatio environment through MCP.
/// </summary>
[McpServerPromptType, Description("Prompts to build a business process from a declarative descriptor")]
[FeatureToggle("process-designer")]
public static class CreateBusinessProcessPrompt {

	/// <summary>
	/// Builds a prompt that creates a business process from an inline JSON descriptor.
	/// </summary>
	[McpServerPrompt(Name = "create-business-process"),
	 Description("Prompt to build a business process from a declarative JSON descriptor")]
	public static string PromptByEnvironmentName(
		[Required] [Description("The name of the target environment")]
		string environmentName,
		[Description("Optional target package name (overrides the descriptor's packageName)")]
		string packageName = null) =>
		$"""
		 Build a business process on Creatio environment `{environmentName}` using the
		 `create-business-process` tool. Supply a JSON descriptor object with these fields:
		 - `name` (unique schema code), `caption`, `packageName`{(string.IsNullOrWhiteSpace(packageName) ? "" : $" (override: `{packageName}`)")}
		 - `elements`: array of items with `id`, `type`, `caption`, optional `userTaskName`; `type` is
		   `startEvent` | `endEvent` | `userTask` (aliases `readData`, `performTask`).
		 - `flows`: array of `source`/`target` pairs referencing element ids (start → tasks → end).
		 - `parameters`: array of `name`/`type`/`direction`/`caption` (process inputs / variables).
		 - `mappings`: array binding `elementId` + `elementParameter` to one of
		   `processParameter` (bind to a process parameter), `value` (constant), or `expression` (formula).
		 - For a record trigger use a `signalStart` element (`signal` = entity + `on`: added|modified|deleted);
		   restrict it to matching records with a `filter` (object + conditions of column/comparison/value, where
		   column is the entity column name and may be a lookup dot-path; the server serializes the platform
		   filter — never hand-write filter JSON).
		 First call `list-user-tasks` for `{environmentName}` to discover valid `userTaskName` values, then
		 confirm the target package with the user before building.
		 """;
}
