using System.Collections.Generic;

namespace Clio.Common.BrowserSession;

/// <summary>
/// A harvested Creatio session expressed as a Playwright-compatible storageState (cookies only;
/// the <c>origins</c> array is empty for Creatio forms-auth). Internal to the service layer — the
/// cookie values are never serialised into an MCP response or CLI stdout.
/// </summary>
/// <param name="Cookies">The session cookies harvested from the login response.</param>
public sealed record StorageStateResult(IReadOnlyList<BrowserCookie> Cookies);

/// <summary>A single browser cookie in Playwright storageState shape.</summary>
/// <param name="Name">Cookie name (e.g. <c>.ASPXAUTH</c>).</param>
/// <param name="Value">Cookie value (a bearer secret — never logged).</param>
/// <param name="Domain">Cookie domain.</param>
/// <param name="Path">Cookie path.</param>
/// <param name="HttpOnly">Whether the cookie is HttpOnly.</param>
/// <param name="Secure">Whether the cookie is Secure.</param>
/// <param name="SameSite">SameSite policy (<c>Lax</c>, <c>Strict</c>, or <c>None</c>).</param>
/// <param name="Expires">Expiry as Unix seconds, or <c>-1</c> for a session cookie.</param>
public sealed record BrowserCookie(
	string Name,
	string Value,
	string Domain,
	string Path,
	bool HttpOnly,
	bool Secure,
	string SameSite,
	double Expires);
