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

	/// <summary>The <c>CHROME_PATH</c> override when it is set and names an existing file; otherwise null.</summary>
	string ChromePathOverride();

	/// <summary>Every Chromium-based browser found in the standard install locations, in preference order.</summary>
	System.Collections.Generic.IReadOnlyList<InstalledBrowser> FindInstalled();
}

/// <summary>A Chromium-based browser installed on the machine.</summary>
/// <param name="Name">For example <c>Microsoft Edge</c>.</param>
/// <param name="Version">Its version when it can be read without starting it; otherwise null.</param>
/// <param name="Path">The executable.</param>
public sealed record InstalledBrowser(string Name, string Version, string Path);
