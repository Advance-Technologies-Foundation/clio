namespace Clio.Command.McpServer;

/// <summary>
/// Provides the server instructions text sent to MCP clients during initialization.
/// The AI model reads this to understand how to effectively use the server's capabilities.
/// </summary>
internal static class McpServerInstructions
{
	internal const string Text = """
		Clio is the CLI and MCP server for the Creatio low-code platform: it manages Creatio
		environments, packages, schemas, and application lifecycle.

		## Core concepts
		- Environment: a registered Creatio instance identified by name. Most tools take an
		  `environment-name`. `list-environments` to list, `reg-web-app` to register.
		- Package: a unit of configuration (schemas, data bindings, resources). `list-packages` to list.
		- Schema: an entity model, page, process, or code unit. `get-schema` to inspect;
		  `create-entity-schema` / `create-lookup` to create.

		## compile-creatio — when it IS / is NOT required
		REQUIRED only for: C# schemas (Source code, SqlScript, executable processes); a `push-workspace`
		that contained any of those; recovering from a "schema is missing in runtime" error.
		NOT required after: `create-app` / `*-section` / `create-page` / `update-page` (Freedom UI bodies
		are runtime AMD modules); `create-entity-schema` / `create-lookup` / `update-entity-schema` /
		`modify-entity-schema-column` (these apply DDL and refresh the runtime schema themselves); or data
		seeding (`create-data-binding` / `add-data-binding-row` / `upsert-data-binding-row-db`). Calling it
		needlessly only wastes time and may force a restart. `restart-by-environment-name` only when
		server-side assemblies were rebuilt or redis was cleared.

		## Safety
		- Destructive tools modify/delete data — double-check the target environment.
		- `uninstall-creatio`, `clear-redis-db-by-environment`, `stop-creatio` are high-impact; confirm first.
		- Do not call `compile-creatio` / `restart-by-environment-name` "just in case". Prefer read-only
		  tools when unsure. Prefer `*-by-environment-name` variants over `*-by-credentials`.

		## Calling tools
		Every tool that takes parameters wraps them in a single top-level `args` object — put all fields
		inside `args`, never at the top level (a flat payload is rejected with a missing-`args` error).
		Tools with no parameters take an empty object.

		## Product telemetry
		Three tools (`get-telemetry-consent`, `send-telemetry`, `withdraw-telemetry-consent`) manage local
		product telemetry for an AI-assisted app-development session driven by a consuming skill/contract.
		If no such skill is active (ad-hoc clio use, scripts, CI), do not call them or prompt for consent.
		`get-telemetry-consent` reads the stored consent (`granted` / `denied` / `unknown`) without writing.
		`send-telemetry` validates and stores one workflow event locally, but ONLY once consent is `granted`
		(events sent earlier are silently dropped) — so establish consent first (if `unknown`, obtain the
		user's decision and persist it once via `send-telemetry`). clio uploads stored events in the
		background; no agent action is needed for delivery. `withdraw-telemetry-consent` sets the decision
		to `denied`, stops all further collection/upload, and discards the local outbox — call it when the
		user asks to stop/turn off/opt out of telemetry; it is forward-looking (already-uploaded events are
		not deleted). The consent wording and per-step event sequence are owned by the skill/contract, not
		these instructions — call `get-tool-contract` for the authoritative payload shape and emission order.
		If consent is denied or telemetry is unavailable, continue the user workflow without blocking.

		## Freedom UI page work — version-check first
		Before planning page work, call `get-component-info` with the target `environment-name` to scope
		the catalog to that platform version, then list mode (no `component-type`) to discover the full set
		(including non-obvious types such as `crt.Gallery`) — never author from memory. Branch on the
		response's `requiresVersionConfirmation` / `resolvedFrom`: when the version is unknown, confirm with
		the user before proceeding. `schema-type: "mobile"` queries the separate mobile catalog.
		The list response also returns a `composites` array — pre-built combinations (e.g. "Expanded list",
		"Attachments", "Approval list") with no `componentType` of their own. Never hand-assemble one from
		raw components: fetch its recipe with `get-component-info composite: "<caption>"`. A component flagged
		`compositeOnly: true` has no standalone toolbar presence — build the matching composite instead of
		inserting it directly.
		To edit a page from a designer URL `#/PageDesigner/<pageUId>[?packageUId=…]`: `list-pages uid=<pageUId>`
		→ `get-page schema-name=<matched>` → `update-page` WITHOUT `target-package-uid` (the backend resolves
		the design package automatically). Call `get-guidance page-modification` before editing the body.

		## Long-running tools (await — do not retry on a perceived timeout)
		`create-app`, `create-app-section`, `update-app-section`, `delete-app-section`, `list-app-sections`,
		`get-app-info` call the backend and can take minutes; they stream `notifications/progress`. Progress
		means still working, NOT a stall — do not cancel/retry or fall back to SQL/manual UI. On a client
		timeout while progress arrives, read back state (`get-app-info` / `list-app-sections`) before retry.

		## Profile language (detect once, reuse, ask on failure)
		Before creating ANY entity, call `get-user-culture` ONCE per session and write every name/label/caption
		IN that language for the rest of the session — the task language does NOT override it. The mandatory
		`en-US` localization entry must hold ENGLISH text; put other languages under their own culture key
		(clio rejects script/culture mismatches, e.g. Cyrillic under `en-US`). Re-detect only when the active
		environment changes. Pass `caption-culture` to force a language for a single creation.
		On `get-user-culture` failure, ASK the user which language to use; never fall back to host locale.

		## Discovering tools
		To learn what tools exist, call `get-tool-contract` with NO args for a compact index of every tool
		(names + one-line purpose + safety flags) — cheap discovery without full schemas. Then call it again
		with specific `tool-names` to load only the full contracts you need (pass `detail=full` to expand all).

		## Error handling
		Every response carries a `correlation-id`. Errors include the full exception chain — inspect inner
		exceptions for root cause. Server logs arrive as `notifications/message` (set log level to debug for
		more). Use `get-guidance` for in-depth workflow guides and `docs://help/command/{CommandName}`
		resources / `get-tool-contract` for full per-command argument contracts.
		""";
}
