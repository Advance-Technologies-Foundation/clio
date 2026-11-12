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
	[property: Description("Method address relative to the base URI; a path parameter as {path}, its wire name, e.g. orders/{id}")]
	string Uri,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional caption; defaults to the code")]
	string Caption = null,

	[property: JsonPropertyName("timeout-ms")]
	[property: Description("Optional response timeout in milliseconds; defaults to 5000 as Studio does")]
	int? TimeoutMs = null,

	[property: JsonPropertyName("content-type")]
	[property: Description("json (default) or xml. An xml method has no body parameters in request or response (Studio refuses them); a process sends the XML body through the element's RequestBody and reads Response")]
	string ContentType = null,

	[property: JsonPropertyName("use-auth")]
	[property: Description("Whether the method sends the service authentication; defaults to true when the service has authentication")]
	bool? UseAuth = null,

	[property: JsonPropertyName("request-parameters")]
	[property: Description("Request parameters; each has in = path, query, header, body or cookie")]
	IReadOnlyList<WebServiceParameterArg> RequestParameters = null,

	[property: JsonPropertyName("response-parameters")]
	[property: Description("Response parameters read from the JSON body; path is a JSONPath such as $.items")]
	IReadOnlyList<WebServiceParameterArg> ResponseParameters = null
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
	[property: Description("Wire name (query, header, cookie or body key; the {placeholder} of a path parameter) or the JSONPath of a response value, e.g. $.status. Required for a new parameter when the environment has a schema-name prefix; otherwise it defaults to the code, and a kept parameter keeps its own.")]
	string Path = null,

	[property: JsonPropertyName("caption")]
	string Caption = null,

	[property: JsonPropertyName("array")]
	bool? Array = null,

	[property: JsonPropertyName("required")]
	bool? Required = null,

	[property: JsonPropertyName("default-value")]
	[property: Description("Optional constant default. Refused on header and cookie parameters and on any key, token, secret or password parameter: use default-sys-setting there.")]
	string DefaultValue = null,

	[property: JsonPropertyName("default-sys-setting")]
	[property: Description("Optional code of an existing system setting the platform reads the value from at call time")]
	string DefaultSysSetting = null,

	[property: JsonPropertyName("items")]
	[property: Description("Fields of each element of an Object array")]
	IReadOnlyList<WebServiceParameterArg> Items = null,

	[property: JsonPropertyName("default-masked")]
	[property: Description("Read-only, from describe-web-service; accepted and ignored. A kept parameter whose secret constant default describe hid keeps that default when no default is sent.")]
	bool? DefaultMasked = null
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
	[property: Description("addMethod, setMethod, removeMethod, setBaseUri, setRetryCount or setAuth")]
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
	WebServiceAuthArg Auth = null
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
	IReadOnlyList<WebServiceNamespaceView> Namespaces = null);

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
	IReadOnlyList<string> Notices = null);

#endregion

/// <summary>
/// The only way a web service schema leaves clio. The platform read returns authentication values (a Basic
/// password included) and constant defaults that services use for API keys and bearer headers, so every
/// tool result is built here and nothing else serializes a <see cref="ServiceSchemaDto"/> outward.
/// </summary>
public static partial class WebServiceSchemaView {

	[GeneratedRegex("key|token|secret|passw|pwd|auth|credential|signature|session|cookie",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex SecretNamePattern();

	public static bool IsSecretLike(string parameterName, int placement) =>
		placement == ServiceSchemaCodes.Placements["header"]
		|| placement == ServiceSchemaCodes.Placements["cookie"]
		|| IsSecretName(parameterName);

	/// <summary>
	/// Whether a parameter's constant default must be hidden: a header or cookie, a SOAP-header element (WS-Security
	/// and licence headers travel there), or a code or wire name that looks like a credential.
	/// </summary>
	public static bool IsSecretLike(ServiceParameterDto parameter) {
		int placement = parameter.IsSoapHeader == true ? ServiceSchemaCodes.Placements["header"] : parameter.Type;
		return IsSecretLike(parameter.Name, placement) || IsSecretLike(parameter.Path, placement);
	}

	/// <summary>Whether a parameter code or wire name looks like a credential (key, token, secret, password, ...).</summary>
	public static bool IsSecretName(string name) => SecretNamePattern().IsMatch(name ?? string.Empty);

	public static WebServiceView From(ServiceSchemaDto schema, string packageName) {
		bool soap = IsSoap(schema);
		return new WebServiceView(schema.UId, schema.Name, Localized(schema.Caption), packageName, TypeName(schema.Type),
			StripUserInfo(schema.BaseUri), schema.RetryCount, Auth(schema.AuthInfo),
			schema.Methods.Items.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
				.Select(m => Method(m, soap)).ToList(),
			soap ? Namespaces(schema.Namespaces) : null);
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
					? "Change the path in Studio > Web services."
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
			.OrderByDescending(n => n.IsDefault)
			.ThenBy(n => n.Prefix, StringComparer.Ordinal)
			.Select(n => new WebServiceNamespaceView(n.Prefix, StripUserInfo(n.Name), n.IsDefault))
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
			soap ? null : method.Request.Uri, method.RequestTimeout,
			method.Request.ContentType == ServiceSchemaCodes.ContentTypeXml ? "xml" : "json",
			method.UseAuthInfo,
			Parameters(method.Request.Parameters, soap),
			Parameters(method.Response.Parameters, soap),
			soap ? method.Request.Uri ?? string.Empty : null,
			soap ? StripUserInfo(method.SoapAction ?? string.Empty) : null);

	private static IReadOnlyList<WebServiceParameterView> Parameters(
		ServiceItemCollection<ServiceParameterDto> parameters, bool soap) =>
		(parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>())
			.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.Select(p => Parameter(p, soap))
			.ToList();

	private static WebServiceParameterView Parameter(ServiceParameterDto parameter, bool soap) {
		ServiceParameterValueDto value = parameter.DefValue;
		bool isConstant = IsConstant(value);
		bool masked = isConstant && IsSecretLike(parameter);
		// The platform stores isSoapHeader false and sequenceElementName "item" on every SOAP parameter, so only the
		// values that change the envelope are reported: a SOAP-header flag, and the item name of a SOAP array.
		bool? soapHeader = soap && parameter.IsSoapHeader == true ? true : null;
		string sequenceElement = soap && parameter.IsArray && !string.IsNullOrEmpty(parameter.SequenceElementName)
			? parameter.SequenceElementName
			: null;
		return new WebServiceParameterView(parameter.Name, Localized(parameter.Caption),
			ServiceSchemaCodes.Placements.FirstOrDefault(p => p.Value == parameter.Type).Key ?? "body",
			parameter.Path, parameter.DataValueTypeName, parameter.IsArray, parameter.IsRequired,
			SysSettingCode(value), isConstant && !masked ? value.Value : null, masked,
			Parameters(parameter.ItemProperties, soap), soapHeader, sequenceElement);
	}
}
