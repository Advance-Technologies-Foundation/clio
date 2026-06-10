using System;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Raised when no Chromium-based browser executable can be located on the host. Surfaced by
/// <see cref="IChromiumLocator.Locate"/> and turned into the canonical AC-04 error by the
/// <c>open-web-app --authenticated</c> command — the command must <b>not</b> fall back to an
/// unauthenticated browser launch when this is thrown.
/// </summary>
public sealed class ChromiumNotFoundException : Exception {
	/// <summary>Creates the exception with a user-facing, actionable message.</summary>
	/// <param name="message">A message that names the failure and the remedy (install a browser / set CHROME_PATH).</param>
	public ChromiumNotFoundException(string message) : base(message) { }
}
