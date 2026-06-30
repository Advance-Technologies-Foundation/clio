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
		 - `parameters`: array of `name`/`type`/`direction`/`caption` (process inputs / variables; or
		   `typeFromElement` + `typeFromElementParameter` to copy an element parameter's exact type — e.g. to expose a
		   user-task output for mapping with no conversion).
		 - `mappings`: array binding a target to a source. Target: `elementName` + `elementParameter` (an element input) or
		   `targetProcessParameter` (a process parameter, e.g. expose an element output as a process output). Source — exactly one of
		   `sourceElement` + `sourceElementParameter` (another element's output), `processParameter`, `value`
		   (constant), or `expression` (formula). Parameter-to-parameter mappings require compatible types
		   (same type group — for a lookup, the same reference object).
		 First call `list-user-tasks` for `{environmentName}` to discover valid `userTaskName` values, then
		 confirm the target package with the user before building.
		 """;
}
