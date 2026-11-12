using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// Turns the agent's description of a REST or SOAP service into the platform contract. The platform's save validates
/// nothing (Studio checks names, URIs and duplicates in its client code only), so every rule Studio enforces
/// is enforced here, and every rule about credentials is stricter than Studio's.
/// </summary>
public static partial class WebServiceDefinitionBuilder {

	internal const int DefaultTimeoutMs = 5000;

	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
	private static partial Regex IdentifierPattern();

	[GeneratedRegex(@"\{([^{}]+)\}")]
	private static partial Regex PathPlaceholderPattern();

	/// <summary>
	/// The SOAP rules a method is built with. Every prefix a message element or a request element path uses must be
	/// declared: the platform saves an undeclared one and the call then fails before any HTTP request is sent ("The
	/// given key was not present in the dictionary.", measured).
	/// </summary>
	/// <param name="Prefixes">The namespace prefixes the service declares.</param>
	public sealed record SoapContext(IReadOnlySet<string> Prefixes);

	[GeneratedRegex(@"^(?:([A-Za-z_][A-Za-z0-9_\-]*):)?([A-Za-z_][A-Za-z0-9_\-]*)$")]
	private static partial Regex PrefixedElementPattern();

	// An unprefixed segment goes to the default namespace as written, and XmlDocument accepts Unicode letters in a name
	// (a 1C service's ДатаНачала); only the prefixed form is ASCII, because the platform's prefix:name regex is.
	[GeneratedRegex(@"^[\p{L}_][\p{L}\p{Mn}\p{Nd}_.\-]*$")]
	private static partial Regex ElementNamePattern();

	// The platform's own reading of a segment (Terrasoft.Services SoapBodyBuilder): unanchored, so it finds a prefix
	// in a name clio would refuse; the prefixes a kept method needs are found the same way.
	[GeneratedRegex(@"([a-zA-Z0-9_\-]+):([a-zA-Z0-9_\-]+)")]
	private static partial Regex PlatformPrefixPattern();

	/// <summary>
	/// The platform service type for a <c>type</c> argument: rest (default), soap11 or soap12, or the names
	/// describe-web-service and list-web-services print (REST, SOAP 1.1, SOAP 1.2).
	/// </summary>
	/// <param name="type">The argument; null or empty means REST.</param>
	/// <param name="errors">Collected problems.</param>
	/// <returns>The service type code, or REST after an error.</returns>
	public static int ParseServiceType(string type, Errors errors) {
		string normalized = (type ?? string.Empty).Replace(" ", string.Empty).Replace(".", string.Empty).ToLowerInvariant();
		switch (normalized) {
			case "" or "rest":
				return ServiceSchemaCodes.ServiceTypeRest;
			case "soap11":
				return ServiceSchemaCodes.ServiceTypeSoap11;
			case "soap12":
				return ServiceSchemaCodes.ServiceTypeSoap12;
			default:
				errors.Add($"type '{type}' must be rest, soap11 or soap12.");
				return ServiceSchemaCodes.ServiceTypeRest;
		}
	}

	/// <summary>
	/// Builds the namespaces of a SOAP service, keeping the UId of a namespace whose prefix the service already has.
	/// Studio requires at least one; exactly one is the default (the first when none is marked), because the platform
	/// fails a call with namespaces and no default before sending it ("Sequence contains no matching element").
	/// </summary>
	/// <param name="args">The namespaces as given.</param>
	/// <param name="existing">The service's current namespaces, or null for a new service.</param>
	/// <param name="errors">Collected problems.</param>
	/// <returns>The namespace collection to store.</returns>
	public static ServiceItemCollection<ServiceNamespaceDto> BuildNamespaces(IReadOnlyList<WebServiceNamespaceArg> args,
		ServiceItemCollection<ServiceNamespaceDto> existing, Errors errors) {
		var result = new ServiceItemCollection<ServiceNamespaceDto>();
		if (args is not { Count: > 0 }) {
			errors.Add("namespaces: a SOAP service needs at least one namespace, e.g. "
				+ "[{\"prefix\": \"tem\", \"uri\": \"http://tempuri.org/\"}]; the first is the default.");
			return result;
		}
		var prefixes = new HashSet<string>(StringComparer.Ordinal);
		int defaults = args.Count(a => a?.Default == true);
		if (defaults > 1) {
			errors.Add("namespaces: mark at most one namespace as default.");
		}
		for (int i = 0; i < args.Count; i++) {
			WebServiceNamespaceArg arg = args[i];
			if (!IsValidNamespace(arg, prefixes, errors)) {
				continue;
			}
			KeyValuePair<string, ServiceNamespaceDto> previousEntry = existing?.Items.FirstOrDefault(
				n => string.Equals(n.Value?.Prefix, arg.Prefix, StringComparison.Ordinal)) ?? default;
			ServiceNamespaceDto previous = previousEntry.Value;
			// A kept namespace keeps its identity: its UId, or the collection key when a row carries no UId.
			Guid uId = previous?.UId
				?? (Guid.TryParse(previousEntry.Key, out Guid key) ? key : Guid.NewGuid());
			var item = new ServiceNamespaceDto {
				UId = uId,
				Prefix = arg.Prefix,
				Name = arg.Uri.Trim(),
				IsDefault = defaults == 0 ? i == 0 : arg.Default == true,
				Extra = previous?.Extra
			};
			result.Items[item.UId.ToString()] = item;
		}
		return result;
	}

