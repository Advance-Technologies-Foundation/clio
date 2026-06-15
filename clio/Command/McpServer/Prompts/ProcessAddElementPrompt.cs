using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt that orients the agent to build a Read data process step: validate first, then drive the
/// designer, then read the result back.
/// </summary>
[McpServerPromptType]
[Description("Guides the agent to validate a planned graph and then drive the designer with process-add-element to add a Read data element, then read it back.")]
public static class ProcessAddElementPrompt {

	/// <summary>
	/// Returns guidance for the validate-then-build-then-readback flow.
	/// </summary>
	/// <param name="readObject">The object the Read data element should read.</param>
	/// <param name="environmentName">The registered environment to build in.</param>
	/// <returns>The prompt text.</returns>
	[McpServerPrompt(Name = "process-add-element-guidance")]
	[Description("Returns the validate-then-build-then-readback flow for adding a Read data element to a process.")]
	public static string ProcessAddElementGuidance(
		[Description("The object the Read data element should read, e.g. Contact.")]
		string readObject = null,
		[Description("The registered clio environment to build in.")]
		string environmentName = null) =>
		$"""
		Add a Read data element (reading {readObject ?? "<object>"}) to a Creatio process
		on environment {environmentName ?? "<environment-name>"}.

		1. (Optional but recommended) Call `validate-process-graph` for your planned
		   Start -> Read data -> End graph; fix any error findings first.
		2. Call `process-add-element` with `environment-name`, `element-type` = `read-data`,
		   `read-object` (e.g. Contact), and optionally `process-caption` (a deterministic readback
		   handle) / `process-id` (to modify an existing process instead of creating a new one).
		   clio validates, opens the authenticated designer over CDP, appends + configures the element,
		   asserts the connection is valid, and SAVEs — returning success + code + uId + caption.
		3. Read it back with `describe-process` (by `process-caption`) or `generate-process-model`
		   (by `code`) to confirm what was built.
		Requires a forms-auth environment and a local Chromium. On any pre-save failure clio reports a
		specific `Error:` and never claims a false-positive save.
		""";
}
