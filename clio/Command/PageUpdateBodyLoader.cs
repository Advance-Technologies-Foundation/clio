namespace Clio.Command;

using System;
using System.IO;

/// <summary>
/// Resolves the page body for <c>update-page</c> when the caller supplies <c>--body-file</c>
/// instead of an inline <c>--body</c>. Centralized so the CLI command path and the MCP tool
/// path both apply pre-save validation and sampling against the resolved body content.
/// </summary>
internal static class PageUpdateBodyLoader {

	/// <summary>
	/// If <see cref="PageUpdateOptions.Body"/> is empty and <see cref="PageUpdateOptions.BodyFile"/>
	/// is set, loads the file content into <see cref="PageUpdateOptions.Body"/>. No-op when the
	/// inline body is already populated.
	/// </summary>
	/// <param name="options">Update-page options to mutate in place.</param>
	/// <returns>
	/// A tuple where <c>Ok</c> is <c>true</c> on success (including the no-op case) and
	/// <c>Error</c> carries a human-readable error when the file cannot be loaded.
	/// </returns>
	public static (bool Ok, string Error) TryLoadBodyFromFile(PageUpdateOptions options) {
		(bool ok, string resolvedBody, string error) = TryResolveBody(options.Body, options.BodyFile);
		if (!ok) {
			return (false, error);
		}
		options.Body = resolvedBody;
		return (true, null);
	}

	/// <summary>
	/// Options-free variant of <see cref="TryLoadBodyFromFile"/>. Resolves the effective page body from
	/// an inline body and an optional file path, so tools that carry no <see cref="PageUpdateOptions"/>
	/// (for example the read-only <c>validate-page</c> tool) apply the same <c>body</c>/<c>body-file</c>
	/// precedence and the same file-not-found wording as the save path.
	/// </summary>
	/// <param name="body">Inline body. Wins over <paramref name="bodyFile"/> when non-empty.</param>
	/// <param name="bodyFile">Absolute path to a file holding the body. Read only when <paramref name="body"/> is empty.</param>
	/// <returns>
	/// <c>Ok</c> is <c>true</c> on success (including the no-op case where neither input is supplied),
	/// <c>Body</c> carries the resolved body, and <c>Error</c> carries a human-readable error otherwise.
	/// </returns>
	public static (bool Ok, string Body, string Error) TryResolveBody(string body, string bodyFile) {
		if (!string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(bodyFile)) {
			return (true, body, null);
		}
		if (!File.Exists(bodyFile)) {
			return (false, null, $"File not found: {bodyFile}");
		}
		// A path that exists can still be unreadable — no read permission, an exclusive lock held by an editor.
		// Those must reach the caller as the tool's own error envelope, not as a protocol-level MCP failure.
		try {
			return (true, File.ReadAllText(bodyFile), null);
		} catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
			return (false, null, $"Cannot read {bodyFile}: {exception.Message}");
		}
	}
}
