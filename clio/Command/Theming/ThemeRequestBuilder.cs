using System;
using System.IO;
using System.Linq;
using Clio.Common;
using Clio.Theming;

namespace Clio.Command.Theming;

/// <summary>
/// Resolves the theme CSS from the mutually-exclusive <c>--css-content</c> / <c>--css-content-file</c> inputs,
/// assembles the native Creatio <c>ThemeService</c> write request, and validates its field contract by
/// delegating each parameter rule to <see cref="ThemeParameterValidator"/>.
/// </summary>
internal static class ThemeRequestBuilder
{
	/// <summary>
	/// Resolves the theme CSS from exactly one of the inline (<paramref name="cssContent"/>) or file
	/// (<paramref name="cssContentFile"/>) inputs. An unsupplied string option is <c>null</c> (absent).
	/// </summary>
	/// <param name="fileSystem">The file system used to read <paramref name="cssContentFile"/>.</param>
	/// <param name="cssContent">Inline CSS value (<c>null</c> when the flag was not supplied).</param>
	/// <param name="cssContentFile">Path to a UTF-8 CSS file (<c>null</c>/blank when not supplied).</param>
	/// <param name="resolved">On success, the resolved CSS string.</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when exactly one input resolved to a CSS string; otherwise <c>false</c>.</returns>
	public static bool TryResolveCssContent(IFileSystem fileSystem, string cssContent, string cssContentFile,
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
		if (!hasFile) {
			resolved = cssContent;
			return true;
		}
		if (!fileSystem.ExistsFile(cssContentFile)) {
			error = $"CSS file not found: '{cssContentFile}'.";
			return false;
		}
		try {
			if (fileSystem.GetFileSize(cssContentFile) > ThemeParameterValidator.MaxCssContentBytes) {
				error = "Theme CSS content must be at most 1 MiB.";
				return false;
			}
			resolved = fileSystem.ReadAllText(cssContentFile);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			error = $"Could not read CSS file '{cssContentFile}': {ex.Message}";
			return false;
		}
		return true;
	}

	/// <summary>
	/// Validates the full create/update field contract carried by <paramref name="request"/>: id, caption,
	/// cssClassName, and the already-resolved cssContent. Each parameter rule is delegated to
	/// <see cref="ThemeParameterValidator"/>.
	/// </summary>
	/// <param name="request">The theme request whose contract fields are validated.</param>
	/// <param name="error">On failure, a user-friendly diagnostic; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when every field satisfies the contract.</returns>
	public static bool TryValidateRequest(ThemeRequest request, out string error) {
		if (!ThemeParameterValidator.TryValidateId(request.Id, out error)) {
			return false;
		}
		if (!ThemeParameterValidator.TryValidateCssClassName(request.CssClassName, out error)) {
			return false;
		}
		if (!ThemeParameterValidator.TryValidateCaption(request.Caption, out error)) {
			return false;
		}
		if (!ThemeParameterValidator.TryValidateCssContent(request.CssContent, out error)) {
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
