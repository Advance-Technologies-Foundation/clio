using System;
using System.Text.RegularExpressions;

namespace Clio.Theming;

/// <summary>
/// The single home for reading a palette's base (-500) declaration from a theme CSS template, so the
/// template token contract is encoded once for every consumer (the CSS builder's system-colour defaults
/// and the palette advisor's preview).
/// </summary>
internal static class ThemeTemplateDefaults {

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	/// <summary>
	/// Tries to read the literal <c>#rrggbb</c> value of the <c>--crt-palette-{role}-500</c> declaration
	/// from <paramref name="templateCss"/>.
	/// </summary>
	/// <param name="templateCss">The theme CSS template to inspect.</param>
	/// <param name="role">The palette role name (for example <c>success</c> or <c>error</c>).</param>
	/// <param name="hex">The lowercase <c>#rrggbb</c> on success; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the declaration carries a literal hex value.</returns>
	internal static bool TryGetPaletteBase(string templateCss, string role, out string hex) {
		hex = null;
		Match match = Regex.Match(
			templateCss,
			$@"--crt-palette-{Regex.Escape(role)}-500\s*:\s*(#[0-9a-fA-F]{{6}})",
			RegexOptions.IgnoreCase,
			RegexTimeout);
		if (!match.Success) {
			return false;
		}
		hex = match.Groups[1].Value.ToLowerInvariant();
		return true;
	}
}
