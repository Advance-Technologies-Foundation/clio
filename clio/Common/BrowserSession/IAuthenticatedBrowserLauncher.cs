using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Launches a Chromium-based browser and injects a harvested Creatio session into it via the Chrome
/// DevTools Protocol (CDP) before navigating to the environment, so the user lands on an authenticated
/// page without seeing the login form. This is "Mode A" of the browser-session-handoff feature; it
/// requires a real browser and CDP transport, so it is exercised by E2E rather than unit tests.
/// </summary>
public interface IAuthenticatedBrowserLauncher {
	/// <summary>
	/// Launches a browser with remote debugging enabled, injects every cookie from
	/// <paramref name="storageStatePath"/> via CDP <c>Network.setCookie</c> (including HttpOnly cookies,
	/// which <c>document.cookie</c> cannot set), and navigates to <paramref name="env"/>'s URI.
	/// </summary>
	/// <param name="env">Target environment (its <c>Uri</c> is the post-login navigation target).</param>
	/// <param name="storageStatePath">Path to a Playwright storageState file produced by the session service.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <exception cref="ChromiumNotFoundException">No Chromium-based browser could be located.</exception>
	Task LaunchAsync(EnvironmentSettings env, string storageStatePath, CancellationToken ct = default);
}