	/// <summary>
	/// The SOAP action is sent as an HTTP header value, so a line break would fail every call, and it is stored and shown
	/// like the base URI, so it must not carry credentials or a secret-like query value.
	/// </summary>
	private static void ValidateSoapAction(string code, string soapAction, Errors errors) {
		if (string.IsNullOrEmpty(soapAction)) {
			return;
		}
		if (soapAction.Any(char.IsControl)) {
			errors.Add($"method '{code}': soap-action must be one line without control characters.");
		}
		if (HasUserInfo(soapAction)) {
			errors.Add($"method '{code}': soap-action must not carry credentials (user:password@).");
		}
		ValidateUriQuery($"method '{code}': soap-action", soapAction, errors);
	}

	/// <summary>
	/// Checks one namespace as given: a plain prefix, not reserved, not used twice, and a URI without spaces, control
	/// characters or credentials. Reports the first problem and adds a valid prefix to <paramref name="prefixes"/>.
	/// </summary>
	private static bool IsValidNamespace(WebServiceNamespaceArg arg, ISet<string> prefixes, Errors errors) {
		if (arg is null) {
			errors.Add("namespaces: an item is empty.");
			return false;
		}
		RejectUnknownFields(arg.ExtensionData, $"namespace '{arg.Prefix}'", errors);
		string problem = NamespacePrefixProblem(arg.Prefix, prefixes) ?? NamespaceUriProblem(arg);
		if (problem is not null) {
			errors.Add(problem);
			return false;
		}
		prefixes.Add(arg.Prefix);
		return true;
	}

	private static string NamespacePrefixProblem(string prefix, ISet<string> prefixes) {
		if (string.IsNullOrWhiteSpace(prefix) || !PrefixedElementPattern().IsMatch(prefix) || prefix.Contains(':')) {
			return $"namespace prefix '{prefix}' must be a name of letters, digits, '_' or '-', e.g. tem.";
		}
		if (IsReservedPrefix(prefix)) {
			return $"namespace prefix '{prefix}' is reserved (XML reserves xml*, and the platform declares soapenv for "
				+ "the envelope itself); choose another, e.g. tem.";
		}
		return prefixes.Contains(prefix) ? $"namespace prefix '{prefix}' is used twice." : null;
	}

	private static string NamespaceUriProblem(WebServiceNamespaceArg arg) {
		if (string.IsNullOrWhiteSpace(arg.Uri) || arg.Uri.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) {
			return $"namespace '{arg.Prefix}' needs a uri without spaces, e.g. http://tempuri.org/.";
		}
		return HasUserInfo(arg.Uri) ? $"namespace '{arg.Prefix}': the uri must not carry credentials (user:password@)." : null;
	}

