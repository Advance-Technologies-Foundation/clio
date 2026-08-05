using System.IO;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Supplies a temporary directory root whose ancestry contains no symbolic link, for fixtures that
/// write trust material a knowledge trust store then has to accept.
/// </summary>
/// <remarks>
/// The trust store refuses any public-key path with a reparse point (symbolic link) anywhere in its
/// existing ancestry, which is a deliberate guard. On macOS the default temporary root is
/// <c>/var/folders/…</c> and <c>/var</c> itself is a symbolic link to <c>/private/var</c>, so a
/// fixture writing under <see cref="Path.GetTempPath"/> is refused for the platform's own reason
/// rather than for anything the test arranged. Resolving the link once here keeps those fixtures
/// runnable on macOS, Linux and Windows alike without relaxing the guard under test.
/// </remarks>
internal static class KnowledgeTrustTestPaths {
	/// <summary>
	/// Gets the temporary directory root with every symbolic link in its ancestry resolved.
	/// </summary>
	internal static string ResolvedTempRoot { get; } = ResolveTempRoot();

	private static string ResolveTempRoot() {
		string root = Path.GetFullPath(Path.GetTempPath());
		string? current = root;
		while (!string.IsNullOrEmpty(current)) {
			FileSystemInfo? target = Directory.Exists(current)
				? Directory.ResolveLinkTarget(current, returnFinalTarget: true)
				: null;
			if (target is not null) {
				string relative = Path.GetRelativePath(current, root);
				return relative is "."
					? Path.GetFullPath(target.FullName)
					: Path.GetFullPath(Path.Combine(target.FullName, relative));
			}
			string? parent = Path.GetDirectoryName(current);
			current = parent == current ? null : parent;
		}
		return root;
	}
}
