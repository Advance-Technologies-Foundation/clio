using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Common;

/// <summary>
/// What the Creatio site itself answered to the call that made a write tool fail: its HTTP status and, when
/// the body carries one in a known shape, the site's own message and error code.
/// </summary>
/// <remarks>
/// <para>
/// Every other error text in a tool result is clio's sentence, sometimes with the site's words inside it, so a
/// caller could not tell which part the site said. This record holds only what the site sent: nothing in it is
/// clio's wording. It is built from the response the call actually received (see
/// <see cref="IStatusPreservingApplicationClient"/>), never inferred from a message.
/// </para>
/// <para>
/// It is absent when no HTTP response exists (timeout, DNS, TLS, connection refused) and when the failure is
/// clio's own (validation, a refused precondition). The site's <c>errorInfo.stackTrace</c> is never copied.
/// </para>
/// </remarks>
/// <param name="Status">The HTTP status of that response. A Creatio service often refuses with HTTP 200 and
/// <c>success:false</c>; the status then reads 200, because that is what the site sent.</param>
/// <param name="Message">The site's own message from the body, verbatim except for a length cap, or
/// <see langword="null"/> when the body holds none in a known shape.</param>
/// <param name="Code">The site's own error code (<c>errorInfo.errorCode</c>, OData <c>error.code</c> or
/// <c>responseStatus.ErrorCode</c>), or <see langword="null"/>.</param>
/// <param name="Body">What kind of body the site sent: <c>json</c>, <c>html</c>, <c>text</c> or
/// <c>empty</c>.</param>
public sealed record SiteResponse(
	[property: JsonPropertyName("status")]
	[property: Newtonsoft.Json.JsonProperty("status")]
	int Status,
	[property: JsonPropertyName("message")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Newtonsoft.Json.JsonProperty("message", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
	string Message,
	[property: JsonPropertyName("code")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Newtonsoft.Json.JsonProperty("code", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
	string Code,
	[property: JsonPropertyName("body")]
	[property: Newtonsoft.Json.JsonProperty("body")]
	string Body) {

	#region Constants: Public

	/// <summary>The body kind of a response whose body parses as a JSON object or array.</summary>
	public const string JsonBody = "json";

	/// <summary>The body kind of a markup response: an HTML error page, an IIS or proxy page, a login page.</summary>
	public const string HtmlBody = "html";

	/// <summary>The body kind of any other non-empty body.</summary>
	public const string TextBody = "text";

	/// <summary>The body kind of an empty or whitespace-only body.</summary>
	public const string EmptyBody = "empty";

	/// <summary>The longest site message carried, in characters; a longer one is cut and ends with "…".</summary>
	public const int MaxMessageLength = 2_000;

	/// <summary>The longest site error code carried, in characters.</summary>
	public const int MaxCodeLength = 200;

	#endregion

	#region Constants: Private

	private const string Ellipsis = "…";

	// A body this large is not parsed for a message: the fields read here are short, and a multi-megabyte
	// answer would cost a full JSON parse on a path that is already failing.
	private const int MaxParsedBodyLength = 1_048_576;

	#endregion

	#region Methods: Public

	/// <summary>
	/// Builds the site response for an answer the caller has classified as the site refusing the call.
	/// </summary>
	/// <param name="response">The answer, with the status the transport kept.</param>
	/// <returns>The site response, or <see langword="null"/> when the status is unknown - the client did
	/// not keep it - so nothing would be honest to report.</returns>
	public static SiteResponse FromRefusal(SiteHttpResponse response) {
		if (response?.StatusCode is not int status) {
			return null;
		}
		string body = response.Body;
		string kind = ClassifyBody(body, out JsonDocument document);
		using (document) {
			if (document is null) {
				return new SiteResponse(status, null, null, kind);
			}
			JsonElement root = document.RootElement;
			return new SiteResponse(status, Cap(FindMessage(root), MaxMessageLength),
				Cap(FindCode(root), MaxCodeLength), kind);
		}
	}

	#endregion

	#region Methods: Private

	private static string ClassifyBody(string body, out JsonDocument document) {
		document = null;
		if (string.IsNullOrWhiteSpace(body)) {
			return EmptyBody;
		}
		ReadOnlySpan<char> trimmed = body.AsSpan().TrimStart();
		if (trimmed[0] == '<') {
			return HtmlBody;
		}
		if (trimmed[0] is not ('{' or '[')) {
			return TextBody;
		}
		if (body.Length > MaxParsedBodyLength) {
			return JsonBody;
		}
		try {
			document = JsonDocument.Parse(body);
			return JsonBody;
		}
		catch (JsonException) {
			return TextBody;
		}
	}

	// The site's own message, from the shapes Creatio answers with, in this order: the service envelope's
	// errorInfo.message, the OData error.message (a string in v4, {lang, value} in v3), the DataService
	// responseStatus.Message, BuildProcess's errorMessage, then an ASP.NET fault's Message/ExceptionMessage.
	// A stack trace is never read.
	// A WCF service with BodyStyle=Wrapped answers {"<Method>Result": {...}}; its envelope is read the same way.
	private static string FindMessage(JsonElement root) {
		if (root.ValueKind != JsonValueKind.Object) {
			return null;
		}
		return FindEnvelopeMessage(root)
			?? (TryGetWrappedResult(root, out JsonElement wrapped) ? FindEnvelopeMessage(wrapped) : null);
	}

	private static string FindEnvelopeMessage(JsonElement envelope) =>
		ReadString(envelope, "errorInfo", "message")
		?? ReadODataMessage(envelope)
		?? ReadString(envelope, "responseStatus", "Message")
		?? ReadString(envelope, null, "errorMessage")
		?? ReadString(envelope, null, "Message")
		?? ReadString(envelope, null, "message")
		?? ReadString(envelope, null, "ExceptionMessage");

	private static string FindCode(JsonElement root) {
		if (root.ValueKind != JsonValueKind.Object) {
			return null;
		}
		return FindEnvelopeCode(root)
			?? (TryGetWrappedResult(root, out JsonElement wrapped) ? FindEnvelopeCode(wrapped) : null);
	}

	private static string FindEnvelopeCode(JsonElement envelope) =>
		ReadString(envelope, "errorInfo", "errorCode")
		?? ReadString(envelope, "error", "code")
		?? ReadString(envelope, "responseStatus", "ErrorCode");

	private static bool TryGetWrappedResult(JsonElement root, out JsonElement wrapped) {
		wrapped = default;
		using JsonElement.ObjectEnumerator properties = root.EnumerateObject();
		if (!properties.MoveNext()) {
			return false;
		}
		JsonProperty only = properties.Current;
		if (properties.MoveNext() || only.Value.ValueKind != JsonValueKind.Object
			|| !only.Name.EndsWith("Result", StringComparison.Ordinal)) {
			return false;
		}
		wrapped = only.Value;
		return true;
	}

	private static string ReadODataMessage(JsonElement root) {
		if (!TryGetObject(root, "error", out JsonElement error)
			|| !error.TryGetProperty("message", out JsonElement message)) {
			return null;
		}
		if (message.ValueKind == JsonValueKind.String) {
			return NonBlank(message.GetString());
		}
		return message.ValueKind == JsonValueKind.Object
			&& message.TryGetProperty("value", out JsonElement value)
			&& value.ValueKind == JsonValueKind.String
				? NonBlank(value.GetString())
				: null;
	}

	private static string ReadString(JsonElement root, string container, string name) {
		JsonElement owner = root;
		if (container is not null && !TryGetObject(root, container, out owner)) {
			return null;
		}
		if (!owner.TryGetProperty(name, out JsonElement value)) {
			return null;
		}
		return value.ValueKind switch {
			JsonValueKind.String => NonBlank(value.GetString()),
			JsonValueKind.Number => value.GetRawText(),
			_ => null
		};
	}

	private static bool TryGetObject(JsonElement root, string name, out JsonElement element) =>
		root.TryGetProperty(name, out element) && element.ValueKind == JsonValueKind.Object;

	private static string NonBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

	private static string Cap(string value, int maxLength) {
		if (value is null || value.Length <= maxLength) {
			return value;
		}
		int keep = maxLength - Ellipsis.Length;
		if (char.IsHighSurrogate(value[keep - 1])) {
			keep--;
		}
		return string.Concat(value.AsSpan(0, keep), Ellipsis);
	}

	#endregion

}

/// <summary>An HTTP answer with the status the transport kept.</summary>
/// <param name="StatusCode">The HTTP status, or <see langword="null"/> when the client that served the call
/// does not keep it (a test double or a legacy client).</param>
/// <param name="Body">The response body, exactly as the string request methods return it.</param>
public sealed record SiteHttpResponse(int? StatusCode, string Body) {

	/// <summary>Whether the status is known and outside the 2xx range.</summary>
	public bool IsKnownFailureStatus => StatusCode is int status && status is < 200 or > 299;

}
