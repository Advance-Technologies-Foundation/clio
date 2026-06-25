namespace Clio.Command.McpServer;

/// <summary>
/// Provides the server instructions text sent to MCP clients during initialization.
/// This is a thin POINTER, not a manual: it carries only the few non-negotiable invariants that
/// must survive client-side truncation, plus a mandatory directive to load the <c>routing</c> guide
/// (<c>get-guidance name=routing</c>) before any operation. The routing table itself and all detailed
/// rules live in <c>GuidanceCatalog</c> (loaded lazily on demand) — never duplicate guide content here.
/// </summary>
internal static class McpServerInstructions
{
	internal const string Text = """
		clio is the CLI + MCP server for the Creatio low-code platform (environments, packages, schemas, apps).
		Most tools need a registered environment-name (list-environments / reg-web-app). Read docs://help/command/{name} for any command.

		## ALWAYS load the routing map FIRST
		Before planning or running ANY operation, call get-guidance name=routing. It is the routing table that maps the task (pages, entities, data, applications) to the exact guide(s) you must read before acting; skipping it is a defect, not a shortcut.

		## Core rules (best-effort orientation; each is ALSO enforced on a guaranteed channel: the tool-result note, server-side validation, or the Destructive flag. They hold even if a client truncates or drops this text)
		- compile-creatio is NOT needed after create-page/update-page/create-entity-schema/update-entity-schema/create-lookup/create-data-binding. It IS needed only for C# schemas (Source code, SqlScript, process code) and after push-workspace carrying those. Never compile or restart "just in case".
		- Long-running tools (create-app, create-app-section, update-app-section, get-app-info) stream progress; a progress notification is NOT a timeout. Do not cancel or retry; read state with get-app-info / list-app-sections first.
		- Captions use the connected user's profile culture: call get-user-culture once per session (re-detect when the environment changes) and reuse it. The en-US entry must hold English text; a non-Latin caption under en-US is rejected. If it fails, ASK the user; never assume the host locale.
		- Destructive tools (uninstall-creatio, clear-redis-db, stop-creatio, delete-*) are high-impact: confirm the target environment with the user first.
		- Every response carries a correlation-id; on error, inspect the inner-exception chain.

		## Product telemetry (only when a consuming skill/contract drives it; otherwise do not call or prompt)
		get-telemetry-consent (read-only consent check) -> send-telemetry (stores one event once consent is granted) -> withdraw-telemetry-consent (stop and discard the local outbox).
		Call get-tool-contract for the authoritative payload shape and emission order; the consent prompt and event sequence are owned by the skill/contract, not these instructions.
		""";
}
