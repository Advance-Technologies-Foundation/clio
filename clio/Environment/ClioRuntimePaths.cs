using System.IO;

namespace Clio;

/// <summary>
/// Central resolver for clio's per-user local directories. Every on-disk location for
/// configuration, cache, and state derives from a single home root, so the layout stays
/// consistent across platforms and can be relocated wholesale via the <c>CLIO_HOME</c>
/// environment variable. See <c>docs/architecture/clio-home-consolidation.md</c>.
/// </summary>
public static class ClioRuntimePaths {

	/// <summary>
	/// The single home root for clio's per-user state. Equals
	/// <see cref="SettingsRepository.AppSettingsFolderPath"/> (which honors <c>CLIO_HOME</c>):
	/// <c>~/creatio/clio</c> on macOS/Linux, <c>%LOCALAPPDATA%\creatio\clio</c> on Windows.
	/// </summary>
	public static string Home => SettingsRepository.AppSettingsFolderPath;

	/// <summary>
	/// Root for disposable cached artifacts (component registry, docker assets). Safe to
	/// delete at any time: entries re-populate from their source (CDN, bundled assets) on
	/// next use. A cache-clear operation must never reach outside this subtree.
	/// </summary>
	public static string CacheRoot => Path.Combine(Home, "cache");
}
