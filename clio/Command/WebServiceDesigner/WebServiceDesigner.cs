using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.WebServiceDesigner;

/// <summary>
/// Registers and changes REST web services, and reads and test-calls REST and SOAP ones, the way Studio > Web
/// services does.
/// </summary>
public interface IWebServiceDesigner {
	/// <summary>
	/// Lists the web services of the environment, REST and SOAP, as Studio > Web services shows them. Needs no
	/// CanManageSolution: it returns codes, captions, packages and base URIs with user information removed.
	/// </summary>
	/// <returns>One row per service.</returns>
	IReadOnlyList<WebServiceListItem> List();

	/// <summary>
	/// Reads one service with its methods and parameters. Credential values and secret-like constant defaults
	/// are never returned; setting codes are.
	/// </summary>
	/// <param name="code">The service code.</param>
	/// <param name="includeUsages">Whether to scan every process schema for Call web service elements calling it.</param>
	/// <returns>The service view, and its usages when asked for.</returns>
	WebServiceToolResult Describe(string code, bool includeUsages);

	/// <summary>
	/// Creates a REST, SOAP 1.1 or SOAP 1.2 service. Every rule Studio enforces on save is checked first, plus the
	/// SOAP rules whose breach the platform saves and then fails every call with (undeclared prefixes, no default
	/// namespace); all problems are reported in one refusal, and nothing is saved unless everything passes.
	/// </summary>
	/// <param name="packageName">The package that will own the service.</param>
	/// <param name="registration">The service as the agent describes it.</param>
	/// <returns>The saved service as the platform stores it, with notices for the user.</returns>
	WebServiceToolResult Register(string packageName, WebServiceRegistration registration);

	/// <summary>
	/// Applies operations to a REST or SOAP service in order, all or nothing. Changing or removing a method a process
	/// calls is refused unless <paramref name="acknowledgedProcesses"/> names exactly those processes. A method
	/// sent without use-auth follows the service authentication as it is after the whole call.
	/// </summary>
	/// <param name="code">The service code.</param>
	/// <param name="operations">
	/// addMethod, setMethod, removeMethod, setBaseUri, setRetryCount, setAuth; for a SOAP service also setNamespaces
	/// and setType (1.1 / 1.2). A new base URI, SOAP version or namespace list changes every call, so it counts as
	/// changing every method for the usage guard; new authentication counts for the methods that send it (use-auth);
	/// setRetryCount changes no request and does not. Each is judged by the service before and after the whole call.
	/// </param>
	/// <param name="acknowledgedProcesses">Process codes the user agreed to affect.</param>
	/// <returns>The saved service, or a refusal listing the affected processes.</returns>
	WebServiceToolResult Modify(string code, IReadOnlyList<WebServiceOperationArg> operations,
		IReadOnlyList<string> acknowledgedProcesses);

	/// <summary>
	/// Sends one real request through the platform, as Studio's Send test request does. Values sent from system
	/// settings (in a JSON body, an XML or SOAP body, its SOAP Header, the URL, headers or cookies), the
	/// Authorization header and secret constant defaults are masked in the returned body, error and parsed values.
	/// </summary>
	/// <param name="code">The service code.</param>
	/// <param name="methodCode">The method code.</param>
	/// <param name="values">
	/// Values by top-level request parameter code: a text, number or boolean for a scalar, a JSON array of them for an
	/// array of scalars, and a JSON array of objects by field code for an array of objects (see
	/// <see cref="WebServiceTestValues"/>). A code the method does not have is refused at every level.
	/// </param>
	/// <param name="userConfirmed">
	/// Whether the user explicitly agreed to this call. A POST, PUT, PATCH or DELETE method and every SOAP method can
	/// change data in the external API, so without it such a call is refused with
	/// <see cref="WebServiceTestResult.ConfirmationRequired"/> and nothing is sent; a GET needs none.
	/// </param>
	/// <returns>
	/// The status code, the masked, length-limited response body or error, and the response parameters as the platform
	/// parsed them; body and values are withheld when a sent setting value cannot be located.
	/// </returns>
	WebServiceTestResult TestMethod(string code, string methodCode, IReadOnlyDictionary<string, JsonElement> values,
		bool userConfirmed = false);
}

