namespace Clio.Command.McpServer;

/// <summary>
/// Provides the server instructions text sent to MCP clients during initialization.
/// The AI model reads this to understand how to effectively use the server's capabilities.
/// </summary>
internal static class McpServerInstructions
{
	internal const string Text = """
		Clio is the CLI and MCP server for the Creatio low-code platform.
		It manages Creatio environments, packages, schemas, and application lifecycle.

		## Core concepts
		- **Environment**: a registered Creatio instance identified by name (e.g. "dev", "production").
		  Most tools require an environment name. Use `list-environments` to list registered environments.
		  Use `reg-web-app` to register a new one.
		- **Package**: a unit of configuration in Creatio (schemas, data bindings, resources).
		  Use `list-packages` to see installed packages.
		- **Schema**: an entity model, page, process, or code unit inside a package.
		  Use `get-schema` to inspect, `create-entity-schema` / `create-lookup` to create.

		## Typical workflows

		### Product telemetry
		Two read-only-aware tools record local product telemetry for app-creation workflows:
		- `get-measurements-consent` returns the locally stored telemetry consent (`granted`, `denied`, or `unknown`) without writing anything.
		- `send-measurements` validates and stores a single workflow measurement event as a local OpenTelemetry-shaped file once consent is `granted`.

		The consent prompt wording and the per-step event sequence are owned by the app-creation skill/contract, not by these MCP instructions.
		Call `get-tool-contract` for `get-measurements-consent` and `send-measurements` to get the authoritative payload shape and emission order. If consent is denied or telemetry is unavailable, continue the user workflow without blocking.

		### Inspect an environment
		1. `list-environments` → pick an environment name
		2. `list-packages` with that environment → see installed packages
		3. `get-schema` → inspect a specific schema

		### Create a new entity
		1. `create-entity-schema` → define the table
		2. `update-entity-schema` → add columns (already applies DDL to the database and refreshes the runtime schema; no separate compile needed)
		3. `create-data-binding` + `add-data-binding-row` → seed lookup data

		### Build & deploy
		1. `push-workspace` → push local workspace packages to the environment
		2. `compile-creatio` → only if the push contains changes that require compilation (see "When `compile-creatio` IS required" below)
		3. `restart-by-environment-name` → restart only when server-side assemblies were rebuilt or `clear-redis-db-by-environment` was called

		## When `compile-creatio` IS required
		- Adding or modifying C# schemas (Source code, SqlScript, business processes with executable code).
		- After `push-workspace` if the pushed packages contain any of the above.
		- Recovering from a "schema is missing in runtime" error reported by the platform.

		## When `compile-creatio` is NOT required
		- After `create-app`, `create-app-section`, `create-page`, `update-page` — Freedom UI bodies are AMD modules served at runtime.
		- After `create-entity-schema` / `update-entity-schema` / `modify-entity-schema-column` — these tools already apply DDL and refresh the runtime schema themselves.
		- After `create-data-binding` / `add-data-binding-row` / `upsert-data-binding-row-db` — data seeding does not change compiled artifacts.
		Calling `compile-creatio` in these cases only wastes time and may trigger an unnecessary restart.

		## Safety rules
		- Tools marked **Destructive** modify or delete data; double-check the target environment.
		- `uninstall-creatio`, `clear-redis-db-by-environment`, `stop-creatio` are high-impact; confirm with the user first.
		- Do not call `compile-creatio` or `restart-by-environment-name` "just in case" — see the rules above.
		- When in doubt, prefer read-only tools (`list-packages`, `get-schema`, `list-environments`).

		## Tool naming conventions
		- Many tools have two variants: `*-by-environment-name` (uses a registered alias)
		  and `*-by-credentials` (takes raw URL/username/password). Prefer the environment-name variant.
		- Read the `docs://help/command/{CommandName}` resources for detailed usage of any command.

		### Edit a page from a Creatio designer URL
		A Freedom UI designer URL is one of:
		- `#/PageDesigner/<pageUId>` — first edit of a page that lives in a locked package; the backend will create a virtual replacing package automatically on first save.
		- `#/PageDesigner/<pageUId>?packageUId=<packageUId>` — subsequent edit of an already-replaced page; the URL's packageUId points to the existing substitution.

		Canonical flow in BOTH cases:
		1. Identify the active environment from the host — use `list-environments` to confirm the matching `environment-name`.
		2. Call `list-pages uid=<pageUId>` — returns the exact page with its `schema-name` and `packageName` in one call.
		3. Call `get-page schema-name=<matched schema-name>` to retrieve the editable body and bundle.
		4. Call `update-page schema-name=<matched schema-name> body=<...>` to save. Do NOT pass `target-package-uid`: the backend's `GetDesignPackageUId` resolves the correct package automatically — it materializes a virtual package on first save (locked-source case) or reuses the existing replacing package (already-substituted case). Each platform package has a deterministic owning app, so there is no ambiguity to override.

		## Error handling
		- Every tool response includes a `correlation-id` for tracing.
		- Errors include the full exception chain. Look at inner exceptions for root cause.
		- Server logs are forwarded as `notifications/message`; set log level to debug for verbose output.
		""";
}
