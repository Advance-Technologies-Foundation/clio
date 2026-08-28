using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt that orients the agent to read an existing process and explain it in plain language using the
/// shared <c>process-modeling</c> guidance vocabulary.
/// </summary>
[McpServerPromptType]
[Description("Guides the agent to read an existing Creatio process with describe-business-process, then narrate what it does using the process-modeling guidance.")]
[FeatureToggle("process-designer")]
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
		   `flows` (source, target, kind), and process `parameters` — not raw metadata.
		2. Call `get-guidance` name `process-modeling` for the element catalog + connection-rule vocabulary.
		3. Narrate, in plain language, the trigger (start event), the ordered steps (follow the flows by
		   source/target), each activity's purpose, and any branches (gateways / conditional flows).
		An `openEditPage` block tells you which page the step opens, for which object and record type, whether the
		user ADDS a record (with the values pre-filled for them) or EDITS an existing one (and which), the
		recommendation shown on the page, and when the step counts as complete. Narrate it in those terms rather than
		by parameter name. One read-back caveat worth stating if you see it: the block can report pre-filled values
		AND a record together, because the runtime applies stored values in either mode — that combination cannot be
		written through the tool, so flag it as something a human configured by hand.
		Note: expressions (mapping formulas, filters) are returned RAW, not decoded into semantics — narrate
		structure, types, flow, and parameter sources; where a condition/filter is not decodable, say so
		explicitly instead of guessing.
		""";
}
