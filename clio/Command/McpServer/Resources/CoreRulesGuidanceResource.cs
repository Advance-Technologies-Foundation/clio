using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// The non-negotiable invariants that apply to EVERY clio MCP operation regardless of task. Loaded on
/// demand via <c>get-guidance name=core-rules</c>; the always-on server instructions mandate reading it
/// first on any operation. Each rule is ALSO enforced on a guaranteed channel (the tool-result note,
/// server-side validation, or the Destructive flag), but the agent must still honor them proactively.
/// </summary>
[McpServerResourceType]
public sealed class CoreRulesGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/core-rules";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP core rules

		       Non-negotiable invariants. They apply to EVERY operation regardless of task; read them before
		       acting. Each is ALSO enforced on a guaranteed channel (the tool-result note, server-side
		       validation, or the Destructive flag), but you must still honor them proactively.

		       - compile-creatio is NOT needed after create-page/update-page/create-entity-schema/update-entity-schema/create-lookup/create-data-binding. It IS needed only for C# schemas (Source code, SqlScript, process code) and after push-workspace carrying those. Never compile or restart "just in case". After create-entity-schema/create-lookup the entity's OData controller (/0/odata/<Entity>) is rebuilt asynchronously (~1-2 min): a 404 from an odata-* tool right after creation is the expected async gap — wait and retry, do NOT compile or restart.
		       - Long-running tools (create-app, create-app-section, update-app-section, get-app-info) stream progress; a progress notification is NOT a timeout. Do not cancel or retry; read state with get-app-info / list-app-sections first.
		       - Captions use the connected user's profile culture: call get-user-culture once per session (re-detect when the environment changes) and reuse it. The en-US entry must hold English text; a non-Latin caption under en-US is rejected. If it fails, ASK the user; never assume the host locale.
		       - Destructive tools (uninstall-creatio, clear-redis-db, stop-creatio, delete-*) are high-impact: confirm the target environment with the user first.
		       - Every response carries a correlation-id; on error, inspect the inner-exception chain.
		       - Resident tools (get-tool-contract index: resident=true) are called natively; every other tool is invoked via clio-run <command>. Never wrap a resident tool in clio-run.
		       """
	};

	/// <summary>
	/// Returns the non-negotiable invariants that apply to every clio MCP operation.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "core-rules-guidance")]
	[Description("Returns the non-negotiable clio MCP invariants (compile/restart, long-running await, profile culture, destructive confirmation, correlation-id) that apply to every operation.")]
	public ResourceContents GetGuide() => Guide;
}
