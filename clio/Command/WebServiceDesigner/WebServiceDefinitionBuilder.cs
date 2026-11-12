using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// Turns the agent's description of a REST service into the platform contract. The platform's save validates
/// nothing (Studio checks names, URIs and duplicates in its client code only), so every rule Studio enforces
/// is enforced here, and every rule about credentials is stricter than Studio's.
/// </summary>
public static partial class WebServiceDefinitionBuilder {

	internal const int DefaultTimeoutMs = 5000;

	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
	private static partial Regex IdentifierPattern();

	[GeneratedRegex(@"\{([^{}]+)\}")]
	private static partial Regex PathPlaceholderPattern();

	/// <summary>Collects every problem of one request so the agent can fix them in one retry.</summary>
	public sealed class Errors : List<string> {
		public void ThrowIfAny() {
			if (Count > 0) {
				throw new ArgumentException(string.Join(" ", this));
			}
		}
	}

	public static void RejectUnknownFields(IReadOnlyDictionary<string, JsonElement> extensionData, string owner,
		Errors errors) {
		if (extensionData is { Count: > 0 }) {
			errors.Add($"{owner}: unknown field(s) {string.Join(", ", extensionData.Keys.Take(10))}.");
		}
	}

	public static void ValidateServiceCode(string code, string prefix, Errors errors) {
		if (string.IsNullOrWhiteSpace(code) || !IdentifierPattern().IsMatch(code)) {
			errors.Add($"code '{code}' must be a Latin identifier (letters, digits, underscore).");
			return;
		}
		if (!string.IsNullOrEmpty(prefix) && !code.StartsWith(prefix, StringComparison.Ordinal)) {
			errors.Add($"code '{code}' must start with the environment's schema-name prefix '{prefix}'.");
		}
	}

	/// <summary>
	/// The designer joins base URI and method URI verbatim, so a base URI without a trailing slash and a
	/// method URI without a leading one produce <c>...ProbeEchoorders</c> (measured on 10.2.565).
	/// </summary>
	public static string NormalizeBaseUri(string baseUri, Errors errors) {
		if (string.IsNullOrWhiteSpace(baseUri)
			|| !Uri.TryCreate(baseUri.Trim(), UriKind.Absolute, out Uri parsed)
			|| (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)) {
			errors.Add($"base-uri '{WebServiceSchemaView.StripUserInfo(baseUri)}' must be an absolute http or https URI.");
			return baseUri;
		}
		if (!string.IsNullOrEmpty(parsed.UserInfo)) {
			errors.Add("base-uri must not carry credentials (user:password@); use auth with system settings.");
			return baseUri;
		}
		if (baseUri.IndexOfAny(['?', '#']) >= 0) {
			// The designer appends the method URI to the base verbatim, so a query here ends up in front of every
			// method path, and a key in it would be a stored constant that list and describe print.
			errors.Add("base-uri must not have a query or a fragment; give query values as query parameters "
				+ "(a key or token with default-sys-setting).");
			return baseUri;
		}
		string trimmed = baseUri.Trim();
		return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
	}

