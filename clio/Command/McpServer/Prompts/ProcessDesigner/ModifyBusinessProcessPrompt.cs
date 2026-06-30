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
		 names. Then supply a JSON `operations` array; each operation is an object with an `op`:
		 - `addElement` — `element` descriptor (`name` (the element handle/local code), `type`, `caption`,
		   optional `userTaskName`, optional `signal` for a `signalStart`). `type` is
		   `startEvent` | `signalStart` | `endEvent` | `userTask` (aliases `readData`, `performTask`).
		 - `removeElement` — `elementName` (the element's local name or UId); its sequence flows are removed too.
		 - `addFlow` / `removeFlow` — `source` and `target` element names.
		 - `addParameter` — `parameter` (a process-level parameter: `name`, `type` e.g. `Text`/`Integer`/`Guid`,
		   optional `direction` In/Out/Variable/Internal, optional `caption`; or `referenceSchema` = an object name
		   such as `City` to create a Lookup to that object; or `typeFromElement` + `typeFromElementParameter` to copy
		   an element parameter's exact type).
		 - `addMapping` — `mapping` — target `elementName` + `elementParameter` or `targetProcessParameter`; source is exactly one of
		   `sourceElement` + `sourceElementParameter` | `processParameter` |
		   `value` | `expression`. Parameter-to-parameter mappings require compatible types (same type group).
		 Operations apply in order; any failure aborts the edit (nothing is saved). Example — switch a process to
		 start on record save: `removeElement` the start event, `addElement` a `signalStart`, then `addFlow` from it
		 to the first task. Confirm destructive removals with the user before proceeding.
		 """;
}
