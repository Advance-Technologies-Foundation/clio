namespace Clio.Command.McpServer;

/// <summary>
/// Identifies the MCP transport a request arrived on. Carried on the
/// per-request <see cref="CredentialContext"/> so downstream consumers (the
/// credential resolver, Story 7+) can distinguish an HTTP passthrough request
/// from a stdio invocation.
/// </summary>
public enum McpTransport
{
	/// <summary>Streamable HTTP transport (the <c>mcp-http</c> host).</summary>
	Http,

	/// <summary>Standard-input/standard-output transport (the <c>mcp-server</c> host).</summary>
	Stdio
}

/// <summary>
/// The kind of authentication material a <see cref="CredentialMaterial"/> carries,
/// after the <c>accessToken → cookie → login+password</c> precedence has been resolved.
/// </summary>
public enum CredentialKind
{
	/// <summary>A pre-issued bearer access token (highest precedence).</summary>
	AccessToken,

	/// <summary>A raw authentication cookie.</summary>
	Cookie,

	/// <summary>A login/password pair (lowest precedence).</summary>
	LoginPassword
}

/// <summary>
/// Precedence-resolved authentication material extracted from a single
/// <c>X-Integration-Credentials</c> payload. Exactly one <see cref="Kind"/> is
/// effective; the fields not relevant to that kind are empty. This is a pure
/// data carrier (DTO/record).
/// </summary>
/// <param name="Kind">The effective credential kind after precedence resolution.</param>
/// <param name="AccessToken">The bearer access token; empty unless <paramref name="Kind"/> is <see cref="CredentialKind.AccessToken"/>.</param>
/// <param name="AccessTokenType">The access-token type (for example <c>Bearer</c>); empty unless a token is present.</param>
/// <param name="Cookie">The authentication cookie; empty unless <paramref name="Kind"/> is <see cref="CredentialKind.Cookie"/>.</param>
/// <param name="Login">The login; empty unless <paramref name="Kind"/> is <see cref="CredentialKind.LoginPassword"/>.</param>
/// <param name="Password">The password; empty unless <paramref name="Kind"/> is <see cref="CredentialKind.LoginPassword"/>.</param>
public sealed record CredentialMaterial(
	CredentialKind Kind,
	string AccessToken,
	string AccessTokenType,
	string Cookie,
	string Login,
	string Password)
{
	/// <summary>Creates access-token material (highest precedence).</summary>
	/// <param name="accessToken">The bearer access token.</param>
	/// <param name="accessTokenType">The token type (for example <c>Bearer</c>); may be empty.</param>
	/// <returns>A <see cref="CredentialMaterial"/> of kind <see cref="CredentialKind.AccessToken"/>.</returns>
	public static CredentialMaterial FromAccessToken(string accessToken, string accessTokenType) =>
		new(CredentialKind.AccessToken, accessToken, accessTokenType ?? string.Empty,
			string.Empty, string.Empty, string.Empty);

	/// <summary>Creates cookie material (middle precedence).</summary>
	/// <param name="cookie">The authentication cookie.</param>
	/// <returns>A <see cref="CredentialMaterial"/> of kind <see cref="CredentialKind.Cookie"/>.</returns>
	public static CredentialMaterial FromCookie(string cookie) =>
		new(CredentialKind.Cookie, string.Empty, string.Empty, cookie,
			string.Empty, string.Empty);

	/// <summary>Creates login/password material (lowest precedence).</summary>
	/// <param name="login">The login.</param>
	/// <param name="password">The password.</param>
	/// <returns>A <see cref="CredentialMaterial"/> of kind <see cref="CredentialKind.LoginPassword"/>.</returns>
	public static CredentialMaterial FromLoginPassword(string login, string password) =>
		new(CredentialKind.LoginPassword, string.Empty, string.Empty, string.Empty,
			login, password);

	/// <summary>
	/// FR-11 (review): the compiler-generated record <c>ToString()</c> prints every field, including the
	/// access token / cookie / password. Override it so an accidental interpolation or log of this record
	/// never leaks a secret — only the non-secret <see cref="Kind"/> is emitted.
	/// </summary>
	/// <returns>A redacted string that names only the credential kind.</returns>
	public override string ToString() => $"{nameof(CredentialMaterial)} {{ Kind = {Kind} }}";
}

/// <summary>
/// The per-request credential context for MCP credential passthrough. Built by
/// the <c>mcp-http</c> middleware from a parsed <c>X-Integration-Credentials</c>
/// payload and exposed to tool handlers via <see cref="ICredentialContextAccessor"/>.
/// This is a pure data carrier (DTO/record).
/// </summary>
/// <param name="Url">The target Creatio environment URL the credentials apply to (always required).</param>
/// <param name="Auth">The precedence-resolved authentication material.</param>
/// <param name="IsNetCore">Whether the target uses the root .NET Core/NET8 route layout; <see langword="false"/> selects the .NET Framework <c>/0/</c> layout.</param>
/// <param name="Transport">The transport the request arrived on.</param>
/// <param name="PassthroughModeEnabled">
/// Whether per-request credential passthrough is enabled for this request. The
/// authoritative gate is Story 5 (FR-09); this flag is only carried through the
/// context to the enforcement point (Story 10, FR-19).
/// </param>
public sealed record CredentialContext(
	string Url,
	CredentialMaterial Auth,
	bool IsNetCore,
	McpTransport Transport,
	bool PassthroughModeEnabled)
{
	/// <summary>
	/// FR-11 (review): override the compiler-generated <c>ToString()</c> so it delegates to the redacted
	/// <see cref="CredentialMaterial.ToString"/> for <see cref="Auth"/> and never prints secret material.
	/// </summary>
	/// <returns>A redacted string (url, transport, flag, and the credential kind only).</returns>
	public override string ToString() =>
		$"{nameof(CredentialContext)} {{ Url = {Url}, Transport = {Transport}, "
		+ $"PassthroughModeEnabled = {PassthroughModeEnabled}, Auth = {Auth} }}";
}
