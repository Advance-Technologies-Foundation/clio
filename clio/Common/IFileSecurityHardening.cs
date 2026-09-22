namespace Clio.Common;

/// <summary>
/// Restricts a file or directory to owner-only access. Used for on-disk artifacts that hold
/// bearer credentials (e.g. a Playwright storageState with live Creatio cookies) so they are not
/// left world-readable under the process umask.
/// </summary>
public interface IFileSecurityHardening {
	/// <summary>
	/// Restricts <paramref name="filePath"/> to the current owner (Unix mode <c>0600</c>;
	/// current-user-only ACL on Windows).
	/// </summary>
	/// <param name="filePath">Absolute path to an existing file.</param>
	void HardenFile(string filePath);

	/// <summary>
	/// Restricts <paramref name="directoryPath"/> to the current owner (Unix mode <c>0700</c>;
	/// current-user-only ACL on Windows).
	/// </summary>
	/// <param name="directoryPath">Absolute path to an existing directory.</param>
	void HardenDirectory(string directoryPath);

	/// <summary>
	/// Reports whether <paramref name="filePath"/> is readable by its owner only.
	/// </summary>
	/// <param name="filePath">Absolute path to an existing file.</param>
	/// <returns>
	/// False when group or other hold any permission on the file. Always true on Windows, where the
	/// per-user profile ACL is the mechanism (see the class summary on <see cref="FileSecurityHardening"/>).
	/// </returns>
	bool IsOwnerOnly(string filePath);
}
