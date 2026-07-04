using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt helpers for editing an existing business process on a Creatio environment through MCP.
/// </summary>
[McpServerPromptType, Description("Prompts to edit an existing business process by applying operations")]
[FeatureToggle("process-designer")]
public static class ModifyBusinessProcessPrompt {

	/// <summary>
	/// Builds a prompt that edits an existing process by applying a list of operations.
	/// </summary>
	[McpServerPrompt(Name = "modify-business-process"),
	 Description("Prompt to edit an existing business process by applying a list of operations")]
	public static string PromptByProcess(
		[Required] [Description("The name of the target environment")]
		string environmentName,
		[Required] [Description("Process code (schema Name) or UId to edit")]
		string process) =>
		$"""
		 Edit the existing business process `{process}` on Creatio environment `{environmentName}` with the
		 `modify-business-process` tool. Steps: (1) call `describe-business-process` to inspect the current elements
		 and their names; (2) read `get-guidance name=process-modeling` for the operation and field contract;
		 (3) supply a JSON `operations` array (applied in order) — each item has an `op`: `addElement`,
		 `removeElement`, `addFlow`, `removeFlow`, `addParameter`, `addMapping`, `setParameter`, or `removeParameter`
		 — plus that op's arguments (the element / parameter / mapping shapes match a build; `setParameter` updates a
		 parameter in place and `removeParameter` is dependency-checked). Any failed operation aborts the whole edit
		 (nothing is saved). Example — switch a process to start on record save: `removeElement` the start event,
		 `addElement` a `signalStart`, then `addFlow` from it to the first task. Confirm destructive removals
		 (`removeElement` / `removeFlow` / `removeParameter`) with the user before proceeding.
		 """;
}
