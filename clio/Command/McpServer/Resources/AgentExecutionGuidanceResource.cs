using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for executing approved plans through clio MCP, including transport rules, execution order, branching, and recovery patterns.
/// </summary>
[McpServerResourceType]
public sealed class AgentExecutionGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/agent-execution";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for plan execution mechanics through clio MCP.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "agent-execution-guidance")]
	[Description("Returns canonical MCP guidance for executing approved plans through clio MCP: transport rules, execution order, branching, and recovery patterns.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP agent execution guide

			       Scope
			       - This guide owns the executable mechanics for an agent that executes an already-approved plan against clio MCP.
			       - Business policy (gates, approvals, BA structure, model decisions) stays in the consumer repository.
			       - For canonical app-modeling and page-flow semantics, follow `docs://mcp/guides/app-modeling` and the per-task guides referenced from there.
			       - For diagnostic-first behavior in support runs, follow `docs://mcp/guides/support-mode`.

			       MCP transport rules
			       - clio MCP is a stdio MCP server. Use the consumer-repo MCP client wrapper (such as `scripts/mcp_client.py`) instead of raw curl for stdio transport.
			       - Respect the `CLIO_CMD` environment variable when a custom clio binary is configured for the run.
			       - Wrap tool arguments under the top-level `args` JSON object exactly as advertised by the tool schema (for example `{"args": {"environment-name": "...", "package-name": "..."}}`). Do not flatten, rename, or hoist canonical fields.
			       - Pass boolean MCP parameters as booleans, not strings.
			       - Use kebab-case JSON argument names exactly as advertised by the discovered tool contract (for example `environment-name`, `package-name`, `schema-name`).
			       - Always read the executable contract through `get-tool-contract` before the first invocation of any MCP tool in a workflow. The contract specifies exact parameter names, aliases, required fields, defaults, and response shapes.

			       Execution order
			       1. Verify MCP reachability through the consumer-repo MCP client wrapper.
			       2. Call `tools/list` and verify required tools exist for the planned steps. Long-tail (non-resident) tools such as the DataForge family are intentionally absent from `tools/list`; confirm them through `get-tool-contract` (`resident: false`) instead and invoke them via `clio-run` / `clio-run-destructive` — do not treat their absence as missing.
			       3. Resolve executable contract metadata through `get-tool-contract` for each tool the plan invokes.
			       4. Resolve the execution branch (new-app vs existing-app) through the current clio contract and the relevant guidance resources.
			       5. Execute the approved schema mutation step using the current clio-owned preferred or fallback tool path.
			       6. Run the post-mutation refresh step required by the current clio guidance (for example `get-app-info` after `create-app` or `sync-schemas`).
			       7. If the plan requires page sync, execute the current clio-owned page inspection/write/verify flow.
			       8. Validate the final normalized result against the success/failure contract.
			       9. Report the implementation summary in conversation, citing only evidence returned by clio MCP.

			       Branching rules
			       - If `create-app` reports that the app or configuration schema already exists, stop the create flow and switch to the existing-app discovery flow (`list-apps` -> `get-app-info` -> `create-app-section`).
			       - Treat `create-app` as a DataForge-assisted create step. Do not add an automatic standalone `dataforge-status` or `dataforge-context` preflight in the standard new-app branch.
			       - Surface which branch actually ran in the persisted evidence and final report.
			       - Do not reinterpret `reuse` / `extend` / `create` decisions during execution. Execute the model decisions already recorded in the plan.
			       - If a requested schema step is not fully covered by recorded model decisions, stop with a blocker instead of improvising a new entity or lookup.
			       - If a requested schema step contradicts a final `reuse` or `extend` decision, stop with a blocker instead of honoring stale BA assumptions.

			       Schema sync recovery patterns
			       - When a `create-app-section` reuse step fails, read the actual error instead of assuming an entity-binding conflict: Creatio allows several sections per entity, so reusing an `entity-schema-name` that already backs a section is normally valid. A `does not exist` error means the `entity-schema-name` is wrong — fix the object name. A failure that asks for an explicit code means the caption is non-Latin — pass a Latin `code` (for example `Contacts` for caption `Контакти`). A detail-less `Failed to create section ...` rejection usually means a section with that code already exists — change the caption or `code`, or run `list-app-sections` to find and reuse the existing section. Do not fabricate a substitute entity that duplicates an existing schema's fields.
			       - When `create-app-section` returns `success: false` due to a metadata readback timeout (the error reports the section `was created but its metadata could not be loaded`, not a `Failed to create section` insert rejection) and `list-app-sections` confirms the section was actually created, proceed with the recovery path but first verify the auto-generated greenfield entity from `create-app`. Call `get-entity-schema-properties` on the app entity (for example `UsrTaskManagementApp`). If it still inherits from `BaseEntity` with only the auto-generated `UsrName` column and the section's `entity-schema-name` is a different entity, delete the orphaned entity using `delete-schema` before proceeding to page sync. If `delete-schema` fails, log a warning with the entity name and failure reason and continue to page sync. Record this cleanup attempt as a recovery action in the implementation evidence.
			       - Treat schema work as successful only when refreshed metadata is available immediately and no schema is left in `Database update required` state.
			       - If post-mutation refresh fails, stop with a blocker.
			       - Use `update-entity-schema` semantics inside `sync-schemas` to extend an existing main entity. Use `create-entity-schema` only for additional business objects with distinct meaning.
			       - Create lookup entities before entities that reference them.
			       - Prefer batched lookup seeding inside `sync-schemas`. Use `create-data-binding-db` only when the run explicitly needs a separate binding artifact, custom filter, or cross-package reference.

			       Default value rules
			       - Seed rows create data only. A requirement like "defaults to New" still needs an explicit schema default or UI default in addition to the seed row.
			       - For lookup-backed field defaults, resolve the executable schema-side or page-side mechanism from the live contract and current page/runtime context. Do not guess field-level request shape from repo docs.
			       - Either mechanism must be in the page-sync plan and executed; never mark lookup defaults as `manualCheckPending` without evidence.

			       Page sync rules
			       - Page sync is mandatory when the run creates a new app or extends the main section entity with approved business fields.
			       - Read live page bodies through `get-page` and use `files.bodyFile` paths instead of manually parsing the raw response.
			       - Send the full `raw.body` string back to `sync-pages` or `update-page`. Do not send `bundle` or `bundle.viewConfig` fragments as the body payload.
			       - Use `sync-pages` for multi-page or plan-driven page writes. Use `update-page` only for a single-page save or dry-run workflow.
			       - For additive page edits that should not overwrite existing customizations, use `update-page` with `mode: "append"`.
			       - Validate page bodies with `validate-page` before persisting.
			       - For new apps or extended main entities, perform page edits after `sync-schemas` and the post-mutation refresh so that page bindings reference materialized columns.

			       Evidence and reporting
			       - Track execution evidence using these status buckets: `implemented`, `machineChecked`, `manualCheckPending`.
			       - Final user-facing status must be derived from the tool execution evidence reported in the conversation. Do not report planned items as implemented without confirmed evidence.
			       - If `create-app` returns a top-level `dataforge` block, treat it as advisory execution diagnostics. Do not treat degraded coverage or warnings as a blocker when the app shell itself was created successfully.
			       - Never claim UI acceptance is verified unless the corresponding evidence was returned by MCP tools.

			       Retry and failure policy
			       - Retry transient MCP transport failures up to 3 attempts with a short delay before fail-fast classification.
			       - For transient site reachability errors (DNS resolution failures, connect timeouts, temporary host-unreachable), retry the same registration/healthcheck path up to 3 additional attempts with 15-second delays before fail-fast classification.
			       - If required tools are missing in `tools/list`, stop with a blocker — but first check whether the tool is long-tail (non-resident): DataForge and other `resident: false` tools are expected to be absent from `tools/list` and are reached via `clio-run` / `clio-run-destructive`, so their absence is not a blocker.
			       - If `get-tool-contract` cannot provide executable metadata, stop with a blocker.
			       - If any normalized tool result is unsuccessful, stop with a blocker and persist the raw evidence.
			       - Use standalone `dataforge-status`, `dataforge-context`, `dataforge-initialize`, and `dataforge-update` only in explicit inspection or remediation branches. Do not use them as automatic retries for the standard create flow.
			       - If the plan tries to create a second `BaseEntity` for the same primary record type as the resolved main section entity, stop with a blocker instead of executing it.

			       Completion criteria
			       - MCP reachability and contract discovery succeeded.
			       - All required schema sync steps executed and canonical context refreshed.
			       - No created or updated schema is left in `Database update required` state.
			       - Page sync executed and verified for every run that required it.
			       - Implementation summary reported in conversation from MCP evidence with explicit `implemented` / `machineChecked` / `manualCheckPending` buckets.
			       """
		};
}