	public static ServiceAuthInfoDto BuildAuth(WebServiceAuthArg auth, Guid? oauthAppId, Errors errors) {
		if (auth is null) {
			return null;
		}
		// "the auth block", not "auth": SensitiveErrorTextRedactor reads "auth: <text>" as a credential pair and
		// replaces the reason with [redacted] before the agent sees it.
		RejectUnknownFields(auth.ExtensionData, "the auth block", errors);

		if (!ServiceSchemaCodes.AuthTypes.TryGetValue(auth.Type ?? string.Empty, out int authType)) {
			errors.Add($"auth.type '{auth.Type}' must be none, basic, digest or oauth20.");
			return null;
		}
		var result = new ServiceAuthInfoDto { AuthType = authType };
		switch (authType) {
			case ServiceSchemaCodes.AuthNone:
				return null;
			case ServiceSchemaCodes.AuthBasic or ServiceSchemaCodes.AuthDigest:
				if (string.IsNullOrWhiteSpace(auth.UsernameSysSetting) || string.IsNullOrWhiteSpace(auth.PasswordSysSetting)) {
					errors.Add("auth basic/digest needs username-sys-setting and password-sys-setting (system setting codes; the user fills the values).");
					return null;
				}
				result.Values.Items["username"] = SysSettingValue(auth.UsernameSysSetting);
				result.Values.Items["password"] = SysSettingValue(auth.PasswordSysSetting);
				return result;
			default:
				if (oauthAppId is null) {
					errors.Add($"auth oauth20 needs oauth-app with the name or id of an existing OAuth application ('{auth.OAuthAppReference}' was not found).");
					return null;
				}
				result.Values.Items["applicationId"] = new ServiceParameterValueDto {
					Value = oauthAppId.Value.ToString(), Source = ServiceSchemaCodes.ValueSourceConst
				};
				return result;
		}
	}

	/// <summary>
	/// Builds a method; when <paramref name="existing"/> is given its UId and the UIds of every parameter that
	/// keeps its code are reused, so the Process Designer keeps resolving the elements that call it.
	/// </summary>
	/// <param name="prefix">
	/// The environment's schema-name prefix. Studio refuses to save a method or a parameter whose code lacks it
	/// (its method and parameter pages validate both codes against <c>SchemaNamePrefix</c> unconditionally), so a
	/// code this call INTRODUCES must carry it. A code the service already has is kept as it is: renaming it is
	/// what breaks the processes that call the method. Null or empty: no prefix rule, as in Studio.
	/// </param>
	public static ServiceMethodDto BuildMethod(WebServiceMethodArg arg, ServiceMethodDto existing,
		bool serviceHasAuth, Errors errors, string prefix = null, string baseUri = null) {
		string code = arg?.Code;
		if (string.IsNullOrWhiteSpace(code) || !IdentifierPattern().IsMatch(code)) {
			errors.Add($"method code '{code}' must be a Latin identifier.");
			return null;
		}
		if (!string.Equals(existing?.Name, code, StringComparison.Ordinal)) {
			RequirePrefix($"method code '{code}'", code, prefix, errors);
		}
		RejectUnknownFields(arg.ExtensionData, $"method '{code}'", errors);
		if (!ServiceSchemaCodes.HttpMethods.TryGetValue(arg.HttpMethod ?? string.Empty, out int verb)) {
			errors.Add($"method '{code}': http-method '{arg.HttpMethod}' must be GET, POST, PUT, PATCH or DELETE.");
		}
		string uri = (arg.Uri ?? string.Empty).TrimStart('/');
		int contentType = string.Equals(arg.ContentType, "xml", StringComparison.OrdinalIgnoreCase)
			? ServiceSchemaCodes.ContentTypeXml
			: ServiceSchemaCodes.ContentTypeJson;
		if (arg.ContentType is not null && !string.Equals(arg.ContentType, "json", StringComparison.OrdinalIgnoreCase)
			&& contentType != ServiceSchemaCodes.ContentTypeXml) {
			errors.Add($"method '{code}': content-type '{arg.ContentType}' must be json or xml.");
		}
		var method = new ServiceMethodDto {
			UId = existing?.UId ?? Guid.NewGuid(),
			Name = code,
			RequestTimeout = arg.TimeoutMs is > 0 ? arg.TimeoutMs.Value : existing?.RequestTimeout ?? DefaultTimeoutMs,
			Caption = Caption(arg.Caption ?? code),
			UseAuthInfo = arg.UseAuth ?? serviceHasAuth,
			Extra = existing?.Extra,
			Request = new ServiceMethodRequestDto {
				Uri = uri, ContentType = contentType, HttpMethodType = verb,
				Extra = existing?.Request?.Extra,
				Parameters = BuildParameters(arg.RequestParameters, existing?.Request?.Parameters, true,
					$"method '{code}' request", errors, prefix)
			},
			Response = new ServiceMethodResponseDto {
				ContentType = contentType,
				Extra = existing?.Response?.Extra,
				Parameters = BuildParameters(arg.ResponseParameters, existing?.Response?.Parameters, false,
					$"method '{code}' response", errors, prefix)
			}
		};
		ValidatePathPlaceholders(code, uri, baseUri, method.Request.Parameters, errors);
		ValidateUriQuery(code, uri, errors);
		ValidateBodyRules(code, verb, contentType, method, errors);
		return method;
	}

