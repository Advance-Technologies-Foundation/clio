using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Clio.Command.WebServiceDesigner;

#region Arguments

public sealed record WebServiceMethodArg(
	[property: JsonPropertyName("code")]
	[property: Description("Method code with the schema-name prefix, unique within the service, e.g. UsrGetOrders. This is what a Call web service element stores.")]
	string Code,

	[property: JsonPropertyName("http-method")]
	[property: Description("GET, POST, PUT, PATCH or DELETE; a GET has no body request parameters")]
	string HttpMethod,

	[property: JsonPropertyName("uri")]
	[property: Description("REST: method address relative to the base URI; a path parameter as {path}, its wire name, e.g. orders/{id}. Not for SOAP (use message-element)")]
	string Uri,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional caption; defaults to the code")]
	string Caption = null,

	[property: JsonPropertyName("timeout-ms")]
	[property: Description("Optional response timeout in milliseconds; defaults to 5000 as Studio does")]
	int? TimeoutMs = null,

	[property: JsonPropertyName("content-type")]
	[property: Description("REST: json (default) or xml. An xml REST method has no body parameters in request or response (Studio refuses them); a process sends the XML body through the element's RequestBody and reads Response. A SOAP method is always xml")]
	string ContentType = null,

	[property: JsonPropertyName("use-auth")]
	[property: Description("Whether the method sends the service authentication; defaults to true when the service has authentication")]
	bool? UseAuth = null,

	[property: JsonPropertyName("request-parameters")]
	[property: Description("Request parameters; each has in = path, query, header, body or cookie (SOAP: body, header or cookie)")]
	IReadOnlyList<WebServiceParameterArg> RequestParameters = null,

	[property: JsonPropertyName("response-parameters")]
	[property: Description("Response parameters; path is a JSONPath such as $.items for a JSON response, an element path such as AddResult or AddResponse/AddResult for a SOAP (XML) response")]
	IReadOnlyList<WebServiceParameterArg> ResponseParameters = null,

	[property: JsonPropertyName("message-element")]
	[property: Description("SOAP only, required there: the root element of the request body with its namespace prefix, e.g. tem:Add; a SOAP method has no uri (its URL is the base URI) and is always POST with xml")]
	string MessageElement = null,

	[property: JsonPropertyName("soap-action")]
	[property: Description("SOAP only, optional: the SOAPAction header value, e.g. http://tempuri.org/Add. Take it from the WSDL (binding operation soapAction) or ask the user; never make one up. Left out, no SOAPAction header is sent, which many SOAP 1.1 servers refuse; setMethod replaces the method, so send describe's soap-action back to keep it")]
	string SoapAction = null
) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

/// <summary>A web service to register: what register-web-service takes besides the environment and the package.</summary>
/// <param name="Code">The service code; must carry the schema-name prefix and be unused.</param>
/// <param name="Caption">The caption; the code when empty.</param>
/// <param name="BaseUri">An absolute http(s) URI without user information.</param>
/// <param name="RetryCount">How many times the platform retries a failed call.</param>
/// <param name="Auth">The authentication, by system-setting codes or an OAuth application only.</param>
/// <param name="Methods">At least one method.</param>
/// <param name="Type">rest (default), soap11 or soap12.</param>
/// <param name="Namespaces">SOAP only, at least one: the namespaces the envelope declares.</param>
/// <param name="Description">Optional description Studio shows on the service.</param>
public sealed record WebServiceRegistration(string Code, string Caption, string BaseUri, int? RetryCount,
	WebServiceAuthArg Auth, IReadOnlyList<WebServiceMethodArg> Methods, string Type = null,
	IReadOnlyList<WebServiceNamespaceArg> Namespaces = null, string Description = null);

/// <summary>A SOAP namespace the envelope declares; exactly one is the default.</summary>
public sealed record WebServiceNamespaceArg(
	[property: JsonPropertyName("prefix")]
	[property: Description("Namespace prefix used in message-element and parameter paths, e.g. tem")]
	string Prefix,

	[property: JsonPropertyName("uri")]
	[property: Description("Namespace URI, e.g. http://tempuri.org/")]
	string Uri,

	[property: JsonPropertyName("default")]
	[property: Description("Whether this is the default namespace; when none is marked, the first one is")]
	bool? Default = null
) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

/// <summary>A request or response parameter; <see cref="Items"/> describes the fields of each element of an Object array.</summary>
public sealed record WebServiceParameterArg(
	[property: JsonPropertyName("code")]
	[property: Description("Parameter code with the schema-name prefix, unique among its siblings, e.g. UsrOrderId; the wire name goes in path")]
	string Code,

	[property: JsonPropertyName("in")]
	[property: Description("Request parameters: path, query, header, body or cookie (default body). Response parameters: body (default), header or cookie; the platform matches a response header name in path exactly as the API sends it, letter case included (often lowercase).")]
	string In = null,

	[property: JsonPropertyName("type")]
	[property: Description("Text (default), Integer, Float, Boolean, DateTime, Date, Time, Guid or Object. Object only with array true; a single nested object is described by its fields, each with a dotted path such as customer.name")]
	string Type = null,

	[property: JsonPropertyName("path")]
	[property: Description("Wire name (query, header, cookie or body key; the {placeholder} of a path parameter) or the JSONPath of a JSON response value, e.g. $.status. SOAP: the element path with declared prefixes, e.g. tem:intA or wsse:Security/wsse:Password for a request, AddResult for a response. Required for a new parameter when the environment has a schema-name prefix; otherwise it defaults to the code, and a kept parameter keeps its own.")]
	string Path = null,

	[property: JsonPropertyName("caption")]
	string Caption = null,

	[property: JsonPropertyName("array")]
	bool? Array = null,

	[property: JsonPropertyName("required")]
	bool? Required = null,

	[property: JsonPropertyName("default-value")]
	[property: Description("Optional constant default. Refused on a cookie; on any key, token, secret, password, auth (Authorization) or session parameter; on a query parameter named sig, sas, code, appid, sign or hmac when 16+ characters; and on a header or soap-header parameter when the value looks like a key (an authentication scheme and its credentials, a credential pair such as password=..., a generated key, a long letters-only or digits-only run, a password-like word, an address with credentials or a key in it) or it sits under a credential-like soap-header parent: use default-sys-setting there. A plain header constant such as Accept-Language: en-US or X-Api-Version: 2 is allowed and describe shows it; a key, password or PIN the user gives still goes into a system setting, even when the tool would accept it.")]
	string DefaultValue = null,

	[property: JsonPropertyName("default-sys-setting")]
	[property: Description("Optional code of an existing system setting the platform reads the value from at call time")]
	string DefaultSysSetting = null,

	[property: JsonPropertyName("items")]
	[property: Description("Fields of each element of an Object array")]
	IReadOnlyList<WebServiceParameterArg> Items = null,

	[property: JsonPropertyName("default-masked")]
	[property: Description("Read-only, from describe-web-service; accepted and ignored. A kept parameter whose secret constant default describe hid keeps that default when no default is sent.")]
	bool? DefaultMasked = null,

	[property: JsonPropertyName("soap-header")]
	[property: Description("SOAP only: send this top-level body request parameter in the envelope's Header (WS-Security and similar); its items follow it")]
	bool? SoapHeader = null,

	[property: JsonPropertyName("sequence-element")]
	[property: Description("SOAP only, arrays: the element name of each item, e.g. string, or prefix:name with a declared prefix, e.g. tem:Region; default item. Empty on a response array reads every child element; send describe's value back to keep it")]
	string SequenceElement = null
) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

