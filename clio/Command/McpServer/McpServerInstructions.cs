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
		- get-guidance name=core-rules - the non-negotiable invariants (compile/restart, long-running await, profile culture, destructive confirmation, correlation-id); apply them to every action.
		- get-guidance name=routing - the routing map from the task (pages, entities, data, applications) to the exact guide(s) to read before acting.
		An unknown name returns availableGuides.

		## Product telemetry (only when a consuming skill/contract drives it; otherwise do not call or prompt)
		get-telemetry-consent (read-only consent check) -> send-telemetry (stores one event once consent is granted) -> withdraw-telemetry-consent (stop and discard the local outbox).
		Call get-tool-contract for the authoritative payload shape and emission order; the consent prompt and event sequence are owned by the skill/contract, not these instructions.
		""";
}
