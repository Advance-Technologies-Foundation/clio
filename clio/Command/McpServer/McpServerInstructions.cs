namespace Clio.Command.McpServer;

/// <summary>
/// Provides the server instructions text sent to MCP clients during initialization.
/// This is a thin ROUTER, not a manual: it carries the few non-negotiable invariants that
/// must survive client-side truncation, plus a names-only routing table that points the AI
/// at the matching <c>get-guidance</c> article. Detailed rules live in <c>GuidanceCatalog</c>
/// (loaded lazily on demand) — never duplicate guide content here.
/// </summary>
internal static class McpServerInstructions
{
	internal const string Text = """
		clio is the CLI + MCP server for the Creatio low-code platform (environments, packages, schemas, apps).
		Most tools need a registered environment-name (list-environments / reg-web-app). Read docs://help/command/{name} for any command.

		## Core rules (best-effort orientation; each is ALSO enforced on a guaranteed channel: the tool-result note, server-side validation, or the Destructive flag. They hold even if a client truncates or drops this text)
		- compile-creatio is NOT needed after create-page/update-page/create-entity-schema/update-entity-schema/create-lookup/create-data-binding. It IS needed only for C# schemas (Source code, SqlScript, process code) and after push-workspace carrying those. Never compile or restart "just in case".
		- Long-running tools (create-app, create-app-section, update-app-section, get-app-info) stream progress; a progress notification is NOT a timeout. Do not cancel or retry; read state with get-app-info / list-app-sections first.
		- Captions use the connected user's profile culture: call get-user-culture once per session (re-detect when the environment changes) and reuse it. The en-US entry must hold English text; a non-Latin caption under en-US is rejected. If it fails, ASK the user; never assume the host locale.
		- Destructive tools (uninstall-creatio, clear-redis-db, stop-creatio, delete-*) are high-impact: confirm the target environment with the user first.
		- Every response carries a correlation-id; on error, inspect the inner-exception chain.

		## Load the matching guidance FIRST (get-guidance name=...; an unknown name returns availableGuides)
		- Freedom UI page create/edit -> get-component-info (read resolvedFrom) + name=page-modification
		- Dashboards / analytics widgets -> name=dashboards AND name=indicator-widget
		- Analytics widget placement/sizing/styling -> name=analytics-widgets
		- Business rules / lookup filtering / dependent fields -> name=business-rules; static filters -> name=business-rule-filters
		- Raw ESQ queries -> name=esq AND name=esq-filters
		- Lookup seeding / data bindings -> name=data-bindings
		- Application / schema modeling -> name=app-modeling
		- Deploy & provisioning -> name=deploy-lifecycle
		- Executing an approved plan -> name=agent-execution
		- Identity assertion / Identity Service V3 token exchange -> name=identity-assertion
		""";
}
