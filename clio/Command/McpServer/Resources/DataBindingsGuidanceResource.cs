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
			       - Before creating or removing a binding, inspect what the package already ships: the binding tools have no list mode, so read `SysPackageSchemaData` with `execute-esq` (filter `SysPackage.Name`; columns `Name`, `SysSchema.Name`). `binding-name` defaults to the schema name, so pass it explicitly to target an existing binding (often a suffixed folder) instead of creating a parallel one.

			       Preferred workflows
			       - Canonical lookup seeding flow: `get-tool-contract` -> `sync-schemas` -> refresh/read-back.
			       - Canonical standalone DB-first binding flow: `get-tool-contract` -> `create-data-binding-db` -> optional `upsert-data-binding-row-db` -> refresh/read-back.
			       - `upsert-data-binding-row-db` decides by primary key: it UPDATES a row that already exists in the table (matched by `Id`) and INSERTS only a genuinely new row (which must then carry every required column). The binding must exist first (`create-data-binding-db`, which may be empty).
			       - Canonical local artifact flow: `get-tool-contract` -> `create-data-binding` -> `add-data-binding-row` or `remove-data-binding-row` -> local artifact verification.
			       - DB-first SaveSchema metadata should be projected from the primary key plus columns referenced by currently bound or requested rows. That projection is also the transfer contract: a column you never passed is absent from `Data/<Schema>_<Suffix>/data.json` and package install supplies NO default for it, so the row arrives on the next environment with that column empty. Pass every column the target schema needs to be usable, not just the ones you happen to be changing, and verify what actually ships with `read-data-binding-db` (`package-name` + `binding-name`) rather than by reading the live row back. It reports the shipped column set, the row count, and every bound row in one read-only call, so `download-application` + extract is only needed when you want the raw archive. One difference to expect: it lists localizable columns inline, while the export moves them into a `Localization` folder — so it can show one more column than `data.json` for the same binding.
			       - Getting a binding right on the FIRST install is the only chance: every non-key column in a package data binding carries `IsForceUpdate: false`, so installing a corrected package does NOT overwrite a row that already exists on the target, and re-installing an unchanged package version applies no data at all. Repairing an already-transferred row means a manual update on that environment.
			       - `sync-schemas` `seed-data` runs the SAME `create-data-binding-db` command underneath, but it cannot pass `binding-name` — the binding always lands under the BARE schema name. That is fine for a lookup you own, and wrong for any schema whose package already ships suffixed bindings: seeding `SysWorkplace` this way adds a third, parallel `SysWorkplace` binding next to the app's `SysWorkplace_MyApps` / `SysWorkplace_Todo` (verified). When the binding name matters, call `create-data-binding-db` / `upsert-data-binding-row-db` directly with an explicit `binding-name`; the column-projection rule above applies identically either way.
			       - `remove-data-binding-row-db` is NOT a package-only operation: it DELETES THE LIVE RECORD (DataService delete) and then unbinds it. It is a delete tool with a binding side effect, and there is no tool that removes a row from a package while leaving the record in place. It is also flagged destructive but accepts NO `confirm` argument, so nothing holds the call — get the user's agreement yourself first, and never call it to "tidy up a binding" on a row you still want or on a row shared with other apps.
			       - Unrelated runtime-only columns are not blockers for DB-first flows; explicitly requested unsupported runtime columns are blockers.
			       - Permission-protected system objects are OUT OF SCOPE for DB-first bindings: rows are applied through the DataService, which enforces object permissions, so a schema such as `SysEntitySchemaOperationRight` is refused even when the authenticated user is Supervisor. Do not retry, do not switch credentials, and do not treat it as an authorization mistake to fix — the failure names the refusal explicitly. For record-level access rights use `set-record-rights` (native RightsService). Object-operation rights have no clio path yet: deploy them through Creatio's Object permissions administration or a package installation script, and tell the user that is what is required.

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
