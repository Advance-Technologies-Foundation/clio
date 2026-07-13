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
	[Description("Returns canonical MCP guidance for the Creatio sys-settings CRU surface: tool order, supported value-type-names and aliases, Lookup resolution, SecureText masking, Date/Time TZ caveat, and Binary (write-only, via value-file-path) upload.")]
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
			       - All tools accept named JSON arguments wrapped under `args`. Do not pass positional CLI-style arguments.

			       Tool call shapes
			       - `list-sys-settings` — `{ "args": { "environment-name": "<env>" } }`
			       - `get-sys-setting` — `{ "args": { "environment-name": "<env>", "code": "<setting code>" } }`
			       - `update-sys-setting` — `{ "args": { "environment-name": "<env>", "code": "<setting code>", "value": "<new value>" } }` (optional `value-type-name` fallback when the setting cannot be located on the environment). For `Binary` settings pass `value-file-path` (a local file path) instead of `value`.
			       - `create-sys-setting` — `{ "args": { "environment-name": "<env>", "code": "<setting code>", "name": "<display name>", "value-type-name": "<type>", "value": "<initial>", "reference-schema-name": "<entity>" } }` (`value` and `reference-schema-name` are optional except where noted below)

			       Preferred workflows
			       - Discover then read: `list-sys-settings` → `get-sys-setting` when the code is unknown.
			       - Read then mutate: `get-sys-setting` → `update-sys-setting` for existing settings.
			       - Provision: `create-sys-setting`. When `value` is supplied, clio internally invokes `update-sys-setting` so the response already carries the read-back value.

			       Supported value-type-name
			       - Text family: `Text`, `ShortText` (Text 50), `MediumText` (Text 250), `LongText` (Text 500), `MaxSizeText` (Unlimited), `SecureText` (Encrypted string).
			       - Scalars: `Boolean`, `DateTime`, `Date`, `Time`, `Integer`.
			       - Numerics: `Money` (canonical), `Float` (canonical). Aliases accepted: `Currency` → `Money`, `Decimal` → `Float`.
			       - `Lookup`: requires `reference-schema-name`. Pass the entity schema name (for example `Contact` or a custom `UsrPhoneFormat`).
			       - `Binary` (a value stored as blob data, e.g. the logo) is WRITE-ONLY through this surface. Set the value with `update-sys-setting` using `value-file-path`: clio reads the file and Base64-encodes it locally, then sends it through the same `PostSysSettingsValues` path as every other type. (`create-sys-setting` has no `value-file-path`; its optional initial `value` accepts inline Base64 only, so for a blob value create the setting first, then upload with `update-sys-setting`.) Do NOT inline the Base64 in `value` for large blobs — a logo is thousands of tokens; pass the file path instead. Reading a `Binary` value back is not exposed through MCP: `list-sys-settings` DOES list Binary settings (so you can discover codes like `LogoImage`) but shows their value as `<binary>`, and `get-sys-setting` returns an empty value for them, because clio's `SysSettingsValue` model does not map the platform's binary media column. (If you genuinely need the raw bytes, the legacy CLI `clio get-syssetting <code>` returns the Base64 via the cliogate endpoint — but avoid piping a multi-thousand-token blob back through an agent.)

			       Lookup resolution rules
			       - On `create-sys-setting` with `value-type-name=Lookup`, `reference-schema-name` is mandatory.
			       - On `update-sys-setting` for an existing Lookup, `value` may be a GUID **or** a display name from the referenced entity. Display names are resolved before save; ambiguous matches (two or more rows sharing the same display name) are rejected with a structured error — pass the record GUID instead to disambiguate.
			       - The current All-Users default returned by `get-sys-setting` for a Lookup is the GUID of the selected record, not the display name.

			       SecureText masking semantics
			       - Stored SecureText values are encrypted by the platform.
			       - Every MCP read path masks the value before surfacing it: `list-sys-settings` shows `"***"` in the catalog row; `get-sys-setting`, `update-sys-setting` read-back, and `create-sys-setting` read-back all return `"***"`.
			       - Unconfigured SecureText settings (no All-Users row) return an empty string so callers can still distinguish "has secret" from "no secret".
			       - Read-back ≠ stored value for SecureText. After `update-sys-setting` with `value-type-name=SecureText` the response says `"***"` even though the write succeeded. Treat `success: true` as proof of the write, not the response value.

			       Date / Time / DateTime caveat
			       - On environments where the platform `DataService` returns DateTime in server-local TZ, `Date` and `Time` settings may round-trip with a single-day or single-hour delta (for example input `2026-12-31` reads back as `2026-12-30` on a server-local `+01:00` deployment).
			       - This is platform behaviour reproducible from the legacy CLI path; it is not a tool defect. Account for it in expectations; do not retry-loop chasing the delta.

			       Input validation guarantees
			       - Sys-setting codes are validated against the Creatio identifier pattern (`^[A-Za-z][A-Za-z0-9_]*$`) before any platform request is issued. Codes with quotes, control characters, whitespace, or other special characters are rejected client-side with a structured error.
			       - The `value` field may contain arbitrary printable characters including quotes, backslashes, and Unicode. clio escapes the value safely before it leaves the process — pass the raw string as-is, do not pre-escape.

			       Verification discipline
			       - Read back after write when correctness matters: use `get-sys-setting` for a single code, `list-sys-settings` for catalog confirmation.
			       - The structured `success: false` envelope carries an explicit error message — read it before retrying.
			       - For SecureText writes, the read-back masking means the plaintext cannot be confirmed through `get-sys-setting`; trust `success: true` from `create` / `update` instead.

			       Cross-references
			       - For application-level modeling context (when sys-settings supply default values for entity columns), see `app-modeling` guidance.
			       - For inspecting an existing app before mutating its sys-settings, see `existing-app-maintenance`.
			       - For lookup table seeding (creating the entries a Lookup sys-setting points at), see `data-bindings` guidance.

			       Anti-patterns
			       - Do not inline a large Binary blob (e.g. the logo) in `value`; pass `value-file-path` so clio encodes the file locally and the bytes stay out of the tool-call arguments.
			       - Do not expect a real `Binary` value from `get-sys-setting` / `list-sys-settings`; the row is listed for discovery but the value shows as `<binary>` (write-only surface for Binary).
			       - Do not call `update-sys-setting` for SecureText and then try to verify the plaintext through `get-sys-setting`. The response is masked.
			       - Do not retry Date/Time writes to "fix" a TZ delta on the read side; the platform-side conversion is consistent and orthogonal to the tool.
			       - Prefer the MCP `create-sys-setting` / `update-sys-setting` tools over shelling out to the legacy `clio set-syssetting` CLI through any shell-execution tool; the MCP path validates input, masks secrets, and returns structured errors.
			       """
		};
}
