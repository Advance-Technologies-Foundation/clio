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
		string trimmed = baseUri.Trim();
		return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
	}

	public static ServiceAuthInfoDto BuildAuth(WebServiceAuthArg auth, Guid? oauthAppId, Errors errors) {
		if (auth is null) {
			return null;
		}
		RejectUnknownFields(auth.ExtensionData, "auth", errors);
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
					errors.Add($"auth oauth20 needs oauth-app naming an existing OAuth application ('{auth.OAuthApp}' was not found).");
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
	public static ServiceMethodDto BuildMethod(WebServiceMethodArg arg, ServiceMethodDto existing,
		bool serviceHasAuth, Errors errors) {
		string code = arg?.Code;
		if (string.IsNullOrWhiteSpace(code) || !IdentifierPattern().IsMatch(code)) {
			errors.Add($"method code '{code}' must be a Latin identifier.");
			return null;
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
					$"method '{code}' request", errors)
			},
			Response = new ServiceMethodResponseDto {
				ContentType = contentType,
				Extra = existing?.Response?.Extra,
				Parameters = BuildParameters(arg.ResponseParameters, existing?.Response?.Parameters, false,
					$"method '{code}' response", errors)
			}
		};
		ValidatePathPlaceholders(code, uri, method.Request.Parameters, errors);
		return method;
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
		bool isRequest, string owner, Errors errors) {
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
			ServiceParameterDto parameter = BuildParameter(arg, previous, isRequest, $"{owner} parameter '{arg.Code}'", errors);
			if (parameter is not null) {
				result.Items[parameter.UId.ToString()] = parameter;
			}
		}
		return result;
	}

	private static ServiceParameterDto BuildParameter(WebServiceParameterArg arg, ServiceParameterDto previous,
		bool isRequest, string owner, Errors errors) {
		RejectUnknownFields(arg.ExtensionData, owner, errors);
		int placement = ServiceSchemaCodes.Placements["body"];
		if (isRequest && arg.In is not null && !ServiceSchemaCodes.Placements.TryGetValue(arg.In, out placement)) {
			errors.Add($"{owner}: in '{arg.In}' must be path, query, header, body or cookie.");
			return null;
		}
		if (!isRequest && arg.In is not null && !string.Equals(arg.In, "body", StringComparison.OrdinalIgnoreCase)) {
			errors.Add($"{owner}: response parameters are read from the body; omit in or use body.");
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
		ServiceParameterValueDto defValue = BuildDefault(arg, placement, owner, errors);
		return new ServiceParameterDto {
			UId = previous?.UId ?? Guid.NewGuid(),
			Name = arg.Code,
			Type = placement,
			Path = string.IsNullOrWhiteSpace(arg.Path) ? arg.Code : arg.Path,
			DataValueTypeName = type,
			IsArray = arg.Array ?? false,
			IsRequired = arg.Required ?? placement == ServiceSchemaCodes.Placements["path"],
			DefValue = defValue,
			Caption = Caption(arg.Caption ?? arg.Code),
			Extra = previous?.Extra,
			ItemProperties = BuildParameters(arg.Items, previous?.ItemProperties, isRequest, owner, errors)
		};
	}

	private static ServiceParameterValueDto BuildDefault(WebServiceParameterArg arg, int placement, string owner,
		Errors errors) {
		bool hasConstant = !string.IsNullOrEmpty(arg.DefaultValue);
		bool hasSetting = !string.IsNullOrWhiteSpace(arg.DefaultSysSetting);
		if (hasConstant && hasSetting) {
			errors.Add($"{owner}: give default-value or default-sys-setting, not both.");
			return Empty();
		}
		if (hasConstant && WebServiceSchemaView.IsSecretLike(arg.Code, placement)) {
			errors.Add($"{owner}: a constant default is refused on a header, cookie or credential-like parameter; "
				+ "reference a system setting with default-sys-setting and let the user fill its value.");
			return Empty();
		}
		return hasSetting ? SysSettingValue(arg.DefaultSysSetting)
			: hasConstant ? new ServiceParameterValueDto { Value = arg.DefaultValue, Source = ServiceSchemaCodes.ValueSourceConst }
			: Empty();
	}

	private static void ValidatePathPlaceholders(string code, string uri,
		ServiceItemCollection<ServiceParameterDto> parameters, Errors errors) {
		var placeholders = PathPlaceholderPattern().Matches(uri).Select(m => m.Groups[1].Value)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var pathParameters = parameters.Items.Values
			.Where(p => p.Type == ServiceSchemaCodes.Placements["path"])
			.Select(p => p.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
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
