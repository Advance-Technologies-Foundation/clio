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
		JsonNode payload = TryReadPayload(token);
		if (payload is null) {
			return null;
		}
		string accessId = payload[AccessIdClaim]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(accessId)) {
			return null;
		}
		return new ExternalAccessGrant(
			accessId,
			payload[OwnerClientIdClaim]?.GetValue<string>() ?? string.Empty,
			ReadBoolean(payload, DataIsolationClaim),
			ReadBoolean(payload, SystemOperationsClaim),
			ReadDate(payload, GrantExpirationClaim),
			ReadUnixSeconds(payload, "exp"));
	}

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

	private static bool ReadBoolean(JsonNode payload, string claim) {
		// The platform writes these as the strings "True"/"False", not as JSON booleans.
		string value = payload[claim]?.ToString();
		return bool.TryParse(value, out bool parsed) && parsed;
	}

	private static DateTimeOffset? ReadDate(JsonNode payload, string claim) =>
		DateTimeOffset.TryParse(payload[claim]?.GetValue<string>(), CultureInfo.InvariantCulture,
			DateTimeStyles.None, out DateTimeOffset parsed)
			? parsed
			: null;

	private static DateTimeOffset? ReadUnixSeconds(JsonNode payload, string claim) =>
		long.TryParse(payload[claim]?.ToString(), out long seconds)
			? DateTimeOffset.FromUnixTimeSeconds(seconds)
			: null;
}
