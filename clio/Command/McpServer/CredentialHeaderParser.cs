using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer;

/// <summary>
/// The pure result of parsing an <c>X-Integration-Credentials</c> header value:
/// the target URL and the precedence-resolved authentication material. Transport
/// and passthrough mode are NOT part of this result — the middleware wraps this
/// into a full <see cref="CredentialContext"/>. Pure data carrier (record).
/// </summary>
/// <param name="Url">The target Creatio environment URL (always present on success).</param>
/// <param name="Auth">The precedence-resolved authentication material.</param>
public sealed record CredentialParseResult(string Url, CredentialMaterial Auth)
{
	/// <summary>
	/// FR-11 (review): override the compiler-generated <c>ToString()</c> so <see cref="Auth"/> renders via
	/// the redacted <see cref="CredentialMaterial.ToString"/> and no secret material is printed.
	/// </summary>
	/// <returns>A redacted string (url and credential kind only).</returns>
	public override string ToString() => $"{nameof(CredentialParseResult)} {{ Url = {Url}, Auth = {Auth} }}";
}

/// <summary>
/// Parses the base64-encoded JSON <c>X-Integration-Credentials</c> header into a
/// precedence-resolved <see cref="CredentialParseResult"/>. The parser is pure
/// (no <c>HttpContext</c> dependency) so it is fully unit-testable.
/// </summary>
public interface ICredentialHeaderParser
{
	/// <summary>
	/// Attempts to parse a base64-encoded JSON credential header value.
	/// </summary>
	/// <param name="headerValue">The raw header value (base64-encoded JSON).</param>
	/// <param name="result">On success, the parsed URL and precedence-resolved auth material; otherwise <see langword="null"/>.</param>
	/// <param name="error">
	/// On failure, a message naming the specific defect. The message NEVER contains
	/// any secret value (token / cookie / password) per FR-11.
	/// </param>
	/// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
	bool TryParse(string headerValue, out CredentialParseResult result, out string error);
}

/// <summary>
/// Default <see cref="ICredentialHeaderParser"/>: base64-decode → JSON-parse →
/// apply <c>accessToken → cookie → login+password</c> precedence. Errors name the
/// defect only and never echo secret material (FR-11).
/// </summary>
public sealed class CredentialHeaderParser : ICredentialHeaderParser
{
	private static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public bool TryParse(string headerValue, out CredentialParseResult result, out string error) {
		result = null;
		error = null;

		if (string.IsNullOrWhiteSpace(headerValue)) {
			error = "credential header is empty";
			return false;
		}

		byte[] decoded;
		try {
			decoded = Convert.FromBase64String(headerValue.Trim());
		} catch (FormatException) {
			// Do not echo the header value — it may embed secret material (FR-11).
			error = "credential header is not valid base64";
			return false;
		}

		CredentialPayload payload;
		try {
			payload = JsonSerializer.Deserialize<CredentialPayload>(
				Encoding.UTF8.GetString(decoded), SerializerOptions);
		} catch (JsonException) {
			// Never surface the JSON exception message — it can carry a snippet of
			// the payload, which may include secret values (FR-11).
			error = "credential header is not valid JSON";
			return false;
		}

		if (payload is null) {
			error = "credential header is not valid JSON";
			return false;
		}

		if (string.IsNullOrWhiteSpace(payload.Url)) {
			error = "missing url";
			return false;
		}

		if (!TryResolveAuth(payload, out CredentialMaterial auth)) {
			error = "no usable auth material";
			return false;
		}

		result = new CredentialParseResult(payload.Url.Trim(), auth);
		return true;
	}

	// Precedence order: accessToken first, then login with password, then cookie. Present means
	// non-whitespace, and login with password is only usable when both are non-whitespace. Cookie ranks
	// LAST (review): it is unsupported in v1 (rejected downstream), so a payload that also carries a usable
	// login+password must resolve to the supported material rather than being shadowed by the cookie into a
	// rejection. A cookie-only payload still resolves to Cookie so the caller gets the specific
	// "cookie not supported in v1" message instead of a generic "no usable auth material".
	private static bool TryResolveAuth(CredentialPayload payload, out CredentialMaterial auth) {
		if (!string.IsNullOrWhiteSpace(payload.AccessToken)) {
			auth = CredentialMaterial.FromAccessToken(
				payload.AccessToken, payload.AccessTokenType ?? string.Empty);
			return true;
		}

		if (!string.IsNullOrWhiteSpace(payload.Login) && !string.IsNullOrWhiteSpace(payload.Password)) {
			auth = CredentialMaterial.FromLoginPassword(payload.Login, payload.Password);
			return true;
		}

		if (!string.IsNullOrWhiteSpace(payload.Cookie)) {
			auth = CredentialMaterial.FromCookie(payload.Cookie);
			return true;
		}

		auth = null;
		return false;
	}

	private sealed class CredentialPayload
	{
		[JsonPropertyName("url")]
		public string Url { get; set; }

		[JsonPropertyName("accessToken")]
		public string AccessToken { get; set; }

		[JsonPropertyName("accessTokenType")]
		public string AccessTokenType { get; set; }

		[JsonPropertyName("cookie")]
		public string Cookie { get; set; }

		[JsonPropertyName("login")]
		public string Login { get; set; }

		[JsonPropertyName("password")]
		public string Password { get; set; }
	}
}
