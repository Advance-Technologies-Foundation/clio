using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for creating and editing Creatio Freedom UI custom themes in a clio workspace.
/// </summary>
[McpServerResourceType]
public sealed class CreatioThemeGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/creatio-theme";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	[McpServerResource(UriTemplate = ResourceUri, Name = "creatio-theme-guidance")]
	[Description("Returns canonical MCP guidance for Creatio Freedom UI custom themes: the theme.json + theme.css artifact, the --crt-* token contract, platform :root primitives that must not be redefined, font integration (local and Google Fonts), scaffolding a theme with new-theme, and activation (push-workspace + clear-redis-db).")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP Creatio theme guide

		       Scope
		       - Use this guide when the user asks to create, edit, or delete a Creatio Freedom UI custom theme in a clio workspace.
		       - Themes are supported on Creatio 10.x and later.

		       What a theme is
		       - A theme is a FILE artifact inside a package, NOT a database entity. It is two files:
		         `<package>/Files/themes/<cssClassName>/theme.json` and `theme.css`.
		       - `theme.json` holds exactly three fields: `{ "id", "caption", "cssClassName" }`.
		         - id: stable identifier, regex `^[A-Za-z0-9_-]+$`, max 100 chars. A UUID is a valid id and is the default.
		         - caption: human-readable name, max 250 chars.
		         - cssClassName: the root CSS class the theme is scoped under, regex `^[A-Za-z][A-Za-z0-9_-]*$`, max 100 chars.
		       - `theme.css` contains all theme variables scoped under `.<cssClassName> { ... }`.

		       Token contract (theme.css)
		       - Everything lives under the single root selector `.<cssClassName>`.
		       - Semantic colors: background / border / text / icon, each with Base and Role variants
		         (primary, secondary, accent, error, success) and states (hover, selected, subtle, soft, on-*).
		         These are defined via `var(--crt-palette-*)` / `var(--crt-color-*)`.
		       - Typography mapping: large-1..4, headline-1..5, body-1..2, caption, button, button-small, overline.
		         These reference `var(--crt-font-family-heading|body)`, `var(--crt-font-size-*)`,
		         `var(--crt-font-weight-*)`, `var(--crt-line-height-*)`.
		       - Palettes: primary, secondary, accent, neutral, error, success — shades 10,25,50,100..900 (concrete hex).

		       Platform :root primitives — DO NOT redefine in a theme
		       - These live globally on `:root` and are owned by the platform. A theme only CONSUMES them via
		         `var(...)`; it must NOT declare them inside `.<cssClassName>`:
		         `--crt-color-base-light` / `--crt-color-base-dark`, `--crt-radius-*`, `--crt-spacing-*`,
		         `--crt-font-size-*`, `--crt-line-height-*`, `--crt-font-weight-*`, and the glassmorphism tokens
		         (`--crt-glass-color-light/dark-*`, `--crt-color-text/icon-glassmorphic-*`).
		       - Safe to change: palette hex values, semantic-color mappings, typography sizes/weights, and the font.
		       - Strongly discouraged: redefining the :root primitives, renaming `--crt-*` variables, removing whole
		         token blocks, or introducing non-`--crt-*` custom properties.

		       Fonts
		       - The theme owns the font choice, the value of `--crt-font-family-heading` / `--crt-font-family-body`,
		         and the import of the font stylesheet. The default baseline font is `Montserrat`.
		       - Local fonts: place `@font-face` declarations in `<package>/Files/fonts/<fontCode>/<fontCode>.css` next
		         to the `.woff` files, then from theme.css `@import '../../fonts/<fontCode>/<fontCode>.css';` and set
		         `--crt-font-family-*` accordingly.
		       - Google Fonts: `@import url('https://fonts.googleapis.com/css2?family=...');` at the top of theme.css,
		         then set `--crt-font-family-*`. The imported family name must match the value assigned.

		       Creating a theme with new-theme
		       - `new-theme <cssClassName> --package <PackageName>` scaffolds theme.json + theme.css from the baseline.
		       - Optional `--caption` (defaults to Title Case of cssClassName) and `--id`
		         (defaults to a UUID).
		       - A new theme is created fully from the baseline; colors, tokens and font are SEPARATE edits afterward.
		       - The hosting package is created if it does not exist.

		       Editing and deleting a theme
		       - There is no dedicated edit or delete command; in a workspace you change the files directly:
		         - edit colors / tokens / font by editing `theme.css`;
		         - rename a theme by renaming BOTH the `Files/themes/<cssClassName>` folder and the `.<cssClassName>`
		           root selector inside theme.css;
		         - delete a theme by deleting its `Files/themes/<cssClassName>` folder.
		       - After any change, re-deliver the package (see Activation).

		       Activation
		       - After `push-workspace`, run `clear-redis-db` so the environment drops its cached theme list and serves
		         the new or updated theme. Static theme files do not require a compile.

		       Common mistakes to avoid
		       - Do NOT put `:root` primitives (radius, spacing, font-size, line-height, font-weight, base colors, glass)
		         inside the theme — reference them with `var(...)` instead.
		       - Do NOT rename or drop `--crt-*` variables, and do NOT add non-`--crt-*` custom properties.
		       - Do NOT expect `new-theme` to reach a Creatio environment — it is local scaffolding; deliver with
		         `push-workspace` + `clear-redis-db`.
		       """
	};
}
