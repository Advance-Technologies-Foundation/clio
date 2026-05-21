namespace Clio.Command;

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
		if (!string.IsNullOrWhiteSpace(options.Body) || string.IsNullOrWhiteSpace(options.BodyFile)) {
			return (true, null);
		}
		if (!File.Exists(options.BodyFile)) {
			return (false, $"File not found: {options.BodyFile}");
		}
		options.Body = File.ReadAllText(options.BodyFile);
		return (true, null);
	}
}
