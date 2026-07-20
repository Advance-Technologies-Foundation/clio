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
		       - Canonical C# delivery cycle: compile-creatio -> (on .NET Framework hosts only: new assemblies do NOT load until the app restarts) restart-by-environment-name -> verify. On .NET 6/8 hosts compile-creatio reloads the runtime itself, so a restart is redundant unless the runtime still reports a stale/missing schema afterward. restart-by-environment-name's waitReady defaults to true, so a plain call already waits for the app to answer before you verify.
		       - Long-running tools (create-app, create-app-section, update-app-section, get-app-info, sync-schemas, compile-creatio, restart-by-environment-name) stream progress; a progress notification is NOT a timeout. Do not cancel or retry; read state with get-app-info / list-app-sections first. compile-creatio and restart-by-environment-name can additionally return exit-code 0 with an in-progress note (operation accepted, still running past the MCP response deadline) — that is NOT a failure and NOT grounds to retry: poll compile-status (compile) or clio-run healthcheck (restart) instead.
		       - Captions use the connected user's profile culture: call get-user-culture once per session (re-detect when the environment changes) and reuse it. The en-US entry must hold English text; a non-Latin caption under en-US is rejected. If it fails, ASK the user; never assume the host locale.
		       - Destructive tools (uninstall-creatio, clear-redis-db, stop-creatio, delete-*) are high-impact: confirm the target environment with the user first.
		       - uninstall-creatio may complete with a WarningMessage, a warning stage, and a success-with-warnings terminal when Windows cannot remove a locked IIS application-pool profile or dbHub live verification is unavailable (exit code 0 / IsError=false); show the warning detail, but do not retry the destructive command. The conditional remove-dbhub-source stage runs only after destructive cleanup and before unregister; an earlier failure skips it and retains the source for reconciliation.
		       - uninstall-creatio preserves an application pool and its Windows profile when another IIS application still uses that pool; a skipped profile stage in this case is expected and requires no manual cleanup.
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
