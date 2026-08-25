namespace Clio.Mcp.E2E.Support;

/// <summary>
/// Resolves a directory to its physical location, following symbolic links at every level.
/// </summary>
/// <remarks>
/// Clio refuses a trusted public-key path whose ancestry contains a reparse point, which is a real
/// safety rule and not something a fixture should weaken. The system temp directory is itself a
/// symlink on some platforms (macOS resolves <c>/var</c> to <c>/private/var</c>), so a fixture that
/// hands Clio the unresolved path is rejected for reasons that have nothing to do with the behavior
/// under test. Resolving up front keeps the production rule intact and the fixture portable: on
/// platforms with no linked ancestor this is the identity function.
/// </remarks>
internal static class PhysicalPath {

	/// <summary>
	/// Returns the fully resolved physical path for <paramref name="path"/>.
	/// </summary>
	/// <param name="path">An absolute or relative directory path.</param>
	/// <returns>The path with every linked ancestor replaced by its target.</returns>
	internal static string Resolve(string path) {
		string full = Path.GetFullPath(path);
		string? parent = Path.GetDirectoryName(full);
		if (parent is null) {
			return full;
		}
		string candidate = Path.Combine(Resolve(parent), Path.GetFileName(full));
		return Directory.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName ?? candidate;
	}
}
