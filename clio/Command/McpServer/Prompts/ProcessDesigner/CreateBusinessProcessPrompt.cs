using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt helpers for building a business process on a Creatio environment through MCP.
/// </summary>
[McpServerPromptType, Description("Prompts to build a business process from a declarative descriptor")]
[FeatureToggle("process-designer")]
public static class CreateBusinessProcessPrompt {

	/// <summary>
	/// Builds a prompt that creates a business process from an inline JSON descriptor.
	/// </summary>
	[McpServerPrompt(Name = "create-business-process"),
	 Description("Prompt to build a business process from a declarative JSON descriptor")]
	public static string PromptByEnvironmentName(
		[Required] [Description("The name of the target environment")]
		string environmentName,
		[Description("Optional target package name (overrides the descriptor's packageName)")]
		string packageName = null) =>
		$"""
		 Build a business process on Creatio environment `{environmentName}` with the `create-business-process` tool.
		 Steps: (1) call `list-user-tasks` for `{environmentName}` to discover valid `userTaskName` values;
		 (2) read `get-guidance name=process-modeling` for the full descriptor contract — element types, flows,
		 parameters (incl. `typeFromElement` to copy an element parameter's exact type, and a constant `value`
		 default), the `mappings` target/source contract, signal triggers (with `changedColumns` to fire only on
		 specific column changes and a data source `filter` to restrict which records fire one), and the
		 type-compatibility rule;
		 (3) supply a JSON descriptor with `name`
		 (unique schema code), `caption`, `packageName`{(string.IsNullOrWhiteSpace(packageName) ? "" : $" (override: `{packageName}`)")} and the `elements` / `flows` / `parameters` / `mappings` arrays.
		 To run the process when a record is added/changed/deleted, use a `signalStart` element (the platform-native
		 trigger), not a page save handler; add `changedColumns` to fire an `on:modified` trigger only when specific
		 columns change, and/or a `filter` to fire only for matching records. To send an email, add a `sendEmail`
		 element with an `email` block — `mode` (auto/manual), `sender`, `to`/`cc`/`bcc` recipients, `subject`, the
		 HTML custom-message `body` (`bodyFormat` `html` only), `importance`, `ignoreErrors`, and a manual-mode
		 `performer`; email TEMPLATES are not supported (custom message only). To put PROCESS DATA in the body use the
		 by-name macros the server resolves for you — `[[param:Name]]`, `[[element:Element.Output]]`, or
		 `[[element:Element.Output.Column]]`; the exact parameter/element names come from the `parameters[]` / `elements[]` you declare in THIS same descriptor
		 — there is no process to `describe-business-process` yet (that is the modify path); an unknown name is rejected, and column names are case-sensitive.
		 To hand a user a purpose-built page and resume when they press a completing button, add a
		 `preconfiguredPage` element with a `preconfiguredPage` block. Its `page` must already exist as a
		 **Freedom UI** page — the server never creates one, and it refuses both an unknown page and a Classic
		 UI page — so when nothing suitable exists, propose a page to the user and create it through the normal
		 `create-page` flow FIRST. At least one completing `button` is REQUIRED and is not defaulted for you:
		 an element without one saves green and then hangs forever at run time. The page's buttons and data
		 sources are FACTS you must read with `get-process-page-facts` and pass through unchanged — a page
		 inherits its buttons from its template chain, so the server cannot see them.
		 A SUCCESSFUL build can still report caveats, and they arrive as `message-type: "Warning"` entries in
		 `execution-log-messages` — there is no separate `warnings` field on the response, so looking for one
		 and finding nothing is not evidence there were none. Confirm the target package with the
		 user before building.
		 """;
}
