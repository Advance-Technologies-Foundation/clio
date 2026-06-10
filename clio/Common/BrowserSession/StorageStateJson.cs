using System;
using System.Linq;
using System.Text.Json;

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
}
