using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt that orients the agent to design a Creatio business process safely: learn the notation,
/// validate the planned graph, then build it with create-business-process.
/// </summary>
[McpServerPromptType]
[Description("Guides the agent to read the process-modeling guidance, validate a planned process graph with validate-process-graph, and only then build it with create-business-process.")]
[FeatureToggle("process-designer")]
public static class ValidateProcessGraphPrompt {

	/// <summary>
	/// Returns guidance steering the agent through the validate-then-drive process-design flow.
	/// </summary>
	/// <param name="goal">The plain-language automation the user described.</param>
	/// <returns>The prompt text.</returns>
	[McpServerPrompt(Name = "validate-process-graph")]
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
		4. Only after a clean validation, build the process with `create-business-process` (or edit an
		   existing one with `modify-business-process`) — clio builds and saves it server-side in one call.
		   Then verify with `describe-business-process`.
		Note: a clean validation does NOT mean every node is buildable — the rules cover the full BPMN
		catalog, but only startEvent/signalStart/endEvent/userTask + plain sequence flows can be built
		today (see the buildable slice in the guidance). Tell the user when the validated design needs
		elements the builder cannot create yet.
		""";
}
