using System;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Authentication failure raised while harvesting a Creatio browser session. Its message is
/// <b>sanitized</b> — it names only the environment URI and a failure class, never the password,
/// request body, response body, headers, or cookies — so it is safe to surface even under
/// <c>--debug</c> (which prints the full <see cref="Exception.ToString()"/>).
/// </summary>
public sealed class CreatioAuthenticationException : Exception {
	/// <summary>Creates the exception with an already-sanitized message.</summary>
	/// <param name="message">A message that must contain no secret (URL query, body, headers, cookies, password).</param>
	public CreatioAuthenticationException(string message) : base(message) { }

	/// <summary>Invalid credentials (the canonical AC-ERR message).</summary>
	public static CreatioAuthenticationException InvalidCredentials(string environmentUri) =>
		new($"authentication failed for environment '{environmentUri}' — check username and password in env config");

	/// <summary>The environment has no forms-auth credentials (OAuth-only or incomplete) — fail closed.</summary>
	public static CreatioAuthenticationException MissingFormsCredentials(string environmentUri) =>
		new($"browser session handoff requires forms-auth credentials (login + password) for environment " +
			$"'{environmentUri}' — OAuth-only or incomplete environments are not supported");

	/// <summary>A network/transport failure (distinct from an auth rejection).</summary>
	public static CreatioAuthenticationException Connectivity(string environmentUri) =>
		new($"could not reach Creatio at '{environmentUri}' — check the environment URL and network connectivity");

	/// <summary>Login succeeded but no session cookies were returned.</summary>
	public static CreatioAuthenticationException NoCookies(string environmentUri) =>
		new($"authentication for environment '{environmentUri}' returned no session cookies");
}
