using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt that orients the agent to read an existing process and explain it in plain language using the
/// shared <c>process-modeling</c> guidance vocabulary.
/// </summary>
[McpServerPromptType]
[Description("Guides the agent to read an existing Creatio process with describe-business-process, then narrate what it does using the process-modeling guidance.")]
public static class DescribeProcessPrompt {

	/// <summary>
	/// Returns guidance for the read-and-explain flow.
	/// </summary>
	/// <param name="process">The process to explain (code, UId, or caption).</param>
	/// <param name="environmentName">The registered environment to read from.</param>
	/// <returns>The prompt text.</returns>
	[McpServerPrompt(Name = "describe-business-process")]
	[Description("Returns the read-and-explain flow for an existing Creatio process.")]
	public static string DescribeProcessGuidance(
		[Description("The process to explain — its code, UId, or caption.")]
		string process = null,
		[Description("The registered clio environment to read from.")]
		string environmentName = null) =>
		$"""
		Explain what the existing Creatio process {process ?? "<code/uid/caption>"} does
		(environment: {environmentName ?? "<environment-name>"}).

		1. Call `describe-business-process` with `environment-name` and exactly one of `process-name` /
		   `process-uid` / `process-caption`. It returns a STRUCTURED graph: `elements`
		   (name, uid, caption, type, buildType, userTaskName, parameters; `signal` for a signal start, and a
		   configuration block for a configured element - `email`, `readData`, `changeData`, `openEditPage`),
		   `flows` (name, source, target, kind, `label`, and on a branch its `condition` plus
		   `branchesOnActivityResult`), and process `parameters` — not raw metadata.
		   A `preconfiguredPage` element also carries its `preconfiguredPage` block: the page it shows, its
		   completing `buttons`, and `dataSources[]` — where `parameter` names the element parameter that
		   receives the id of the record the page saved, the handle later steps map from and the only place it
		   is reported. Read `inSync` and `shadowedPageParameters` there too: `inSync: true` means "nothing
		   left to synchronize", not "the element carries every page parameter".
		2. Call `get-guidance` name `process-modeling` for the element catalog + connection-rule vocabulary.
		3. Narrate, in plain language, the trigger (start event), the ordered steps (follow the flows by
		   source/target), each activity's purpose, and any branches (gateways / conditional flows).
		An `openEditPage` block tells you which page the step opens, for which object and record type, whether the
		user ADDS a record (with the values pre-filled for them) or EDITS an existing one (and which), the
		recommendation shown on the page, and when the step counts as complete. Narrate it in those terms rather than
		by parameter name. One read-back caveat worth stating if you see it: the block can report pre-filled values
		AND a record together, because the runtime applies stored values in either mode — that combination cannot be
		written through the tool, so flag it as something a human configured by hand.
		Reading a branch takes both fields, and they can disagree. `condition` is the stored expression,
		reported whenever the flow carries condition TEXT — including on a flow whose `kind` is not
		conditional, where the platform drops it at generation time and it never runs. And when
		`branchesOnActivityResult` is true the branch is decided by which BUTTONS the preceding activity was
		completed with, not by the expression: the text is still shown, and the runtime ignores it entirely.
		So never narrate a condition as "what decides this branch" without checking `kind` and
		`branchesOnActivityResult` first — on 337 of the 1 406 conditional flows in the shipped 7.8.0 corpus
		that reading would be wrong.
		`label` is the text the designer draws ON the connector, and it is the wording a human reader of the
		diagram actually sees — so narrate a branch by its label when it has one, and by its condition when it
		does not. It is also the field to READ BEFORE ANY WRITE that touches a flow: a label is a person's own
		wording, most flows that have one were labelled by hand, and a `setFlow` or `addFlow` carrying a
		`label` overwrites it silently. Report the label you found before proposing to change it. An ABSENT
		`label` is genuinely ambiguous and must not be reported as "this flow has no label": the server omits
		the key both when the flow carries none and when the environment's `CrtProcessBuilder` is older than
		1.6.0.8 and cannot report one at all. Distinguish them with `list-packages` before making a claim.
		Note: expressions (mapping formulas, filters) are returned RAW, not decoded into semantics — narrate
		structure, types, flow, and parameter sources; where a condition/filter is not decodable, say so
		explicitly instead of guessing.
		""";
}