	/// <summary>
	/// XML reserves every prefix that starts with <c>xml</c>, and the platform writes the envelope with its own
	/// <c>soapenv</c> declaration, so a service namespace under either would clash with them.
	/// </summary>
	private static bool IsReservedPrefix(string prefix) =>
		prefix.StartsWith("xml", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(prefix, "soapenv", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether a value may carry <c>user:password@</c>: an <c>@</c> anywhere after <c>://</c>. It fails closed, like
	/// <see cref="WebServiceSchemaView.StripUserInfo"/>: a password may hold <c>/</c>, <c>?</c>, <c>#</c> or <c>@</c>,
	/// which <see cref="Uri"/> cannot parse, so a parse would let exactly those through. A <c>urn:</c> value has no
	/// <c>://</c> and is not affected.
	/// </summary>
	internal static bool HasUserInfo(string value) {
		if (string.IsNullOrEmpty(value)) {
			return false;
		}
		int scheme = value.IndexOf("://", StringComparison.Ordinal);
		return scheme >= 0 && value.IndexOf('@', scheme + 3) >= 0;
	}

	/// <summary>
	/// A comparable form of a namespace list (prefix, URI and default flag, by prefix), so a setNamespaces that sends
	/// the list back unchanged is not taken for a change of every call.
	/// </summary>
	/// <param name="namespaces">The namespaces.</param>
	/// <returns>The signature text.</returns>
	public static string NamespaceSignature(ServiceItemCollection<ServiceNamespaceDto> namespaces) =>
		string.Join("|", (namespaces?.Items.Values ?? Enumerable.Empty<ServiceNamespaceDto>())
			.Where(n => n is not null)
			.OrderBy(n => n.Prefix, StringComparer.Ordinal)
			.Select(n => $"{n.Prefix}={n.Name}:{n.IsDefault == true}"));

	/// <summary>
	/// The prefixes a SOAP method uses in its message element and in its body request parameter paths and array item
	/// elements.
	/// </summary>
	/// <param name="method">The method.</param>
	/// <returns>The prefixes.</returns>
	public static IReadOnlySet<string> UsedPrefixes(ServiceMethodDto method) =>
		new[] { method.Request?.Uri }
			.Concat(BodyParameters(method.Request?.Parameters).Select(p => p.Path))
			.Concat(RequestItemElements(method).Select(p => p.SequenceElementName))
			.SelectMany(path => (path ?? string.Empty).Split('/'))
			.Select(segment => PlatformPrefixPattern().Match(segment))
			.Where(match => match.Success)
			.Select(match => match.Groups[1].Value)
			.ToHashSet(StringComparer.Ordinal);

	/// <summary>The prefixes a namespace collection declares.</summary>
	/// <param name="namespaces">The namespaces.</param>
	/// <returns>The prefixes, compared ordinally as XML compares them.</returns>
	public static SoapContext SoapContextOf(ServiceItemCollection<ServiceNamespaceDto> namespaces) =>
		new((namespaces?.Items.Values ?? Enumerable.Empty<ServiceNamespaceDto>())
			.Select(n => n?.Prefix).Where(p => !string.IsNullOrEmpty(p)).ToHashSet(StringComparer.Ordinal));

	/// <summary>
	/// Checks every prefixed element name a SOAP method uses against the declared prefixes: its message element, the
	/// element paths of its body request parameters, nested items included, and the item element of each request array
	/// (the platform builds it with the same <c>prefix:name</c> lookup). Used for a method the call builds; a kept
	/// method is checked against new namespaces through <see cref="UsedPrefixes"/>.
	/// </summary>
	private static void ValidateSoapPrefixes(ServiceMethodDto method, SoapContext soap, Errors errors,
		bool includeMessageElement = true) {
		if (includeMessageElement) {
			ValidateElementPath($"method '{method.Name}': message-element", method.Request?.Uri, soap, errors);
		}
		foreach (ServiceParameterDto parameter in BodyParameters(method.Request?.Parameters)) {
			ValidateElementPath($"method '{method.Name}' request parameter '{parameter.Name}': path", parameter.Path, soap,
				errors);
		}
		// The item element's form is checked where the parameter is built; here only its prefix.
		foreach (ServiceParameterDto parameter in RequestItemElements(method)) {
			Match match = PrefixedElementPattern().Match(parameter.SequenceElementName.Trim());
			if (match.Success && match.Groups[1].Success && !soap.Prefixes.Contains(match.Groups[1].Value)) {
				errors.Add($"method '{method.Name}' request parameter '{parameter.Name}': sequence-element "
					+ $"'{parameter.SequenceElementName}': prefix '{match.Groups[1].Value}' is not a namespace of the service; "
					+ "the platform would save it and then fail every call before sending it.");
			}
		}
	}

	/// <summary>Body request arrays that name their item element; a response array is read by local name only.</summary>
	private static IEnumerable<ServiceParameterDto> RequestItemElements(ServiceMethodDto method) =>
		BodyParameters(method.Request?.Parameters)
			.Where(p => p.IsArray && !string.IsNullOrWhiteSpace(p.SequenceElementName));

	private static IEnumerable<ServiceParameterDto> BodyParameters(ServiceItemCollection<ServiceParameterDto> parameters) =>
		(parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>())
			.Where(p => p.Type == ServiceSchemaCodes.Placements["body"])
			.SelectMany(p => BodyParametersOfItems(p.ItemProperties).Prepend(p));

	private static IEnumerable<ServiceParameterDto> BodyParametersOfItems(ServiceItemCollection<ServiceParameterDto> items) =>
		(items?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>())
			.SelectMany(p => BodyParametersOfItems(p.ItemProperties).Prepend(p));

	/// <summary>
	/// One element path (<c>wsse:Security/wsse:UsernameToken</c>, a leading <c>/</c> ignored, as the platform reads
	/// it): each segment is <c>prefix:name</c> with a declared prefix, or a plain element name. The platform matches
	/// <c>prefix:name</c> unanchored, so <c>tem:Api.Key</c> would silently render as <c>tem:Api</c>; such a segment is
	/// refused rather than saved.
	/// </summary>
	private static void ValidateElementPath(string owner, string path, SoapContext soap, Errors errors) {
		string trimmed = (path ?? string.Empty).Trim().TrimStart('/');
		if (trimmed.Length == 0) {
			errors.Add($"{owner} is empty.");
			return;
		}
		foreach (string segment in trimmed.Split('/')) {
			if (!segment.Contains(':')) {
				if (!ElementNamePattern().IsMatch(segment)) {
					errors.Add($"{owner} '{path}': '{segment}' is not an XML element name.");
				}
				continue;
			}
			Match match = PrefixedElementPattern().Match(segment);
			if (!match.Success) {
				errors.Add($"{owner} '{path}': '{segment}' must be prefix:name with letters, digits, '_' or '-' only "
					+ "(the platform cuts the name at any other character).");
				continue;
			}
			string prefix = match.Groups[1].Value;
			if (!soap.Prefixes.Contains(prefix)) {
				errors.Add($"{owner} '{path}': prefix '{prefix}' is not a namespace of the service (declared: "
					+ (soap.Prefixes.Count == 0 ? "none" : string.Join(", ", soap.Prefixes.Order(StringComparer.Ordinal)))
					+ "); the platform would save it and then fail every call before sending it.");
			}
		}
	}

	/// <summary>
	/// Whether every segment of a SOAP element path is one the platform sends as written (the rule
	/// <see cref="ValidateSoapPrefixes"/> applies, without the prefix lookup): read-wsdl leaves out what register would
	/// refuse.
	/// </summary>
	/// <param name="path">The element path, <c>prefix:name</c> segments joined by <c>/</c>.</param>
	/// <returns>Whether register-web-service accepts the form of the path.</returns>
	internal static bool IsSendableElementPath(string path) {
		string trimmed = (path ?? string.Empty).Trim().TrimStart('/');
		return trimmed.Length > 0 && trimmed.Split('/').All(segment => segment.Contains(':')
			? PrefixedElementPattern().IsMatch(segment)
			: ElementNamePattern().IsMatch(segment));
	}

	/// <summary>Whether register-web-service accepts the form of an array's item element name.</summary>
	/// <param name="name">The item element name.</param>
	/// <param name="isRequest">Whether the array is a request parameter.</param>
	/// <returns>Whether the name is accepted.</returns>
	internal static bool IsSendableSequenceElement(string name, bool isRequest) =>
		!string.IsNullOrWhiteSpace(name) && SequenceElementProblem(name.Trim(), isRequest) is null;

	/// <summary>
	/// Collects every problem of one request so the agent can fix them in one retry. At most
	/// <see cref="MaxReported"/> are reported, so a huge definition cannot make the refusal itself huge.
	/// </summary>
	public sealed class Errors : List<string> {
		/// <summary>The most problems one refusal lists.</summary>
		public const int MaxReported = 50;

		public void ThrowIfAny() {
			if (Count > 0) {
				string shown = string.Join(" ", this.Take(MaxReported));
				throw new ArgumentException(Count > MaxReported ? $"{shown} ...and {Count - MaxReported} more problems." : shown);
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
	/// <param name="baseUri">The base URI as given.</param>
	/// <param name="errors">Collected problems.</param>
	/// <param name="soap">
	/// A SOAP call goes to the base URI alone, so it is stored as given: a trailing slash added to an <c>.asmx</c>
	/// endpoint makes the server answer 500 (measured).
	/// </param>
	public static string NormalizeBaseUri(string baseUri, Errors errors, bool soap = false) {
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
		if (soap && baseUri.Contains('?') && !baseUri.Contains('#')) {
			// A SOAP call goes to the base URI as stored, query included (ServiceClientRequest), and SOAP has no query
			// parameters, so a fixed query such as SAP's ?sap-client=100 can only live here. A credential-like key with a
			// constant value would be printed by list and describe, so that is refused.
			ValidateUriQuery("base-uri", baseUri.Trim(), errors);
		} else if (baseUri.IndexOfAny(['?', '#']) >= 0) {
			// The designer appends the method URI to the base verbatim, so a query here ends up in front of every
			// method path, and a key in it would be a stored constant that list and describe print.
			errors.Add(soap
				? "base-uri must not have a fragment."
				: "base-uri must not have a query or a fragment; give query values as query parameters "
					+ "(a key or token with default-sys-setting).");
			return baseUri;
		}
		string trimmed = baseUri.Trim();
		if (soap && trimmed.IndexOfAny(['{', '}']) >= 0) {
			// A SOAP method has no path parameters, so a placeholder in its base URI could never be filled.
			errors.Add("base-uri of a SOAP service cannot have a {placeholder}: SOAP methods have no path parameters.");
			return baseUri;
		}
		return soap || trimmed.EndsWith('/') ? trimmed : trimmed + "/";
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
	/// <param name="soap">The SOAP rules, for a method of a SOAP service; null for REST.</param>
	public static ServiceMethodDto BuildMethod(WebServiceMethodArg arg, ServiceMethodDto existing,
		bool serviceHasAuth, Errors errors, string prefix = null, string baseUri = null, SoapContext soap = null) {
		string code = arg?.Code;
		if (string.IsNullOrWhiteSpace(code) || !IdentifierPattern().IsMatch(code)) {
			errors.Add($"method code '{code}' must be a Latin identifier.");
			return null;
		}
		if (!string.Equals(existing?.Name, code, StringComparison.Ordinal)) {
			RequirePrefix($"method code '{code}'", code, prefix, errors);
		}
		RejectUnknownFields(arg.ExtensionData, $"method '{code}'", errors);
		if (soap is not null) {
			return BuildSoapMethod(arg, existing, serviceHasAuth, errors, prefix, soap);
		}
		if (arg.MessageElement is not null || arg.SoapAction is not null) {
			errors.Add($"method '{code}': message-element and soap-action belong to a SOAP service; a REST method has "
				+ "http-method and uri.");
		}
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
			SoapAction = existing?.SoapAction,
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
		ValidateUriQuery($"method '{code}': uri", uri, errors);
		ValidateBodyRules(code, verb, contentType, method, errors);
		return method;
	}

	/// <summary>
	/// A SOAP method: POST with an XML request and response (Studio fixes both), the message element stored where REST
	/// keeps the address, and the SOAP action. Its parameters follow the SOAP placements and element-path rules. The
	/// method replaces the stored one: an omitted soap-action is empty, as an omitted REST field is reset.
	/// </summary>
	private static ServiceMethodDto BuildSoapMethod(WebServiceMethodArg arg, ServiceMethodDto existing,
		bool serviceHasAuth, Errors errors, string prefix, SoapContext soap) {
		string code = arg.Code;
		if (!string.IsNullOrWhiteSpace(arg.Uri)) {
			errors.Add($"method '{code}': a SOAP method has no uri (the call goes to the base URI); give its body root "
				+ "element as message-element, e.g. tem:Add.");
		}
		if (arg.HttpMethod is not null && !string.Equals(arg.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) {
			errors.Add($"method '{code}': a SOAP method is always POST; omit http-method.");
		}
		if (arg.ContentType is not null && !string.Equals(arg.ContentType, "xml", StringComparison.OrdinalIgnoreCase)) {
			errors.Add($"method '{code}': a SOAP method is always xml; omit content-type.");
		}
		string messageElement = arg.MessageElement?.Trim() ?? string.Empty;
		if (messageElement.Length == 0) {
			errors.Add($"method '{code}': message-element is required, the root element of the request body with its "
				+ "namespace prefix, e.g. tem:Add.");
		}
		var method = new ServiceMethodDto {
			UId = existing?.UId ?? Guid.NewGuid(),
			Name = code,
			RequestTimeout = arg.TimeoutMs is > 0 ? arg.TimeoutMs.Value : existing?.RequestTimeout ?? DefaultTimeoutMs,
			Caption = Caption(arg.Caption ?? code),
			UseAuthInfo = arg.UseAuth ?? serviceHasAuth,
			SoapAction = arg.SoapAction?.Trim() ?? string.Empty,
			Extra = existing?.Extra,
			Request = new ServiceMethodRequestDto {
				Uri = messageElement, ContentType = ServiceSchemaCodes.ContentTypeXml,
				HttpMethodType = ServiceSchemaCodes.HttpMethods["POST"],
				Extra = existing?.Request?.Extra,
				Parameters = BuildParameters(arg.RequestParameters, existing?.Request?.Parameters, true,
					$"method '{code}' request", errors, prefix, soap: soap)
			},
			Response = new ServiceMethodResponseDto {
				ContentType = ServiceSchemaCodes.ContentTypeXml,
				Extra = existing?.Response?.Extra,
				Parameters = BuildParameters(arg.ResponseParameters, existing?.Response?.Parameters, false,
					$"method '{code}' response", errors, prefix, soap: soap)
			}
		};
		ValidateSoapAction(code, arg.SoapAction, errors);
		if (messageElement.Contains('/')) {
			// The platform reads only the first prefix:name of the message element, silently, so a path is refused.
			errors.Add($"method '{code}': message-element '{messageElement}' is one element, the root of the request "
				+ "body, e.g. tem:Add; its children are request parameters.");
		}
		ValidateSoapPrefixes(method, soap, errors, includeMessageElement: messageElement.Length > 0);
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

	/// <summary>
	/// Every system-setting reference with its owner (<c>auth</c> or <c>method:&lt;code&gt;</c>), so a reference a
	/// call adds can be told apart from one the same owner already had.
	/// </summary>
	/// <param name="schema">The schema.</param>
	/// <returns>Pairs of owner and setting code.</returns>
	public static IReadOnlyCollection<(string Owner, string Code)> CollectSysSettingReferences(ServiceSchemaDto schema) {
		var references = new HashSet<(string Owner, string Code)>();
		var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ServiceParameterValueDto value in schema.AuthInfo?.Values.Items.Values ?? Enumerable.Empty<ServiceParameterValueDto>()) {
			AddSysSetting(codes, value);
		}
		foreach (string code in codes) {
			references.Add(("auth", code.ToUpperInvariant()));
		}
		foreach (ServiceMethodDto method in schema.Methods.Items.Values) {
			codes.Clear();
			CollectFromParameters(codes, method.Request?.Parameters);
			foreach (string code in codes) {
				references.Add(($"method:{method.Name?.ToUpperInvariant()}", code.ToUpperInvariant()));
			}
		}
		return references;
	}

	/// <summary>
	/// Checks a method the call keeps against only the base-URI placeholders the call adds or removes: an added one
	/// needs a path parameter, and a path parameter must not lose the placeholder it filled. The method's own URI is
	/// not re-checked, so a Studio-made method whose placeholder case differs from its parameter does not block an
	/// unrelated base-URI change.
	/// </summary>
	/// <param name="method">The stored method.</param>
	/// <param name="baseUriBefore">The base URI before the call.</param>
	/// <param name="baseUriAfter">The base URI after the call.</param>
	/// <param name="errors">Collected problems.</param>
	public static void ValidateBaseUriChange(ServiceMethodDto method, string baseUriBefore, string baseUriAfter,
		Errors errors) {
		HashSet<string> before = Placeholders(baseUriBefore);
		HashSet<string> after = Placeholders(baseUriAfter);
		HashSet<string> own = Placeholders(method.Request?.Uri);
		var pathParameters = (method.Request?.Parameters?.Items.Values ?? Enumerable.Empty<ServiceParameterDto>())
			.Where(p => p.Type == ServiceSchemaCodes.Placements["path"]).Select(p => p.Path)
			.ToHashSet(StringComparer.Ordinal);
		foreach (string added in after.Except(before).Where(p => !pathParameters.Contains(p))) {
			errors.Add($"method '{method.Name}': base-uri placeholder {{{added}}} has no path parameter.");
		}
		foreach (string removed in before.Except(after).Where(p => pathParameters.Contains(p) && !own.Contains(p))) {
			errors.Add($"method '{method.Name}': path parameter '{removed}' loses its {{{removed}}} placeholder in "
				+ "the new base-uri.");
		}
	}

	private static HashSet<string> Placeholders(string uri) =>
		PathPlaceholderPattern().Matches(uri ?? string.Empty).Select(m => m.Groups[1].Value)
			.ToHashSet(StringComparer.Ordinal);

	public static Dictionary<string, string> Caption(string text) => new() { ["en-US"] = text };

	/// <summary>Where a parameter list is built: request or response, its owner for messages, nesting and the SOAP rules.</summary>
	private sealed record ParameterScope(bool IsRequest, string Owner, Errors Errors, string Prefix,
		bool Nested = false, SoapContext Soap = null, bool InSoapHeader = false, bool PreviousInSoapHeader = false);

	private static ServiceItemCollection<ServiceParameterDto> BuildParameters(
		IReadOnlyList<WebServiceParameterArg> args, ServiceItemCollection<ServiceParameterDto> existing,
		bool isRequest, string owner, Errors errors, string prefix, SoapContext soap = null) =>
		BuildParameters(args, existing, new ParameterScope(isRequest, owner, errors, prefix, Soap: soap));

	private static ServiceItemCollection<ServiceParameterDto> BuildParameters(
		IReadOnlyList<WebServiceParameterArg> args, ServiceItemCollection<ServiceParameterDto> existing, ParameterScope scope) {
		var result = new ServiceItemCollection<ServiceParameterDto>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Errors errors = scope.Errors;
		foreach (WebServiceParameterArg arg in args ?? []) {
			if (string.IsNullOrWhiteSpace(arg?.Code)) {
				errors.Add($"{scope.Owner}: every parameter needs a code.");
				continue;
			}
			if (!seen.Add(arg.Code)) {
				errors.Add($"{scope.Owner}: parameter code '{arg.Code}' is used twice.");
				continue;
			}
			ServiceParameterDto previous = existing?.Items.Values
				.FirstOrDefault(p => string.Equals(p.Name, arg.Code, StringComparison.OrdinalIgnoreCase));
			if (!string.Equals(previous?.Name, arg.Code, StringComparison.Ordinal)) {
				RequireNewParameterNames($"{scope.Owner} parameter", arg, scope.Prefix, scope.IsRequest, errors,
					scope.Soap is not null);
			}
			if (scope.Nested && arg.In is not null && !string.Equals(arg.In, "body", StringComparison.OrdinalIgnoreCase)) {
				// A field of an Object array element is read from that element; the platform fills header and cookie
				// values for top-level parameters only.
				errors.Add($"{scope.Owner} parameter '{arg.Code}': a field inside an Object array has no placement of its "
					+ "own; omit in.");
				continue;
			}
			ServiceParameterDto parameter = BuildParameter(arg, previous,
				scope with { Owner = $"{scope.Owner} parameter '{arg.Code}'" });
			if (parameter is not null) {
				result.Items[parameter.UId.ToString()] = parameter;
			}
		}
		return result;
	}

	/// <param name="scope">The scope; its owner names this parameter in messages.</param>
	private static ServiceParameterDto BuildParameter(WebServiceParameterArg arg, ServiceParameterDto previous,
		ParameterScope scope) {
		string owner = scope.Owner;
		Errors errors = scope.Errors;
		bool isRequest = scope.IsRequest;
		RejectUnknownFields(arg.ExtensionData, owner, errors);
		int placement = ServiceSchemaCodes.Placements["body"];
		if (isRequest && arg.In is not null && !ServiceSchemaCodes.Placements.TryGetValue(arg.In, out placement)) {
			errors.Add($"{owner}: in '{arg.In}' must be path, query, header, body or cookie.");
			return null;
		}
		// Studio's SOAP parameter page offers body, HTTP header and cookie only: the SOAP URL is the base URI, so
		// there is no method address to carry a path or query value.
		if (scope.Soap is not null && isRequest && (placement == ServiceSchemaCodes.Placements["path"]
				|| placement == ServiceSchemaCodes.Placements["query"])) {
			errors.Add($"{owner}: a SOAP request parameter goes in the body, a header or a cookie, not in '{arg.In}'.");
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
		string path = string.IsNullOrWhiteSpace(arg.Path) ? previous?.Path ?? arg.Code : arg.Path;
		bool inHeader = ValidateSoapMembers(arg, placement, path, scope) || scope.InSoapHeader;
		bool previousInHeader = scope.PreviousInSoapHeader || previous?.IsSoapHeader == true;
		ServiceParameterValueDto defValue = BuildDefault(arg, previous, placement, owner, errors, inHeader,
			scope.PreviousInSoapHeader);
		return new ServiceParameterDto {
			UId = previous?.UId ?? Guid.NewGuid(),
			Name = arg.Code,
			Type = placement,
			Path = path,
			DataValueTypeName = type,
			IsArray = arg.Array ?? false,
			IsRequired = arg.Required ?? placement == ServiceSchemaCodes.Placements["path"],
			DefValue = defValue,
			Caption = Caption(arg.Caption ?? arg.Code),
			// SOAP: the parameter is rebuilt as given, with the defaults Studio writes (not a SOAP header, item
			// elements named "item"); a SOAP-header parameter passes the flag to its items, as Studio does. REST: the
			// typed members the platform sent are carried like the extension bag, so a save keeps them as read.
			IsSoapHeader = scope.Soap is not null ? inHeader : previous?.IsSoapHeader,
			SequenceElementName = scope.Soap is not null ? SoapSequenceElement(arg) : previous?.SequenceElementName,
			Extra = previous?.Extra,
			ItemProperties = BuildParameters(arg.Items, previous?.ItemProperties,
				scope with { Nested = true, InSoapHeader = inHeader, PreviousInSoapHeader = previousInHeader })
		};
	}

	/// <summary>
	/// The item element of an array is one element name, <c>prefix:name</c> (as Studio's wizard writes it, e.g.
	/// <c>tem:Region</c>) or a plain name. A request item is built with the platform's unanchored <c>prefix:name</c>
	/// match, so the whole name must fit it, and its prefix is checked against the namespaces with the method's other
	/// names. A response item is matched by local name only, so there only the part after the prefix matters.
	/// </summary>
	private static string SequenceElementProblem(string name, bool isRequest) {
		if (!isRequest) {
			return ElementNamePattern().IsMatch(name[(name.IndexOf(':') + 1)..]) ? null : "is not an XML element name.";
		}
		if (!name.Contains(':')) {
			return ElementNamePattern().IsMatch(name) ? null : "is not an XML element name.";
		}
		return PrefixedElementPattern().IsMatch(name)
			? null
			: "must be prefix:name with letters, digits, '_' or '-' only (the platform cuts the name at any other character).";
	}

	/// <summary>
	/// The item element name a SOAP parameter is stored with; Studio writes <c>item</c> when none is given. An explicit
	/// empty value is kept: a response array with it reads every child element (XmlResponseBodyParser), and describe
	/// reports it so a round trip does not turn it into <c>item</c>, which would read nothing from such a response.
	/// </summary>
	private static string SoapSequenceElement(WebServiceParameterArg arg) =>
		arg.SequenceElement is null ? "item" : arg.SequenceElement.Trim();

	/// <summary>
	/// The SOAP-only parameter members and rules. <c>soap-header</c> goes on a top-level body request parameter only
	/// (Studio enables it there and copies it to the items); <c>sequence-element</c> names the items of an array; a
	/// response value of an XML response is read by element path, so a JSONPath there would read nothing. A REST
	/// service has none of these members.
	/// </summary>
	/// <returns>Whether this parameter is itself marked as a SOAP header.</returns>
	private static bool ValidateSoapMembers(WebServiceParameterArg arg, int placement, string path, ParameterScope scope) {
		string owner = scope.Owner;
		Errors errors = scope.Errors;
		if (scope.Soap is null) {
			if (arg.SoapHeader is not null || arg.SequenceElement is not null) {
				errors.Add($"{owner}: soap-header and sequence-element belong to a SOAP service.");
			}
			return false;
		}
		bool soapHeader = ValidateSoapHeader(arg, placement, scope);
		if (!string.IsNullOrWhiteSpace(arg.SequenceElement)) {
			if (arg.Array != true) {
				errors.Add($"{owner}: sequence-element names the items of an array; this parameter is not one.");
			} else if (SequenceElementProblem(arg.SequenceElement.Trim(), scope.IsRequest) is { } problem) {
				errors.Add($"{owner}: sequence-element '{arg.SequenceElement}' {problem}");
			}
		}
		if (!scope.IsRequest && placement == ServiceSchemaCodes.Placements["body"] && path.TrimStart().StartsWith('$')) {
			errors.Add($"{owner}: path '{path}' is a JSONPath, but a SOAP response is XML, read by element path (local "
				+ "names joined by '/', e.g. AddResult or AddResponse/AddResult); a JSONPath reads nothing.");
		}
		return soapHeader;
	}

	/// <summary>
	/// Whether a SOAP parameter is itself a SOAP header. describe reports the flag on the items of a SOAP-header
	/// parameter too (Studio stores it there), so it is accepted back there; anywhere else off a top-level body request
	/// parameter it is refused.
	/// </summary>
	private static bool ValidateSoapHeader(WebServiceParameterArg arg, int placement, ParameterScope scope) {
		if (arg.SoapHeader != true || (scope.Nested && scope.InSoapHeader)) {
			return false;
		}
		string problem = SoapHeaderProblem(placement, scope);
		if (problem is null) {
			return true;
		}
		scope.Errors.Add($"{scope.Owner}: {problem}");
		return false;
	}

	private static string SoapHeaderProblem(int placement, ParameterScope scope) {
		if (!scope.IsRequest) {
			return "a response parameter cannot be a soap-header; soap-header goes on a top-level body request parameter.";
		}
		if (scope.Nested) {
			return "soap-header on a field of an array item follows its parent; mark the top-level body request "
				+ "parameter instead.";
		}
		return placement != ServiceSchemaCodes.Placements["body"]
			? "an HTTP header or cookie parameter cannot be a soap-header; soap-header goes on a top-level body request "
				+ "parameter."
			: null;
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
		bool isRequest, Errors errors, bool soap = false) {
		if (string.IsNullOrEmpty(prefix)) {
			return;
		}
		// A SOAP response value is read by element path, a REST JSON one by JSONPath.
		bool responseBody = !soap && !isRequest
			&& (arg.In is null || string.Equals(arg.In, "body", StringComparison.OrdinalIgnoreCase));
		string example = $"code '{prefix}{char.ToUpperInvariant(arg.Code[0])}{arg.Code[1..]}', path '{(responseBody ? "$." + arg.Code : arg.Code)}'";
		if (!arg.Code.StartsWith(prefix, StringComparison.Ordinal)) {
			errors.Add($"{owner} code '{arg.Code}' must start with the environment's schema-name prefix '{prefix}', or "
				+ $"Studio refuses to save the method; keep the wire name in path, e.g. {example}.");
		} else if (string.IsNullOrWhiteSpace(arg.Path)) {
			errors.Add($"{owner} '{arg.Code}' needs path: its code carries the schema-name prefix, so it is not the "
				+ "name the API sees; give the wire name ("
				+ (soap
					? "an element path such as tem:intA for a request body value or AddResult for a response value"
					: "a JSONPath such as $.status for a response body value")
				+ ", the header or cookie name for a header or cookie).");
		}
	}

	/// <summary>
	/// Whether describe hid the previous constant: it is secret-like itself, or it travelled in a SOAP Header (its own
	/// flag or its parent's). The NEW placement never decides it: a visible constant moved into the Header is not
	/// carried, or a body constant could be turned into a stored Header constant in two calls.
	/// </summary>
	private static bool IsHiddenSecretConstant(ServiceParameterDto previous, bool previousInSoapHeader) =>
		previous?.DefValue is { Source: ServiceSchemaCodes.ValueSourceConst } value && !string.IsNullOrEmpty(value.Value)
		&& (previousInSoapHeader || previous.IsSoapHeader == true || WebServiceSchemaView.IsSecretLike(previous));

	/// <param name="soapHeader">
	/// Whether the parameter travels in a SOAP Header (WS-Security and licence values): its constant default is treated
	/// like a header constant and refused.
	/// </param>
	/// <param name="previousInSoapHeader">Whether the previous parameter's parent travelled in a SOAP Header.</param>
	private static ServiceParameterValueDto BuildDefault(WebServiceParameterArg arg, ServiceParameterDto previous,
		int placement, string owner, Errors errors, bool soapHeader = false, bool previousInSoapHeader = false) {
		if (soapHeader) {
			placement = ServiceSchemaCodes.Placements["header"];
		}
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
		if (previous is not null && !hasConstant && !clears && IsHiddenSecretConstant(previous, previousInSoapHeader)) {
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
	private static void ValidateUriQuery(string owner, string uri, Errors errors) {
		int question = uri.IndexOf('?');
		if (question < 0) {
			return;
		}
		foreach (string pair in uri[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)) {
			int equals = pair.IndexOf('=');
			// The platform sends the key unescaped, so api%5Fkey is api_key on the wire.
			string key = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
			string value = equals < 0 ? string.Empty : pair[(equals + 1)..];
			if (value.Length > 0 && !PathPlaceholderPattern().IsMatch(value)
					&& WebServiceSchemaView.IsSecretLike(key, ServiceSchemaCodes.Placements["query"])) {
				errors.Add($"{owner} query '{key}' looks like a credential and has a constant value; "
					+ "make it a parameter with default-sys-setting instead.");
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
