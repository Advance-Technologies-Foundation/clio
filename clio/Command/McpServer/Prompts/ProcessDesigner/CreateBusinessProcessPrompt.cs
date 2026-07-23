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
		 Build a business process on Creatio environment `{environmentName}` with the `create-business-process` tool.
		 Steps: (1) call `list-user-tasks` for `{environmentName}` to discover valid `userTaskName` values;
		 (2) read `get-guidance name=process-modeling` for the full descriptor contract — element types, flows,
		 parameters (incl. `typeFromElement` to copy an element parameter's exact type, and a constant `value`
		 default), the `mappings` target/source contract, signal triggers (with `changedColumns` to fire only on
		 specific column changes and a data source `filter` to restrict which records fire one), and the
		 type-compatibility rule;
		 (3) supply a JSON descriptor with `name`
		 (unique schema code), `caption`, `packageName`{(string.IsNullOrWhiteSpace(packageName) ? "" : $" (override: `{packageName}`)")} and the `elements` / `flows` / `parameters` / `mappings` arrays.
		 To run the process when a record is added/changed/deleted, use a `signalStart` element (the platform-native
		 trigger), not a page save handler; add `changedColumns` to fire an `on:modified` trigger only when specific
		 columns change, and/or a `filter` to fire only for matching records. Confirm the target package with the
		 user before building.
		 """;
}
