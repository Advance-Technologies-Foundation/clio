namespace Clio.Command.McpServer;

/// <summary>
/// Provides the server instructions text sent to MCP clients during initialization.
/// This is a thin POINTER, not a manual: it carries only a mandatory directive to load the
/// <c>core-rules</c> guide (the non-negotiable invariants) and the <c>routing</c> guide (task -> guide map)
/// before any operation, plus the telemetry advertisement. All invariants, the routing table, and detailed
/// rules live in active trusted knowledge libraries (loaded lazily on demand) — never duplicate guide content here.
/// </summary>
internal static class McpServerInstructions
{
	internal const string Text = """
		clio is the CLI + MCP server for the Creatio low-code platform (environments, packages, schemas, apps).
		Most tools need a registered environment-name (list-environments / reg-web-app). Read docs://help/command/{name} for any command.

		## ALWAYS read these FIRST (mandatory on every operation; skipping either is a defect, not a shortcut)
		- get-guidance name=core-rules - the non-negotiable invariants (compile/restart, long-running await, profile culture, destructive confirmation, verification in browser preference, correlation-id); apply them to every action.
		- get-guidance name=routing - the routing map from the task (pages, entities, data, applications) to the exact guide(s) to read before acting.
		An unknown name returns availableGuides.

		## Product telemetry (for AI-assisted Creatio work you do here; skip only for non-agent use - plain scripts or CI)
		Note the scope: an agent doing Creatio work through this server is IN scope even when no skill file is loaded. "No skill loaded" is not "ad-hoc use".
		get-telemetry-consent (read-only consent check) -> send-telemetry (stores one event once consent is granted) -> withdraw-telemetry-consent (stop and discard the local outbox).
		Telemetry applies to EVERY Creatio workflow, not just app creation. Event names are flow-agnostic stages (workflow_started, plan_presented, plan_approved, build_started, work_item_completed, workflow_completed, workflow_failed, ...); WHICH flow it was travels in the `workflow` field (for example classic-to-freedom-migration, mobile-page-conversion, branding, app-creation, app-maintenance). A migration or branding run is not exempt just because it skips the app-creation gates - it emits the same stages with its own `workflow` value.
		Call get-tool-contract for the authoritative event_name list, payload shape and emission order; the consent prompt and per-skill emission points are owned by the skill/contract, not these instructions.
		""";
}
