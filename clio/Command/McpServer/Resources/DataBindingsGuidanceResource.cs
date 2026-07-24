using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for Creatio data bindings through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class DataBindingsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/data-bindings";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for generic lookup seeding and binding artifact workflows.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "data-bindings-guidance")]
	[Description("Returns canonical MCP guidance for generic Creatio data bindings, lookup seeding, and binding artifact workflows.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP data-bindings guide

			       Core contract
			       - Resolve exact binding tool shape through `get-tool-contract` before the first invocation in a workflow.
			       - Use `get-guidance` with `name` set to `data-bindings` for canonical workflow knowledge about lookup seeding and binding artifact selection.
			       - Do not copy request schemas, aliases, or response shapes from consumer-repo docs.

			       Progressive discovery
			       - Identify whether the task is inline lookup seeding, standalone DB-first binding work, or local binding artifact work.
			       - Load only the contracts needed for that path:
			         - `sync-schemas` for inline lookup seed rows
			         - `create-data-binding-db`, `upsert-data-binding-row-db`, and `remove-data-binding-row-db` for remote DB-first binding work
			         - `create-data-binding`, `add-data-binding-row`, and `remove-data-binding-row` for local artifact workflows
			       - When the task depends on current binding or schema context, inspect that context before binding mutations.

			       Preferred workflows
			       - Canonical lookup seeding flow: `get-tool-contract` -> `sync-schemas` -> refresh/read-back.
			       - Canonical standalone DB-first binding flow: `get-tool-contract` -> `create-data-binding-db` -> optional `upsert-data-binding-row-db` -> refresh/read-back.
			       - `upsert-data-binding-row-db` decides by primary key: it UPDATES a row that already exists in the table (matched by `Id`) and INSERTS only a genuinely new row (which must then carry every required column). The binding must exist first (`create-data-binding-db`, which may be empty).
			       - Canonical local artifact flow: `get-tool-contract` -> `create-data-binding` -> `add-data-binding-row` or `remove-data-binding-row` -> local artifact verification.
			       - DB-first SaveSchema metadata should be projected from the primary key plus columns referenced by currently bound or requested rows.
			       - Unrelated runtime-only columns are not blockers for DB-first flows; explicitly requested unsupported runtime columns are blockers.

			       Lookup seeding rules
			       - Prefer inline seed rows in `sync-schemas` when the lookup is already part of the current schema batch.
			       - Use a separate binding artifact only when the workflow explicitly needs one.
			       - Seed rows do not implement defaults.
			       - Generate fresh GUIDs for explicit rows at execution time.
			       - Seed-data replay safety is keyed on `Name`: a row is replay-safe only when the target schema has a `Name` column AND the row carries a `Name`; rows without a `Name` (or schemas without a `Name` column) are non-convergent — a stable-`Id`, no-`Name` row PK-conflicts on replay. Re-running a `sync-schemas` batch whose seed rows carry a `Name` skips the already-present rows (no duplicates); do not add explicit `Id`s to no-`Name` rows expecting a re-run to be safe.

			       Verification discipline
			       - Read before write when the task depends on current app, page, schema, or binding context.
			       - Read back after remote mutation.
			       - Do not treat install logs or planned payloads as proof of installed remote state.
			       - Verify local artifacts by inspecting generated files or normalized command output.
			       - Never treat a planned row list as proof of installed state.

			       Anti-patterns
			       - Do not duplicate live contract tables in skill docs.
			       - Do not use direct SQL as the canonical MCP path.
			       - Do not treat lookup seed rows as default implementation.
			       - Do not leave `DisplayValue` semantics implicit for non-null lookup or image-reference row payloads.
			       """
		};
}
