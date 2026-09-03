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
		   (name, uid, caption, type, buildType, userTaskName, parameters; `signal` for a signal start),
		   `flows` (source, target, kind), and process `parameters` — not raw metadata. It also reports the
		   version standing: `version`, `isActiveVersion`, `activeVersionName`, `activeVersionSchemaUId` and
		   the `versions[]` family.
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
		Note: expressions (mapping formulas, filters) are returned RAW, not decoded into semantics — narrate
		structure, types, flow, and parameter sources; where a condition/filter is not decodable, say so
		explicitly instead of guessing.
		""";
}
