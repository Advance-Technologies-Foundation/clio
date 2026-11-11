using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// Hides, in what a test call returns, the values the platform filled in from system settings, the
/// Authorization header, and the secret constant defaults describe-web-service hides. An API may echo
/// request headers or put a key into an error body, and the agent must never read a credential that way.
/// </summary>
/// <remarks>
/// Setting values are taken from the request the platform reports as sent, not from the settings: reading a
/// setting value needs cliogate, which many environments lack, and the sent value is exactly what can
/// come back. A path parameter is found by matching the method URI template against the request line;
/// values shorter than <see cref="MinMaskedLength"/> are left alone so that common short words are not erased.
/// </remarks>
public static partial class WebServiceResponseMasker {

	/// <summary>Text that replaces a masked value.</summary>
	public const string Mask = "[masked]";

	/// <summary>The shortest value worth masking.</summary>
	public const int MinMaskedLength = 4;

	private static readonly JsonSerializerOptions RelaxedJson =
		new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

	private const int PlacementBody = 1;
	private const int PlacementPath = 2;
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
				case PlacementPath:
					secrets.AddRange(PathValues(lines.FirstOrDefault(), method?.Request?.Uri, name) ?? []);
					break;
			}
		}
		// describe-web-service hides a secret constant (an API key typed in Studio); an echo must not show it
		// either. It is known from the schema, so it is masked whether or not the request carried it.
		secrets.AddRange(SecretConstants(method?.Request?.Parameters));
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

	/// <summary>
	/// Masks a response BODY. In a JSON body every value that carries a secret is masked - strings inside, numbers
	/// and booleans whole - and so is every property NAME that carries one (fail closed: a secret an API echoes as a
	/// key is still the secret); keys masked to the same text get a #n suffix so no value is lost. Any other body,
	/// and any JSON the parser or the walk rejects (duplicate keys among them), is masked as plain text.
	/// </summary>
	/// <param name="body">The response body.</param>
	/// <param name="secrets">Values from <see cref="SentSecrets"/>.</param>
	/// <param name="masked">Whether anything was replaced.</param>
	/// <returns>The body without the secrets; unchanged, byte for byte, when nothing was masked.</returns>
	public static string ApplyToBody(string body, IReadOnlyList<string> secrets, out bool masked) {
		masked = false;
		if (string.IsNullOrEmpty(body) || secrets is not { Count: > 0 }) {
			return body;
		}
		try {
			JsonNode root = JsonNode.Parse(body);
			if (root is not (JsonObject or JsonArray)) {
				return Apply(body, secrets, out masked);
			}
			bool any = false;
			JsonNode result = MaskNode(root, secrets, ref any);
			masked = any;
			return any ? result.ToJsonString(IndentedRelaxedJson) : body;
		} catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException) {
			// JsonNode parses lazily: a duplicate key surfaces as ArgumentException during the walk.
			return Apply(body, secrets, out masked);
		}
	}

	private static readonly JsonSerializerOptions IndentedRelaxedJson =
		new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

	private static JsonNode MaskNode(JsonNode node, IReadOnlyList<string> secrets, ref bool masked) {
		switch (node) {
			case JsonObject obj: {
				var rebuilt = new JsonObject();
				List<KeyValuePair<string, JsonNode>> properties = obj.ToList();
				obj.Clear();
				foreach (KeyValuePair<string, JsonNode> property in properties) {
					string key = Apply(property.Key, secrets, out bool keyMasked);
					masked |= keyMasked;
					JsonNode value = property.Value is null ? null : MaskNode(property.Value, secrets, ref masked);
					// Two keys masked to the same text keep both values: a suffix keeps them apart.
					string unique = key;
					for (int n = 2; rebuilt.ContainsKey(unique); n++) {
						unique = $"{key}#{n}";
					}
					rebuilt[unique] = value;
				}
				return rebuilt;
			}
			case JsonArray array: {
				var rebuilt = new JsonArray();
				List<JsonNode> items = array.ToList();
				array.Clear();
				foreach (JsonNode item in items) {
					rebuilt.Add(item is null ? null : MaskNode(item, secrets, ref masked));
				}
				return rebuilt;
			}
			case JsonValue value when value.TryGetValue(out string text): {
				string replaced = Apply(text, secrets, out bool changed);
				masked |= changed;
				return changed ? JsonValue.Create(replaced) : value;
			}
			case JsonValue value: {
				// A number or a boolean: masked whole when its text carries a secret (an account number, a PIN).
				string text = value.ToJsonString();
				Apply(text, secrets, out bool changed);
				masked |= changed;
				return changed ? JsonValue.Create(Mask) : value;
			}
			default:
				return node;
		}
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

	private static IEnumerable<string> SecretConstants(ServiceItemCollection<ServiceParameterDto> parameters) {
		foreach (ServiceParameterDto parameter in parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>()) {
			if (parameter.DefValue is { Source: ServiceSchemaCodes.ValueSourceConst, Value: { Length: > 0 } value }
					&& (WebServiceSchemaView.IsSecretLike(parameter.Name, parameter.Type)
						|| WebServiceSchemaView.IsSecretLike(parameter.Path, parameter.Type))) {
				yield return value;
			}
			foreach (string nested in SecretConstants(parameter.ItemProperties)) {
				yield return nested;
			}
		}
	}

	/// <summary>
	/// Whether a request parameter filled from a system setting sits in the URL path and its value cannot be
	/// located in the sent request line. The value is then unknown and cannot be masked, so the caller withholds
	/// the response instead of returning it unmasked.
	/// </summary>
	/// <param name="method">The called method.</param>
	/// <param name="requestRawData">The request line and headers the platform reports as sent.</param>
	/// <returns><c>true</c> when such a value could not be located.</returns>
	public static bool HasUnlocatedPathSecret(ServiceMethodDto method, string requestRawData) {
		string requestLine = (requestRawData ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.None)[0];
		return SettingBound(method?.Request?.Parameters)
			.Where(p => p.Type == PlacementPath)
			.Any(p => PathValues(requestLine, method.Request.Uri, string.IsNullOrEmpty(p.Path) ? p.Name : p.Path)
				is not { Count: > 0 });
	}

	/// <summary>
	/// The value a setting-bound path parameter took in the sent URL: the method URI template
	/// (<c>orders/{id}</c>) is matched against the end of the request path. A placeholder may span several
	/// segments. Returns <c>null</c> when the value cannot be located.
	/// </summary>
	private static List<string> PathValues(string requestLine, string template, string name) {
		var values = new List<string>();
		// The target sits between the method and the protocol: the first and the last space.
		string line = requestLine ?? string.Empty;
		int first = line.IndexOf(' ');
		int last = line.LastIndexOf(' ');
		string target = first >= 0 && last > first ? line[(first + 1)..last] : string.Empty;
		int cut = target.IndexOfAny(['?', '#']);
		string path = cut < 0 ? target : target[..cut];
		string pattern = template ?? string.Empty;
		int query = pattern.IndexOfAny(['?', '#']);
		pattern = (query < 0 ? pattern : pattern[..query]).Trim('/');
		if (pattern.Length == 0 || string.IsNullOrEmpty(name)) {
			return null;
		}
		var regex = new StringBuilder("/");
		var placeholders = new List<string>();
		int position = 0;
		foreach (Match placeholder in PlaceholderPattern().Matches(pattern)) {
			regex.Append(Regex.Escape(pattern[position..placeholder.Index])).Append("(.+?)");
			placeholders.Add(placeholder.Groups[1].Value);
			position = placeholder.Index + placeholder.Length;
		}
		regex.Append(Regex.Escape(pattern[position..])).Append("/?$");
		Match match;
		try {
			match = Regex.Match(path, regex.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
		} catch (RegexMatchTimeoutException) {
			return null;
		}
		if (!match.Success) {
			return null;
		}
		for (int i = 0; i < placeholders.Count; i++) {
			if (string.Equals(placeholders[i], name, StringComparison.Ordinal)) {
				string raw = match.Groups[i + 1].Value;
				values.Add(raw);
				values.Add(Uri.UnescapeDataString(raw));
			}
		}
		return values.Count > 0 ? values : null;
	}

	[GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
	private static partial Regex PlaceholderPattern();

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
