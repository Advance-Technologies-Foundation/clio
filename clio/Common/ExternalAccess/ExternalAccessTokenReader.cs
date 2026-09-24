using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Common.ExternalAccess;

/// <summary>
/// Reads the claims an external-access token carries about its own grant.
/// </summary>
/// <remarks>
/// The payload is read WITHOUT verifying the signature, and that is correct here: clio is not the
/// party that decides whether the token is genuine — the customer site validates it against the
/// identity service it trusts, and refuses the exchange otherwise. These claims are used only to key
/// the session cache and to tell the caller which grant they are working under, so a forged token
/// buys nothing: it still fails at the exchange.
/// <para>
/// Because the payload is unauthenticated, every value taken from it is constrained HERE rather than
/// where it is consumed. <see cref="ExternalAccessGrant.AccessId"/> keys the session cache and so
/// becomes part of a file name: it must parse as a GUID and is carried on in its normalized "N"
/// form. A payload that does not satisfy that reads as <see langword="null"/>, which the session
/// provider already handles as "no cache entry, go straight to the exchange".
/// </para>
/// </remarks>
public static class ExternalAccessTokenReader {
	private const string AccessIdClaim = "prop:ResourceId";
	private const string OwnerClientIdClaim = "prop:OwnerClientId";
	private const string DataIsolationClaim = "prop:IsDataIsolationEnabled";
	private const string SystemOperationsClaim = "prop:IsSystemOperationsRestricted";
	private const string GrantExpirationClaim = "prop:ExpirationDate";

	/// <summary>Reads the grant terms out of <paramref name="token"/>.</summary>
	/// <param name="token">The external-access token, with or without a "Bearer " prefix.</param>
	/// <returns>The grant terms, or <see langword="null"/> when the token is not a readable JWT.</returns>
	public static ExternalAccessGrant Read(string token) {
		// Every claim read below is guarded, not just the decode: the payload is attacker-shaped, so
		// a JSON array where an object is expected, or a claim that is a number where a string is
		// expected, must read as "not a usable token" rather than throw a stack trace at the operator
		// before the site is ever contacted.
		try {
			if (TryReadPayload(token) is not JsonObject payload) {
				return null;
			}
			// The access id keys the session cache, so it reaches a file name. prop:ResourceId is an
			// ExternalAccess row id; anything that is not a GUID is refused here rather than
			// sanitized, so no separator or traversal segment can reach BrowserSessionCache.GetPath.
			if (!Guid.TryParse(ReadString(payload, AccessIdClaim), out Guid grantId)) {
				return null;
			}
			return new ExternalAccessGrant(
				grantId.ToString("N"),
				ReadString(payload, OwnerClientIdClaim) ?? string.Empty,
				ReadBoolean(payload, DataIsolationClaim),
				ReadBoolean(payload, SystemOperationsClaim),
				ReadDate(payload, GrantExpirationClaim),
				ReadUnixSeconds(payload, "exp"));
		} catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException
			or InvalidOperationException) {
			return null;
		}
	}

	// ToString() rather than GetValue<string>(): the platform's claim types are inconsistent, and a
	// claim that arrives as a number or a boolean must not throw.
	private static string ReadString(JsonObject payload, string claim) => payload[claim]?.ToString();

	private static JsonNode TryReadPayload(string token) {
		if (string.IsNullOrWhiteSpace(token)) {
			return null;
		}
		string bare = token.Trim();
		const string prefix = "Bearer ";
		if (bare.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
			bare = bare[prefix.Length..].Trim();
		}
		string[] segments = bare.Split('.');
		if (segments.Length != 3) {
			return null;
		}
		try {
			return JsonNode.Parse(Encoding.UTF8.GetString(DecodeBase64Url(segments[1])));
		} catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) {
			return null;
		}
	}

	// JWT segments are base64url without padding; Convert.FromBase64String accepts neither.
	private static byte[] DecodeBase64Url(string segment) {
		string padded = segment.Replace('-', '+').Replace('_', '/');
		return Convert.FromBase64String(padded.PadRight(padded.Length + (3 - (padded.Length + 3) % 4) % 4, '='));
	}

	private static bool ReadBoolean(JsonObject payload, string claim) {
		// The platform writes these as the strings "True"/"False", not as JSON booleans.
		string value = ReadString(payload, claim);
		return bool.TryParse(value, out bool parsed) && parsed;
	}

	private static DateTimeOffset? ReadDate(JsonObject payload, string claim) =>
		DateTimeOffset.TryParse(ReadString(payload, claim), CultureInfo.InvariantCulture,
			DateTimeStyles.None, out DateTimeOffset parsed)
			? parsed
			: null;

	// The range check is not decoration: a millisecond-based `exp` parses as a long and then makes
	// FromUnixTimeSeconds throw, which the operator would see as a stack trace.
	private static DateTimeOffset? ReadUnixSeconds(JsonObject payload, string claim) =>
		long.TryParse(ReadString(payload, claim), out long seconds)
			&& seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds()
			&& seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
			? DateTimeOffset.FromUnixTimeSeconds(seconds)
			: null;
}