/// <summary>Service authentication. Credentials are system-setting codes only: the agent never handles a value.</summary>
public sealed record WebServiceAuthArg(
	[property: JsonPropertyName("type")]
	[property: Description("none, basic, digest or oauth20")]
	string Type,

	[property: JsonPropertyName("username-sys-setting")]
	[property: Description("basic/digest: code of the system setting that holds the user name")]
	string UsernameSysSetting = null,

	[property: JsonPropertyName("password-sys-setting")]
	[property: Description("basic/digest: code of the system setting that holds the password")]
	string PasswordSysSetting = null,

	[property: JsonPropertyName("oauth-app")]
	[property: Description("oauth20: name or id of an existing OAuth application (OAuthApplications); describe-web-service reports the id as oauth-app-id")]
	string OAuthApp = null,

	[property: JsonPropertyName("oauth-app-id")]
	[property: Description("oauth20: the application id as describe-web-service reports it; the same as oauth-app")]
	string OAuthAppId = null,

	[property: JsonPropertyName("constant-credentials-masked")]
	[property: Description("Read-only, from describe-web-service; accepted and ignored")]
	bool? ConstantCredentialsMasked = null
) {
	/// <summary>The OAuth application named by oauth-app, or by oauth-app-id when only that is given.</summary>
	[JsonIgnore]
	public string OAuthAppReference => string.IsNullOrWhiteSpace(OAuthApp) ? OAuthAppId : OAuthApp;


	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

public sealed record WebServiceOperationArg(
	[property: JsonPropertyName("op")]
	[property: Description("addMethod, setMethod, removeMethod, setBaseUri, setRetryCount, setAuth, setDescription; for a SOAP service also setNamespaces and setType")]
	string Op,

	[property: JsonPropertyName("method")]
	[property: Description("addMethod and setMethod: the whole method definition. setMethod replaces the method of the same code, keeping the UIds of parameters it keeps.")]
	WebServiceMethodArg Method = null,

	[property: JsonPropertyName("method-code")]
	[property: Description("removeMethod: the code of the method to remove")]
	string MethodCode = null,

	[property: JsonPropertyName("base-uri")]
	string BaseUri = null,

	[property: JsonPropertyName("retry-count")]
	int? RetryCount = null,

	[property: JsonPropertyName("auth")]
	WebServiceAuthArg Auth = null,

	[property: JsonPropertyName("namespaces")]
	[property: Description("setNamespaces: the whole namespace list, replacing the old one; a prefix a method still uses must stay")]
	IReadOnlyList<WebServiceNamespaceArg> Namespaces = null,

	[property: JsonPropertyName("type")]
	[property: Description("setType: soap11 or soap12 (SOAP 1.1 / SOAP 1.2); a REST service cannot become SOAP or back")]
	string Type = null,

	[property: JsonPropertyName("description")]
	[property: Description("setDescription: the service description Studio shows; an empty string clears it")]
	string Description = null
) {
	[JsonExtensionData] public Dictionary<string, JsonElement> ExtensionData { get; init; }
}

#endregion

#region Results

public sealed record WebServiceParameterView(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("in")] string In,
	[property: JsonPropertyName("path")] string Path,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("array")] bool Array,
	[property: JsonPropertyName("required")] bool Required,
	[property: JsonPropertyName("default-sys-setting")] string DefaultSysSetting,
	[property: JsonPropertyName("default-value")] string DefaultValue,
	[property: JsonPropertyName("default-masked")] bool DefaultMasked,
	[property: JsonPropertyName("items")] IReadOnlyList<WebServiceParameterView> Items,

	[property: JsonPropertyName("soap-header")]
	[property: Description("SOAP: true when the parameter is sent in the envelope's Header; absent otherwise")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? SoapHeader = null,

	[property: JsonPropertyName("sequence-element")]
	[property: Description("SOAP: the element name of each item of an array parameter")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string SequenceElement = null);

public sealed record WebServiceMethodView(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("http-method")] string HttpMethod,

	[property: JsonPropertyName("uri")]
	[property: Description("REST: the address relative to the base URI; absent on a SOAP method, whose URL is the base URI")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string Uri,

	[property: JsonPropertyName("timeout-ms")] int TimeoutMs,
	[property: JsonPropertyName("content-type")] string ContentType,
	[property: JsonPropertyName("use-auth")] bool UseAuth,
	[property: JsonPropertyName("request-parameters")] IReadOnlyList<WebServiceParameterView> RequestParameters,
	[property: JsonPropertyName("response-parameters")] IReadOnlyList<WebServiceParameterView> ResponseParameters,

	[property: JsonPropertyName("message-element")]
	[property: Description("SOAP: the root element of the request body, e.g. tem:Add")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string MessageElement = null,

	[property: JsonPropertyName("soap-action")]
	[property: Description("SOAP: the SOAPAction header value; empty means no SOAPAction header is sent")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string SoapAction = null);

/// <summary>A SOAP namespace the envelope declares; the default one qualifies an element path without a prefix.</summary>
public sealed record WebServiceNamespaceView(
	[property: JsonPropertyName("prefix")] string Prefix,
	[property: JsonPropertyName("uri")] string Uri,
	[property: JsonPropertyName("default")] bool Default);

public sealed record WebServiceAuthView(
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("username-sys-setting")] string UsernameSysSetting,
	[property: JsonPropertyName("password-sys-setting")] string PasswordSysSetting,
	[property: JsonPropertyName("oauth-app-id")] string OAuthAppId,
	[property: JsonPropertyName("constant-credentials-masked")] bool ConstantCredentialsMasked);

public sealed record WebServiceView(
	[property: JsonPropertyName("uid")] Guid UId,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("package")] string Package,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("base-uri")] string BaseUri,
	[property: JsonPropertyName("retry-count")] int RetryCount,
	[property: JsonPropertyName("auth")] WebServiceAuthView Auth,
	[property: JsonPropertyName("methods")] IReadOnlyList<WebServiceMethodView> Methods,

	[property: JsonPropertyName("namespaces")]
	[property: Description("SOAP: the namespaces the envelope declares, the default first")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<WebServiceNamespaceView> Namespaces = null,

	[property: JsonPropertyName("description")]
	[property: Description("The service description, when it has one")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string Description = null);

public sealed record WebServiceListItem(
	[property: JsonPropertyName("uid")] Guid UId,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("package")] string Package,
	[property: JsonPropertyName("base-uri")] string BaseUri = null);

public sealed record WebServiceUsage(
	[property: JsonPropertyName("process")] string Process,
	[property: JsonPropertyName("process-caption")] string ProcessCaption,
	[property: JsonPropertyName("package")] string Package,
	[property: JsonPropertyName("element")] string Element,
	[property: JsonPropertyName("method")] string Method);

public sealed record WebServiceToolResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string Error = null,
	[property: JsonPropertyName("service")] WebServiceView Service = null,
	[property: JsonPropertyName("usages")] IReadOnlyList<WebServiceUsage> Usages = null,
	[property: JsonPropertyName("notices")] IReadOnlyList<string> Notices = null);

public sealed record WebServiceListResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string Error = null,
	[property: JsonPropertyName("services")] IReadOnlyList<WebServiceListItem> Services = null);