	/// <summary>
	/// The two body rules Studio's method page enforces (<c>WebServiceMethodPage._requestTypeValidator</c>,
	/// <c>RestWebServiceMethodPage._contentTypeValidator</c>): a GET has no body parameter, and an XML method has no
	/// body parameter in its request or its response. The platform builds a REST body as JSON only, so an XML method
	/// with one fails at call time ("Rest:Xml"), and .NET refuses a GET with a body; Studio cannot save either.
	/// </summary>
	private static void ValidateBodyRules(string code, int verb, int contentType, ServiceMethodDto method, Errors errors) {
		int body = ServiceSchemaCodes.Placements["body"];
		bool requestBody = method.Request.Parameters.Items.Values.Any(p => p.Type == body);
		bool responseBody = method.Response.Parameters.Items.Values.Any(p => p.Type == body);
		if (verb == ServiceSchemaCodes.HttpMethods["GET"] && requestBody) {
			errors.Add($"method '{code}': a GET method cannot have body request parameters; use query or path parameters, "
				+ "or another http-method.");
		}
		if (contentType == ServiceSchemaCodes.ContentTypeXml && (requestBody || responseBody)) {
			errors.Add($"method '{code}': an xml method cannot have body parameters in its request or response (Studio "
				+ "refuses them and the platform builds REST bodies as JSON only); keep path, query, header and cookie "
				+ "parameters, and let a process send the XML through the Call web service element's RequestBody and read "
				+ "its Response.");
		}
	}

