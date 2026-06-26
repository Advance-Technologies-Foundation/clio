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
		 Edit the existing business process `{process}` on Creatio environment `{environmentName}` using the
		 `modify-business-process` tool. First call `describe-business-process` to inspect the current elements and their
		 ids. Then supply a JSON `operations` array; each operation is an object with an `op`:
		 - `addElement` — `element` descriptor (`id`, `type`, `caption`, optional `userTaskName`, optional `signal`
		   for a `signalStart`). `type` is `startEvent` | `signalStart` | `endEvent` | `userTask`
		   (aliases `readData`, `performTask`).
		 - `removeElement` — `elementId` (the element's local id or UId); its sequence flows are removed too.
		 - `addFlow` / `removeFlow` — `source` and `target` element ids.
		 - `addParameter` — `parameter` (a process-level parameter: `name`, `type` e.g. `Text`/`Integer`/`Guid`,
		   optional `direction` In/Out/Variable/Internal, optional `caption`; or `referenceSchema` = an object name
		   such as `City` to create a Lookup to that object).
		 - `addMapping` — `mapping` (`elementId`, `elementParameter`, and exactly one of `processParameter` |
		   `value` | `expression`) to bind an element's input parameter to a value.
		 Operations apply in order; any failure aborts the edit (nothing is saved). Example — switch a process to
		 start on record save: `removeElement` the start event, `addElement` a `signalStart`, then `addFlow` from it
		 to the first task. Confirm destructive removals with the user before proceeding.
		 """;
}
