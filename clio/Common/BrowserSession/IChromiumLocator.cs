namespace Clio.Common.BrowserSession;

/// <summary>
/// Locates a Chromium-based browser executable on the host so the <c>open-web-app --authenticated</c>
/// flow can launch it directly with a remote-debugging port (the macOS <c>open</c> indirection yields
/// no CDP handle, so a concrete binary path is required).
/// </summary>
public interface IChromiumLocator {
	/// <summary>
	/// Resolves the absolute path to a Chromium-based browser. Honors the <c>CHROME_PATH</c>
	/// environment variable first, then probes the standard install locations for the current OS.
	/// </summary>
	/// <returns>The absolute path to a browser executable.</returns>
	/// <exception cref="ChromiumNotFoundException">No Chromium-based browser could be located.</exception>
	string Locate();
}
