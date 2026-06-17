using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt that orients the agent to design a Creatio business process safely: learn the notation,
/// validate the planned graph, then drive the designer.
/// </summary>
[McpServerPromptType]
[Description("Guides the agent to read the process-modeling guidance, validate a planned process graph with validate-process-graph, and only then drive the designer.")]
[FeatureToggle("process-designer")]
public static class ValidateProcessGraphPrompt {

	/// <summary>
	/// Returns guidance steering the agent through the validate-then-drive process-design flow.
	/// </summary>
	/// <param name="goal">The plain-language automation the user described.</param>
	/// <returns>The prompt text.</returns>
	[McpServerPrompt(Name = "process-design-guidance")]
	[Description("Returns the canonical validate-then-drive flow for designing a Creatio business process from a plain-language goal.")]
	public static string ProcessDesignGuidance(
		[Description("The plain-language automation the user wants (e.g. 'when a contact is added, read it and send an email').")]
		string goal = null) =>
		$"""
		Design a Creatio business process for: {goal ?? "<describe the automation>"}.

		clio makes no LLM call — you own the intent->BPMN translation. Follow this flow:
		1. Call `get-guidance` with name `process-modeling` to load the element catalog, the connection
		   rules (R1-R17), parameters/mapping/formulas, and the supported slice.
		2. Translate the goal into a graph: one start event, the activities, the sequence/conditional
		   flows, and an end event. Use the catalog `data-id` strings for node types
		   (e.g. startEvent, readDataUserTask, exclusiveGateway, endEvent).
		3. Call `validate-process-graph` with your planned `nodes` and `edges`. Resolve every
		   `error`-severity finding before building; advisory `warning` findings are optional to address.
		4. Only after a clean validation, drive the designer (e.g. `process-add-element`) — and treat the
		   live designer's `.djs-validate-outline` marker as the final authority over the static validator.
		""";
}
