using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Clio.Theming;

/// <summary>
/// The single home for validating and resolving the input parameters of a Creatio theme — the css-class-name
/// (its character rule, plus derivation from a caption when omitted), the id, the caption, and the css-content
/// size. Each rule is the contract enforced by the native <c>ThemeService</c> and by the local theme build;
/// the CSS builder validates the css-class-name through <see cref="IsValidCssClassName"/> here, so the rule
/// lives once and cannot drift between the enforcement points. Stateless and deterministic.
/// </summary>
internal static class ThemeParameterValidator {

	/// <summary>Maximum accepted <c>cssContent</c> size in bytes (1 MiB), matching the server contract.</summary>
	internal const int MaxCssContentBytes = 1024 * 1024;

	/// <summary>Maximum accepted css-class-name length.</summary>
	internal const int MaxCssClassNameLength = 100;

	/// <summary>Maximum accepted theme id length.</summary>
	internal const int MaxIdLength = 100;

	/// <summary>Maximum accepted theme caption length.</summary>
	internal const int MaxCaptionLength = 250;

	private const string CssClassNameFallback = "theme";

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	/// <summary>The css-class-name character rule: starts with a letter; letters, digits, hyphen, underscore only.</summary>
	private static readonly Regex CssClassNamePattern = new(@"^[A-Za-z][A-Za-z0-9_-]*\z", RegexOptions.Compiled, RegexTimeout);
	private static readonly Regex IdPattern = new(@"^[A-Za-z0-9_-]+\z", RegexOptions.Compiled, RegexTimeout);

	/// <summary>Collapses each run of characters outside <c>[a-z0-9]</c> into a single hyphen.</summary>
	private static readonly Regex NonSlugRun = new("[^a-z0-9]+", RegexOptions.Compiled, RegexTimeout);

	private static bool IsMatchSafe(Regex regex, string value) {
		try {
			return regex.IsMatch(value);
		}
		catch (RegexMatchTimeoutException) {
			return false;
		}
	}

	/// <summary>
	/// Whether <paramref name="value"/> is a fully valid css-class-name: non-empty, at most
	/// <see cref="MaxCssClassNameLength"/> characters, and matching the character rule. This is the shared
	/// predicate both the CSS builder and the resolve path use, so the rule is defined once.
	/// </summary>
	/// <param name="value">The candidate css-class-name.</param>
	/// <returns><c>true</c> when the value satisfies every part of the contract.</returns>
	internal static bool IsValidCssClassName(string value) {
		return !string.IsNullOrEmpty(value) && value.Length <= MaxCssClassNameLength && IsMatchSafe(CssClassNamePattern, value);
	}

	/// <summary>
	/// Derives a valid css-class-name from a human caption, or <c>null</c> when the caption is empty.
	/// Lowercases, replaces runs of non-alphanumerics with a single hyphen, trims hyphens, guarantees a
	/// leading letter, caps the length at <see cref="MaxCssClassNameLength"/>, and falls back to <c>theme</c>
	/// when nothing usable remains.
	/// </summary>
	/// <param name="caption">The human theme caption to derive a class name from.</param>
	/// <returns>A value matching <c>^[A-Za-z][A-Za-z0-9_-]*$</c> (≤100), or <c>null</c> for an empty caption.</returns>
	internal static string DeriveCssClassNameFromCaption(string caption) {
		if (string.IsNullOrWhiteSpace(caption)) {
			return null;
		}
		string slug;
		try {
			slug = NonSlugRun.Replace(caption.Trim().ToLowerInvariant(), "-").Trim('-');
		}
		catch (RegexMatchTimeoutException) {
			slug = CssClassNameFallback;
		}
		if (slug.Length == 0) {
			slug = CssClassNameFallback;
		}
		if (!char.IsLetter(slug[0])) {
			slug = "t-" + slug;
		}
		if (slug.Length > MaxCssClassNameLength) {
			slug = slug.Substring(0, MaxCssClassNameLength).TrimEnd('-');
		}
		return slug;
	}

