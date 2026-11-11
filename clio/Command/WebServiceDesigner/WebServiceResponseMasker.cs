using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// Hides, in what a test call returns, the values the platform filled in from system settings and the
/// Authorization header. An API may echo request headers or put a key into an error body, and the
/// agent must never read a credential that way.
/// </summary>
/// <remarks>
/// The values are taken from the request the platform reports as sent, not from the settings: reading a
/// setting value needs cliogate, which many environments lack, and the sent value is exactly what can
/// come back. A path
/// parameter cannot be told apart from the rest of the URL and is not masked; values shorter than
/// <see cref="MinMaskedLength"/> are left alone so that common short words are not erased.
/// </remarks>
public static class WebServiceResponseMasker {

	/// <summary>Text that replaces a masked value.</summary>
	public const string Mask = "[masked]";

	/// <summary>The shortest value worth masking.</summary>
	public const int MinMaskedLength = 4;

	private static readonly JsonSerializerOptions RelaxedJson =
		new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	private const int PlacementBody = 1;
	private const int PlacementQuery = 3;
	private const int PlacementHeader = 4;
	private const int PlacementCookie = 5;

	/// <summary>
	/// Collects the values the call sent that must not be shown: every value of a request parameter
	/// whose default comes from a system setting, and the Authorization header with its Basic parts.
	/// </summary>
	/// <param name="method">The called method.</param>
	/// <param name="requestRawData">The request line and headers the platform reports as sent.</param>
	/// <param name="requestBody">The request body the platform reports as sent.</param>
	/// <returns>Distinct values, longest first.</returns>
	public static IReadOnlyList<string> SentSecrets(ServiceMethodDto method, string requestRawData, string requestBody) {
		string[] lines = (requestRawData ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.None);
		Dictionary<string, List<string>> headers = Headers(lines);
		Dictionary<string, List<string>> query = Query(lines.FirstOrDefault());
		Dictionary<string, List<string>> cookies = Cookies(headers);
		JsonNode body = ParseBody(requestBody);
		var secrets = new List<string>();
		foreach (ServiceParameterDto parameter in SettingBound(method?.Request?.Parameters)) {
			string name = string.IsNullOrEmpty(parameter.Path) ? parameter.Name : parameter.Path;
			switch (parameter.Type) {
				case PlacementHeader:
					secrets.AddRange(Lookup(headers, name));
					break;
				case PlacementQuery:
					secrets.AddRange(Lookup(query, name));
					break;
				case PlacementCookie:
					secrets.AddRange(Lookup(cookies, name));
					break;
				case PlacementBody:
					// A field of a nested object is described by a dotted path (customer.name, $.auth.secret);
					// the body carries it under its last segment.
					secrets.AddRange(BodyValues(body, LastPathSegment(name)));
					break;
			}
		}
		foreach (string authorization in Lookup(headers, "Authorization")) {
			secrets.AddRange(AuthorizationParts(authorization));
		}
		return secrets.Where(s => s is { Length: >= MinMaskedLength })
			.Distinct(StringComparer.Ordinal)
			.OrderByDescending(s => s.Length)
			.ToList();
	}

	/// <summary>Replaces every secret in <paramref name="text"/>, also in its URL- and JSON-escaped forms.</summary>
	/// <param name="text">Text returned to the caller.</param>
	/// <param name="secrets">Values from <see cref="SentSecrets"/>.</param>
	/// <param name="masked">Whether anything was replaced.</param>
	/// <returns>The text without the secrets.</returns>
	public static string Apply(string text, IReadOnlyList<string> secrets, out bool masked) {
		masked = false;
		if (string.IsNullOrEmpty(text)) {
			return text;
		}
		string result = text;
		foreach (string secret in secrets) {
			foreach (string form in Forms(secret)) {
				if (result.Contains(form, StringComparison.Ordinal)) {
					result = result.Replace(form, Mask, StringComparison.Ordinal);
					masked = true;
				}
			}
		}
		return result;
	}

