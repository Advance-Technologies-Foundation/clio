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
		 `removeElement`, `addFlow`, `removeFlow`, `addParameter`, `addMapping`, `setParameter`, `removeParameter`,
		 `setFilter`, `clearFilter`, `setSignal`, `setElement`, `setConnections`, or `clearConnections`
		 — plus that op's arguments (the element / parameter / mapping / filter / signal shapes match a build;
		 `setParameter` updates a parameter in place, `removeParameter` is dependency-checked, `setFilter`/`clearFilter`
		 set or remove a `signalStart`'s record filter, `setSignal` reconfigures a `signalStart`'s record trigger
		 and its tracked-change `changedColumns` in place, and `setElement` changes element-level fields such as
		 `useBackgroundMode` in place on any element kind; `setConnections` binds the "Connected to" links of the
		 Activity an element creates and is an UPSERT keyed on `column`, so columns you do not list are left alone,
		 and `clearConnections` unbinds them). Any failed operation aborts the whole edit
		 (nothing is saved). Example — switch a process to start on record save: `removeElement` the start event,
		 `addElement` a `signalStart`, then `addFlow` from it to the first task. Confirm destructive removals
		 (`removeElement` / `removeFlow` / `removeParameter` / `clearConnections`) with the user before proceeding.
		 A SUCCESSFUL edit can still return `warnings` — read them: a connection bound to a column with no
		 connection-registry row is written at run time yet invisible in the designer, and a cleared binding is
		 reported only there because it disappears from `describe-business-process` afterwards.
		 """;
}
