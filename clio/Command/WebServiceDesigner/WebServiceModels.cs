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
	[property: Description("Method code, unique within the service, e.g. GetOrders. This is what a Call web service element stores.")]
	string Code,

	[property: JsonPropertyName("http-method")]
	[property: Description("GET, POST, PUT, PATCH or DELETE")]
	string HttpMethod,

	[property: JsonPropertyName("uri")]
	[property: Description("Method address relative to the base URI; path parameters as {code}, e.g. orders/{id}")]
	string Uri,

	[property: JsonPropertyName("caption")]
	[property: Description("Optional caption; defaults to the code")]
	string Caption = null,

	[property: JsonPropertyName("timeout-ms")]
	[property: Description("Optional response timeout in milliseconds; defaults to 5000 as Studio does")]
	int? TimeoutMs = null,

	[property: JsonPropertyName("content-type")]
	[property: Description("json (default) or xml")]
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

/// <summary>A request or response parameter; <see cref="Items"/> describes the fields of an Object.</summary>
public sealed record WebServiceParameterArg(
	[property: JsonPropertyName("code")]
	[property: Description("Parameter code, unique among its siblings")]
	string Code,

	[property: JsonPropertyName("in")]
	[property: Description("Request parameters: path, query, header, body or cookie (default body). Response parameters: body.")]
	string In = null,

	[property: JsonPropertyName("type")]
	[property: Description("Text (default), Integer, Float, Boolean, DateTime, Date, Time, Guid or Object")]
	string Type = null,

	[property: JsonPropertyName("path")]
	[property: Description("Optional wire name or JSONPath; defaults to the code for request parameters and $.<code> for response parameters")]
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
	[property: Description("Nested parameters of an Object (or of each element of an Object array)")]
	IReadOnlyList<WebServiceParameterArg> Items = null
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
	[property: Description("oauth20: name of an existing OAuth application (OAuthApplications)")]
	string OAuthApp = null
) {
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
	[property: JsonPropertyName("items")] IReadOnlyList<WebServiceParameterView> Items);

public sealed record WebServiceMethodView(
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("http-method")] string HttpMethod,
	[property: JsonPropertyName("uri")] string Uri,
	[property: JsonPropertyName("timeout-ms")] int TimeoutMs,
	[property: JsonPropertyName("content-type")] string ContentType,
	[property: JsonPropertyName("use-auth")] bool UseAuth,
	[property: JsonPropertyName("request-parameters")] IReadOnlyList<WebServiceParameterView> RequestParameters,
	[property: JsonPropertyName("response-parameters")] IReadOnlyList<WebServiceParameterView> ResponseParameters);

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
	[property: JsonPropertyName("methods")] IReadOnlyList<WebServiceMethodView> Methods);

public sealed record WebServiceListItem(
	[property: JsonPropertyName("uid")] Guid UId,
	[property: JsonPropertyName("code")] string Code,
	[property: JsonPropertyName("caption")] string Caption,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("package")] string Package);

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
	[property: JsonPropertyName("response-body-truncated")] bool ResponseBodyTruncated = false);

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
		|| SecretNamePattern().IsMatch(parameterName ?? string.Empty);

	public static WebServiceView From(ServiceSchemaDto schema, string packageName) =>
		new(schema.UId, schema.Name, Localized(schema.Caption), packageName,
			schema.Type == ServiceSchemaCodes.ServiceTypeRest ? "REST" : "SOAP",
			StripUserInfo(schema.BaseUri), schema.RetryCount, Auth(schema.AuthInfo),
			schema.Methods.Items.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
				.Select(Method).ToList());

	/// <summary>Removes <c>user:password@</c> from an absolute URI; Studio accepts one in the base URI.</summary>
	public static string StripUserInfo(string uri) {
		if (string.IsNullOrEmpty(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out Uri parsed)
			|| string.IsNullOrEmpty(parsed.UserInfo)) {
			return uri;
		}
		return uri.Replace(parsed.UserInfo + "@", "[redacted]@", StringComparison.Ordinal);
	}

	public static string Localized(Dictionary<string, string> captions) =>
		captions is null || captions.Count == 0
			? null
			: captions.TryGetValue("en-US", out string english) && !string.IsNullOrEmpty(english)
				? english
				: captions.Values.FirstOrDefault(v => !string.IsNullOrEmpty(v));

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

	private static WebServiceMethodView Method(ServiceMethodDto method) =>
		new(method.Name, Localized(method.Caption),
			ServiceSchemaCodes.HttpMethods.FirstOrDefault(p => p.Value == method.Request.HttpMethodType).Key,
			method.Request.Uri, method.RequestTimeout,
			method.Request.ContentType == ServiceSchemaCodes.ContentTypeXml ? "xml" : "json",
			method.UseAuthInfo,
			Parameters(method.Request.Parameters, isRequest: true),
			Parameters(method.Response.Parameters, isRequest: false));

	private static IReadOnlyList<WebServiceParameterView> Parameters(
		ServiceItemCollection<ServiceParameterDto> parameters, bool isRequest) =>
		(parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>())
			.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.Select(p => Parameter(p, isRequest))
			.ToList();

	private static WebServiceParameterView Parameter(ServiceParameterDto parameter, bool isRequest) {
		ServiceParameterValueDto value = parameter.DefValue;
		bool isConstant = IsConstant(value);
		bool masked = isConstant && IsSecretLike(parameter.Name, parameter.Type);
		return new WebServiceParameterView(parameter.Name, Localized(parameter.Caption),
			isRequest ? ServiceSchemaCodes.Placements.FirstOrDefault(p => p.Value == parameter.Type).Key : "body",
			parameter.Path, parameter.DataValueTypeName, parameter.IsArray, parameter.IsRequired,
			SysSettingCode(value), isConstant && !masked ? value.Value : null, masked,
			Parameters(parameter.ItemProperties, isRequest));
	}
}