	private static IEnumerable<string> Forms(string secret) {
		yield return secret;
		string escaped = Uri.EscapeDataString(secret);
		if (escaped != secret) {
			yield return escaped;
		}
		// Servers escape JSON differently: most write \" and some \/, while .NET's default writes ".
		foreach (string json in new[] {
			JsonSerializer.Serialize(secret, RelaxedJson)[1..^1],
			JsonSerializer.Serialize(secret)[1..^1],
			JsonSerializer.Serialize(secret, RelaxedJson)[1..^1].Replace("/", @"\/", StringComparison.Ordinal)
		}.Distinct(StringComparer.Ordinal)) {
			if (json != secret) {
				yield return json;
			}
		}
	}

	private static IEnumerable<ServiceParameterDto> SettingBound(ServiceItemCollection<ServiceParameterDto> parameters) {
		foreach (ServiceParameterDto parameter in parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>()) {
			if (parameter.DefValue?.Source == ServiceSchemaCodes.ValueSourceSysSetting) {
				yield return parameter;
			}
			foreach (ServiceParameterDto nested in SettingBound(parameter.ItemProperties)) {
				yield return nested;
			}
		}
	}

	private static Dictionary<string, List<string>> Headers(IEnumerable<string> lines) {
		var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in lines.Skip(1)) {
			int colon = line.IndexOf(':');
			if (colon > 0) {
				Add(headers, line[..colon].Trim(), line[(colon + 1)..].Trim());
			}
		}
		return headers;
	}

	private static Dictionary<string, List<string>> Query(string requestLine) {
		var query = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		string[] parts = (requestLine ?? string.Empty).Split(' ');
		string target = parts.Length > 1 ? parts[1] : string.Empty;
		int question = target.IndexOf('?');
		if (question < 0) {
			return query;
		}
		foreach (string pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)) {
			int equals = pair.IndexOf('=');
			if (equals > 0) {
				Add(query, Uri.UnescapeDataString(pair[..equals]), Uri.UnescapeDataString(pair[(equals + 1)..]));
			}
		}
		return query;
	}

	private static Dictionary<string, List<string>> Cookies(Dictionary<string, List<string>> headers) {
		var cookies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (string header in Lookup(headers, "Cookie")) {
			foreach (string pair in header.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
				int equals = pair.IndexOf('=');
				if (equals > 0) {
					Add(cookies, pair[..equals].Trim(), pair[(equals + 1)..].Trim());
				}
			}
		}
		return cookies;
	}

	private static JsonNode ParseBody(string requestBody) {
		if (string.IsNullOrWhiteSpace(requestBody)) {
			return null;
		}
		try {
			return JsonNode.Parse(requestBody);
		} catch (JsonException) {
			return null;
		}
	}

	private static string LastPathSegment(string path) {
		int dot = path.LastIndexOf('.');
		return dot >= 0 && dot < path.Length - 1 ? path[(dot + 1)..] : path;
	}

	private static IEnumerable<string> BodyValues(JsonNode node, string name) {
		switch (node) {
			case JsonObject obj:
				foreach (KeyValuePair<string, JsonNode> property in obj) {
					if (property.Value is JsonValue value && string.Equals(property.Key, name, StringComparison.Ordinal)) {
						yield return value.ToString();
					}
					foreach (string nested in BodyValues(property.Value, name)) {
						yield return nested;
					}
				}
				break;
			case JsonArray array:
				foreach (string nested in array.SelectMany(item => BodyValues(item, name))) {
					yield return nested;
				}
				break;
		}
	}

	private static IEnumerable<string> AuthorizationParts(string authorization) {
		int space = authorization.IndexOf(' ');
		string credentials = space > 0 ? authorization[(space + 1)..].Trim() : authorization;
		yield return credentials;
		if (space <= 0 || !authorization[..space].Equals("Basic", StringComparison.OrdinalIgnoreCase)) {
			yield break;
		}
		string decoded;
		try {
			decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credentials));
		} catch (FormatException) {
			yield break;
		}
		int colon = decoded.IndexOf(':');
		if (colon >= 0) {
			yield return decoded[..colon];
			yield return decoded[(colon + 1)..];
		}
	}

	private static IEnumerable<string> Lookup(Dictionary<string, List<string>> map, string key) =>
		key is not null && map.TryGetValue(key, out List<string> values) ? values : Enumerable.Empty<string>();

	private static void Add(Dictionary<string, List<string>> map, string key, string value) {
		if (!map.TryGetValue(key, out List<string> values)) {
			map[key] = values = [];
		}
		values.Add(value);
	}
}
