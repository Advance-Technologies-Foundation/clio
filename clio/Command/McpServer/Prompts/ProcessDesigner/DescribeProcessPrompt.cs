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
		   configuration block for a configured element - `email`, `readData`, `changeData`, `addData`,
		   `openEditPage`), `flows` (name, source, target, kind, and on a branch its `condition` plus
		   `branchesOnActivityResult`), and process `parameters` — not raw metadata. It also reports the
		   version standing: `version`, `isActiveVersion`, `activeVersionName`, `activeVersionSchemaUId` and
		   the `versions[]` family.
		   A `preconfiguredPage` element also carries its `preconfiguredPage` block: the page it shows, its
		   completing `buttons`, and `dataSources[]` — where `parameter` names the element parameter that
		   receives the id of the record the page saved, the handle later steps map from and the only place it
		   is reported. Read `inSync` and `shadowedPageParameters` there too: `inSync: true` means "nothing
		   left to synchronize", not "the element carries every page parameter".
		2. CHECK `isActiveVersion` BEFORE narrating anything. Every saved version is a separate schema with
		   its own name, so resolving by `process-name` returns the family ROOT — and on a versioned process
		   that is NOT what the runtime executes. Three outcomes: true, narrate this graph; false WITH an
		   `activeVersionSchemaUId`, describe again by that UId and narrate THAT graph; false or absent
		   WITHOUT one, there is nothing to redirect to — read `versionReadWarning`, say the version standing
		   is unknown, and do NOT fall back to the graph you hold or assume the process is unversioned. The
		   warning can also arrive beside fields that WERE established, so read it even when values are
		   present.
		3. Call `get-guidance` name `process-modeling` for the element catalog + connection-rule vocabulary.
		4. Narrate, in plain language, the trigger (start event), the ordered steps (follow the flows by
		   source/target), each activity's purpose, and any branches (gateways / conditional flows). State
		   which version you read, and say so explicitly when it is not the active one.
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
		Note: expressions (mapping formulas, filters) are returned RAW, not decoded into semantics — narrate
		structure, types, flow, and parameter sources; where a condition/filter is not decodable, say so
		explicitly instead of guessing.
		""";
}
