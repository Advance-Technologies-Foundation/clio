using System;
using System.Text.RegularExpressions;

namespace Clio.Command.Theming;

/// <summary>
/// Derives a valid theme css-class-name (<c>^[A-Za-z][A-Za-z0-9_-]*$</c>, ≤100) from a human theme caption,
/// and resolves the effective css-class-name for a theme: an explicit value wins; otherwise it is slugified
/// from the caption. Deterministic — the palette/name conversation supplies a single human name and clio
/// turns it into the machine identifier, so the agent never hand-rolls the slug (and the human name is
/// preserved as the caption rather than being replaced by a slug).
/// </summary>
internal static class ThemeCssClassName {

	private const int MaxLength = 100;
	private const string Fallback = "theme";
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	/// <summary>Collapses each run of characters outside <c>[a-z0-9]</c> into a single hyphen.</summary>
	private static readonly Regex NonSlugRun = new("[^a-z0-9]+", RegexOptions.Compiled, RegexTimeout);

	/// <summary>
	/// Slugifies a human caption into a valid css-class-name, or <c>null</c> when the caption is empty.
	/// Lowercases, replaces runs of non-alphanumerics with a single hyphen, trims hyphens, guarantees a
	/// leading letter, caps the length at 100, and falls back to <c>theme</c> when nothing usable remains.
	/// </summary>
	/// <param name="caption">The human theme caption to derive a class name from.</param>
	/// <returns>A value matching <c>^[A-Za-z][A-Za-z0-9_-]*$</c> (≤100), or <c>null</c> for an empty caption.</returns>
	internal static string Slugify(string caption) {
		if (string.IsNullOrWhiteSpace(caption)) {
			return null;
		}
		string slug = NonSlugRun.Replace(caption.Trim().ToLowerInvariant(), "-").Trim('-');
		if (slug.Length == 0) {
			slug = Fallback;
		}
		if (!char.IsLetter(slug[0])) {
			slug = "t-" + slug;
		}
		if (slug.Length > MaxLength) {
			slug = slug.Substring(0, MaxLength).TrimEnd('-');
		}
		return slug;
	}

	/// <summary>
	/// Resolves the effective css-class-name: returns <paramref name="cssClassName"/> as-is when supplied,
	/// otherwise the slug of <paramref name="caption"/>. Fails only when both are empty.
	/// </summary>
	/// <param name="cssClassName">The explicit css-class-name, if any.</param>
	/// <param name="caption">The human caption to derive the class name from when none was supplied.</param>
	/// <param name="resolved">The effective css-class-name on success; otherwise <c>null</c>.</param>
	/// <param name="error">A user-friendly diagnostic when both inputs are empty; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when a class name was supplied or derived; <c>false</c> when both are empty.</returns>
	internal static bool TryResolve(string cssClassName, string caption, out string resolved, out string error) {
		error = null;
		if (!string.IsNullOrWhiteSpace(cssClassName)) {
			resolved = cssClassName;
			return true;
		}
		resolved = Slugify(caption);
		if (resolved is null) {
			error = "Provide a caption (the theme name) or a css-class-name — at least one is required.";
			return false;
		}
		return true;
	}
}
