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
		Two read-only-aware tools record local product telemetry about an AI-assisted Creatio app-development session run through this MCP server, driven by a consuming skill/contract. If no such skill is active (ad-hoc clio use, scripts, or CI), do not call these tools or prompt for consent. The tools:
		- `get-telemetry-consent` returns the locally stored telemetry consent (`granted`, `denied`, or `unknown`) without writing anything.
		- `send-telemetry` validates and stores a single workflow telemetry event as a local OpenTelemetry-shaped file once consent is `granted`.

		Consent gates storage: `send-telemetry` stores nothing until consent is `granted`, so establish consent before sending any event (call `get-telemetry-consent`; if `unknown`, obtain the user's decision and persist it once via `send-telemetry`); events sent earlier are silently dropped.
		The consent prompt wording and the per-step event sequence are owned by the app-creation skill/contract, not by these MCP instructions.
		Call `get-tool-contract` for `get-telemetry-consent` and `send-telemetry` to get the authoritative payload shape and emission order. If consent is denied or telemetry is unavailable, continue the user workflow without blocking.
		Once consent is granted, stored events are uploaded in the background automatically; no agent action is needed.

		### Inspect an environment
		1. `list-environments` → pick an environment name
		2. `list-packages` with that environment → see installed packages
		3. `get-schema` → inspect a specific schema

		### Create a new entity
		1. `create-entity-schema` → define the table (applies DDL and publishes the schema, so it is immediately
		   usable as a Lookup reference in sys-settings and lookup pickers)
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
		- After `create-entity-schema` / `create-lookup` — these tools apply DDL AND publish the schema themselves,
		  so the new entity is immediately visible to lookup pickers and sys-setting reference schema lists.
		- After `update-entity-schema` / `modify-entity-schema-column` — these tools already apply DDL and refresh the runtime schema themselves.
		  Note: unlike `create-entity-schema`, these tools do not re-publish the full configuration (by design — the schema is already published).
		  If a newly added lookup column must appear in reference schema lists immediately, run `compile-creatio`.
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

		### Freedom UI components — discover and version-check BEFORE planning page work
		Before you propose components or generate an implementation plan for a Freedom UI page, do BOTH of these — do not rely on memory or assume a component set:
		1. **Resolve the target platform version.** Call `get-component-info` with `environment-name` set to the environment you will edit, and read `resolvedFrom` on the response:
		   - `resolvedFrom: "environment"` — the platform version is KNOWN and the exact per-version catalog was loaded; the catalog is authoritative, proceed with no confirmation.
		   - `resolvedFrom: "environment-superset"` — the platform version was known (probe-success or explicit `--version`) but the exact per-version catalog was not published on the CDN, so `latest` was served as the closest available. The response carries `versionWarning` with a soft caveat. Flag this to the user and verify critical component types against the actual environment before committing to an implementation plan — a type listed in `latest` may not exist in the target’s actual platform version.
		   - `resolvedFrom: "latest-fallback"` — the version could NOT be determined (no active environment, probe failed, or unparseable version). The response sets the machine-readable `requiresVersionConfirmation: true` flag — branch on it, not on the prose: do NOT silently assume a component set. Tell the user the platform version is unknown and request explicit confirmation before proceeding, or fix the upstream signal (register/activate the environment, upgrade cliogate). `resolvedFromReason` says whether the failure is transient (`probe-error` — a retry or a reachable environment may help) or stable (`no-active-environment` / `core-version-missing` / `core-version-unparseable` — supply an explicit version). The response's `versionWarning` carries the same caveat as prose.
		2. **Discover the full component set proactively.** Call `get-component-info` with no `component-type` (list mode) to enumerate every component available for that version. Non-obvious components (e.g. `crt.Gallery`) live in the catalog and must be considered and suggested when relevant — never conclude a capability is missing, or wait for the user to ask you to search, without listing the catalog first. Pass `schema-type: "mobile"` to discover the separate mobile component set.

		### Edit a page from a Creatio designer URL
		A Freedom UI designer URL is one of:
		- `#/PageDesigner/<pageUId>` — first edit of a page that lives in a locked package; the backend will create a virtual replacing package automatically on first save.
		- `#/PageDesigner/<pageUId>?packageUId=<packageUId>` — subsequent edit of an already-replaced page; the URL's packageUId points to the existing substitution.

		Canonical flow in BOTH cases:
		1. Identify the active environment from the host — use `list-environments` to confirm the matching `environment-name`.
		2. Call `list-pages uid=<pageUId>` — returns the exact page with its `schema-name` and `packageName` in one call.
		3. Call `get-page schema-name=<matched schema-name>` to retrieve the editable body and bundle.
		4. Call `update-page schema-name=<matched schema-name> body=<...>` to save. Do NOT pass `target-package-uid`: the backend's `GetDesignPackageUId` resolves the correct package automatically — it materializes a virtual package on first save (locked-source case) or reuses the existing replacing package (already-substituted case). Each platform package has a deterministic owning app, so there is no ambiguity to override.

		## Long-running tools (await — do not retry on a perceived timeout)
		- `create-app`, `create-app-section`, `update-app-section`, `delete-app-section`,
		  `list-app-sections`, and `get-app-info` call the Creatio backend and can take minutes on
		  a cold or busy environment. They stream `notifications/progress` while working.
		- A progress notification means the server is still working — it is NOT a stall. Do not
		  cancel and retry, and do not fall back to raw SQL or manual UI on a perceived client
		  timeout; that duplicates work and can leave partial state.
		- If your client surfaces a hard timeout while progress is still arriving, treat the call
		  as in-flight: read back state with `get-app-info` / `list-app-sections` before any retry.

		## Profile language for created entities (detect once, reuse, ask on failure)
		- Before creating ANY entity (application, object, page, section, lookup, column), call
		  `get-user-culture` ONCE per session to detect the connected user's profile language, and
		  reuse that result for all generated names, labels, and captions for the rest of the session.
		- The detected culture is the LANGUAGE OF THE CAPTION TEXT, not just the localization key:
		  write every name, label, and caption IN that language. The conversation/task language does
		  NOT override the profile language — an `en-US` profile means English captions. The mandatory
		  `en-US` localization-map entry MUST hold ENGLISH text; put non-English text under its own
		  culture key (e.g. `uk-UA`). clio REJECTS a caption whose script does not match a Latin-script
		  culture key (e.g. Cyrillic under `en-US`).
		- If `get-user-culture` returns `success:false`, ASK the user which language to use before
		  proceeding. Do NOT silently fall back to the host machine locale or to `en-US`.
		- Re-detect only when the active environment changes within the session (the result is keyed
		  by environment). Do not re-detect per entity — the server caches it per environment.
		- To force a specific language for a single creation, pass the `caption-culture` argument
		  (precedence: `caption-culture` > detected profile culture > `en-US`).

		## Error handling
		- Every tool response includes a `correlation-id` for tracing.
		- Errors include the full exception chain. Look at inner exceptions for root cause.
		- Server logs are forwarded as `notifications/message`; set log level to debug for verbose output.
		""";
}
