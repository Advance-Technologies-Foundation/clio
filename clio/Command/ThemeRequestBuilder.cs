using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Clio.Command;

/// <summary>
/// Resolves the theme CSS from the mutually-exclusive <c>--css-content</c> / <c>--css-content-file</c> inputs
/// and validates the field contract enforced by the native Creatio <c>ThemeService</c> (id, caption,
/// cssClassName, cssContent).
/// </summary>
internal static class ThemeRequestBuilder
{
	/// <summary>Maximum accepted <c>cssContent</c> size in bytes (1 MiB), matching the server contract.</summary>
	internal const int MaxCssContentBytes = 1024 * 1024;

	private const int MaxIdLength = 100;
	private const int MaxCaptionLength = 250;
	private const int MaxCssClassNameLength = 100;

	private static readonly Regex IdRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
	private static readonly Regex CssClassNameRegex = new("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.Compiled);

	/// <summary>
	/// Resolves the theme CSS from exactly one of the inline (<paramref name="cssContent"/>) or file
	/// (<paramref name="cssContentFile"/>) inputs. An unsupplied string option is <c>null</c> (absent), while an
	/// explicitly empty <c>--css-content ""</c> is <c>""</c> (present, valid empty CSS).
	/// </summary>
	/// <param name="cssContent">Inline CSS value (<c>null</c> when the flag was not supplied).</param>
	/// <param name="cssContentFile">Path to a UTF-8 CSS file (<c>null</c>/blank when not supplied).</param>
	/// <param name="resolved">On success, the resolved CSS string (may be empty).</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when exactly one input resolved to a CSS string; otherwise <c>false</c>.</returns>
	public static bool TryResolveCssContent(string cssContent, string cssContentFile,
		out string resolved, out string error) {
		resolved = null;
		error = null;
		bool hasInline = cssContent is not null;
		bool hasFile = !string.IsNullOrWhiteSpace(cssContentFile);
		if (hasInline && hasFile) {
			error = "Specify either --css-content or --css-content-file, not both.";
			return false;
		}
		if (!hasInline && !hasFile) {
			error = "Theme CSS is required: provide it inline with --css-content or from a file with --css-content-file.";
			return false;
		}
		if (hasFile) {
			if (!File.Exists(cssContentFile)) {
				error = $"CSS file not found: '{cssContentFile}'.";
				return false;
			}
			try {
				resolved = File.ReadAllText(cssContentFile, Encoding.UTF8);
			}
			catch (IOException ex) {
				error = $"Could not read CSS file '{cssContentFile}': {ex.Message}";
				return false;
			}
			catch (UnauthorizedAccessException ex) {
				error = $"Could not read CSS file '{cssContentFile}': {ex.Message}";
				return false;
			}
			return true;
		}
		resolved = cssContent;
		return true;
	}

	/// <summary>
	/// Validates a theme <c>id</c> against the server contract (<c>^[A-Za-z0-9_-]+$</c>, ≤100 chars).
	/// </summary>
	/// <param name="id">The theme id to validate.</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when the id is valid.</returns>
	public static bool TryValidateId(string id, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(id)) {
			error = "Theme id is required.";
			return false;
		}
		if (id.Length > MaxIdLength) {
			error = $"Theme id must be at most {MaxIdLength} characters.";
			return false;
		}
		if (!IdRegex.IsMatch(id)) {
			error = "Theme id must match ^[A-Za-z0-9_-]+$ (letters, digits, underscore, hyphen).";
			return false;
		}
		return true;
	}

	/// <summary>
	/// Validates the full create/update field contract carried by <paramref name="request"/>: id, caption,
	/// cssClassName, and the already-resolved cssContent.
	/// </summary>
	/// <param name="request">The theme request whose contract fields are validated.</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when every field satisfies the contract.</returns>
	public static bool TryValidateRequest(ThemeRequest request, out string error) {
		if (!TryValidateId(request.Id, out error)) {
			return false;
		}
		if (string.IsNullOrWhiteSpace(request.CssClassName)) {
			error = "css-class-name is required.";
			return false;
		}
		if (request.CssClassName.Length > MaxCssClassNameLength) {
			error = $"css-class-name must be at most {MaxCssClassNameLength} characters.";
			return false;
		}
		if (!CssClassNameRegex.IsMatch(request.CssClassName)) {
			error = "css-class-name must match ^[A-Za-z][A-Za-z0-9_-]*$ (start with a letter).";
			return false;
		}
		if (string.IsNullOrWhiteSpace(request.Caption)) {
			error = "Theme caption is required.";
			return false;
		}
		if (request.Caption.Length > MaxCaptionLength) {
			error = $"Theme caption must be at most {MaxCaptionLength} characters.";
			return false;
		}
		if (request.CssContent is null) {
			error = "Theme CSS content is required.";
			return false;
		}
		if (Encoding.UTF8.GetByteCount(request.CssContent) > MaxCssContentBytes) {
			error = "Theme CSS content must be at most 1 MiB.";
			return false;
		}
		error = null;
		return true;
	}

	/// <summary>
	/// Derives a human-readable caption from a CSS class name: drops a trailing <c>theme</c> segment, splits on
	/// <c>-</c>/<c>_</c> (collapsing repeats), and Title-Cases each word — e.g. <c>ocean-theme</c> → <c>Ocean</c>,
	/// <c>my-cool-theme</c> → <c>My Cool</c>, <c>freedom</c> → <c>Freedom</c>. Returns an empty string for blank input.
	/// </summary>
	/// <param name="cssClassName">The CSS class name to derive a caption from.</param>
	/// <returns>The derived caption, or an empty string when <paramref name="cssClassName"/> is blank.</returns>
	public static string DeriveCaptionFromCssClassName(string cssClassName) {
		if (string.IsNullOrWhiteSpace(cssClassName)) {
			return string.Empty;
		}
		string[] words = cssClassName.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
		if (words.Length > 1 && words[^1].Equals("theme", StringComparison.OrdinalIgnoreCase)) {
			words = words[..^1];
		}
		return string.Join(' ', words.Select(word =>
			word.Length == 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..]));
	}
}