	public static IReadOnlyCollection<string> CollectSysSettingCodes(ServiceSchemaDto schema) {
		var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ServiceParameterValueDto value in schema.AuthInfo?.Values.Items.Values ?? Enumerable.Empty<ServiceParameterValueDto>()) {
			AddSysSetting(codes, value);
		}
		foreach (ServiceMethodDto method in schema.Methods.Items.Values) {
			CollectFromParameters(codes, method.Request?.Parameters);
		}
		return codes;
	}

	public static Dictionary<string, string> Caption(string text) => new() { ["en-US"] = text };

	private static ServiceItemCollection<ServiceParameterDto> BuildParameters(
		IReadOnlyList<WebServiceParameterArg> args, ServiceItemCollection<ServiceParameterDto> existing,
		bool isRequest, string owner, Errors errors, string prefix, bool nested = false) {
		var result = new ServiceItemCollection<ServiceParameterDto>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (WebServiceParameterArg arg in args ?? []) {
			if (string.IsNullOrWhiteSpace(arg?.Code)) {
				errors.Add($"{owner}: every parameter needs a code.");
				continue;
			}
			if (!seen.Add(arg.Code)) {
				errors.Add($"{owner}: parameter code '{arg.Code}' is used twice.");
				continue;
			}
			ServiceParameterDto previous = existing?.Items.Values
				.FirstOrDefault(p => string.Equals(p.Name, arg.Code, StringComparison.OrdinalIgnoreCase));
			if (!string.Equals(previous?.Name, arg.Code, StringComparison.Ordinal)) {
				RequireNewParameterNames($"{owner} parameter", arg, prefix, isRequest, errors);
			}
			if (nested && arg.In is not null && !string.Equals(arg.In, "body", StringComparison.OrdinalIgnoreCase)) {
				// A field of an Object array element is read from that element; the platform fills header and cookie
				// values for top-level parameters only.
				errors.Add($"{owner} parameter '{arg.Code}': a field inside an Object array has no placement of its own; "
					+ "omit in.");
				continue;
			}
			ServiceParameterDto parameter = BuildParameter(arg, previous, isRequest, $"{owner} parameter '{arg.Code}'",
				errors, prefix);
			if (parameter is not null) {
				result.Items[parameter.UId.ToString()] = parameter;
			}
		}
		return result;
	}

	private static ServiceParameterDto BuildParameter(WebServiceParameterArg arg, ServiceParameterDto previous,
		bool isRequest, string owner, Errors errors, string prefix) {
		RejectUnknownFields(arg.ExtensionData, owner, errors);
		int placement = ServiceSchemaCodes.Placements["body"];
		if (isRequest && arg.In is not null && !ServiceSchemaCodes.Placements.TryGetValue(arg.In, out placement)) {
			errors.Add($"{owner}: in '{arg.In}' must be path, query, header, body or cookie.");
			return null;
		}
		// A response parameter is read from the body, a response header or a response cookie (Studio offers the
		// three; ServiceClientResponse reads header and cookie values by the parameter's path, case-sensitively).
		if (!isRequest && arg.In is not null && (!ServiceSchemaCodes.ResponsePlacements.Contains(arg.In)
				|| !ServiceSchemaCodes.Placements.TryGetValue(arg.In, out placement))) {
			errors.Add($"{owner}: in '{arg.In}' must be body, header or cookie for a response parameter.");
			return null;
		}
		if (placement != ServiceSchemaCodes.Placements["body"] && placement != ServiceSchemaCodes.Placements["path"]
				&& placement != ServiceSchemaCodes.Placements["query"] && (arg.Type is not null
				&& string.Equals(arg.Type, "Object", StringComparison.OrdinalIgnoreCase) || arg.Items is { Count: > 0 })) {
			errors.Add($"{owner}: a header or cookie parameter carries one value; it cannot be an Object or have items.");
		}
		string type = ServiceSchemaCodes.DataTypes.FirstOrDefault(
			t => string.Equals(t, arg.Type ?? "Text", StringComparison.OrdinalIgnoreCase));
		if (type is null) {
			errors.Add($"{owner}: type '{arg.Type}' must be one of {string.Join(", ", ServiceSchemaCodes.DataTypes)}.");
			return null;
		}
		bool hasItems = arg.Items is { Count: > 0 };
		if (hasItems && type != "Object") {
			errors.Add($"{owner}: items are allowed only on an Object parameter.");
		}
		if (type == "Object" && !hasItems) {
			errors.Add($"{owner}: an Object parameter needs items.");
		}
		// Studio refuses a single Object ("You can select Object type only if Is Array attribute set"), its JSON
		// quick setup never makes one, and a Call web service element maps one to a plain Object parameter with no
		// fields to bind: the product describes a single nested object by its fields, each with a dotted path.
		// A parameter the service already has is kept, as with the prefix rule.
		if (type == "Object" && arg.Array != true && !string.Equals(previous?.Name, arg.Code, StringComparison.Ordinal)) {
			errors.Add($"{owner}: an Object parameter must be an array. Describe a single nested object by its fields as "
				+ "separate parameters with a dotted path instead, e.g. path 'customer.name' for a request body field or "
				+ "'$.customer.name' for a response value.");
		}
		ServiceParameterValueDto defValue = BuildDefault(arg, previous, placement, owner, errors);
		return new ServiceParameterDto {
			UId = previous?.UId ?? Guid.NewGuid(),
			Name = arg.Code,
			Type = placement,
			Path = string.IsNullOrWhiteSpace(arg.Path) ? previous?.Path ?? arg.Code : arg.Path,
			DataValueTypeName = type,
			IsArray = arg.Array ?? false,
			IsRequired = arg.Required ?? placement == ServiceSchemaCodes.Placements["path"],
			DefValue = defValue,
			Caption = Caption(arg.Caption ?? arg.Code),
			Extra = previous?.Extra,
			ItemProperties = BuildParameters(arg.Items, previous?.ItemProperties, isRequest, owner, errors, prefix,
				nested: true)
		};
	}

	private static void RequirePrefix(string what, string code, string prefix, Errors errors) {
		if (!string.IsNullOrEmpty(prefix) && !code.StartsWith(prefix, StringComparison.Ordinal)) {
			errors.Add($"{what} must start with the environment's schema-name prefix '{prefix}', or Studio refuses to "
				+ "save the method; the address and the wire name come from uri and path, not from the code.");
		}
	}

	/// <summary>
	/// A parameter code that carries the prefix is no longer the name the API sees, so a new parameter on a
	/// prefixed environment names its wire name (or JSONPath) in <c>path</c> instead of inheriting the code:
	/// a query <c>UsrPage</c> or a header <c>UsrApiKey</c> would be sent under the wrong name, silently.
	/// </summary>
	private static void RequireNewParameterNames(string owner, WebServiceParameterArg arg, string prefix,
		bool isRequest, Errors errors) {
		if (string.IsNullOrEmpty(prefix)) {
			return;
		}
		bool responseBody = !isRequest && (arg.In is null || string.Equals(arg.In, "body", StringComparison.OrdinalIgnoreCase));
		string example = $"code '{prefix}{char.ToUpperInvariant(arg.Code[0])}{arg.Code[1..]}', path '{(responseBody ? "$." + arg.Code : arg.Code)}'";
		if (!arg.Code.StartsWith(prefix, StringComparison.Ordinal)) {
			errors.Add($"{owner} code '{arg.Code}' must start with the environment's schema-name prefix '{prefix}', or "
				+ $"Studio refuses to save the method; keep the wire name in path, e.g. {example}.");
		} else if (string.IsNullOrWhiteSpace(arg.Path)) {
			errors.Add($"{owner} '{arg.Code}' needs path: its code carries the schema-name prefix, so it is not the "
				+ "name the API sees; give the wire name (a JSONPath such as $.status for a response body value, the "
				+ "header or cookie name for a response header or cookie).");
		}
	}

	private static bool IsHiddenSecretConstant(ServiceParameterDto previous) =>
		previous?.DefValue is { Source: ServiceSchemaCodes.ValueSourceConst } value && !string.IsNullOrEmpty(value.Value)
		&& (WebServiceSchemaView.IsSecretLike(previous.Name, previous.Type)
			|| WebServiceSchemaView.IsSecretLike(previous.Path, previous.Type));

	private static ServiceParameterValueDto BuildDefault(WebServiceParameterArg arg, ServiceParameterDto previous,
		int placement, string owner, Errors errors) {
		bool hasConstant = !string.IsNullOrEmpty(arg.DefaultValue);
		bool hasSetting = !string.IsNullOrWhiteSpace(arg.DefaultSysSetting);
		if (hasConstant && hasSetting) {
			errors.Add($"{owner}: give default-value or default-sys-setting, not both.");
			return Empty();
		}
		string wireName = string.IsNullOrWhiteSpace(arg.Path) ? previous?.Path : arg.Path;
		if (hasConstant && (WebServiceSchemaView.IsSecretLike(arg.Code, placement)
				|| WebServiceSchemaView.IsSecretLike(wireName, placement))) {
			errors.Add($"{owner}: a constant default is refused on a header, cookie or credential-like parameter; "
				+ "reference a system setting with default-sys-setting and let the user fill its value.");
			return Empty();
		}
		if (hasSetting) {
			return SysSettingValue(arg.DefaultSysSetting);
		}
		// describe-web-service hides a secret constant default (default-masked), so a setMethod rebuilt from describe
		// cannot send it back; without this the save would erase the key or token the service sends. An explicit
		// default-value "" clears it. It is kept only where it stays hidden: moved to a visible place (a query
		// parameter, a path that is not credential-like) the next reply would print it, and it would travel in the
		// URL, so that move is refused instead.
		bool clears = arg.DefaultValue is { Length: 0 };
		if (previous is not null && !hasConstant && !clears && IsHiddenSecretConstant(previous)) {
			if (!WebServiceSchemaView.IsSecretLike(arg.Code, placement)
					&& !WebServiceSchemaView.IsSecretLike(wireName, placement)) {
				errors.Add($"{owner}: it carries a secret constant default that describe hides, and the new placement "
					+ "or path would show it; reference a system setting with default-sys-setting, or send "
					+ "default-value \"\" to clear it.");
				return Empty();
			}
			return new ServiceParameterValueDto { Value = previous.DefValue.Value, Source = previous.DefValue.Source };
		}
		return hasConstant
			? new ServiceParameterValueDto { Value = arg.DefaultValue, Source = ServiceSchemaCodes.ValueSourceConst }
			: Empty();
	}

	/// <summary>
	/// A method URI may carry a fixed query (<c>search?format=json</c>), but a credential-like key there would be a
	/// stored constant that describe prints and every reader of the schema sees.
	/// </summary>
	private static void ValidateUriQuery(string code, string uri, Errors errors) {
		int question = uri.IndexOf('?');
		if (question < 0) {
			return;
		}
		foreach (string pair in uri[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)) {
			int equals = pair.IndexOf('=');
			string key = equals < 0 ? pair : pair[..equals];
			string value = equals < 0 ? string.Empty : pair[(equals + 1)..];
			if (value.Length > 0 && !PathPlaceholderPattern().IsMatch(value)
					&& WebServiceSchemaView.IsSecretLike(key, ServiceSchemaCodes.Placements["query"])) {
				errors.Add($"method '{code}': uri query '{key}' looks like a credential and has a constant value; "
					+ "make it a query parameter with default-sys-setting instead.");
			}
		}
	}

	private static void ValidatePathPlaceholders(string code, string uri, string baseUri,
		ServiceItemCollection<ServiceParameterDto> parameters, Errors errors) {
		// Ordinal: the platform substitutes "{" + Path + "}" with a case-sensitive Replace, so {Id} is never
		// filled from a parameter whose path is "id". It fills placeholders in the base URI too.
		var placeholders = PathPlaceholderPattern().Matches(uri).Select(m => m.Groups[1].Value)
			.Concat(PathPlaceholderPattern().Matches(baseUri ?? string.Empty).Select(m => m.Groups[1].Value))
			.ToHashSet(StringComparer.Ordinal);
		var pathParameters = parameters.Items.Values
			.Where(p => p.Type == ServiceSchemaCodes.Placements["path"])
			.Select(p => p.Path)
			.ToHashSet(StringComparer.Ordinal);
		foreach (string missing in placeholders.Except(pathParameters)) {
			errors.Add($"method '{code}': uri placeholder {{{missing}}} has no path parameter.");
		}
		foreach (string unused in pathParameters.Except(placeholders)) {
			errors.Add($"method '{code}': path parameter '{unused}' has no {{{unused}}} placeholder in the uri.");
		}
	}

	private static void CollectFromParameters(SortedSet<string> codes, ServiceItemCollection<ServiceParameterDto> parameters) {
		foreach (ServiceParameterDto parameter in parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>()) {
			AddSysSetting(codes, parameter.DefValue);
			CollectFromParameters(codes, parameter.ItemProperties);
		}
	}

	private static void AddSysSetting(SortedSet<string> codes, ServiceParameterValueDto value) {
		if (value is { Source: ServiceSchemaCodes.ValueSourceSysSetting } && !string.IsNullOrWhiteSpace(value.Value)) {
			codes.Add(value.Value);
		}
	}

	private static ServiceParameterValueDto SysSettingValue(string code) =>
		new() { Value = code.Trim(), Source = ServiceSchemaCodes.ValueSourceSysSetting };

	private static ServiceParameterValueDto Empty() =>
		new() { Value = string.Empty, Source = ServiceSchemaCodes.ValueSourceNone };
}
