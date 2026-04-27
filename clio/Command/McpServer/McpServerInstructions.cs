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
		  Most tools require an environment name. Use `show-web-app-list` to list registered environments.
		  Use `reg-web-app` to register a new one.
		- **Package**: a unit of configuration in Creatio (schemas, data bindings, resources).
		  Use `get-pkg-list` to see installed packages.
		- **Schema**: an entity model, page, process, or code unit inside a package.
		  Use `get-schema` to inspect, `create-entity-schema` / `create-lookup` to create.

		## Typical workflows

		### Inspect an environment
		1. `show-web-app-list` → pick an environment name
		2. `get-pkg-list` with that environment → see installed packages
		3. `get-schema` → inspect a specific schema

		### Create a new entity
		1. `create-entity-schema` → define the table
		2. `update-entity-schema` → add columns
		3. `compile-configuration` → apply changes to DB
		4. `create-data-binding` + `add-data-binding-row` → seed lookup data

		### Build & deploy
		1. `compile-configuration` → compile the environment
		2. `push-workspace` → push local workspace packages to the environment
		3. `restart-by-environment-name` → restart after deployment

		## Safety rules
		- Tools marked **Destructive** modify or delete data; double-check the target environment.
		- `uninstall-creatio`, `clear-redis`, `stop-creatio` are high-impact; confirm with the user first.
		- When in doubt, prefer read-only tools (`get-pkg-list`, `get-schema`, `show-web-app-list`).

		## Tool naming conventions
		- Many tools have two variants: `*-by-environment-name` (uses a registered alias)
		  and `*-by-credentials` (takes raw URL/username/password). Prefer the environment-name variant.
		- Read the `docs://help/command/{CommandName}` resources for detailed usage of any command.

		## Error handling
		- Every tool response includes a `correlation-id` for tracing.
		- Errors include the full exception chain. Look at inner exceptions for root cause.
		- Server logs are forwarded as `notifications/message`; set log level to debug for verbose output.
		""";
}
