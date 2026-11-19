using System;
using System.Net.Http;
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
/// <param name="Message">The site's own message from the body, or <see langword="null"/> when the body holds none
/// in a known shape. It passes clio's sensitive-text redaction, like the error text beside it, and a length cap;
/// nothing else is changed.</param>
/// <param name="Code">The site's own error code (<c>errorInfo.errorCode</c>, OData <c>error.code</c> or
/// <c>responseStatus.ErrorCode</c>), or <see langword="null"/>.</param>
/// <param name="Body">What KIND of body the site sent: one of <c>json</c>, <c>html</c>, <c>text</c> or
/// <c>empty</c>. It never holds the body itself.</param>
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

	/// <summary>The body kind of a markup response: an HTML error page, an IIS or proxy page, a login page - or any
	/// other markup, such as an XML or SOAP fault, which is not an HTML page and so never proves a refusal without
	/// processing.</summary>
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
			SiteResponse read = document is null
				? new SiteResponse(status, null, null, kind)
				: new SiteResponse(status, Carry(FindMessage(document.RootElement), MaxMessageLength),
					Carry(FindCode(document.RootElement), MaxCodeLength), kind);
			return read with { RefusedWithoutProcessing = response.IsRefusedWithoutProcessing };
		}
	}

	/// <summary>
	/// Whether the answer this was read from shows the site refused the request without processing it; see
	/// <see cref="SiteHttpResponse.IsRefusedWithoutProcessing"/>. Never serialized.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	[Newtonsoft.Json.JsonIgnore]
	internal bool RefusedWithoutProcessing { get; init; }

	/// <summary>
	/// Whether <paramref name="body"/> carries no answer of the service: empty, an HTML page, or plain text. Anything
	/// that may be the application's own answer is excluded, after skipping whitespace and a byte-order mark (some
	/// Creatio WCF endpoints start their JSON with one): a body that starts like JSON (<c>{</c>, <c>[</c> or a
	/// <c>"</c> string), parseable or not; any other body that parses as JSON (a number, <c>true</c>,
	/// <c>null</c>); a body too long to check; and markup that is not an HTML page (an XML or SOAP fault).
	/// </summary>
	/// <param name="body">The response body.</param>
	internal static bool CarriesNoServiceAnswer(string body) {
		ReadOnlySpan<char> content = SkipLeadingNoise(body);
		if (content.IsEmpty) {
			return true;
		}
		if (content[0] is '{' or '[' or '"' || content.Length > MaxParsedBodyLength) {
			return false;
		}
		if (content[0] == '<') {
			return content.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
				|| content.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
		}
		// Only a JSON number, true, false or null is left that could parse; other text is plain text.
		if (content[0] is not ('-' or 't' or 'f' or 'n' or (>= '0' and <= '9'))) {
			return true;
		}
		try {
			using JsonDocument scalar = JsonDocument.Parse(content.ToString());
			return false;
		}
		catch (Exception exception) when (exception is JsonException or ArgumentException) {
			return true;
		}
	}

	// The body without the leading whitespace and byte-order marks that come before its first real character.
	private static ReadOnlySpan<char> SkipLeadingNoise(string body) {
		ReadOnlySpan<char> content = body.AsSpan();
		int start = 0;
		while (start < content.Length && (char.IsWhiteSpace(content[start]) || content[start] == '\uFEFF')) {
			start++;
		}
		return content[start..];
	}

	#endregion

	#region Methods: Private

	private static string ClassifyBody(string body, out JsonDocument document) {
		document = null;
		ReadOnlySpan<char> trimmed = SkipLeadingNoise(body);
		if (trimmed.IsEmpty) {
			return EmptyBody;
		}
		if (trimmed[0] == '<') {
			return HtmlBody;
		}
		if (trimmed[0] is not ('{' or '[')) {
			return TextBody;
		}
		if (trimmed.Length > MaxParsedBodyLength) {
			return JsonBody;
		}
		try {
			document = JsonDocument.Parse(trimmed.ToString());
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

	// The site's text goes through the same sensitive-text redaction as the error field next to it (URIs, paths,
	// host:port, emails, credential and connection-string pairs become placeholders), then the length cap: a
	// result reaches a model transcript, so it must not carry more than the error text already does.
	private static string Carry(string value, int maxLength) =>
		value is null ? null : Cap(SensitiveErrorTextRedactor.Redact(value), maxLength);

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

	/// <summary>
	/// Whether clio sent the request a second time because it read the first answer's body as an expired session.
	/// That reading is a guess from the body (GitHub #1313): the first send may have been applied, so an answer
	/// that comes after such a re-send proves nothing about it.
	/// </summary>
	public bool Resent { get; init; }

	/// <summary>
	/// Whether the answer is to a request the HTTP client sent itself after following a redirect - another method
	/// (creatio.client turns a POST or PATCH answered 301, 302 or 303 into a GET) or another address - rather than
	/// to the write clio sent. The write may have been applied before the redirect, so such an answer proves
	/// nothing about it.
	/// </summary>
	public bool Redirected { get; init; }

	/// <summary>
	/// Reads an answer, keeping its status and noting whether the client followed a redirect to produce it: the
	/// request the response belongs to has another method or another address than the one sent.
	/// </summary>
	/// <param name="response">The response the client returned.</param>
	/// <param name="body">Its body, already read.</param>
	/// <param name="sentMethod">The HTTP method clio sent.</param>
	/// <param name="sentUrl">The URL clio sent, absolute or relative to the site.</param>
	/// <remarks>
	/// A response without its request (only test doubles build one) cannot show a redirect, so it is read as not
	/// redirected.
	/// </remarks>
	public static SiteHttpResponse Read(HttpResponseMessage response, string body, HttpMethod sentMethod,
		string sentUrl) {
		ArgumentNullException.ThrowIfNull(response);
		HttpRequestMessage answered = response.RequestMessage;
		bool redirected = answered is not null
			&& (answered.Method != sentMethod || !IsSameTarget(answered.RequestUri, sentUrl));
		return new SiteHttpResponse((int)response.StatusCode, body) { Redirected = redirected };
	}

	private static bool IsSameTarget(Uri answered, string sentUrl) {
		if (answered is null || string.IsNullOrEmpty(sentUrl)) {
			return answered is null && string.IsNullOrEmpty(sentUrl);
		}
		if (Uri.TryCreate(sentUrl, UriKind.Absolute, out Uri sent)) {
			return Uri.Compare(answered, sent, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped,
				StringComparison.OrdinalIgnoreCase) == 0;
		}
		return answered.IsAbsoluteUri
			? answered.PathAndQuery.EndsWith(sentUrl, StringComparison.OrdinalIgnoreCase)
			: string.Equals(answered.OriginalString, sentUrl, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Whether this answer shows the site refused the request without processing it. All of these must hold:
	/// <list type="bullet">
	/// <item>the status refuses the request for its shape (<see cref="HasRefusalStatus"/>);</item>
	/// <item>the body carries no answer of the application: it is empty, an HTML page or plain text, never JSON and
	/// never other markup such as an XML or SOAP fault (<see cref="SiteResponse.CarriesNoServiceAnswer"/>). Such a
	/// body is written by the application, which may have done part of its work before it refused;</item>
	/// <item>clio did not send the request again (<see cref="Resent"/>), and the answer is to the request clio sent,
	/// not to one the client sent after following a redirect (<see cref="Redirected"/>).</item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// Whether such a refusal proves the CALL wrote nothing also depends on what the call sent before it; see
	/// <see cref="SiteResponseCapture.RecordRefusalOfSoleWrite"/>.
	/// </remarks>
	public bool IsRefusedWithoutProcessing =>
		!Resent && !Redirected && HasRefusalStatus(StatusCode) && SiteResponse.CarriesNoServiceAnswer(Body);

	/// <summary>
	/// Whether <paramref name="status"/> refuses the request for its shape, before any application code runs:
	/// 405 (the method), 413 (the body size), 414 (the URI length) and 415 (the media type) - statuses with which
	/// HTTP defines the server as refusing to process or to service the request (RFC 9110).
	/// </summary>
	/// <remarks>
	/// <para>
	/// These are decided by the method, URI and body, which a re-send repeats unchanged: even when the HTTP client
	/// sends the request again out of sight (creatio.client re-sends after a 401, and after a 403 when the sign-in
	/// changed meanwhile, without showing the first answer), the first send was refused the same way.
	/// </para>
	/// <para>
	/// Nothing else qualifies. 431 does not: a re-send after signing in again carries new cookies and a new CSRF
	/// header, so its header size differs from the first send's. 401 does not: creatio.client signs in again and
	/// re-sends after any 401, so the 401 a caller sees answers the second send and the first answer is never seen;
	/// and ASP.NET writes its 401 <c>{"Message":"Authentication failed."}</c> envelope for any redirect during a REST
	/// call (ScriptModule's <c>HttpResponse_Redirecting</c>), including one after the handler ran. 403 does not: a
	/// gateway that inspects responses answers 403 after the backend ran (the reason
	/// <c>CompileBusinessProcessCommand</c> gives, #ENG-92711). 400, 404, 409 and 422 can come from the handler after
	/// it started; a 3xx can follow a request the server did process (a 303 or 302 after a POST); a 5xx can come from
	/// a gateway after the write landed.
	/// </para>
	/// </remarks>
	/// <param name="status">An HTTP status, or <see langword="null"/>.</param>
	public static bool HasRefusalStatus(int? status) => status is 405 or 413 or 414 or 415;

}
