using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt that orients the agent to design a Creatio business process safely: learn the notation,
/// validate the planned graph, then build it with create-business-process.
/// </summary>
[McpServerPromptType]
[Description("Guides the agent to read the process-modeling guidance, validate a planned process graph with validate-process-graph, and only then build it with create-business-process.")]
public static class ValidateProcessGraphPrompt {

	/// <summary>
	/// Returns guidance steering the agent through the validate-then-drive process-design flow.
	/// </summary>
	/// <param name="goal">The plain-language automation the user described.</param>
	/// <returns>The prompt text.</returns>
	[McpServerPrompt(Name = "validate-process-graph")]
	// The buildable slice is deliberately NOT restated in the prompt text below: one list, in
	// ValidateProcessGraphTool's own [Description]. A second copy is what went stale - this prompt told
	// the agent to warn about designs "the builder cannot create yet" while the list it warned from was
	// two element kinds short.
	[Description("Returns the canonical validate-then-drive flow for designing a Creatio business process from a plain-language goal.")]
	public static string ProcessDesignGuidance(
		[Description("The plain-language automation the user wants (e.g. 'when a contact is added, read it and send an email').")]
		string goal = null) =>
		$"""
		Design a Creatio business process for: {goal ?? "<describe the automation>"}.

		clio makes no LLM call — you own the intent->BPMN translation. Follow this flow:
		1. Call `get-guidance` with name `process-modeling` to load the element catalog, the connection
		   rules (R1-R20), parameters/mapping/formulas, and the supported slice.
		2. Translate the goal into a graph: a start event (several are allowed — one per trigger the
		   process must react to), the activities, the sequence/conditional
		   flows, and an end event. Use the catalog `data-id` strings for node types
		   (e.g. startEvent, readDataUserTask, exclusiveGateway, endEvent).
		3. Call `validate-process-graph` with your planned `nodes` and `edges`. Resolve every
		   `error`-severity finding before building. Most `warning` findings are advisory - but one is not:
		   an R13 warning about a conditional flow that carries no condition names a refusal the build path
		   makes every time, so give that flow a condition (or make it `sequence`) before going on - UNLESS
		   the branch is decided by an activity RESULT, where neither fix is wanted: a formula is
		   unmaintainable on such a source and `sequence` deletes the branch. Declare the selection as the
		   edge's `results` instead and the warning goes away. Treating
		   it as optional buys a failed `create-business-process` one round trip later.
		4. Only after a clean validation, build the process with `create-business-process` (or edit an
		   existing one with `modify-business-process`) — clio builds and saves it server-side in one call.
		   Then verify with `describe-business-process`.
		Note: a clean validation does NOT mean every node is buildable — the rules cover the full BPMN
		catalog, while the builder creates the slice `validate-process-graph`'s own tool description
		publishes, joined by all three flow kinds declaratively (`flows[].kind` with
		`flows[].condition`). Still out of reach:
		inclusiveGateway, eventBasedGateway, timer/message starts, intermediate events, and formula and
		script tasks. Branching on an activity RESULT is NOT on that list any more: it is declared as the
		edge's `results`, exactly as step 3 above says, and a formula on such a connector is refused by
		the build. The Sub-process element
		(`callActivity` here, `subProcess` in a descriptor) IS buildable; the EVENT and EXPANDED
		sub-processes are not, and neither is one that runs the called process once per item of a
		collection. R16 - the called process must begin with a Simple start event - cannot be checked
		here either, because a planned graph carries no reference to that process; the build path
		refuses it, and `execute-esq` over `VwProcessLib` answers it in advance through
		`HasStartEvent`. Check the buildable slice in
		`get-guidance name=process-modeling` before promising a build, and tell the user when the
		validated design needs elements the builder cannot create yet.
		Two rules worth carrying into the design rather than discovering at build time: out of a
		gateway that CHOOSES, every outgoing flow must be conditional (with a condition) or default,
		and there is at most one default per element — and flow ORDER is branch precedence, since the
		runtime takes the first condition that evaluates true and nothing else encodes which that is.
		""";
}
