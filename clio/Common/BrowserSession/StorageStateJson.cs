using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Common.BrowserSession;

/// <summary>
/// Serializes a harvested session into Playwright storageState JSON — <c>{ "cookies": [...],
/// "origins": [] }</c> — with the exact camelCase field names Playwright's <c>storageState</c> expects.
/// </summary>
public static class StorageStateJson {
	/// <summary>Serializes <paramref name="result"/> to Playwright storageState JSON.</summary>
	/// <param name="result">The harvested session.</param>
	/// <returns>A JSON string suitable for Playwright's <c>storageState</c> option.</returns>
	public static string Serialize(StorageStateResult result) {
		ArgumentNullException.ThrowIfNull(result);
		var payload = new {
			cookies = result.Cookies.Select(c => new {
				name = c.Name,
				value = c.Value,
				domain = c.Domain,
				path = c.Path,
				httpOnly = c.HttpOnly,
				secure = c.Secure,
				sameSite = c.SameSite,
				expires = c.Expires
			}),
			origins = Array.Empty<object>()
		};
		return JsonSerializer.Serialize(payload);
	}

	/// <summary>
	/// Builds an HTTP <c>Cookie</c> request-header value (<c>name=value; name2=value2</c>) from a
	/// storageState JSON document, for validating a cached session. Returns an empty string for a
	/// corrupt or cookie-less document.
	/// </summary>
	/// <param name="storageStateJson">A storageState JSON string previously produced by <see cref="Serialize"/>.</param>
	/// <returns>The Cookie header value, or an empty string when no usable cookies are present.</returns>
	public static string ToCookieHeader(string storageStateJson) {
		if (string.IsNullOrWhiteSpace(storageStateJson)) {
			return string.Empty;
		}
		try {
			if (JsonNode.Parse(storageStateJson)?["cookies"] is not JsonArray cookies) {
				return string.Empty;
			}
			var pairs = cookies
				.Select(c => (Name: c?["name"]?.GetValue<string>(), Value: c?["value"]?.GetValue<string>()))
				.Where(p => !string.IsNullOrEmpty(p.Name))
				.Select(p => $"{p.Name}={p.Value}");
			return string.Join("; ", pairs);
		} catch (JsonException) {
			return string.Empty;
		} catch (InvalidOperationException) {
			return string.Empty;
		}
	}
}
