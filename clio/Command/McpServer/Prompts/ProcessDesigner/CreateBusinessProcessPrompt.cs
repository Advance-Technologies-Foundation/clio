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
		 - `elements`: array of items with `name` (the element handle/local code), `type`, `caption`,
		   optional `userTaskName`; `type` is `startEvent` | `signalStart` | `endEvent` | `userTask`
		   (aliases `readData`, `performTask`).
		   For `signalStart` add a `signal` object with `entity` (the object name) and `on` =
		   `added` | `modified` | `deleted` — the platform-native "run a process when a record is
		   saved/added/deleted" trigger.
		 - `flows`: array of `source`/`target` pairs referencing element names (start → tasks → end).
		 - `parameters`: array of `name`/`type`/`direction`/`caption`/`description` (process inputs / variables), each with an optional `value` (a constant default).
		 - `mappings`: array binding `elementName` + `elementParameter` to one of
		   `processParameter` (bind to a process parameter), `value` (constant), or `expression` (formula).
		 First call `list-user-tasks` for `{environmentName}` to discover valid `userTaskName` values, then
		 confirm the target package with the user before building.
		 """;
}