	/// <summary>
	/// Resolves the effective css-class-name: an explicit value (validated via <see cref="IsValidCssClassName"/>)
	/// wins; otherwise it is derived from the caption via <see cref="DeriveCssClassNameFromCaption"/>. Because the
	/// resolved value becomes both a CSS identifier and a filesystem path segment, a malformed explicit value is
	/// rejected here rather than downstream.
	/// </summary>
	/// <param name="cssClassName">The explicit css-class-name, if any.</param>
	/// <param name="caption">The human caption to derive the class name from when none was supplied.</param>
	/// <param name="resolved">The effective css-class-name on success; otherwise <c>null</c>.</param>
	/// <param name="error">A user-friendly diagnostic when both inputs are empty or the explicit value is
	/// malformed; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when a valid class name was supplied or derived; <c>false</c> when both are empty
	/// or the explicit value violates the css-class-name rule.</returns>
	internal static bool TryResolveCssClassName(string cssClassName, string caption, out string resolved, out string error) {
		error = null;
		if (!string.IsNullOrWhiteSpace(cssClassName)) {
			if (!IsValidCssClassName(cssClassName)) {
				resolved = null;
				error = "css-class-name must match ^[A-Za-z][A-Za-z0-9_-]*$ (start with a letter; letters, digits, "
					+ $"hyphen, underscore only) and be at most {MaxCssClassNameLength} characters. "
					+ $"Received: '{cssClassName}'.";
				return false;
			}
			resolved = cssClassName;
			return true;
		}
		resolved = DeriveCssClassNameFromCaption(caption);
		if (resolved is null) {
			error = "Provide a caption (the theme name) or a css-class-name — at least one is required.";
			return false;
		}
		return true;
	}

	/// <summary>
	/// Validates a theme <c>id</c> against the server contract (<c>^[A-Za-z0-9_-]+$</c>, ≤100 chars).
	/// </summary>
	/// <param name="id">The theme id to validate.</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the id is valid.</returns>
	internal static bool TryValidateId(string id, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(id)) {
			error = "Theme id is required.";
			return false;
		}
		if (id.Length > MaxIdLength) {
			error = $"Theme id must be at most {MaxIdLength} characters.";
			return false;
		}
		if (!IsMatchSafe(IdPattern, id)) {
			error = "Theme id must match ^[A-Za-z0-9_-]+$ (letters, digits, underscore, hyphen).";
			return false;
		}
		return true;
	}

	/// <summary>
	/// Validates an explicit css-class-name against the shared rule (required, ≤100 chars, matching the
	/// character pattern).
	/// </summary>
	/// <param name="cssClassName">The css-class-name to validate.</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the css-class-name satisfies the contract.</returns>
	internal static bool TryValidateCssClassName(string cssClassName, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(cssClassName)) {
			error = "css-class-name is required.";
			return false;
		}
		if (!IsValidCssClassName(cssClassName)) {
			error = cssClassName.Length > MaxCssClassNameLength
				? $"css-class-name must be at most {MaxCssClassNameLength} characters."
				: "css-class-name must match ^[A-Za-z][A-Za-z0-9_-]*$ (start with a letter; letters, digits, "
					+ "hyphen, underscore only).";
			return false;
		}
		return true;
	}

	/// <summary>
	/// Validates a theme caption (required, ≤250 chars).
	/// </summary>
	/// <param name="caption">The caption to validate.</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the caption satisfies the contract.</returns>
	internal static bool TryValidateCaption(string caption, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(caption)) {
			error = "Theme caption is required.";
			return false;
		}
		if (caption.Length > MaxCaptionLength) {
			error = $"Theme caption must be at most {MaxCaptionLength} characters.";
			return false;
		}
		return true;
	}

	/// <summary>
	/// Validates the resolved theme CSS content (required, non-empty, at most <see cref="MaxCssContentBytes"/>
	/// when UTF-8 encoded).
	/// </summary>
	/// <param name="cssContent">The resolved CSS content.</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the content is present, non-empty, and within the size limit.</returns>
	internal static bool TryValidateCssContent(string cssContent, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(cssContent)) {
			error = "Theme CSS content is required and cannot be empty.";
			return false;
		}
		if (Encoding.UTF8.GetByteCount(cssContent) > MaxCssContentBytes) {
			error = "Theme CSS content must be at most 1 MiB.";
			return false;
		}
		return true;
	}
}