public sealed class WebServiceDesigner(
	IWebServiceDesignerClient client,
	ICreatioRightsClient rights,
	IWebServicePrefixReader prefixReader) : IWebServiceDesigner {

	internal const int UsageScanPageSize = 50;
	internal const int MaxResponseBodyLength = 20_000;
	private const string ManageSolutionOperation = "CanManageSolution";
	private const string CaptionColumn = "Caption";
	private const string PackageNameColumn = "SysPackage.Name";

	private const string MetaDataColumn = "MetaData";
	private static readonly string[] ServiceColumns =
		["UId", "Name", CaptionColumn, "TypeName", "PackageUId", PackageNameColumn];

	private sealed record ServiceRow(Guid UId, Guid PackageUId, string Name, string Caption, string Type, string Package);

	public IReadOnlyList<WebServiceListItem> List() =>
		client.Select("VwWebServiceV2", [.. ServiceColumns, MetaDataColumn], null, orderColumn: "Name")
			.Select(row => (Row: ToServiceRow(row), BaseUri: BaseUriFromMetadata(Text(row, MetaDataColumn))))
			.Select(item => new WebServiceListItem(item.Row.UId, item.Row.Name, item.Row.Caption, item.Row.Type,
				item.Row.Package, item.BaseUri))
			.ToList();

	/// <summary>
	/// The base URI out of a service's stored metadata, without credentials. The metadata itself is never
	/// returned: it carries constant credential values. <c>IQ1</c> is the key the platform serializes the base
	/// URI under (read on 10.2.565); a schema without it yields null rather than a guess.
	/// </summary>
	internal static string BaseUriFromMetadata(string metadata) {
		if (string.IsNullOrWhiteSpace(metadata)) {
			return null;
		}
		try {
			JsonNode root = JsonNode.Parse(metadata.TrimStart('\uFEFF'));
			return root?["MetaData"]?["Schema"]?["IQ1"] is JsonValue value && value.TryGetValue(out string uri)
				? WebServiceSchemaView.StripUserInfo(uri)
				: null;
		} catch (JsonException) {
			return null;
		}
	}

	public WebServiceToolResult Describe(string code, bool includeUsages) {
		RequireManageSolution();
		ServiceRow row = FindService(code);
		ServiceSchemaDto schema = client.ReadSchema(row.UId, row.PackageUId);
		IReadOnlyList<WebServiceUsage> usages = includeUsages ? FindUsages(schema.UId) : null;
		IReadOnlyList<string> notices = WebServiceSchemaView.XmlResponsePathNotices(schema.Methods.Items.Values,
			WebServiceSchemaView.IsSoap(schema));
		return new WebServiceToolResult(true, Service: WebServiceSchemaView.From(schema, row.Package), Usages: usages,
			Notices: notices.Count > 0 ? notices : null);
	}

	public WebServiceToolResult Register(string packageName, WebServiceRegistration registration) {
		ArgumentNullException.ThrowIfNull(registration);
		(string code, string caption, string baseUri, int? retryCount, WebServiceAuthArg auth,
			IReadOnlyList<WebServiceMethodArg> methods, string type, IReadOnlyList<WebServiceNamespaceArg> namespaces) =
			registration;
		RequireManageSolution();
		var errors = new WebServiceDefinitionBuilder.Errors();
		int serviceType = WebServiceDefinitionBuilder.ParseServiceType(type, errors);
		// An unknown type would make every SOAP member read as a REST error; report the type alone.
		errors.ThrowIfAny();
		bool soap = serviceType != ServiceSchemaCodes.ServiceTypeRest;
		if (!soap && namespaces is { Count: > 0 }) {
			errors.Add("namespaces belong to a SOAP service; give type soap11 or soap12, or leave them out.");
		}
		string prefix = prefixReader.Read();
		WebServiceDefinitionBuilder.ValidateServiceCode(code, prefix, errors);
		if (errors.Count == 0 && !client.IsSchemaNameUnique(ServiceSchemaCodes.ManagerName, code)) {
			// The platform's save looks an existing schema up by name first, so a duplicate code would
			// silently overwrite that service, UId included, and orphan every process that calls it.
			errors.Add($"code '{code}' is already used by a web service; change it with modify-web-service.");
		}
		Guid? packageUId = FindPackageUId(packageName, errors);
		var schema = new ServiceSchemaDto {
			Name = code,
			Caption = WebServiceDefinitionBuilder.Caption(string.IsNullOrWhiteSpace(caption) ? code : caption),
			BaseUri = WebServiceDefinitionBuilder.NormalizeBaseUri(baseUri, errors, soap),
			Type = serviceType,
			RetryCount = Math.Max(retryCount ?? 0, 0),
			AuthInfo = WebServiceDefinitionBuilder.BuildAuth(auth, FindOAuthApp(auth), errors)
		};
		if (soap) {
			schema.Namespaces = WebServiceDefinitionBuilder.BuildNamespaces(namespaces, null, errors);
		}
		WebServiceDefinitionBuilder.SoapContext soapContext =
			soap ? WebServiceDefinitionBuilder.SoapContextOf(schema.Namespaces) : null;
		schema.UId = schema.RealUId = Guid.NewGuid();
		schema.PackageUId = packageUId ?? Guid.Empty;
		if (methods is not { Count: > 0 }) {
			errors.Add("methods: register at least one method.");
		}
		foreach (WebServiceMethodArg method in methods ?? []) {
			AddMethod(schema, method, prefix, HasAuth(schema), schema.BaseUri, errors, soapContext);
		}
		(IReadOnlyCollection<string> settings, _) = RequireSysSettings(schema, [], errors);
		errors.ThrowIfAny();
		client.SaveSchema(schema);
		ServiceSchemaDto saved = client.ReadSchema(schema.UId, schema.PackageUId);
		return new WebServiceToolResult(true, Service: WebServiceSchemaView.From(saved, packageName),
			Notices: [.. SoapActionNotices(schema, schema.Methods.Items.Values), .. Notices(settings, packageName)]);
	}

	/// <summary>
	/// An empty SOAP action sends no SOAPAction header at all (measured); many SOAP 1.1 servers refuse such a call. The
	/// notice is written to be passed on whole: an agent that relayed only its first half dropped the risk (isolated run).
	/// Only a SOAP 1.1 service gets it: the header is the SOAP 1.1 dispatch convention.
	/// </summary>
	private static IEnumerable<string> SoapActionNotices(ServiceSchemaDto schema, IEnumerable<ServiceMethodDto> methods) =>
		schema.Type != ServiceSchemaCodes.ServiceTypeSoap11 ? [] : methods.Where(m => m.SoapAction is { Length: 0 })
			.Select(m => $"Tell the user: method '{m.Name}' has no soap-action, so no SOAPAction header is sent, and many "
				+ "SOAP 1.1 servers refuse such a call. Ask them for the SOAP action (the WSDL's binding operation "
				+ "soapAction); do not make one up.");

	/// <summary>
	/// The refusal of the usage guard. A changed or removed method leaves its callers' elements with mappings that point
	/// at nothing; a new base URI, authentication, SOAP version or namespace list leaves the elements as they are
	/// (measured for the SOAP version and namespaces: element unchanged in the designer, the run returned the result) but
	/// changes the calls they make. Both need the user's yes.
	/// </summary>
	/// <param name="everyCallChanges">What this call changes for every call, as named to the user (e.g. "base URI").</param>
	private static string UsageRefusal(IReadOnlyList<WebServiceOperationArg> operations,
		IReadOnlyList<WebServiceUsage> unacknowledged, IReadOnlyCollection<string> everyCallChanges) {
		var restructured = new HashSet<string>(operations
			.Select(o => o?.Op switch {
				SetMethodOp => o.Method?.Code,
				RemoveMethodOp => o.MethodCode,
				_ => null
			})
			.Where(c => !string.IsNullOrWhiteSpace(c)), StringComparer.OrdinalIgnoreCase);
		// A caller whose metadata could not be read (no method) counts as calling every method, so it breaks only when
		// the call changes or removes a method at all.
		bool Breaks(WebServiceUsage usage) =>
			usage.Method is null ? restructured.Count > 0 : restructured.Contains(usage.Method);
		var reasons = new List<string>();
		if (unacknowledged.Any(Breaks)) {
			reasons.Add("the changed or removed methods are called by processes that would break silently (their "
				+ "elements lose their parameters on the next designer load and fail at run time)");
		}
		// Named whenever the call makes one, also to a process whose method breaks: the user must hear that the address
		// or the credentials change too, not only that a method does.
		if (everyCallChanges.Count > 0) {
			string what = string.Join(" and ", everyCallChanges);
			string verb = everyCallChanges.Count > 1 ? "change" : "changes";
			reasons.Add($"the new {what} {verb} every call the listed processes make (an element whose method is not "
				+ "changed keeps its parameters); re-run those processes afterwards to check them");
		}
		return $"Refused: {string.Join("; and ", reasons)}. Show these processes to the user and, only on their yes, "
			+ "repeat the call with acknowledge-usages naming them.";
	}

	// The method operations of modify-web-service, as the agent names them.
	private const string AddMethodOp = "addMethod";
	private const string SetMethodOp = "setMethod";
	private const string RemoveMethodOp = "removeMethod";

	public WebServiceToolResult Modify(string code, IReadOnlyList<WebServiceOperationArg> operations,
		IReadOnlyList<string> acknowledgedProcesses) {
		RequireManageSolution();
		if (operations is not { Count: > 0 }) {
			throw new ArgumentException("operations: give at least one operation.");
		}
		ServiceRow row = FindService(code);
		ServiceSchemaDto schema = client.ReadSchema(row.UId, row.PackageUId);
		int typeBefore = schema.Type;
		bool soap = WebServiceSchemaView.IsSoap(schema);
		if (!soap && schema.Type != ServiceSchemaCodes.ServiceTypeRest) {
			throw new ArgumentException($"'{schema.Name}' is a {WebServiceSchemaView.TypeName(schema.Type)} service, "
				+ "which these tools cannot change; change it in Studio > Web services.");
		}
		var errors = new WebServiceDefinitionBuilder.Errors();
		var changedMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// What decides every call, before the call: compared with the result after the whole call, so a value changed and
		// changed back, or describe output sent back, is not a change.
		string baseUriBefore = schema.BaseUri;
		string authBefore = AuthSignature(schema.AuthInfo);
		string namespacesBefore = WebServiceDefinitionBuilder.NamespaceSignature(schema.Namespaces);
		// Only a method definition carries codes the prefix rule applies to; skip the read otherwise.
		string prefix = operations.Any(o => o?.Op is AddMethodOp or SetMethodOp) ? prefixReader.Read() : null;
		IReadOnlyCollection<(string Owner, string Code)> referencesBefore =
			WebServiceDefinitionBuilder.CollectSysSettingReferences(schema);
		// Methods are checked against the base URI, the namespaces and the authentication as they are after the whole
		// call, so the order of the operations does not change the result.
		WebServiceOperationArg lastSetBaseUri = operations.LastOrDefault(o => o?.Op == "setBaseUri");
		WebServiceOperationArg lastSetNamespaces = operations.LastOrDefault(o => o?.Op == "setNamespaces");
		string finalBaseUri = lastSetBaseUri is null
			? schema.BaseUri
			: WebServiceDefinitionBuilder.NormalizeBaseUri(lastSetBaseUri.BaseUri, new WebServiceDefinitionBuilder.Errors(),
				soap);
		WebServiceDefinitionBuilder.SoapContext soapContext = soap ? FinalSoapContext(schema, lastSetNamespaces) : null;
		bool methodsUseAuth = MethodsUseAuth(schema, operations.LastOrDefault(o => o?.Op == "setAuth"));
		var before = new KeptMethodBaseline(WebServiceDefinitionBuilder.SoapContextOf(schema.Namespaces).Prefixes,
			lastSetBaseUri is null ? null : schema.BaseUri, soap && lastSetNamespaces is not null);
		var builtMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (WebServiceOperationArg operation in operations) {
			Apply(schema, operation, prefix, methodsUseAuth, finalBaseUri, builtMethods, changedMethods, errors, soapContext);
		}
		List<string> everyCallChanges = EveryCallChanges(schema, changedMethods, baseUriBefore, authBefore,
			namespacesBefore, typeBefore);
		ValidateKeptMethods(schema, builtMethods, before, errors);
		(IReadOnlyCollection<string> settings, IReadOnlyList<string> missingKept) =
			RequireSysSettings(schema, referencesBefore, errors);
		errors.ThrowIfAny();
		IReadOnlyList<WebServiceUsage> affected = changedMethods.Count == 0
			? []
			: FindUsages(schema.UId).Where(u => u.Method is null || changedMethods.Contains(u.Method)).ToList();
		var acknowledged = new HashSet<string>(acknowledgedProcesses ?? [], StringComparer.OrdinalIgnoreCase);
		List<WebServiceUsage> unacknowledged = affected.Where(u => !acknowledged.Contains(u.Process)).ToList();
		if (unacknowledged.Count > 0) {
			return new WebServiceToolResult(false, UsageRefusal(operations, unacknowledged, everyCallChanges),
				Usages: unacknowledged);
		}
		client.SaveSchema(schema);
		ServiceSchemaDto saved = client.ReadSchema(schema.UId, row.PackageUId);
		return new WebServiceToolResult(true, Service: WebServiceSchemaView.From(saved, row.Package),
			Usages: affected,
			Notices: [.. SavedChangeNotices(schema, soap, typeBefore, builtMethods, authBefore, operations),
				.. Notices(settings, row.Package, missingKept)]);
	}

	/// <summary>
	/// The notices a saved change owes the user: empty SOAP actions of the methods it built (of every method after a
	/// switch to SOAP 1.1), and the methods that send no authentication after it changed the authentication.
	/// </summary>
	private static IEnumerable<string> SavedChangeNotices(ServiceSchemaDto schema, bool soap, int typeBefore,
		ISet<string> builtMethods, string authBefore, IReadOnlyList<WebServiceOperationArg> operations) {
		var notices = new List<string>();
		if (soap) {
			// A switch to SOAP 1.1 makes every method's empty action matter, not only the methods this call built.
			bool becameSoap11 = typeBefore != schema.Type;
			notices.AddRange(SoapActionNotices(schema,
				schema.Methods.Items.Values.Where(m => becameSoap11 || builtMethods.Contains(m.Name ?? ""))));
		}
		if (AuthSignature(schema.AuthInfo) != authBefore) {
			notices.AddRange(NoAuthMethodNotices(schema, ExplicitUseAuthMethods(operations)));
		}
		return notices;
	}

	public WebServiceTestResult TestMethod(string code, string methodCode,
		IReadOnlyDictionary<string, JsonElement> values, bool userConfirmed = false) {
		RequireManageSolution();
		ServiceRow row = FindService(code);
		ServiceSchemaDto schema = client.ReadSchema(row.UId, row.PackageUId);
		ServiceMethodDto method = FindMethod(schema, methodCode)
			?? throw new ArgumentException(
				$"method '{methodCode}' not found; methods: {string.Join(", ", schema.Methods.Items.Values.Select(m => m.Name))}.");
		WebServiceTestCall call = WebServiceTestValues.Build(method, values);
		if (!userConfirmed && CanChangeData(schema, method)) {
			// Owner decision (ADR D14): a request to test is not a confirmation. Checked after the values, so the call the
			// agent repeats after the user's yes is already valid.
			return new WebServiceTestResult(false, ConfirmationRefusal(schema, method), ConfirmationRequired: true);
		}
		ServiceMethodCallResult result = client.CallMethod(schema.Name, method.Name, call.Parameters);
		if (IsRestXmlRefusal(schema, method, result)) {
			// Nothing was built or sent, so there is no secret to locate and the explanation must not be withheld.
			return new WebServiceTestResult(false, SensitiveErrorTextRedactor.Redact(ExplainRestXml(result.Error)),
				result.StatusCode);
		}
		// The codes the call gave a value, nested ones included, tell an array the platform skipped from one the
		// lookup missed.
		IReadOnlyList<string> secrets = WebServiceResponseMasker.SentSecrets(method, result.RequestRawData,
			result.RequestBody, HasAuth(schema), call.SentCodes, out bool secretUnlocated);
		if (!string.IsNullOrEmpty(result.Error) && string.IsNullOrEmpty(result.RequestRawData)
			&& string.IsNullOrEmpty(result.RequestBody) && WebServiceResponseMasker.HasCredentialResponseDefault(method)) {
			// A failure with nothing reported as sent may come from converting a response default after the call (a
			// FormatException quoting a setting value), and that value is not among the sent secrets: fail closed.
			return new WebServiceTestResult(result.Success,
				"The platform's error is withheld: the call failed with nothing reported as sent, and a response parameter "
				+ "of this method takes its default from a system setting or a secret constant, which such an error can "
				+ "quote. Check the method's parameter types and defaults with describe-web-service.",
				result.StatusCode, null, false, true);
		}
		if (secretUnlocated) {
			// The value is unknown, so it cannot be masked: fail closed and return the status only. The parsed
			// response values come from the same response and are withheld with it.
			return new WebServiceTestResult(result.Success,
				"The response is withheld: a value filled from a system setting could not be located in what the "
				+ "platform reports as sent (or the request body is neither JSON nor XML), so it could not be masked.",
				result.StatusCode, null, false, true);
		}
		// Masked before truncation, so a value cut at the boundary cannot survive half-visible.
		string body = WebServiceResponseMasker.ApplyToBody(result.ResponseBody, secrets, out bool bodyMasked);
		string error = WebServiceResponseMasker.Apply(result.Error, secrets, out bool errorMasked);
		JsonElement? responseValues = ResponseValues(result.ParameterValues, method, secrets, out bool valuesMasked,
			out bool valuesOmitted);
		bool truncated = body is { Length: > MaxResponseBodyLength };
		// An XML response read through a JSONPath gives null with no error, which reads like a failure of the call.
		IReadOnlyList<string> notices = WebServiceSchemaView.XmlResponsePathNotices([method], WebServiceSchemaView.IsSoap(schema));
		return new WebServiceTestResult(result.Success,
			error is null ? null : SensitiveErrorTextRedactor.Redact(error),
			result.StatusCode, truncated ? body[..MaxResponseBodyLength] : body, truncated,
			bodyMasked || errorMasked || valuesMasked, responseValues, valuesOmitted ? true : null,
			notices.Count > 0 ? notices : null);
	}

	/// <summary>
	/// The platform's parsed response parameters, masked like the body and, by code, wherever a parameter may carry a
	/// credential (see <see cref="WebServiceResponseMasker.MaskResponseValues"/>). Values that cannot be read as a JSON
	/// object or array are not returned: unparsed text would bypass the per-value masking. Values longer than the body
	/// limit are not returned either (<paramref name="omitted"/>): a JSON tree cannot be cut like text.
	/// </summary>
	private static JsonElement? ResponseValues(string parameterValues, ServiceMethodDto method,
		IReadOnlyList<string> secrets, out bool masked, out bool omitted) {
		masked = false;
		omitted = false;
		if (string.IsNullOrWhiteSpace(parameterValues)) {
			return null;
		}
		string text = WebServiceResponseMasker.MaskResponseValues(parameterValues, method, secrets, out masked)
			?.ToJsonString();
		if (text is null) {
			return null;
		}
		if (text.Length > MaxResponseBodyLength) {
			omitted = true;
			return null;
		}
		using JsonDocument document = JsonDocument.Parse(text);
		return document.RootElement.Clone();
	}

	/// <summary>
	/// Whether a test call of the method can change data in the external API: every SOAP call is a POST whose effect clio
	/// cannot know, and so is a REST method with any verb but GET (an unknown verb counts too). A REST method with an XML
	/// body is not exempt although the platform usually refuses to build it: a custom body builder (<c>bodyBuilderId</c>)
	/// makes it go out.
	/// </summary>
	private static bool CanChangeData(ServiceSchemaDto schema, ServiceMethodDto method) =>
		WebServiceSchemaView.IsSoap(schema)
		|| method.Request?.HttpMethodType != ServiceSchemaCodes.HttpMethods["GET"];

	private static string ConfirmationRefusal(ServiceSchemaDto schema, ServiceMethodDto method) {
		string verb = WebServiceSchemaView.IsSoap(schema)
			? "a SOAP method"
			: $"a {ServiceSchemaCodes.HttpMethods.FirstOrDefault(m => m.Value == method.Request?.HttpMethodType).Key ?? "non-GET"} method";
		return $"Not sent: '{method.Name}' of '{schema.Name}' is {verb}, so the test call sends a real request that can "
			+ "change data in that API, and a request to test is not a confirmation. Tell the user exactly that and ask for "
			+ "their explicit yes; do not set user-confirmed yourself. Only after they say yes, repeat the same call with "
			+ "user-confirmed: true.";
	}

	/// <summary>
	/// Whether the platform refused a REST method with an XML body before building it: it has no request builder for
	/// one and answers "Rest:Xml" with nothing sent (measured with and without body parameters).
	/// </summary>
	private static bool IsRestXmlRefusal(ServiceSchemaDto schema, ServiceMethodDto method, ServiceMethodCallResult result) =>
		!WebServiceSchemaView.IsSoap(schema)
		&& method.Request?.ContentType == ServiceSchemaCodes.ContentTypeXml
		&& result.Error?.Contains("Rest:Xml", StringComparison.OrdinalIgnoreCase) == true
		&& string.IsNullOrEmpty(result.RequestRawData) && string.IsNullOrEmpty(result.RequestBody);

	private static string ExplainRestXml(string error) =>
		$"{error} The platform cannot build the request of a REST method with content-type xml, so this method "
		+ "cannot be test-called and nothing was sent. A process calls it through the Call web service element's "
		+ "RequestBody, which carries the whole XML body, and reads the raw Response.";

	internal IReadOnlyList<WebServiceUsage> FindUsages(Guid serviceUId) {
		string uid = serviceUId.ToString();
		var usages = new List<WebServiceUsage>();
		string[] columns = ["Id", "Name", CaptionColumn, PackageNameColumn, "MetaData"];
		var filter = new Dictionary<string, string> { ["ManagerName"] = ServiceSchemaCodes.ProcessManagerName };
		int offset = 0;
		int pageCount;
		do {
			IReadOnlyList<JsonObject> page = client.Select("SysSchema", columns, filter, UsageScanPageSize, offset, "Id");
			foreach (JsonObject row in page) {
				usages.AddRange(UsagesInRow(row, uid));
			}
			pageCount = page.Count;
			offset += UsageScanPageSize;
		} while (pageCount == UsageScanPageSize);
		return usages;
	}

	private static IEnumerable<WebServiceUsage> UsagesInRow(JsonObject row, string uid) {
		string metadata = Text(row, "MetaData");
		if (metadata is null || metadata.IndexOf(uid, StringComparison.OrdinalIgnoreCase) < 0) {
			return [];
		}
		return FindElements(metadata, uid).Select(found => new WebServiceUsage(Text(row, "Name"),
			Text(row, CaptionColumn), Text(row, PackageNameColumn), found.Element, found.Method));
	}

	/// <remarks>
	/// Fails closed: a process whose metadata names the service but cannot be read - not JSON, a duplicate key,
	/// or the UId found nowhere an element stores it - is reported as one usage with no element and no method,
	/// which the guard treats as calling every method. Metadata saved by the platform may start with a
	/// byte-order mark, which <c>JsonNode.Parse</c> rejects.
	/// </remarks>
	internal static IReadOnlyList<(string Element, string Method)> FindElements(string metadata, string uid) {
		IReadOnlyList<(string Element, string Method)> unreadable = [(null, null)];
		List<(string Element, string Method)> found;
		try {
			found = Walk(JsonNode.Parse(metadata.TrimStart('\uFEFF')), uid);
		} catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) {
			return unreadable;
		}
		return found.Count > 0 ? found : unreadable;
	}

	private static List<(string Element, string Method)> Walk(JsonNode root, string uid) {
		var found = new List<(string Element, string Method)>();
		var nodes = new List<JsonNode> { root };
		for (int i = 0; i < nodes.Count; i++) {
			if (nodes[i] is JsonObject node && IsElementOf(node, uid)) {
				found.Add((node["A2"]?.ToString(), node["IX2"]?.ToString()));
			}
			nodes.AddRange(Children(nodes[i]));
		}
		return found;
	}

	private static bool IsElementOf(JsonObject node, string uid) =>
		node["IX1"] is JsonValue ix1 && string.Equals(ix1.ToString(), uid, StringComparison.OrdinalIgnoreCase);

	private static IEnumerable<JsonNode> Children(JsonNode node) => node switch {
		JsonObject obj => obj.Select(child => child.Value).Where(child => child is not null),
		JsonArray array => array.Where(item => item is not null),
		_ => []
	};

	private void Apply(ServiceSchemaDto schema, WebServiceOperationArg operation, string prefix, bool methodsUseAuth,
		string finalBaseUri, ISet<string> builtMethods, ISet<string> changedMethods,
		WebServiceDefinitionBuilder.Errors errors, WebServiceDefinitionBuilder.SoapContext soap) {
		WebServiceDefinitionBuilder.RejectUnknownFields(operation?.ExtensionData, $"op '{operation?.Op}'", errors);
		switch (operation?.Op) {
			case AddMethodOp:
				AddMethod(schema, operation.Method, prefix, methodsUseAuth, finalBaseUri, errors, soap);
				builtMethods.Add(operation.Method?.Code ?? string.Empty);
				break;
			case SetMethodOp:
				ServiceMethodDto existing = FindMethod(schema, operation.Method?.Code);
				if (existing is null) {
					errors.Add($"setMethod: method '{operation.Method?.Code}' does not exist; use addMethod.");
					break;
				}
				ServiceMethodDto replacement = WebServiceDefinitionBuilder.BuildMethod(operation.Method, existing,
					methodsUseAuth, errors, prefix, finalBaseUri, soap);
				builtMethods.Add(operation.Method?.Code ?? string.Empty);
				if (replacement is not null) {
					schema.Methods.Items.Remove(existing.UId.ToString());
					schema.Methods.Items[replacement.UId.ToString()] = replacement;
					changedMethods.Add(existing.Name);
				}
				break;
			case RemoveMethodOp:
				ServiceMethodDto removed = FindMethod(schema, operation.MethodCode);
				if (removed is null) {
					errors.Add($"removeMethod: method '{operation.MethodCode}' does not exist.");
					break;
				}
				schema.Methods.Items.Remove(removed.UId.ToString());
				changedMethods.Add(removed.Name);
				break;
			case "setBaseUri":
				schema.BaseUri = WebServiceDefinitionBuilder.NormalizeBaseUri(operation.BaseUri, errors, soap is not null);
				break;
			case "setNamespaces":
				if (soap is null) {
					errors.Add("setNamespaces: a REST service has no namespaces.");
					break;
				}
				schema.Namespaces = WebServiceDefinitionBuilder.BuildNamespaces(operation.Namespaces, schema.Namespaces, errors);
				break;
			case "setType":
				SetType(schema, operation.Type, soap is not null, errors);
				break;
			case "setRetryCount":
				// Retries change no request the API receives, so no caller has to agree.
				schema.RetryCount = Math.Max(operation.RetryCount ?? 0, 0);
				break;
			case "setAuth":
				schema.AuthInfo = WebServiceDefinitionBuilder.BuildAuth(operation.Auth, FindOAuthApp(operation.Auth), errors);
				break;
			default:
				errors.Add($"op '{operation?.Op}' must be addMethod, setMethod, removeMethod, setBaseUri, setRetryCount, "
					+ "setAuth, setNamespaces or setType.");
				break;
		}
	}

	/// <summary>
	/// Whether a method sent without use-auth sends the service authentication: it follows the authentication as it is
	/// after this call, so [addMethod, setAuth] gives the new method authentication just like [setAuth, addMethod].
	/// </summary>
	private static bool MethodsUseAuth(ServiceSchemaDto schema, WebServiceOperationArg lastSetAuth) =>
		lastSetAuth is null
			? HasAuth(schema)
			: ServiceSchemaCodes.AuthTypes.TryGetValue(lastSetAuth.Auth?.Type ?? string.Empty, out int authType)
				&& authType != ServiceSchemaCodes.AuthNone;

	/// <summary>What a kept method is checked against after the call: the service as it was before it.</summary>
	/// <param name="Prefixes">The namespace prefixes before the call.</param>
	/// <param name="BaseUri">The base URI before the call, when the call sets a new one; otherwise null.</param>
	/// <param name="NamespacesSet">Whether the call sets the namespaces of a SOAP service.</param>
	private sealed record KeptMethodBaseline(IReadOnlySet<string> Prefixes, string BaseUri, bool NamespacesSet);

	/// <summary>
	/// Checks the methods the call keeps against only what the call changed. A kept SOAP method must not lose a prefix
	/// it uses (the platform would save the service and then fail every call of that method before sending it); a kept
	/// method's path parameters must keep the base-URI placeholders they fill. Nothing else of a kept method is
	/// re-validated, so a Studio-made method the stricter rules would refuse does not block an unrelated change.
	/// </summary>
	private static void ValidateKeptMethods(ServiceSchemaDto schema, ISet<string> builtMethods, KeptMethodBaseline before,
		WebServiceDefinitionBuilder.Errors errors) {
		IReadOnlySet<string> declared = WebServiceDefinitionBuilder.SoapContextOf(schema.Namespaces).Prefixes;
		var removed = before.Prefixes.Where(p => !declared.Contains(p)).ToHashSet(StringComparer.Ordinal);
		foreach (ServiceMethodDto kept in schema.Methods.Items.Values.Where(m => !builtMethods.Contains(m.Name ?? ""))) {
			if (before.NamespacesSet) {
				foreach (string lostPrefix in WebServiceDefinitionBuilder.UsedPrefixes(kept).Where(removed.Contains)) {
					errors.Add($"setNamespaces: method '{kept.Name}' still uses prefix '{lostPrefix}', which this call removes; "
						+ "keep the namespace or change the method in the same call.");
				}
			}
			if (before.BaseUri is not null) {
				WebServiceDefinitionBuilder.ValidateBaseUriChange(kept, before.BaseUri, schema.BaseUri, errors);
			}
		}
	}

	/// <summary>
	/// The prefixes methods are checked against: the namespaces as they are after the whole call. When the last
	/// setNamespaces is itself invalid (it is reported on its own), its valid items and the current namespaces are used,
	/// so every built method is not also reported against an empty list.
	/// </summary>
	private static WebServiceDefinitionBuilder.SoapContext FinalSoapContext(ServiceSchemaDto schema,
		WebServiceOperationArg lastSetNamespaces) {
		if (lastSetNamespaces is null) {
			return WebServiceDefinitionBuilder.SoapContextOf(schema.Namespaces);
		}
		var probe = new WebServiceDefinitionBuilder.Errors();
		ServiceItemCollection<ServiceNamespaceDto> final =
			WebServiceDefinitionBuilder.BuildNamespaces(lastSetNamespaces.Namespaces, schema.Namespaces, probe);
		IReadOnlySet<string> prefixes = WebServiceDefinitionBuilder.SoapContextOf(final).Prefixes;
		return probe.Count == 0
			? new WebServiceDefinitionBuilder.SoapContext(prefixes)
			: new WebServiceDefinitionBuilder.SoapContext(prefixes
				.Concat(WebServiceDefinitionBuilder.SoapContextOf(schema.Namespaces).Prefixes)
				.ToHashSet(StringComparer.Ordinal));
	}

	/// <summary>
	/// What the call changed for the calls processes make, judged by the service before and after the whole call. The base
	/// URI, the namespaces and the SOAP version decide every call, so each counts as changing every method. The
	/// authentication is attached only by a method with use-auth (measured), so new authentication counts as changing the
	/// methods that send it after the call; a method that sends none makes the same request as before.
	/// </summary>
	/// <returns>What changed, as the refusal names it to the user.</returns>
	private static List<string> EveryCallChanges(ServiceSchemaDto schema, ISet<string> changedMethods, string baseUriBefore,
		string authBefore, string namespacesBefore, int typeBefore) {
		var changes = new List<string>();
		void Every(string what) {
			foreach (ServiceMethodDto method in schema.Methods.Items.Values) {
				changedMethods.Add(method.Name);
			}
			changes.Add(what);
		}
		if (!string.Equals(schema.BaseUri, baseUriBefore, StringComparison.Ordinal)) {
			Every("base URI");
		}
		if (WebServiceDefinitionBuilder.NamespaceSignature(schema.Namespaces) != namespacesBefore) {
			Every("namespace list");
		}
		if (schema.Type != typeBefore) {
			Every("SOAP version");
		}
		if (AuthSignature(schema.AuthInfo) != authBefore) {
			List<string> sending = schema.Methods.Items.Values.Where(m => m.UseAuthInfo).Select(m => m.Name).ToList();
			if (sending.Count > 0) {
				changedMethods.UnionWith(sending);
				changes.Add("authentication");
			}
		}
		return changes;
	}

	/// <summary>
	/// The authentication as one comparable string: its type and every value with its source. No authentication and
	/// type none read the same, as the platform sends nothing for either.
	/// </summary>
	private static string AuthSignature(ServiceAuthInfoDto auth) {
		if (auth is null || auth.AuthType == ServiceSchemaCodes.AuthNone) {
			return string.Empty;
		}
		IEnumerable<string> values = (auth.Values?.Items ?? [])
			.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
			.Select(v => $"{v.Key.ToLowerInvariant()}={v.Value?.Source}:{v.Value?.Value}");
		return $"{auth.AuthType}|{string.Join("|", values)}";
	}

	// The most method names one notice lists; the rest are counted.
	private const int MaxNamedMethods = 20;

	/// <summary>
	/// After a call that changed the authentication and left the service with some: the methods with use-auth false, which
	/// still send no Authorization header (the platform attaches it per method; measured on SOAP 1.2, the same code path
	/// for REST). A method this call defined with an explicit use-auth is left out: that was the agent's choice.
	/// </summary>
	private static IEnumerable<string> NoAuthMethodNotices(ServiceSchemaDto schema, IReadOnlySet<string> explicitUseAuth) {
		if (!HasAuth(schema)) {
			return [];
		}
		List<string> without = schema.Methods.Items.Values.Where(m => !m.UseAuthInfo && !explicitUseAuth.Contains(m.Name ?? ""))
			.Select(m => m.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
		if (without.Count == 0) {
			return [];
		}
		string named = string.Join(", ", without.Take(MaxNamedMethods).Select(n => $"'{n}'"))
			+ (without.Count > MaxNamedMethods ? $" and {without.Count - MaxNamedMethods} more" : string.Empty);
		string subject = without.Count == 1 ? $"Method {named} has" : $"Methods {named} have";
		return [$"{subject} use-auth false, so the new authentication is not sent with {(without.Count == 1 ? "it" : "them")}. "
			+ "Ask the user whether it should be; do not change use-auth yourself. Only on their yes, repeat the "
			+ "definition with use-auth true in setMethod."];
	}

	/// <summary>The codes of the methods this call defines with an explicit use-auth.</summary>
	private static IReadOnlySet<string> ExplicitUseAuthMethods(IReadOnlyList<WebServiceOperationArg> operations) =>
		operations.Where(o => o?.Op is AddMethodOp or SetMethodOp && o.Method?.UseAuth is not null)
			.Select(o => o.Method.Code ?? string.Empty)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

	/// <summary>Switches a SOAP service between 1.1 and 1.2; Studio offers no other change of type.</summary>
	private static void SetType(ServiceSchemaDto schema, string type, bool soap,
		WebServiceDefinitionBuilder.Errors errors) {
		int errorsBefore = errors.Count;
		int target = WebServiceDefinitionBuilder.ParseServiceType(type, errors);
		if (errors.Count > errorsBefore) {
			return;
		}
		if (string.IsNullOrWhiteSpace(type)) {
			errors.Add("setType: give type soap11 or soap12.");
		} else if (!soap || target == ServiceSchemaCodes.ServiceTypeRest) {
			errors.Add("setType: only SOAP 1.1 and SOAP 1.2 can be switched; a REST service cannot become SOAP or back "
				+ "(Studio does not allow it either). Register a new service instead.");
		} else {
			schema.Type = target;
		}
	}

	private static void AddMethod(ServiceSchemaDto schema, WebServiceMethodArg arg, string prefix, bool useAuth,
		string baseUri, WebServiceDefinitionBuilder.Errors errors, WebServiceDefinitionBuilder.SoapContext soap = null) {
		if (FindMethod(schema, arg?.Code) is not null) {
			errors.Add($"method code '{arg?.Code}' is used twice.");
			return;
		}
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(arg, null, useAuth, errors, prefix, baseUri, soap);
		if (method is not null) {
			schema.Methods.Items[method.UId.ToString()] = method;
		}
	}

	private static bool HasAuth(ServiceSchemaDto schema) =>
		schema.AuthInfo is { AuthType: not ServiceSchemaCodes.AuthNone };

	private static ServiceMethodDto FindMethod(ServiceSchemaDto schema, string code) =>
		string.IsNullOrWhiteSpace(code)
			? null
			: schema.Methods.Items.Values.FirstOrDefault(m => string.Equals(m.Name, code, StringComparison.OrdinalIgnoreCase));

	private void RequireManageSolution() {
		if (!rights.GetCanExecuteOperation(ManageSolutionOperation, new CreatioRequestOptions())) {
			throw new UnauthorizedAccessException(
				"The environment user lacks CanManageSolution, which Studio > Web services requires.");
		}
	}

	private ServiceRow FindService(string code) {
		if (string.IsNullOrWhiteSpace(code)) {
			throw new ArgumentException("service code is required.");
		}
		IReadOnlyList<ServiceRow> rows = ReadServiceRows(code);
		return rows.FirstOrDefault(r => string.Equals(r.Name, code, StringComparison.OrdinalIgnoreCase))
			?? throw new ArgumentException(
				$"web service '{code}' is not registered; list-web-services shows the registered codes.");
	}

	private IReadOnlyList<ServiceRow> ReadServiceRows(string code) {
		Dictionary<string, string> filter = code is null ? null : new() { ["Name"] = code };
		return client.Select("VwWebServiceV2", ServiceColumns, filter, orderColumn: "Name")
			.Select(ToServiceRow)
			.ToList();
	}

	private static ServiceRow ToServiceRow(JsonObject row) =>
		new(Guid.Parse(Text(row, "UId")), Guid.Parse(Text(row, "PackageUId")),
			Text(row, "Name"), Text(row, CaptionColumn), Text(row, "TypeName"), Text(row, PackageNameColumn));

	private Guid? FindPackageUId(string packageName, WebServiceDefinitionBuilder.Errors errors) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			errors.Add("package-name is required.");
			return null;
		}
		JsonObject row = client.Select("SysPackage", ["UId"], new Dictionary<string, string> { ["Name"] = packageName })
			.FirstOrDefault();
		if (row is null) {
			errors.Add($"package '{packageName}' does not exist.");
			return null;
		}
		return Guid.Parse(Text(row, "UId"));
	}

	private Guid? FindOAuthApp(WebServiceAuthArg auth) {
		if (auth?.Type is null || !string.Equals(auth.Type, "oauth20", StringComparison.OrdinalIgnoreCase)
			|| string.IsNullOrWhiteSpace(auth.OAuthAppReference)) {
			return null;
		}
		// describe-web-service reports the linked application as oauth-app-id, so a setAuth rebuilt from it passes
		// the id; a person passes the name. Both resolve to the same row; when both are given they must agree.
		Guid? byApp = ResolveOAuthApp(auth.OAuthApp);
		Guid? byId = ResolveOAuthApp(auth.OAuthAppId);
		if (!string.IsNullOrWhiteSpace(auth.OAuthApp) && !string.IsNullOrWhiteSpace(auth.OAuthAppId)) {
			if (byApp is null || byId is null) {
				string unknown = byApp is null ? $"oauth-app '{auth.OAuthApp}'" : $"oauth-app-id '{auth.OAuthAppId}'";
				throw new ArgumentException($"the auth block's {unknown} names no OAuth application; give one existing "
					+ "application by name or id.");
			}
			if (byApp != byId) {
				throw new ArgumentException("the auth block names two different OAuth applications in oauth-app and "
					+ "oauth-app-id; give one.");
			}
		}
		return byApp ?? byId;
	}

	private Guid? ResolveOAuthApp(string reference) {
		if (string.IsNullOrWhiteSpace(reference)) {
			return null;
		}
		string value = reference.Trim();
		string column = Guid.TryParse(value, out Guid id) ? "Id" : "Name";
		JsonObject row = client.Select("OAuthApplications", ["Id"],
			new Dictionary<string, string> { [column] = column == "Id" ? id.ToString() : value }).FirstOrDefault();
		return row is null ? null : Guid.Parse(Text(row, "Id"));
	}

	/// <summary>
	/// Refuses references this call adds to system settings that do not exist; a reference the same owner (the
	/// authentication or a method) already had is reported as a notice instead, so a setting deleted since does not
	/// block an unrelated change. The returned codes are the existing ones the user fills.
	/// </summary>
	private (IReadOnlyCollection<string> Codes, IReadOnlyList<string> MissingKept) RequireSysSettings(
		ServiceSchemaDto schema, IReadOnlyCollection<(string Owner, string Code)> referencesBefore,
		WebServiceDefinitionBuilder.Errors errors) {
		IReadOnlyCollection<string> codes = WebServiceDefinitionBuilder.CollectSysSettingCodes(schema);
		var existing = new HashSet<string>(client.FindExistingSysSettingCodes(codes) ?? [],
			StringComparer.OrdinalIgnoreCase);
		var before = new HashSet<(string Owner, string Code)>(referencesBefore);
		IReadOnlyCollection<(string Owner, string Code)> after = WebServiceDefinitionBuilder.CollectSysSettingReferences(schema);
		var missingKept = new List<string>();
		foreach (string code in codes.Where(c => !existing.Contains(c))) {
			string upper = code.ToUpperInvariant();
			if (after.Where(r => r.Code == upper).All(before.Contains)) {
				missingKept.Add(code);
			} else {
				// A default bound to a missing setting is dropped from the request without any error at
				// run time (measured on 10.2.565), so the reference is refused here instead.
				errors.Add($"system setting '{code}' does not exist; create it with create-sys-setting and no value "
					+ "(value-type-name SecureText for a key, token or password), then ask the user to fill it. "
					+ "The agent never sets its value.");
			}
		}
		return (codes.Where(existing.Contains).ToList(), missingKept);
	}

	private static IReadOnlyList<string> Notices(IReadOnlyCollection<string> settings, string packageName,
		IReadOnlyList<string> missingKept = null) {
		var notices = new List<string>();
		if (missingKept is { Count: > 0 }) {
			notices.Add($"The service already referenced system settings that do not exist: "
				+ $"{string.Join(", ", missingKept)}. At run time those values are not sent at all; create the "
				+ "settings with create-sys-setting and ask the user to fill them, or change the references.");
		}
		if (settings.Count > 0) {
			notices.Add($"The user fills the values of system settings {string.Join(", ", settings)} in "
				+ "System settings; an empty value is sent as nothing. Before shipping the package, bind those "
				+ "settings to it (data-binding), as Studio does on save.");
		}
		notices.Add($"A process in a package other than '{packageName}' needs a dependency on it "
			+ "(add-package-dependency) before that process package is exported.");
		return notices;
	}

	private static string Text(JsonObject row, string columnPath) =>
		row[WebServiceDesignerClient.ColumnAlias(columnPath)] switch {
			null => null,
			JsonValue value => value.ToString(),
			JsonObject lookup => lookup["displayValue"]?.ToString(),
			var other => other.ToJsonString()
		};
}
