namespace Clio.Mcp.E2E.Support;

/// <summary>
/// Creates and removes the fixture-owned temporary directories of tests that do not derive from
/// <see cref="Mcp.McpContractFixtureBase"/>.
/// </summary>
internal static class ScratchDirectory {

	/// <summary>
	/// Creates a uniquely named directory under the physical OS temp root.
	/// </summary>
	/// <remarks>
	/// The OS temp directory is a symlink on macOS and clio reports symlink-resolved paths, so a directory under
	/// the resolved root lets a test compare a path clio reports with the path it requested.
	/// </remarks>
	/// <param name="prefix">Filesystem-safe prefix that identifies the test.</param>
	/// <returns>The full path of the created directory.</returns>
	internal static string CreateUnderPhysicalTemp(string prefix) {
		string path = Path.Combine(PhysicalPath.Resolve(Path.GetTempPath()), $"{prefix}-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	/// <summary>
	/// Deletes <paramref name="path"/> recursively, ignoring the IO and access failures a child process that
	/// still holds a handle can cause: a leaked Guid-named directory is harmless and must not fail a test.
	/// </summary>
	/// <param name="path">Directory to delete.</param>
	internal static void TryDelete(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException) { /* best-effort cleanup */ }
		catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
	}
}
