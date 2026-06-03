namespace Clio.Common.Skills;

/// <summary>
/// Copies the toolkit plugin runtime surface into Cursor's local plugins directory.
/// </summary>
public interface ICursorPluginRuntimeInstaller {
	/// <summary>
	/// Removes and re-copies the files/directories listed in the source's
	/// <c>.release-manifest.json</c> <c>plugin_runtime</c> array into the target.
	/// </summary>
	/// <param name="sourceRoot">Resolved toolkit checkout/extract root.</param>
	/// <param name="targetPluginDir">Destination plugin directory under Cursor.</param>
	void Install(string sourceRoot, string targetPluginDir);
}