public sealed record WebServiceTestResult(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string Error = null,
	[property: JsonPropertyName("status-code")] int? StatusCode = null,
	[property: JsonPropertyName("response-body")] string ResponseBody = null,
	[property: JsonPropertyName("response-body-truncated")] bool ResponseBodyTruncated = false,
	[property: JsonPropertyName("sent-secrets-masked")] bool SentSecretsMasked = false,

	[property: JsonPropertyName("response-values")]
	[property: Description("The response parameters as the platform parsed them, by code, masked like the body. A value the API did not send shows the parameter's default (or null); a parameter that may carry a credential (a setting or secret default, a cookie, a credential-named header) is masked")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	JsonElement? ResponseValues = null,

	[property: JsonPropertyName("response-values-omitted")]
	[property: Description("True when the parsed response values were longer than the response-body limit and are not returned")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? ResponseValuesOmitted = null,

	[property: JsonPropertyName("notices")]
	[property: Description("Problems in the method definition the call surfaced, e.g. a JSONPath on an XML response that reads nothing")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<string> Notices = null,

	[property: JsonPropertyName("confirmation-required")]
	[property: Description("True when nothing was sent because the method can change data in the external API and the call did not carry user-confirmed: true; ask the user, and repeat with it only after their explicit yes")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	bool? ConfirmationRequired = null);

#endregion

/// <summary>
/// The only way a web service schema leaves clio. The platform read returns authentication values (a Basic
/// password included) and constant defaults that services use for API keys and bearer headers, so every
/// tool result is built here and nothing else serializes a <see cref="ServiceSchemaDto"/> outward.
/// </summary>
public static partial class WebServiceSchemaView {

	[GeneratedRegex(@"key|token|secret|passw|passphrase|passcode|(?<!com|by|sur)pass(?-i:(?![a-z]))|pwd|auth|credential|signature|session|cookie|licen[cs]e|nonce|bearer|jwt",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex SecretNamePattern();

	/// <summary>
	/// Whether a stored parameter's constant default must be hidden, judged where the platform sends it
	/// (<see cref="IsSecretConstantAt"/>): a SOAP-header element (its own flag, or <paramref name="inSoapHeader"/> for an
	/// item the platform renders under a SOAP-header parent) is judged as a header.
	/// </summary>
	/// <param name="parameter">The stored parameter.</param>
	/// <param name="inSoapHeader">
	/// Whether the parameter travels in a SOAP Header through a parent (the parameter's own flag is read here too, so a
	/// caller may pass own-or-parent).
	/// </param>
	/// <param name="underSecretParent">
	/// Whether a parent in the SOAP Header has a credential-like code or path (<see cref="IsSecretSoapParent"/>).
	/// </param>
	/// <returns>Whether its constant default is a secret.</returns>
	public static bool IsSecretLike(ServiceParameterDto parameter, bool inSoapHeader = false, bool underSecretParent = false) =>
		IsSecretConstantAt(parameter.Name, parameter.Path, parameter.Type, parameter.DefValue?.Value,
			inSoapHeader || parameter.IsSoapHeader == true, underSecretParent);

	/// <summary>
	/// The one rule by which a constant default is a secret, shared by register and modify-web-service (refused),
	/// describe-web-service (hidden), the carry-over of a hidden constant, read-wsdl's carry-over of a Studio constant,
	/// and a test call (masked in the echo). A cookie always is one, also one stored with a SOAP-header flag. Under a
	/// credential-like code or wire name (<see cref="IsSecretName"/>, Authorization included) any value is. A query value
	/// follows <see cref="IsSecretQueryValue"/>. In a header or the SOAP Header, an item under a credential-like
	/// SOAP-header parent is one, and a value is one when it looks like a key (<see cref="IsKeyLikeValue"/>), so
	/// <c>Accept-Language: en-US</c> or <c>X-Api-Version: 2</c> is an ordinary constant, as Studio allows it. A body or
	/// path value is judged by name only.
	/// </summary>
	/// <param name="code">The parameter code.</param>
	/// <param name="wireName">The parameter path (wire name), or null.</param>
	/// <param name="placement">The parameter placement.</param>
	/// <param name="value">The constant default, or null.</param>
	/// <param name="inSoapHeader">Whether the parameter travels in the SOAP Header (its own flag or a parent's).</param>
	/// <param name="underSecretParent">Whether a parent in the SOAP Header has a credential-like code or path.</param>
	/// <returns>Whether a constant default there is a secret.</returns>
	public static bool IsSecretConstantAt(string code, string wireName, int placement, string value, bool inSoapHeader,
		bool underSecretParent) {
		if (placement == ServiceSchemaCodes.Placements["cookie"] || IsSecretName(code) || IsSecretName(wireName)) {
			return true;
		}
		if (inSoapHeader) {
			placement = ServiceSchemaCodes.Placements["header"];
		}
		if (placement == ServiceSchemaCodes.Placements["query"]) {
			return IsSecretQueryValue(code, value) || IsSecretQueryValue(wireName, value);
		}
		return placement == ServiceSchemaCodes.Placements["header"]
			&& ((inSoapHeader && underSecretParent) || IsKeyLikeValue(value));
	}

	/// <summary>
	/// Whether a stored parameter has a non-empty constant default that is a secret
	/// (<see cref="IsSecretLike(ServiceParameterDto, bool, bool)"/>). The one check describe-web-service hides by, a test
	/// call masks by, and modify-web-service carries a hidden constant over by, so no constant is shown in one place and
	/// masked in another.
	/// </summary>
	/// <param name="parameter">The stored parameter, or null.</param>
	/// <param name="inSoapHeader">Whether the parameter travels in a SOAP Header through a parent.</param>
	/// <param name="underSecretParent">Whether a parent in the SOAP Header has a credential-like code or path.</param>
	/// <returns>Whether it holds a secret constant.</returns>
	public static bool IsSecretConstant(ServiceParameterDto parameter, bool inSoapHeader = false,
		bool underSecretParent = false) =>
		parameter?.DefValue is { Source: ServiceSchemaCodes.ValueSourceConst, Value.Length: > 0 }
		&& IsSecretLike(parameter, inSoapHeader, underSecretParent);

	/// <summary>
	/// Whether the items of a parameter travel in the SOAP Header under a credential-like parent: the parameter is in the
	/// Header and its code or path looks like a credential, or a parent of it already is such a parent. Every tool passes
	/// this down the item tree the same way, so an item there is hidden, refused, carried and masked alike.
	/// </summary>
	/// <param name="inSoapHeader">Whether the parameter travels in the SOAP Header (its own flag or a parent's).</param>
	/// <param name="underSecretParent">Whether a parent of it is already such a parent.</param>
	/// <param name="code">The parameter code.</param>
	/// <param name="path">The parameter path.</param>
	/// <returns>Whether its items are under a credential-like SOAP-header parent.</returns>
	public static bool IsSecretSoapParent(bool inSoapHeader, bool underSecretParent, string code, string path) =>
		inSoapHeader && (underSecretParent || IsSecretName(code) || IsSecretName(path));

	// A value part at least this long can be a generated key: 16 characters is the shortest key in common use (a 64-bit
	// key in hex), the length MinHostingKeyLength takes for a key under a hosting key name. Shorter parts are versions,
	// language tags, short media types and flags.
	private const int MinKeyLikeLength = MinHostingKeyLength;

	// A run of one kind of character this long is a key too: a letters-only key or passphrase (correcthorsebatterystaple),
	// which the letters-and-digits arm misses. Shorter words (internationalization is 20) stay plain.
	private const int MinOpaqueLettersLength = 24;

	// A digits-only part this long is an account, card or merchant number, or a numeric key; a version, a count or a
	// short code is shorter.
	private const int MinOpaqueDigitsLength = 12;

	// A part of this length or more with an upper-case letter, a lower-case letter, a digit and a symbol has the shape of
	// a chosen password (Summer2024!); a plain header value seldom mixes all four.
	private const int MinChosenWordLength = 8;
	private const string ChosenWordSymbols = "!@#$%^&*?~";

	// Where a header value splits into parts judged one by one: a scheme and its key (Bearer abc...), list items
	// (gzip, deflate) and parameters (text/xml; charset=utf-8). A key in base64, base64url, hex or a JWT holds none.
	private static readonly char[] HeaderValueSeparators = [' ', '\t', '\r', '\n', ',', ';'];

	// Where a media type or a product token (application/vnd.github.v3+json, AppleWebKit/537.36) splits into the words
	// and version numbers it is made of.
	private static readonly char[] ProductTokenSeparators = ['/', '.', '+', '-', '_'];

	// Authentication schemes: a header value that starts with one carries a credential in what follows, whatever its
	// length (Basic over a short user:password is under 16 characters).
	private static readonly HashSet<string> AuthSchemes = new(StringComparer.OrdinalIgnoreCase) {
		"Basic", "Bearer", "Digest", "Token", "Negotiate", "NTLM", "HMAC", "AWS4-HMAC-SHA256", "SharedKey",
		"SharedKeyLite", "ApiKey", "OAuth", "Hawk", "MAC"
	};

	[GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*$", RegexOptions.CultureInvariant)]
	private static partial Regex UriSchemePattern();

	// A service or contract name made of capitalised words, with an optional interface prefix, version and file
	// extension: ICustomerRelationshipService, GetCustomer, ICustomerService2, IOrderManagementV2, CustomerServiceV2.svc.
	[GeneratedRegex(@"^I?(?:[A-Z][a-z]+)+(?:V?\d{1,3})?(?:\.[a-z]{2,5})?$", RegexOptions.CultureInvariant)]
	private static partial Regex ServiceNamePattern();

	/// <summary>
	/// Whether a header value looks like a key, judged without its name, because a header carrying a key is often named
	/// neutrally (<c>X-Partner</c>, <c>X-Client</c>). It is when <see cref="KeyLikeParts"/> finds any part.
	/// </summary>
	/// <param name="value">The header value, or null.</param>
	/// <returns>Whether any part of it looks like a key.</returns>
	public static bool IsKeyLikeValue(string value) => KeyLikeParts(value).Any();

	/// <summary>
	/// The parts of a header value that make it a key, in the forms an API may echo them alone: a test call masks each
	/// of them, not only the whole value. The value is split at spaces, commas and semicolons, and a part outside an
	/// address at '&amp;'. A value whose first part is an authentication scheme (<c>Basic</c>, <c>Bearer</c>,
	/// <c>Token</c>..., also quoted) gives what follows it, a bare token after it, and each of its parameters that is a key
	/// or under a credential name. Otherwise a part, without quotes, base64 padding and a
	/// leading <c>name=</c>, is a key when the name is a credential name, or a hosting key name (<c>sig</c>, <c>code</c>...)
	/// with a value of 16+ characters (<see cref="IsSecretQueryValue"/>), or when the value is: 16+ characters mixing
	/// letters and digits (a generated key: a random 16-character letters-and-digits key lacks a digit about one time in
	/// 17, (52/62)^16; a base64 one about one time in 15; a 32-character one about one time in 280), 24+ letters or
	/// underscores, 12+ digits, or 8+ characters with an upper and a lower case letter, a digit and one of
	/// <c>!@#$%^&amp;*?~</c> (a chosen password). Any user information of an address (anything between <c>://</c> and an
	/// <c>@</c>) is a key; so is a path segment, query value or fragment value of an address judged the same way (a long
	/// path segment that is a service or contract name, ICustomerService2 or CustomerServiceV2.svc, is not), and the text before a scheme that is not one. Text made
	/// of words, short numbers and at most one short mixed piece joined by <c>/ . + - _</c> is plain: a media type
	/// (<c>application/vnd.github.v3+json</c>), a product token (<c>python-requests/2.31.0</c>), a host name, a
	/// hyphenated name (<c>production-eu-west-1</c>). Not caught: a short key or password without those four kinds of
	/// character (<c>4821</c>, <c>hunter2</c>, <c>Winter-2024</c>) under a neutral name; the agent rule that a key always
	/// goes through a system setting covers those. Known false positives, hidden and refused like keys: a GUID, an ISO
	/// timestamp, a build tag (<c>4.18.2-build20240101</c>), a multipart boundary, and a value whose first word is a
	/// scheme name (<c>Basic monthly</c>).
	/// </summary>
	/// <param name="value">The header value, or null.</param>
	/// <returns>The key-like parts, distinct; empty when the value is plain.</returns>
	public static IReadOnlyList<string> KeyLikeParts(string value) {
		var parts = new List<string>();
		if (string.IsNullOrEmpty(value)) {
			return parts;
		}
		AddUserInformation(parts, value);
		string[] split = value.Split(HeaderValueSeparators, StringSplitOptions.RemoveEmptyEntries);
		bool scheme = split.Length > 1 && AuthSchemes.Contains(split[0].Trim('"', '\''));
		if (scheme) {
			// Everything after the scheme is its credential, whatever its shape: the value is a key, and the credential
			// as a whole is masked too.
			string trimmed = value.Trim().Trim('"', '\'');
			AddPart(parts, trimmed[(trimmed.IndexOfAny(HeaderValueSeparators) + 1)..].Trim());
		}
		for (int i = 0; i < split.Length; i++) {
			string part = split[i].Trim('"', '\'');
			if (scheme && i > 0) {
				AddSchemePieces(parts, part);
				continue;
			}
			AddPartPieces(parts, part);
		}
		return parts.Distinct(StringComparer.Ordinal).ToList();
	}

	// Fail closed, as a soap-action is checked: any '@' after a "://" ends user information, whatever the password holds
	// ('/', '#', ';', ',' or a space would otherwise end the authority or the part first). The forms an echo may carry
	// are the whole user information and the password, as written and unescaped.
	private static void AddUserInformation(List<string> parts, string value) {
		int scheme = value.IndexOf("://", StringComparison.Ordinal);
		int at = value.LastIndexOf('@');
		if (scheme < 0 || at <= scheme + 3) {
			return;
		}
		string userInfo = value[(scheme + 3)..at];
		int colon = userInfo.IndexOf(':');
		foreach (string piece in new[] { userInfo, colon < 0 ? null : userInfo[(colon + 1)..] }) {
			AddPart(parts, piece);
			AddPart(parts, piece is null ? null : Uri.UnescapeDataString(piece));
		}
	}

	// What follows an authentication scheme is its credential: a bare token in every form, and of a scheme parameter
	// (token=..., signature="...", oauth_signature="...%2F...") the value, when it is a key or under a credential name.
	// A plain parameter (realm="api", qop=auth, nc=00000001) is left out, so it does not mask ordinary text or numbers.
	private static void AddSchemePieces(List<string> parts, string part) {
		int equals = part.IndexOf('=');
		if (equals <= 0 || equals == part.Length - 1 || part.TrimEnd('=').IndexOf('=') < 0) {
			if (!part.All(char.IsAsciiDigit)) {
				AddPart(parts, part);
				AddPart(parts, part.TrimEnd('='));
			}
			return;
		}
		string name = part[..equals];
		string schemeValue = part[(equals + 1)..].Trim('"', '\'');
		string unescaped = Uri.UnescapeDataString(schemeValue);
		if (IsSecretQueryValue(name, schemeValue) || IsKeyShaped(schemeValue.TrimEnd('='))
				|| IsKeyShaped(unescaped.TrimEnd('='))) {
			AddPart(parts, part);
			AddPart(parts, schemeValue);
			AddPart(parts, schemeValue.TrimEnd('='));
			AddPart(parts, unescaped);
		}
	}

	private static void AddPart(List<string> parts, string part) {
		if (!string.IsNullOrEmpty(part)) {
			parts.Add(part);
		}
	}

	private static void AddPartPieces(List<string> parts, string part) {
		int scheme = part.IndexOf("://", StringComparison.Ordinal);
		if (scheme > 0 && UriSchemePattern().IsMatch(part[..scheme])) {
			AddAddressPieces(parts, part[(scheme + 3)..]);
			return;
		}
		if (scheme >= 0) {
			// Not a scheme in front (id=KEY&return=https://...): the text before it is judged like any part, and so is
			// the address after it.
			AddPairPieces(parts, part[..scheme]);
			AddAddressPieces(parts, part[(scheme + 3)..]);
			return;
		}
		AddPairPieces(parts, part);
	}

	// A part outside an address may join name=value pairs with '&' (user=svc&pwd=abc); each is judged, so the key alone
	// is found too. A key in base64, hex or a JWT holds no '&'.
	private static void AddPairPieces(List<string> parts, string part) {
		foreach (string pair in part.Split('&', StringSplitOptions.RemoveEmptyEntries)) {
			AddTokenPieces(parts, pair);
		}
	}

	// A host is not a key; user information, a path segment (a webhook token), a query or a fragment value can be.
	private static void AddAddressPieces(List<string> parts, string rest) {
		// User information was taken from the whole value (AddUserInformation).
		int authorityEnd = rest.IndexOfAny(['/', '?', '#']);
		if (authorityEnd < 0) {
			return;
		}
		string tail = rest[authorityEnd..];
		int hash = tail.IndexOf('#');
		string fragment = hash < 0 ? string.Empty : tail[(hash + 1)..];
		string beforeFragment = hash < 0 ? tail : tail[..hash];
		int question = beforeFragment.IndexOf('?');
		string path = question < 0 ? beforeFragment : beforeFragment[..question];
		string query = question < 0 ? string.Empty : beforeFragment[(question + 1)..];
		// A path segment is often a service or contract name (IOrderFulfillmentService, ICustomerService2,
		// CustomerServiceV2.svc), which is not a key when it reads as capitalised words, optionally followed by a version
		// and a file extension; a random letters run or a mixed one (a webhook token) is.
		foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
			string unescaped = Uri.UnescapeDataString(segment);
			if (!ServiceNamePattern().IsMatch(unescaped)) {
				AddTokenPieces(parts, unescaped);
			}
		}
		foreach (string pair in (query + "&" + fragment).Split('&', StringSplitOptions.RemoveEmptyEntries)) {
			AddTokenPieces(parts, Uri.UnescapeDataString(pair));
		}
	}

	// Base64 padding is dropped first, so a key's trailing '=' is not read as a name=value separator; a name=value part
	// is a key under a credential or hosting key name (password=..., sig=...) and is otherwise judged by its value
	// (max-age=3600 is not a key).
	private static void AddTokenPieces(List<string> parts, string token) {
		string core = token.TrimEnd('=');
		int equals = core.IndexOf('=');
		if (equals > 0) {
			string name = core[..equals].Trim('"', '\'');
			string pairValue = core[(equals + 1)..].Trim('"', '\'');
			if (IsSecretQueryValue(name, pairValue)) {
				AddPart(parts, token);
				AddPart(parts, pairValue);
				AddPart(parts, Uri.UnescapeDataString(pairValue));
				return;
			}
			core = pairValue;
		}
		if (IsKeyShaped(core)) {
			AddPart(parts, token);
			AddPart(parts, core);
			AddPart(parts, Uri.UnescapeDataString(core));
		}
	}

	private static bool IsKeyShaped(string core) {
		if (core.Length >= MinKeyLikeLength && core.Any(char.IsAsciiLetter) && core.Any(char.IsAsciiDigit)
				&& !IsWordsToken(core)) {
			return true;
		}
		if (core.Length >= MinOpaqueLettersLength && core.All(c => char.IsAsciiLetter(c) || c == '_')) {
			return true;
		}
		if (core.Length >= MinOpaqueDigitsLength && core.All(char.IsAsciiDigit)) {
			return true;
		}
		return core.Length >= MinChosenWordLength && core.Any(char.IsAsciiLetterUpper) && core.Any(char.IsAsciiLetterLower)
			&& core.Any(char.IsAsciiDigit) && core.Any(c => ChosenWordSymbols.Contains(c));
	}

	// Text made of words and short numbers joined by '/', '.', '+', '-' or '_': a media type (application/vnd.github.v3+json),
	// a product token (python-requests/2.31.0, CreatioIntegration/1.0), a hyphenated name (production-eu-west-1,
	// crm-integration-v2). It holds at least one real word (5+ letters); each piece is a word (under 24 letters), a short
	// number (under 12 digits) or a piece of up to 4 characters; at most one piece mixes letters and digits; and it is not
	// three or more groups of one length (a licence key, ABCD-EFGH-1JKL-MNOP). A generated key has a long mixed piece, and
	// a structured key (AB12-CD34-EF56-GH78, acct/123456789012345, a GUID) has several mixed pieces, equal groups or a long
	// number, so neither is one.
	private static bool IsWordsToken(string token) {
		string[] pieces = token.Split(ProductTokenSeparators, StringSplitOptions.RemoveEmptyEntries);
		static bool Mixed(string piece) => piece.Any(char.IsAsciiLetter) && piece.Any(char.IsAsciiDigit);
		bool equalGroups = pieces.Length >= 3 && pieces.All(piece => piece.Length == pieces[0].Length);
		return pieces.Length > 1 && !equalGroups && pieces.Count(Mixed) <= 1
			&& pieces.Any(piece => piece.Length > 4 && piece.All(char.IsAsciiLetter))
			&& pieces.All(piece => piece.Length <= 4
				|| (piece.All(char.IsAsciiLetter) && piece.Length < MinOpaqueLettersLength)
				|| (piece.All(char.IsAsciiDigit) && piece.Length < MinOpaqueDigitsLength));
	}

	/// <summary>Whether a parameter code or wire name looks like a credential (key, token, secret, password, ...).</summary>
	public static bool IsSecretName(string name) => SecretNamePattern().IsMatch(name ?? string.Empty);

	// Query parameters that carry a key in common hosting and APIs (an Azure SAS signature, an Azure Functions key, an
	// OpenWeatherMap appid, a request signature) but are not matched by the credential-name pattern.
	private static readonly HashSet<string> SecretQueryNames = new(StringComparer.OrdinalIgnoreCase) {
		"sig", "sas", "code", "appid", "sign", "hmac"
	};

	// A value under a hosting key name at least this long is a key (an Azure SAS signature or Functions key is 40+
	// characters); a shorter one is an ordinary value (rates?code=USD).
	private const int MinHostingKeyLength = 16;

	// Where a key may start: after a pair separator, the query mark, or a character a key never holds (a path, a
	// scheme, user information, or an OData key segment such as Orders(Id=1,Key=2) in front of it).
	private const string KeyBoundaries = "&;?/@:(,";

	// Where a value in the path (before the first '?') ends: a ;name=value path parameter or an OData key segment
	// closes at these or at the query mark; a value in the query runs to the next '&'.
	private const string PathValueEnds = "/;),?";

	// Where the part of a value that decides the hosting-key length ends: rates?code=USD;callback=f keeps USD. Not at
	// '/', '+' or '=': a base64 key (an Azure SAS signature) holds them.
	private const string ValueCoreEnds = ";#";

	// Where an echoed key may have been cut by a server that splits there: one more form of each hidden value.
	private const string EchoFormEnds = ";?@/#";

	/// <summary>
	/// Whether a URI query parameter name carries a credential, whatever its value: the credential-name pattern, plus
	/// the key names common hosting and APIs use. read-wsdl uses it, stricter than
	/// <see cref="IsSecretQueryValue"/>: it refuses a WSDL URL with such a parameter and leaves one out of a base-uri.
	/// </summary>
	/// <param name="name">The parameter name, unescaped.</param>
	/// <returns>Whether a value under it is taken as a secret.</returns>
	public static bool IsSecretQueryName(string name) =>
		IsSecretName(name) || SecretQueryNames.Contains(name ?? string.Empty);

	/// <summary>
	/// Whether a URI query value is a secret: any value under a credential name, or a key-length value (16 characters
	/// or more) under a hosting key name (<c>sig</c>, <c>sas</c>, <c>code</c>, <c>appid</c>, <c>sign</c>,
	/// <c>hmac</c>), whose short values are ordinary. One rule for a key written into a URI and for a query parameter's
	/// constant default: describe-web-service, list-web-services and a test call hide such a value, and register and
	/// modify-web-service refuse to write one.
	/// </summary>
	/// <param name="name">The parameter name, unescaped.</param>
	/// <param name="value">The value, unescaped.</param>
	/// <returns>Whether the value is a secret.</returns>
	public static bool IsSecretQueryValue(string name, string value) =>
		IsSecretName(name)
		|| (SecretQueryNames.Contains(name ?? string.Empty) && (value ?? string.Empty).Length >= MinHostingKeyLength);

	/// <summary>
	/// Whether a query or path value is only a <c>{placeholder}</c>, filled per call rather than a constant, also in
	/// quotes as an OData string key writes it (<c>Products(ProductKey='{id}')</c>).
	/// </summary>
	/// <param name="value">The value as written.</param>
	/// <returns>Whether the whole value is one placeholder.</returns>
	public static bool IsPlaceholderValue(string value) => WholePlaceholderPattern().IsMatch(value ?? string.Empty);

	/// <summary>
	/// The keys of a URI that register and modify-web-service refuse, read exactly as describe-web-service reads them:
	/// keys whose value is a secret (<see cref="IsSecretQueryValue"/>), and keys whose value holds the mask describe
	/// shows in place of one.
	/// </summary>
	/// <param name="uri">The URI as the agent sent it.</param>
	/// <returns>The keys, unescaped; empty for no URI.</returns>
	public static UriQueryKeys FindQueryKeys(string uri) {
		QueryScan scan = ScanQuery(uri);
		return new UriQueryKeys(scan.SecretKeys, scan.MaskKeys);
	}

	/// <summary>
	/// The path parameters of a method whose constant default fills a key of its URI: their <c>{placeholder}</c> sits
	/// in the value of a key named like a credential or a hosting key (<c>search?api_key={k}</c>,
	/// <c>?sig={a}{b}</c>) and the constant is a secret under that key. They are hidden, masked in an echo and refused
	/// on write like a key written into the URI itself.
	/// </summary>
	/// <param name="uri">The method URI as stored or sent.</param>
	/// <param name="parameters">The method's request parameters.</param>
	/// <returns>Each such parameter's <c>path</c> with its constant.</returns>
	public static IReadOnlyDictionary<string, string> SecretPlaceholderPaths(string uri,
		ServiceItemCollection<ServiceParameterDto> parameters) {
		var paths = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach ((string key, string name) in ScanQuery(uri).Placeholders) {
			ServiceParameterDto filler = parameters?.Items.Values.FirstOrDefault(p =>
				p.Type == ServiceSchemaCodes.Placements["path"] && string.Equals(p.Path, name, StringComparison.Ordinal));
			if (filler?.DefValue is { Source: ServiceSchemaCodes.ValueSourceConst, Value: { Length: > 0 } value }
					&& IsSecretQueryValue(key, value)) {
				paths[name] = value;
			}
		}
		return paths;
	}

	/// <summary>
	/// A stored URI with every secret query value (<see cref="IsSecretQueryValue"/>) replaced by
	/// <see cref="WebServiceResponseMasker.Mask"/>. Studio stores such a constant (<c>search?api_key=...</c>), so a
	/// read must not print it. A <c>{placeholder}</c> value is kept.
	/// </summary>
	/// <param name="uri">The URI as stored.</param>
	/// <returns>The URI with its query secrets masked.</returns>
	public static string MaskUriQuery(string uri) =>
		string.IsNullOrEmpty(uri) ? uri : ApplySpans(uri, ScanQuery(uri).Spans, 0);

	/// <summary>
	/// The values <see cref="MaskUriQuery(string)"/> hides, in the forms they go out or come back in: as stored, cut
	/// at the first <c>;</c>, <c>?</c>, <c>@</c>, <c>/</c> or <c>#</c> (a server may split there), URL-unescaped, and
	/// with <c>+</c> read as a space.
	/// </summary>
	/// <param name="uri">The URI as stored.</param>
	/// <returns>The secret query values; empty when there are none.</returns>
	public static IReadOnlyList<string> UriQuerySecrets(string uri) {
		var values = new List<string>();
		foreach ((int start, int end) in ScanQuery(uri).Spans) {
			string value = uri[start..end];
			int cut = value.IndexOfAny(EchoFormEnds.ToCharArray());
			string core = cut < 0 ? value : value[..cut];
			foreach (string form in new[] { value, core }.SelectMany(v =>
					new[] { v, Uri.UnescapeDataString(v), Uri.UnescapeDataString(v.Replace('+', ' ')) })) {
				if (form.Length > 0 && !values.Contains(form)) {
					values.Add(form);
				}
			}
		}
		return values;
	}

	/// <summary>
	/// The display form of a stored base URI: user information hidden as <see cref="StripUserInfo"/> hides it, and
	/// query secrets masked. The secrets are found in the stored text first, so an <c>@</c> inside the query, where
	/// <see cref="StripUserInfo"/> cuts, never shows the rest of a secret value.
	/// </summary>
	/// <param name="uri">The URI as stored.</param>
	/// <returns>The URI as list, describe and refusals may show it.</returns>
	public static string ShownUri(string uri) {
		if (string.IsNullOrEmpty(uri)) {
			return uri;
		}
		List<(int Start, int End)> spans = ScanQuery(uri).Spans;
		int scheme = uri.IndexOf("://", StringComparison.Ordinal);
		int start = scheme < 0 ? 0 : scheme + 3;
		int at = uri.LastIndexOf('@');
		return at < start
			? ApplySpans(uri, spans, 0)
			: uri[..start] + "[redacted]" + ApplySpans(uri, spans, at);
	}

	/// <summary>
	/// The display form of a namespace URI or a SOAP action: user information is hidden only when the value is an
	/// absolute URI that carries one (<see cref="StripUserInfo"/> would change an <c>urn:</c> value or an action with
	/// an <c>@</c>), and query secrets always.
	/// </summary>
	/// <param name="value">The value as stored.</param>
	/// <returns>The value as describe shows it.</returns>
	public static string ShownUriValue(string value) =>
		WebServiceDefinitionBuilder.HasUserInfo(value) ? ShownUri(value) : MaskUriQuery(value);

	// uri from `from` on, every secret span replaced by the mask. A span that `from` falls inside (the '@' StripUserInfo
	// cut at sits in a secret value) shows that character and then the mask, so no tail of the value shows.
	private static string ApplySpans(string uri, List<(int Start, int End)> spans, int from) {
		var parts = new List<string>();
		int position = from;
		foreach ((int start, int end) in spans) {
			if (end <= position) {
				continue;
			}
			parts.Add(start < position ? uri[position].ToString() : uri[position..start]);
			parts.Add(WebServiceResponseMasker.Mask);
			position = end;
		}
		parts.Add(uri[position..]);
		return string.Concat(parts);
	}

	private sealed record QueryScan(List<(int Start, int End)> Spans, List<string> SecretKeys, List<string> MaskKeys,
		List<(string Key, string Name)> Placeholders);

	// One linear pass over the whole URI; the read (describe, list, test call) and the write check use it alike. A
	// pair runs to the next '&' or the end. Inside it a key is the text between the last boundary ('&', ';', '?', '/',
	// '@', ':', '(', ',') and an '=', so a path, user information or an OData key segment is never part of a key. In
	// the query a value runs to the end of its pair, so the first key with a secret value hides everything after its
	// '=' (';', '?', '@' or a fragment included) and no tail of a secret shows; in the path a value ends at '/', ';',
	// ')' or ','. A pair that holds the mask is reported under the last key before the mask.
	private static QueryScan ScanQuery(string uri) {
		var scan = new QueryScan([], [], [], []);
		if (string.IsNullOrEmpty(uri)) {
			return scan;
		}
		int firstQuestion = uri.IndexOf('?');
		int pairStart = 0;
		while (pairStart <= uri.Length) {
			int pairEnd = uri.IndexOf('&', pairStart);
			if (pairEnd < 0) {
				pairEnd = uri.Length;
			}
			ScanPair(uri, pairStart, pairEnd, firstQuestion, scan);
			pairStart = pairEnd + 1;
		}
		return scan;
	}

	// Every key of the pair is read; a key inside a value already hidden is skipped (that span covers it), so path
	// values, which end before the pair does, are each checked. A placeholder anywhere in the value of a key named like
	// a credential or a hosting key is recorded for SecretPlaceholderPaths.
	private static void ScanPair(string uri, int start, int end, int firstQuestion, QueryScan scan) {
		int maskAt = IndexOfMask(uri, start, end);
		int keyStart = start;
		int hiddenUntil = start;
		string maskKey = null;
		for (int i = start; i < end; i++) {
			char c = uri[i];
			if (c == '=' && keyStart >= 0) {
				ScanKey(new PairContext(uri, end, firstQuestion, maskAt, scan), keyStart, i, ref hiddenUntil, ref maskKey);
				keyStart = -1;
			} else if (KeyBoundaries.Contains(c)) {
				keyStart = i + 1;
			}
		}
		if (maskKey is not null) {
			scan.MaskKeys.Add(maskKey);
		}
	}

	// The key that ends at the '=' at equalsAt: noted when the mask follows it in the pair, and its value read unless an
	// earlier hidden value already covers it.
	private static void ScanKey(PairContext pair, int keyStart, int equalsAt, ref int hiddenUntil, ref string maskKey) {
		if (equalsAt <= keyStart) {
			return;
		}
		string key = Uri.UnescapeDataString(pair.Uri[keyStart..equalsAt]);
		if (pair.MaskAt > equalsAt) {
			maskKey = key;
		}
		if (equalsAt >= hiddenUntil) {
			hiddenUntil = ScanValue(pair.Uri, key, equalsAt, pair.End, pair.FirstQuestion, pair.Scan, hiddenUntil);
		}
	}

	// What every key of one pair is read against: the URI, where the pair ends, the first '?', where the mask sits in
	// the pair (or -1) and the scan the results go into.
	private readonly record struct PairContext(string Uri, int End, int FirstQuestion, int MaskAt, QueryScan Scan);

	// Reads the value after the '=' at equalsAt: a secret value is hidden (the returned position is where hiding ends), a
	// placeholder in the value of a credential-like key is recorded. A value after the first '?' runs to the end of the
	// pair; one in the path ends where a path value does.
	private static int ScanValue(string uri, string key, int equalsAt, int end, int firstQuestion, QueryScan scan,
		int hiddenUntil) {
		int valueEnd = firstQuestion >= 0 && equalsAt > firstQuestion ? end : PathValueEnd(uri, equalsAt + 1, end);
		if (IsSecretValueAt(key, uri, equalsAt + 1, valueEnd)) {
			scan.Spans.Add((equalsAt + 1, valueEnd));
			scan.SecretKeys.Add(key);
			return valueEnd;
		}
		if (IsSecretName(key) || SecretQueryNames.Contains(key)) {
			AddPlaceholders(uri, equalsAt + 1, valueEnd, key, scan);
		}
		return hiddenUntil;
	}

	private static void AddPlaceholders(string uri, int from, int end, string key, QueryScan scan) {
		int open = from < end ? uri.IndexOf('{', from, end - from) : -1;
		while (open >= 0) {
			int close = open + 1 < end ? uri.IndexOf('}', open + 1, end - open - 1) : -1;
			if (close < 0) {
				return;
			}
			if (close > open + 1) {
				scan.Placeholders.Add((key, uri[(open + 1)..close]));
			}
			open = close + 1 < end ? uri.IndexOf('{', close + 1, end - close - 1) : -1;
		}
	}

	private static int PathValueEnd(string uri, int from, int end) {
		int stop = uri.IndexOfAny(PathValueEnds.ToCharArray(), from, end - from);
		return stop < 0 ? end : stop;
	}

	// The name is checked first, so the value is cut out only for a key that can be a secret. A hosting key name
	// counts its value only up to a ';', '?', '@', '/' or '#', where a server may end it.
	private static bool IsSecretValueAt(string key, string uri, int from, int end) {
		if (end <= from || (!IsSecretName(key) && !SecretQueryNames.Contains(key))) {
			return false;
		}
		string value = uri[from..end];
		if (IsMask(value) || IsPlaceholderValue(value)) {
			return false;
		}
		int cut = value.IndexOfAny(ValueCoreEnds.ToCharArray());
		return IsSecretQueryValue(key, Uri.UnescapeDataString(cut < 0 ? value : value[..cut]));
	}

	private static bool IsMask(string value) =>
		string.Equals(Uri.UnescapeDataString(value), WebServiceResponseMasker.Mask, StringComparison.OrdinalIgnoreCase);

	// The first "masked" in the pair, any case, after '[' or an escaped "%5B": the mask as describe shows it, or as an
	// agent escapes it.
	private static int IndexOfMask(string uri, int start, int end) {
		int at = start;
		while (at < end) {
			int word = uri.IndexOf("masked", at, end - at, StringComparison.OrdinalIgnoreCase);
			if (word < 0) {
				return -1;
			}
			if ((word > 0 && uri[word - 1] == '[')
					|| (word >= 3 && string.Compare(uri, word - 3, "%5B", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)) {
				return word;
			}
			at = word + 1;
		}
		return -1;
	}

	[GeneratedRegex(@"^(['""]?)\{[^{}]+\}\1$")]
	private static partial Regex WholePlaceholderPattern();

	public static WebServiceView From(ServiceSchemaDto schema, string packageName) {
		bool soap = IsSoap(schema);
		return new WebServiceView(schema.UId, schema.Name, Localized(schema.Caption), packageName, TypeName(schema.Type),
			ShownUri(schema.BaseUri), schema.RetryCount, Auth(schema.AuthInfo),
			schema.Methods.Items.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
				.Select(m => Method(m, soap)).ToList(),
			soap ? Namespaces(schema.Namespaces) : null,
			string.IsNullOrWhiteSpace(Localized(schema.Description)) ? null : Localized(schema.Description));
	}

	/// <summary>
	/// One notice per response parameter of an XML response whose path is a JSONPath (<c>$.AddResult</c>). The
	/// platform reads an XML response by element path (local names joined by <c>/</c>) and a JSONPath there reads
	/// nothing (measured: null in the parsed values, 0 in a process), with no error anywhere.
	/// </summary>
	/// <param name="methods">The methods to check.</param>
	/// <param name="soap">Whether the service is SOAP; it decides where the path can be changed.</param>
	/// <returns>The notices; empty when every path fits its response.</returns>
	public static IReadOnlyList<string> XmlResponsePathNotices(IEnumerable<ServiceMethodDto> methods, bool soap) =>
		(methods ?? [])
			.Where(m => m?.Response?.ContentType == ServiceSchemaCodes.ContentTypeXml)
			.SelectMany(m => JsonPathParameters(m.Response.Parameters).Select(p =>
				$"method '{m.Name}': response parameter '{p.Name}' has the JSONPath '{p.Path}', but the response is XML, "
				+ "which the platform reads by element path (local names joined by '/', e.g. 'AddResult' or "
				+ "'AddResponse/AddResult'); this path reads nothing. "
				+ (soap
					? "Give it an element path with modify-web-service setMethod."
					: "A REST xml method reads no body parameters at all: read the raw Response in the process, or "
						+ "make the method json with modify-web-service.")))
			.ToList();

	// Body parameters only: a header or cookie path is a name, never a JSONPath.
	private static IEnumerable<ServiceParameterDto> JsonPathParameters(ServiceItemCollection<ServiceParameterDto> parameters) =>
		(parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>())
			.Where(p => p.Type == ServiceSchemaCodes.Placements["body"])
			.SelectMany(p => JsonPathParameters(p.ItemProperties).Prepend(p))
			.Where(p => p.Path?.TrimStart().StartsWith('$') == true);

	/// <summary>Whether the service is SOAP 1.1 or 1.2.</summary>
	public static bool IsSoap(ServiceSchemaDto schema) =>
		schema?.Type is ServiceSchemaCodes.ServiceTypeSoap11 or ServiceSchemaCodes.ServiceTypeSoap12;

	/// <summary>The service type as list-web-services and Studio name it.</summary>
	public static string TypeName(int type) => type switch {
		ServiceSchemaCodes.ServiceTypeRest => "REST",
		ServiceSchemaCodes.ServiceTypeSoap11 => "SOAP 1.1",
		ServiceSchemaCodes.ServiceTypeSoap12 => "SOAP 1.2",
		_ => $"unknown ({type})"
	};

	private static IReadOnlyList<WebServiceNamespaceView> Namespaces(ServiceItemCollection<ServiceNamespaceDto> namespaces) =>
		(namespaces?.Items.Values ?? Enumerable.Empty<ServiceNamespaceDto>())
			.Where(n => n is not null)
			.OrderByDescending(n => n.IsDefault == true)
			.ThenBy(n => n.Prefix, StringComparer.Ordinal)
			.Select(n => new WebServiceNamespaceView(n.Prefix, ShownUriValue(n.Name), n.IsDefault == true))
			.ToList();

	/// <summary>
	/// Removes <c>user:password@</c> from a URI; Studio accepts one in the base URI. Works on the raw text and
	/// fails closed: everything between the scheme and the LAST <c>@</c> of the text is hidden, because a
	/// password may itself contain <c>@</c>, <c>/</c>, <c>?</c> or <c>#</c>, <see cref="Uri"/> rejects some of
	/// those, and it reports an escaped password in a form that no longer occurs in the stored text. A URI with
	/// an <c>@</c> only in its path or query loses that part of the display too; that is the accepted cost.
	/// </summary>
	/// <param name="uri">The URI as stored.</param>
	/// <returns>The URI without its user information.</returns>
	public static string StripUserInfo(string uri) {
		if (string.IsNullOrEmpty(uri)) {
			return uri;
		}
		int scheme = uri.IndexOf("://", StringComparison.Ordinal);
		int start = scheme < 0 ? 0 : scheme + 3;
		int at = uri.LastIndexOf('@');
		return at < start ? uri : uri[..start] + "[redacted]" + uri[at..];
	}

	public static string Localized(Dictionary<string, string> captions) {
		if (captions is null || captions.Count == 0) {
			return null;
		}
		return captions.TryGetValue("en-US", out string english) && !string.IsNullOrEmpty(english)
			? english
			: captions.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
	}

	private static WebServiceAuthView Auth(ServiceAuthInfoDto auth) {
		if (auth is null || auth.AuthType == ServiceSchemaCodes.AuthNone) {
			return null;
		}
		string type = ServiceSchemaCodes.AuthTypes.FirstOrDefault(p => p.Value == auth.AuthType).Key ?? "unknown";
		auth.Values.Items.TryGetValue("username", out ServiceParameterValueDto username);
		auth.Values.Items.TryGetValue("password", out ServiceParameterValueDto password);
		auth.Values.Items.TryGetValue("applicationId", out ServiceParameterValueDto application);
		bool constantMasked = IsConstant(username) || IsConstant(password);
		return new WebServiceAuthView(type, SysSettingCode(username), SysSettingCode(password),
			application?.Value, constantMasked);
	}

	private static bool IsConstant(ServiceParameterValueDto value) =>
		value is { Source: ServiceSchemaCodes.ValueSourceConst } && !string.IsNullOrEmpty(value.Value);

	private static string SysSettingCode(ServiceParameterValueDto value) =>
		value is { Source: ServiceSchemaCodes.ValueSourceSysSetting } ? value.Value : null;

	// A SOAP method's request uri is its message element, not an address: the URL is the base URI alone
	// (Terrasoft.Services ServiceClientRequest), so it is reported under its own name and uri is left out.
	private static WebServiceMethodView Method(ServiceMethodDto method, bool soap) =>
		new(method.Name, Localized(method.Caption),
			ServiceSchemaCodes.HttpMethods.FirstOrDefault(p => p.Value == method.Request.HttpMethodType).Key,
			soap ? null : MaskUriQuery(method.Request.Uri), method.RequestTimeout,
			method.Request.ContentType == ServiceSchemaCodes.ContentTypeXml ? "xml" : "json",
			method.UseAuthInfo,
			Parameters(method.Request.Parameters, soap,
				secretPaths: soap ? null : SecretPlaceholderPaths(method.Request.Uri, method.Request.Parameters)),
			Parameters(method.Response.Parameters, soap),
			soap ? MaskUriQuery(method.Request.Uri ?? string.Empty) : null,
			soap ? ShownUriValue(method.SoapAction ?? string.Empty) : null);

	// Stored order, not sorted: the platform renders a SOAP body and Header in collection order
	// (Terrasoft.Services ServiceCommunication.BodyParameters), and setMethod stores parameters in the order it gets
	// them, so describe output sent back must keep that order or the envelope elements are reordered.
	// secretPaths: the path parameters whose constant fills a key of the method URI (SecretPlaceholderPaths); their
	// constant is hidden like the key itself.
	private static IReadOnlyList<WebServiceParameterView> Parameters(
		ServiceItemCollection<ServiceParameterDto> parameters, bool soap, bool inSoapHeader = false,
		IReadOnlyDictionary<string, string> secretPaths = null, bool underSecretParent = false) =>
		(parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>())
			.Select(p => Parameter(p, soap, inSoapHeader, secretPaths, underSecretParent))
			.ToList();

	// The platform renders the items of a SOAP-header parameter under the Header whatever their own flag says, so a
	// constant there is judged like a header constant (IsSecretLike) even when an item lacks the flag. The Header is read
	// from the stored flag as the masker and modify-web-service read it, so the three agree.
	private static WebServiceParameterView Parameter(ServiceParameterDto parameter, bool soap, bool inSoapHeader,
		IReadOnlyDictionary<string, string> secretPaths = null, bool underSecretParent = false) {
		ServiceParameterValueDto value = parameter.DefValue;
		bool isConstant = IsConstant(value);
		bool header = inSoapHeader || parameter.IsSoapHeader == true;
		bool fillsKey = parameter.Type == ServiceSchemaCodes.Placements["path"]
			&& secretPaths?.ContainsKey(parameter.Path ?? string.Empty) == true;
		bool masked = isConstant && (fillsKey || IsSecretLike(parameter, header, underSecretParent));
		// The platform stores isSoapHeader false and sequenceElementName "item" on every SOAP parameter, so only the
		// values that change the envelope are reported: a SOAP-header flag, and the item name of a SOAP array.
		bool? soapHeader = soap && parameter.IsSoapHeader == true ? true : null;
		// An empty value is reported too: on a response array it means every child element, and setMethod keeps it.
		string sequenceElement = soap && parameter.IsArray ? parameter.SequenceElementName : null;
		return new WebServiceParameterView(parameter.Name, Localized(parameter.Caption),
			ServiceSchemaCodes.Placements.FirstOrDefault(p => p.Value == parameter.Type).Key ?? "body",
			parameter.Path, parameter.DataValueTypeName, parameter.IsArray, parameter.IsRequired,
			SysSettingCode(value), isConstant && !masked ? value.Value : null, masked,
			Parameters(parameter.ItemProperties, soap, header, null,
				IsSecretSoapParent(header, underSecretParent, parameter.Name, parameter.Path)),
			soapHeader, sequenceElement);
	}
}

/// <summary>The query keys of a URI the write check refuses (see <see cref="WebServiceSchemaView.FindQueryKeys"/>).</summary>
/// <param name="SecretKeys">Keys whose value is a secret.</param>
/// <param name="MaskKeys">Keys whose value holds the mask describe-web-service shows in place of one.</param>
public sealed record UriQueryKeys(IReadOnlyList<string> SecretKeys, IReadOnlyList<string> MaskKeys);
