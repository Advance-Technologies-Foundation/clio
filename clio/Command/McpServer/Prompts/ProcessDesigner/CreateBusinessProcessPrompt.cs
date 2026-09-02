using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts.ProcessDesigner;

/// <summary>
/// Prompt helpers for building a business process on a Creatio environment through MCP.
/// </summary>
[McpServerPromptType, Description("Prompts to build a business process from a declarative descriptor")]
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
		 To grant or revoke record permissions on records matching a filter, add a `changeAccessRights` element
		 with an `accessRights` block (target object + `add`/`remove` permission entries) plus the element's record
		 `filter` — WHICH records get them; without one the runtime silently does nothing. When the descriptor contains a `changeAccessRights` element, confirm it the way a
		 destructive edit is confirmed: show the user the target object, the element record `filter` that decides
		 WHICH records are affected, every grantee, and each entry's operations and level (call out `delegate` as
		 onward re-sharing, `restrict` as the platform Deny level, which is UNVERIFIED (the same value is
		 captioned "NotSet" elsewhere and no captured specimen uses it) and which lands in the `add`
		 GRANT collection, observed to write no right at all where UseDenyRecordRights is off - neither denying nor granting, with nothing reporting it, and a `remove` entry as a revoke), and get an explicit yes before calling
		 `create-business-process` — the element reports nothing at run time about what it granted or revoked.
		 Confirm the target package with the
		 user before building.
		 """;
}
