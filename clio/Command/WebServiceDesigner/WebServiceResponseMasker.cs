using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

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
	public static IReadOnlyList<string> SentSecrets(ServiceMethodDto method, string requestRawData, string requestBody) =>
		SentSecrets(method, requestRawData, requestBody, out _);

	/// <summary>
	/// Collects the values the call sent that must not be shown, and reports in the same pass whether a
	/// setting-bound path value could not be located, so the caller decides on one consistent reading.
	/// </summary>
	/// <param name="method">The called method.</param>
	/// <param name="requestRawData">The request line and headers the platform reports as sent.</param>
	/// <param name="requestBody">The request body the platform reports as sent.</param>
	/// <param name="secretUnlocated">
	/// Whether a setting-bound value may have been sent but cannot be located: a path value not found in the URL,
	/// a header, query or cookie value with no request line and headers reported, a body value when the reported
	/// body is missing or is neither JSON nor XML, or a top-level body element missing from an XML body (a SOAP
	/// envelope, its Header included).
	/// </param>
	/// <returns>Distinct values, longest first.</returns>
	public static IReadOnlyList<string> SentSecrets(ServiceMethodDto method, string requestRawData, string requestBody,
		out bool secretUnlocated) =>
		SentSecrets(method, requestRawData, requestBody, true, out secretUnlocated);

	/// <summary>
	/// As <see cref="SentSecrets(ServiceMethodDto, string, string, out bool)"/>, for a service whose authentication
	/// is known: a method keeps its use-auth flag after the service authentication is removed, and then sends none.
	/// </summary>
	/// <param name="method">The called method.</param>
	/// <param name="requestRawData">The request line and headers the platform reports as sent.</param>
	/// <param name="requestBody">The request body the platform reports as sent.</param>
	/// <param name="serviceHasAuth">Whether the service has authentication.</param>
	/// <param name="secretUnlocated">See the four-argument overload.</param>
	/// <returns>Distinct values, longest first.</returns>
	public static IReadOnlyList<string> SentSecrets(ServiceMethodDto method, string requestRawData, string requestBody,
		bool serviceHasAuth, out bool secretUnlocated) {
		secretUnlocated = false;
		string[] lines = (requestRawData ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.None);
		Dictionary<string, List<string>> headers = Headers(lines);
		Dictionary<string, List<string>> query = Query(lines.FirstOrDefault());
		Dictionary<string, List<string>> cookies = Cookies(headers);
		JsonNode body = ParseBody(requestBody);
		// A body that is not JSON is read as XML: a SOAP envelope, whose Header carries WS-Security values.
		XDocument xmlBody = body is null ? ParseXmlBody(requestBody) : null;
		var secrets = new List<string>();
		bool rawReported = !string.IsNullOrWhiteSpace(requestRawData);
		// The Authorization header carries the service credentials (from settings or typed in Studio); without the
		// reported headers its value cannot be found, so it cannot be masked either.
		secretUnlocated |= serviceHasAuth && method?.UseAuthInfo == true && !rawReported;
		foreach ((ServiceParameterDto parameter, ServiceParameterDto parent) in SettingBound(method?.Request?.Parameters, null)) {
			string name = WireName(parameter);
			// Values are found in what the platform reports as sent; when that part is missing or is neither JSON nor
			// XML, a value that went out cannot be found, so the caller withholds the response instead.
			secretUnlocated |= parameter.Type == PlacementBody ? body is null && xmlBody is null : !rawReported;
			if (parameter.Type == PlacementBody && xmlBody is not null) {
				List<string> found = XmlBodyValues(xmlBody, name);
				// A top-level element is always rendered, and so is every field of a parent the platform rendered; a
				// parent with no value is skipped whole. So a field missing while its parent carries content means the
				// lookup missed it: fail closed.
				secretUnlocated |= found.Count == 0
					&& (parent is null || XmlElements(xmlBody, WireName(parent)).Any(e => e.HasElements));
				secrets.AddRange(found);
				continue;
			}
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
					List<string> located = PathValues(lines.FirstOrDefault(), method?.Request?.Uri, name);
					secretUnlocated |= located is null;
					secrets.AddRange(located ?? []);
					break;
			}
		}
		// describe-web-service hides a secret constant (an API key typed in Studio); an echo must not show it
		// either. It is known from the schema, so it is masked whether or not the request carried it.
		secrets.AddRange(SecretConstants(method?.Request?.Parameters));
		// A response parameter's default fills a value the API did not send, so a secret constant there can come back
		// in the parsed response values.
		secrets.AddRange(SecretConstants(method?.Response?.Parameters));
		foreach (string authorization in Lookup(headers, "Authorization")) {
			secrets.AddRange(AuthorizationParts(authorization));
		}
		// The SOAP envelope is written with CRLF line ends and read back with LF, and an API may echo a multi-line value
		// (a PEM key, an assertion) either way or line by line, so each line is a secret of its own too.
		secrets.AddRange(secrets.Where(s => s?.Contains('\n') == true).SelectMany(s => s.Split('\n'))
			.Select(line => line.TrimEnd('\r')).ToList());
		return secrets.Where(s => s is { Length: >= MinMaskedLength })
			.Distinct(StringComparer.Ordinal)
			.OrderByDescending(s => s.Length)
			.ToList();
	}

	/// <summary>Replaces every secret in <paramref name="text"/>, also in its URL-, JSON- and XML-escaped forms.</summary>
	/// <param name="text">Text returned to the caller.</param>
	/// <param name="secrets">Values from <see cref="SentSecrets"/>.</param>
	/// <param name="masked">Whether anything was replaced.</param>
	/// <returns>The text without the secrets.</returns>
	public static string Apply(string text, IReadOnlyList<string> secrets, out bool masked) =>
		ApplyForms(text, AllForms(secrets), out masked);

	/// <summary>Every form of every secret, longest secret first, built once per masked text.</summary>
	private static List<string> AllForms(IReadOnlyList<string> secrets) =>
		(secrets ?? []).SelectMany(Forms).Distinct(StringComparer.Ordinal).ToList();

	private static string ApplyForms(string text, IReadOnlyList<string> forms, out bool masked) {
		masked = false;
		if (string.IsNullOrEmpty(text)) {
			return text;
		}
		string result = text;
		foreach (string form in forms) {
			if (form.Length > 0 && result.Contains(form, StringComparison.Ordinal)) {
				result = result.Replace(form, Mask, StringComparison.Ordinal);
				masked = true;
			}
		}
		return result;
	}

	/// <summary>
	/// Whether a whole value equals a secret once both are read as a Guid or as a number: the platform converts a
	/// parsed response value to the parameter's type, so an echoed key comes back as <c>6f9619ff-...</c> or
	/// <c>123456</c> where the setting holds <c>6F9619FF...</c> or <c>00123456</c>.
	/// </summary>
	private static bool EqualsSecretByValue(string text, IReadOnlyList<string> secrets) {
		if (string.IsNullOrWhiteSpace(text)) {
			return false;
		}
		if (Guid.TryParse(text, out Guid guid)) {
			return secrets.Any(s => Guid.TryParse(s, out Guid other) && other == guid);
		}
		if (decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
				out decimal number)) {
			return secrets.Any(s => decimal.TryParse(s, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out decimal other) && other == number);
		}
		return false;
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
		List<string> forms = AllForms(secrets);
		try {
			JsonNode root = JsonNode.Parse(body);
			if (root is not (JsonObject or JsonArray)) {
				return ApplyForms(body, forms, out masked);
			}
			bool any = false;
			JsonNode result = MaskNode(root, secrets, forms, ref any);
			masked = any;
			return any ? result.ToJsonString(IndentedRelaxedJson) : body;
		} catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException) {
			// JsonNode parses lazily: a duplicate key surfaces as ArgumentException during the walk.
			return ApplyXmlOrText(body, secrets, forms, out masked);
		}
	}

	/// <summary>
	/// Masks an XML body (a SOAP fault, an XML API) by its decoded text and attribute values, so a secret written with
	/// character references (<c>&amp;#39;</c>, <c>&amp;#xD;</c>) is found too, then masks the written result as text.
	/// Anything that is not XML is masked as plain text.
	/// </summary>
	private static string ApplyXmlOrText(string body, IReadOnlyList<string> secrets, IReadOnlyList<string> forms,
		out bool masked) {
		XDocument document = ParseXmlBody(body);
		if (document is null) {
			return ApplyForms(body, forms, out masked);
		}
		bool any = false;
		foreach (XText text in document.DescendantNodes().OfType<XText>()) {
			string replaced = MaskText(text.Value, secrets, forms, ref any);
			if (!ReferenceEquals(replaced, text.Value)) {
				text.Value = replaced;
			}
		}
		foreach (XAttribute attribute in document.Descendants().SelectMany(e => e.Attributes())) {
			string replaced = MaskText(attribute.Value, secrets, forms, ref any);
			if (!ReferenceEquals(replaced, attribute.Value)) {
				attribute.Value = replaced;
			}
		}
		if (!any) {
			// Nothing was found in the decoded values; a secret in a comment or a processing instruction is still masked.
			return ApplyForms(body, forms, out masked);
		}
		string written = (document.Declaration is null ? string.Empty : document.Declaration + Environment.NewLine)
			+ document.ToString(SaveOptions.DisableFormatting);
		masked = true;
		return ApplyForms(written, forms, out _);
	}

	private static string MaskText(string value, IReadOnlyList<string> secrets, IReadOnlyList<string> forms, ref bool masked) {
		if (EqualsSecretByValue(value?.Trim(), secrets)) {
			masked = true;
			return Mask;
		}
		string replaced = ApplyForms(value, forms, out bool changed);
		masked |= changed;
		return changed ? replaced : value;
	}

	/// <summary>
	/// Masks the platform's parsed response values (<c>ParameterValues</c>, a JSON object by parameter code). Besides
	/// the sent secrets, the value of every response parameter that may carry a credential is masked by its code,
	/// whatever it holds: one whose default comes from a system setting or is a secret constant (the platform fills the
	/// default when the API did not send the value), one read from a cookie, and one read from a header whose code or
	/// name looks like a credential. A null stays null.
	/// </summary>
	/// <param name="parameterValues">The JSON text the platform returned.</param>
	/// <param name="method">The called method.</param>
	/// <param name="secrets">Values from <see cref="SentSecrets"/>.</param>
	/// <param name="masked">Whether anything was replaced.</param>
	/// <returns>The masked JSON object or array, or <c>null</c> when the text is not one.</returns>
	public static JsonNode MaskResponseValues(string parameterValues, ServiceMethodDto method,
		IReadOnlyList<string> secrets, out bool masked) {
		masked = false;
		JsonNode root;
		try {
			root = JsonNode.Parse(parameterValues);
			if (root is not (JsonObject or JsonArray)) {
				return null;
			}
			var codes = new HashSet<string>(CredentialResponseCodes(method?.Response?.Parameters), StringComparer.Ordinal);
			bool any = false;
			MaskByCode(root, codes, ref any);
			JsonNode result = secrets is { Count: > 0 } ? MaskNode(root, secrets, AllForms(secrets), ref any) : root;
			masked = any;
			return result;
		} catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException) {
			return null;
		}
	}

	/// <summary>
	/// Whether a response parameter (array item fields included) has a default from a system setting or a secret
	/// constant. The platform converts such a default to the parameter's type after the call, and a conversion
	/// failure ends the call with an error that can quote the value and with nothing reported as sent.
	/// </summary>
	/// <param name="method">The called method.</param>
	/// <returns><c>true</c> when an error text may carry such a default.</returns>
	public static bool HasCredentialResponseDefault(ServiceMethodDto method) =>
		Flatten(method?.Response?.Parameters).Any(parameter =>
			parameter.DefValue?.Source == ServiceSchemaCodes.ValueSourceSysSetting
			|| (parameter.DefValue is { Source: ServiceSchemaCodes.ValueSourceConst, Value.Length: > 0 }
				&& WebServiceSchemaView.IsSecretLike(parameter)));

	private static IEnumerable<ServiceParameterDto> Flatten(ServiceItemCollection<ServiceParameterDto> parameters) =>
		(parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>())
			.SelectMany(parameter => Flatten(parameter.ItemProperties).Prepend(parameter));

	private static IEnumerable<string> CredentialResponseCodes(ServiceItemCollection<ServiceParameterDto> parameters) {
		foreach (ServiceParameterDto parameter in parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>()) {
			bool fromSetting = parameter.DefValue?.Source == ServiceSchemaCodes.ValueSourceSysSetting;
			bool secretConstant = parameter.DefValue is { Source: ServiceSchemaCodes.ValueSourceConst, Value.Length: > 0 }
				&& WebServiceSchemaView.IsSecretLike(parameter);
			bool credentialPlacement = parameter.Type == PlacementCookie
				|| (parameter.Type == PlacementHeader
					&& (WebServiceSchemaView.IsSecretName(parameter.Name) || WebServiceSchemaView.IsSecretName(parameter.Path)));
			if (fromSetting || secretConstant || credentialPlacement) {
				yield return parameter.Name;
			}
			foreach (string nested in CredentialResponseCodes(parameter.ItemProperties)) {
				yield return nested;
			}
		}
	}

	private static void MaskByCode(JsonNode node, ISet<string> codes, ref bool masked) {
		switch (node) {
			case JsonObject obj:
				foreach (string key in obj.Select(p => p.Key).ToList()) {
					if (codes.Contains(key) && obj[key] is not null) {
						obj[key] = Mask;
						masked = true;
					} else if (obj[key] is { } child) {
						MaskByCode(child, codes, ref masked);
					}
				}
				break;
			case JsonArray array:
				foreach (JsonNode item in array.Where(i => i is not null)) {
					MaskByCode(item, codes, ref masked);
				}
				break;
		}
	}

	private static readonly JsonSerializerOptions IndentedRelaxedJson =
		new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

	private static JsonNode MaskNode(JsonNode node, IReadOnlyList<string> secrets, IReadOnlyList<string> forms,
		ref bool masked) {
		switch (node) {
			case JsonObject obj: {
				var rebuilt = new JsonObject();
				List<KeyValuePair<string, JsonNode>> properties = obj.ToList();
				obj.Clear();
				foreach (KeyValuePair<string, JsonNode> property in properties) {
					string key = ApplyForms(property.Key, forms, out bool keyMasked);
					masked |= keyMasked;
					JsonNode value = property.Value is null ? null : MaskNode(property.Value, secrets, forms, ref masked);
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
					rebuilt.Add(item is null ? null : MaskNode(item, secrets, forms, ref masked));
				}
				return rebuilt;
			}
			case JsonValue value when value.TryGetValue(out string text): {
				if (EqualsSecretByValue(text, secrets)) {
					masked = true;
					return JsonValue.Create(Mask);
				}
				string replaced = ApplyForms(text, forms, out bool changed);
				masked |= changed;
				return changed ? JsonValue.Create(replaced) : value;
			}
			case JsonValue value: {
				// A number or a boolean: masked whole when its text carries a secret (an account number, a PIN), also
				// when the platform's type conversion dropped leading zeros or trailing decimals.
				string text = value.ToJsonString();
				ApplyForms(text, forms, out bool changed);
				changed |= EqualsSecretByValue(text, secrets);
				masked |= changed;
				return changed ? JsonValue.Create(Mask) : value;
			}
			default:
				return node;
		}
	}

	private static IEnumerable<string> Forms(string secret) {
		yield return secret;
		// The platform writes a SOAP envelope with CRLF line ends, so a multi-line value goes out and comes back so.
		if (secret.Contains('\n') && !secret.Contains("\r\n", StringComparison.Ordinal)) {
			yield return secret.Replace("\n", "\r\n", StringComparison.Ordinal);
		}
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
		// An XML body or a SOAP fault escapes markup characters: always & and <, often > and sometimes the quotes.
		string minimalXml = secret.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
		string escapedGt = minimalXml.Replace(">", "&gt;", StringComparison.Ordinal);
		foreach (string xml in new[] {
			escapedGt.Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal),
			escapedGt,
			minimalXml
		}.Distinct(StringComparer.Ordinal)) {
			if (xml != secret) {
				yield return xml;
			}
		}
	}

	private static IEnumerable<string> SecretConstants(ServiceItemCollection<ServiceParameterDto> parameters) {
		foreach (ServiceParameterDto parameter in parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>()) {
			if (parameter.DefValue is { Source: ServiceSchemaCodes.ValueSourceConst, Value: { Length: > 0 } value }
					&& WebServiceSchemaView.IsSecretLike(parameter)) {
				yield return value;
			}
			foreach (string nested in SecretConstants(parameter.ItemProperties)) {
				yield return nested;
			}
		}
	}

	/// <summary>
	/// Whether a request parameter filled from a system setting may have been sent but its value cannot be
	/// located in what the platform reports as sent. The value is then unknown and cannot be masked, so the caller
	/// withholds the response instead of returning it unmasked.
	/// </summary>
	/// <param name="method">The called method.</param>
	/// <param name="requestRawData">The request line and headers the platform reports as sent.</param>
	/// <param name="requestBody">The request body the platform reports as sent.</param>
	/// <returns><c>true</c> when such a value could not be located.</returns>
	public static bool HasUnlocatedSecret(ServiceMethodDto method, string requestRawData, string requestBody = null) {
		SentSecrets(method, requestRawData, requestBody, out bool unlocated);
		return unlocated;
	}

	/// <summary>
	/// The request target of a request line: the text between the first and the last space, because the
	/// platform prints the URL unescaped and a value may contain spaces.
	/// </summary>
	private static string RequestTarget(string requestLine) {
		string line = requestLine ?? string.Empty;
		int first = line.IndexOf(' ');
		int last = line.LastIndexOf(' ');
		return first >= 0 && last > first ? line[(first + 1)..last] : string.Empty;
	}

	/// <summary>
	/// The value a setting-bound path parameter took in the sent URL: the method URI template
	/// (<c>orders/{id}</c>) is matched against the end of the request path. A placeholder may span several
	/// segments. Returns <c>null</c> when the value cannot be located.
	/// </summary>
	private static List<string> PathValues(string requestLine, string template, string name) {
		var values = new List<string>();
		string target = RequestTarget(requestLine);
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
				// The match is anchored at the end only, so a capture may start too early and carry segments of the
				// base path ("v1/acme-key" for "acme-key"). The real value is one of its suffixes at a '/', so
				// every such suffix is masked: over-masking a path word is the accepted cost of failing closed.
				string raw = match.Groups[i + 1].Value;
				for (int start = 0; start >= 0; start = raw.IndexOf('/', start) is int slash and >= 0 ? slash + 1 : -1) {
					string candidate = raw[start..];
					values.Add(candidate);
					values.Add(Uri.UnescapeDataString(candidate));
				}
			}
		}
		return values.Count > 0 ? values : null;
	}

	[GeneratedRegex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant)]
	private static partial Regex PlaceholderPattern();

	private static IEnumerable<(ServiceParameterDto Parameter, ServiceParameterDto Parent)> SettingBound(
		ServiceItemCollection<ServiceParameterDto> parameters, ServiceParameterDto parent) {
		foreach (ServiceParameterDto parameter in parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>()) {
			if (parameter.DefValue?.Source == ServiceSchemaCodes.ValueSourceSysSetting) {
				yield return (parameter, parent);
			}
			foreach ((ServiceParameterDto, ServiceParameterDto) item in SettingBound(parameter.ItemProperties, parameter)) {
				yield return item;
			}
		}
	}

	private static string WireName(ServiceParameterDto parameter) =>
		string.IsNullOrEmpty(parameter.Path) ? parameter.Name : parameter.Path;

	/// <summary>
	/// Parses a request body as XML with DTD processing prohibited and no resolver, so a body can neither expand
	/// entities nor make clio read a file or a URL. Returns <c>null</c> when the body is not XML.
	/// </summary>
	private static XDocument ParseXmlBody(string requestBody) {
		if (string.IsNullOrWhiteSpace(requestBody)) {
			return null;
		}
		var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
		try {
			using var text = new StringReader(requestBody);
			using var reader = XmlReader.Create(text, settings);
			return XDocument.Load(reader);
		} catch (XmlException) {
			return null;
		}
	}

	/// <summary>
	/// The values a body parameter took in an XML body. The platform renders a parameter as the element its path
	/// names (<c>wsse:Security/wsse:UsernameToken/wsse:Password</c>; a leading <c>/</c> is ignored), under the
	/// message element or under the SOAP Header, so the element is matched by the local name of the last segment
	/// wherever it occurs. An element with children (a scalar array whose items carry the sequence element name)
	/// gives every leaf text.
	/// </summary>
	private static List<string> XmlBodyValues(XDocument body, string path) =>
		XmlElements(body, path)
			.SelectMany(element => element.HasElements
				? element.Descendants().Where(e => !e.HasElements).Select(e => e.Value)
				: [element.Value])
			.ToList();

	private static IEnumerable<XElement> XmlElements(XDocument body, string path) {
		string localName = XmlLocalName(path);
		return body.Descendants().Where(e => e.Name.LocalName == localName);
	}

	/// <summary>
	/// The local name the platform gives the element of a path's last segment. Terrasoft.Services
	/// <c>SoapBodyBuilder.ParseElementPath</c> takes the first <c>prefix:name</c> match of
	/// <see cref="PrefixedNamePattern"/>, unanchored, so <c>tem:Api.Key</c> renders as <c>tem:Api</c>; a segment
	/// without a prefix is the name as written.
	/// </summary>
	private static string XmlLocalName(string path) {
		string segment = (path ?? string.Empty).TrimEnd('/');
		int slash = segment.LastIndexOf('/');
		segment = slash >= 0 ? segment[(slash + 1)..] : segment;
		Match prefixed = PrefixedNamePattern().Match(segment);
		return prefixed.Success ? prefixed.Groups[2].Value : segment;
	}

	[GeneratedRegex(@"([a-zA-Z0-9_\-]+):([a-zA-Z0-9_\-]+)", RegexOptions.CultureInvariant)]
	private static partial Regex PrefixedNamePattern();

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
		string target = RequestTarget(requestLine);
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
