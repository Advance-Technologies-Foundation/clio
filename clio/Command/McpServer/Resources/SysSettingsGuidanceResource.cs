using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for Creatio system setting (sys-settings) CRU workflows through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class SysSettingsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/sys-settings";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for creating, reading, listing, and updating Creatio system settings.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "sys-settings-guidance")]
	[Description("Returns canonical MCP guidance for the Creatio sys-settings CRU surface: tool order, supported value-type-names and aliases, Lookup resolution, SecureText masking, Date/Time TZ caveat, and Binary exclusion.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP sys-settings guide

			       Core contract
			       - Resolve the exact tool shape through `get-tool-contract` before the first invocation in a sys-setting workflow.
			       - Use `get-guidance` with `name` set to `sys-settings` to recall the rules below; do not copy schemas from consumer-repo docs.
			       - Sys-settings expose four MCP tools: `list-sys-settings`, `get-sys-setting`, `create-sys-setting`, `update-sys-setting`. All operate on the All-Users default; per-user overrides are out of scope for this surface.

			       Preferred workflows
			       - Discover then read: `list-sys-settings` → `get-sys-setting <code>` when the code is unknown.
			       - Read then mutate: `get-sys-setting <code>` → `update-sys-setting <code> <value>` for existing settings.
			       - Provision: `create-sys-setting <code> <name> <value-type-name> [value] [reference-schema-name]`. When `value` is supplied, clio internally invokes `update-sys-setting` so the response already carries the read-back value.

			       Supported value-type-name
			       - Text family: `Text`, `ShortText` (Text 50), `MediumText` (Text 250), `LongText` (Text 500), `MaxSizeText` (Unlimited), `SecureText` (Encrypted string).
			       - Scalars: `Boolean`, `DateTime`, `Date`, `Time`, `Integer`.
			       - Numerics: `Money` (canonical), `Float` (canonical). Aliases accepted: `Currency` → `Money`, `Decimal` → `Float`.
			       - `Lookup`: requires `reference-schema-name`. Pass the entity schema name (for example `Contact` or a custom `UsrPhoneFormat`).
			       - `Binary` is intentionally absent from the advertised surface. `SysSettingsValue` has no `BinaryValue` column and `PostSysSettingsValues` is scalar-only, so MCP cannot round-trip binary payloads. Use the legacy `clio set-syssetting <code> <value> Binary` CLI plus a dedicated upload flow when binary storage is actually needed.

			       Lookup resolution rules
			       - On `create-sys-setting type=Lookup`: `reference-schema-name` is mandatory; the tool resolves the entity schema UId server-side before the row is inserted.
			       - On `update-sys-setting` for an existing Lookup: `value` may be a GUID **or** a display name. Display names are resolved server-side via a typed `SelectQuery` against the referenced entity (no client-side string substitution).
			       - The current All-Users default returned by `get-sys-setting` for a Lookup is the GUID of the selected lookup record, not the display name.

			       SecureText masking semantics
			       - Stored SecureText values are encrypted by the platform.
			       - All four MCP read paths mask the value before surfacing it: `list-sys-settings` shows `"***"` in the catalog row; `get-sys-setting`, `update-sys-setting` read-back, and `create-sys-setting` read-back all return `"***"`.
			       - Unconfigured SecureText settings (no All-Users row) return an empty string so callers can still distinguish "has secret" from "no secret".
			       - Read-back ≠ stored value for SecureText. After `update-sys-setting type=SecureText value=plaintext` the response says `"***"` even though the write succeeded. Treat `success: true` as proof of the write, not the response value.

			       Date / Time / DateTime caveat
			       - On environments where the platform `DataService` returns DateTime in server-local TZ, `Date` and `Time` settings may round-trip with a single-day or single-hour delta (for example input `2026-12-31` reads back as `2026-12-30` on a server-local `+01:00` deployment).
			       - This is platform behaviour reproducible from the legacy `clio set-syssetting` path; it is not a tool defect. Account for it in expectations; do not retry-loop chasing the delta.

			       Codes and payload safety
			       - Sys-setting codes are validated against `^[A-Za-z][A-Za-z0-9_]*$` before the platform is contacted. Codes with quotes, control characters, or whitespace are rejected client-side.
			       - String values are serialized through `JsonSerializer` (System.Text.Json) so quotes and backslashes in the value cannot break out of the JSON payload.

			       Verification discipline
			       - Read back after write when correctness matters: use `get-sys-setting` for a single code, `list-sys-settings` for catalog confirmation.
			       - The structured `success: false` envelope carries an explicit error message — read it before retrying.
			       - For SecureText writes, the read-back masking means you can't confirm the value through `get-sys-setting`; trust `success: true` from `create` / `update` instead.

			       Cross-references
			       - For application-level modeling context (when sys-settings supply default values for entity columns), see `app-modeling` guidance.
			       - For inspecting an existing app before mutating its sys-settings, see `existing-app-maintenance`.
			       - For lookup table seeding (creating the entries a Lookup sys-setting points at), see `data-bindings` guidance.

			       Anti-patterns
			       - Do not advertise `Binary` to callers as a usable MCP sys-setting type; it is excluded for a reason.
			       - Do not call `update-sys-setting` for SecureText and then try to verify the plaintext through `get-sys-setting`. The response is masked.
			       - Do not retry Date/Time writes to "fix" a TZ delta on the read side; the platform-side conversion is consistent and orthogonal to the tool.
			       - Do not invoke `set-syssetting` via raw CLI through `Bash` when the same change is achievable through `create-sys-setting` / `update-sys-setting`; the MCP path is structured, validated, and masked, the CLI path is not.
			       """
		};
}
