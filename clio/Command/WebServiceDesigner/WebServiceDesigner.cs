using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.WebServiceDesigner;

/// <summary>Registers, reads and changes REST web services the way Studio > Web services does.</summary>
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
	/// Creates a REST service. Every rule Studio enforces on save is checked first and all problems are reported
	/// in one refusal; nothing is saved unless everything passes.
	/// </summary>
	/// <param name="packageName">The package that will own the service.</param>
	/// <param name="code">The service code; must carry the schema-name prefix and be unused.</param>
	/// <param name="caption">The caption; the code when empty.</param>
	/// <param name="baseUri">An absolute http(s) URI without user information.</param>
	/// <param name="retryCount">How many times the platform retries a failed call.</param>
	/// <param name="auth">The authentication, by system-setting codes or an OAuth application only.</param>
	/// <param name="methods">At least one method.</param>
	/// <returns>The saved service as the platform stores it, with notices for the user.</returns>
	WebServiceToolResult Register(string packageName, string code, string caption, string baseUri, int? retryCount,
		WebServiceAuthArg auth, IReadOnlyList<WebServiceMethodArg> methods);

	/// <summary>
	/// Applies operations to a REST service in order, all or nothing. Changing or removing a method a process
	/// calls is refused unless <paramref name="acknowledgedProcesses"/> names exactly those processes. A method
	/// sent without use-auth follows the service authentication as it is after the whole call.
	/// </summary>
	/// <param name="code">The service code.</param>
	/// <param name="operations">addMethod, setMethod, removeMethod, setBaseUri, setRetryCount or setAuth.</param>
	/// <param name="acknowledgedProcesses">Process codes the user agreed to affect.</param>
	/// <returns>The saved service, or a refusal listing the affected processes.</returns>
	WebServiceToolResult Modify(string code, IReadOnlyList<WebServiceOperationArg> operations,
		IReadOnlyList<string> acknowledgedProcesses);

	/// <summary>
	/// Sends one real request through the platform, as Studio's Send test request does. Values sent from system
	/// settings, the Authorization header and secret constant defaults are masked in the returned body and error.
	/// </summary>
	/// <param name="code">The service code.</param>
	/// <param name="methodCode">The method code.</param>
	/// <param name="values">Values for top-level request parameters, by parameter code.</param>
	/// <returns>The status code and the masked, length-limited response body or error.</returns>
	WebServiceTestResult TestMethod(string code, string methodCode, IReadOnlyDictionary<string, string> values);
}

public sealed class WebServiceDesigner(
	IWebServiceDesignerClient client,
	ICreatioRightsClient rights,
	ISchemaNamePrefixResolver prefixResolver) : IWebServiceDesigner {

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
		return new WebServiceToolResult(true, Service: WebServiceSchemaView.From(schema, row.Package), Usages: usages);
	}

	public WebServiceToolResult Register(string packageName, string code, string caption, string baseUri,
		int? retryCount, WebServiceAuthArg auth, IReadOnlyList<WebServiceMethodArg> methods) {
		RequireManageSolution();
		var errors = new WebServiceDefinitionBuilder.Errors();
		string prefix = prefixResolver.Resolve(null);
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
			BaseUri = WebServiceDefinitionBuilder.NormalizeBaseUri(baseUri, errors),
			Type = ServiceSchemaCodes.ServiceTypeRest,
			RetryCount = Math.Max(retryCount ?? 0, 0),
			AuthInfo = WebServiceDefinitionBuilder.BuildAuth(auth, FindOAuthApp(auth), errors)
		};
		schema.UId = schema.RealUId = Guid.NewGuid();
		schema.PackageUId = packageUId ?? Guid.Empty;
		if (methods is not { Count: > 0 }) {
			errors.Add("methods: register at least one method.");
		}
		foreach (WebServiceMethodArg method in methods ?? []) {
			AddMethod(schema, method, prefix, HasAuth(schema), errors);
		}
		IReadOnlyCollection<string> settings = RequireSysSettings(schema, errors);
		errors.ThrowIfAny();
		client.SaveSchema(schema);
		ServiceSchemaDto saved = client.ReadSchema(schema.UId, schema.PackageUId);
		return new WebServiceToolResult(true, Service: WebServiceSchemaView.From(saved, packageName),
			Notices: Notices(settings, packageName));
	}

	public WebServiceToolResult Modify(string code, IReadOnlyList<WebServiceOperationArg> operations,
		IReadOnlyList<string> acknowledgedProcesses) {
		RequireManageSolution();
		if (operations is not { Count: > 0 }) {
			throw new ArgumentException("operations: give at least one operation.");
		}
		ServiceRow row = FindService(code);
		ServiceSchemaDto schema = client.ReadSchema(row.UId, row.PackageUId);
		if (schema.Type != ServiceSchemaCodes.ServiceTypeRest) {
			throw new ArgumentException($"'{schema.Name}' is a SOAP service; change it in Studio > Web services.");
		}
		var errors = new WebServiceDefinitionBuilder.Errors();
		var changedMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// Only a method definition carries codes the prefix rule applies to; skip the read otherwise.
		string prefix = operations.Any(o => o?.Op is "addMethod" or "setMethod") ? prefixResolver.Resolve(null) : null;
		// A method sent without use-auth follows the service's authentication as it is after this call, so
		// [addMethod, setAuth] gives the new method authentication just like [setAuth, addMethod] does.
		WebServiceOperationArg lastSetAuth = operations.LastOrDefault(o => o?.Op == "setAuth");
		bool methodsUseAuth = lastSetAuth is null
			? HasAuth(schema)
			: ServiceSchemaCodes.AuthTypes.TryGetValue(lastSetAuth.Auth?.Type ?? string.Empty, out int authType)
				&& authType != ServiceSchemaCodes.AuthNone;
		foreach (WebServiceOperationArg operation in operations) {
			Apply(schema, operation, prefix, methodsUseAuth, changedMethods, errors);
		}
		IReadOnlyCollection<string> settings = RequireSysSettings(schema, errors);
		errors.ThrowIfAny();
		IReadOnlyList<WebServiceUsage> affected = changedMethods.Count == 0
			? []
			: FindUsages(schema.UId).Where(u => u.Method is null || changedMethods.Contains(u.Method)).ToList();
		var acknowledged = new HashSet<string>(acknowledgedProcesses ?? [], StringComparer.OrdinalIgnoreCase);
		List<WebServiceUsage> unacknowledged = affected.Where(u => !acknowledged.Contains(u.Process)).ToList();
		if (unacknowledged.Count > 0) {
			return new WebServiceToolResult(false,
				"Refused: the changed or removed methods are called by processes that would break silently (their "
				+ "elements lose their parameters on the next designer load and fail at run time). Show these "
				+ "processes to the user and, only on their yes, repeat the call with acknowledge-usages naming them.",
				Usages: unacknowledged);
		}
		client.SaveSchema(schema);
		ServiceSchemaDto saved = client.ReadSchema(schema.UId, row.PackageUId);
		return new WebServiceToolResult(true, Service: WebServiceSchemaView.From(saved, row.Package),
			Usages: affected, Notices: Notices(settings, row.Package));
	}

	public WebServiceTestResult TestMethod(string code, string methodCode, IReadOnlyDictionary<string, string> values) {
		RequireManageSolution();
		ServiceRow row = FindService(code);
		ServiceSchemaDto schema = client.ReadSchema(row.UId, row.PackageUId);
		ServiceMethodDto method = FindMethod(schema, methodCode)
			?? throw new ArgumentException(
				$"method '{methodCode}' not found; methods: {string.Join(", ", schema.Methods.Items.Values.Select(m => m.Name))}.");
		ServiceMethodCallResult result = client.CallMethod(schema.Name, method.Name, values);
		if (WebServiceResponseMasker.HasUnlocatedPathSecret(method, result.RequestRawData)) {
			// The value is unknown, so it cannot be masked: fail closed and return the status only.
			return new WebServiceTestResult(result.Success,
				"The response is withheld: a path parameter filled from a system setting could not be located in "
				+ "the sent URL, so it could not be masked.", result.StatusCode, null, false, true);
		}
		IReadOnlyList<string> secrets =
			WebServiceResponseMasker.SentSecrets(method, result.RequestRawData, result.RequestBody);
		// Masked before truncation, so a value cut at the boundary cannot survive half-visible.
		string body = WebServiceResponseMasker.ApplyToBody(result.ResponseBody, secrets, out bool bodyMasked);
		string error = WebServiceResponseMasker.Apply(result.Error, secrets, out bool errorMasked);
		bool truncated = body is { Length: > MaxResponseBodyLength };
		return new WebServiceTestResult(result.Success,
			error is null ? null : SensitiveErrorTextRedactor.Redact(error),
			result.StatusCode, truncated ? body[..MaxResponseBodyLength] : body, truncated,
			bodyMasked || errorMasked);
	}

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
	/// Fails closed: a process whose metadata names the service but cannot be parsed is reported as one usage
	/// with no element and no method, which the guard treats as calling every method. Metadata saved by the
	/// platform may start with a byte-order mark, which <c>JsonNode.Parse</c> rejects.
	/// </remarks>
	internal static IEnumerable<(string Element, string Method)> FindElements(string metadata, string uid) {
		JsonNode root;
		try {
			root = JsonNode.Parse(metadata.TrimStart('\uFEFF'));
		} catch (JsonException) {
			root = null;
		}
		if (root is null) {
			yield return (null, null);
			yield break;
		}
		var pending = new Stack<JsonNode>();
		pending.Push(root);
		while (pending.Count > 0) {
			JsonNode current = pending.Pop();
			if (current is JsonObject node && IsElementOf(node, uid)) {
				yield return (node["A2"]?.ToString(), node["IX2"]?.ToString());
			}
			foreach (JsonNode child in Children(current)) {
				pending.Push(child);
			}
		}
	}

	private static bool IsElementOf(JsonObject node, string uid) =>
		node["IX1"] is JsonValue ix1 && string.Equals(ix1.ToString(), uid, StringComparison.OrdinalIgnoreCase);

	private static IEnumerable<JsonNode> Children(JsonNode node) => node switch {
		JsonObject obj => obj.Select(child => child.Value).Where(child => child is not null),
		JsonArray array => array.Where(item => item is not null),
		_ => []
	};

	private void Apply(ServiceSchemaDto schema, WebServiceOperationArg operation, string prefix, bool methodsUseAuth,
		ISet<string> changedMethods, WebServiceDefinitionBuilder.Errors errors) {
		WebServiceDefinitionBuilder.RejectUnknownFields(operation?.ExtensionData, $"op '{operation?.Op}'", errors);
		switch (operation?.Op) {
			case "addMethod":
				AddMethod(schema, operation.Method, prefix, methodsUseAuth, errors);
				break;
			case "setMethod":
				ServiceMethodDto existing = FindMethod(schema, operation.Method?.Code);
				if (existing is null) {
					errors.Add($"setMethod: method '{operation.Method?.Code}' does not exist; use addMethod.");
					break;
				}
				ServiceMethodDto replacement = WebServiceDefinitionBuilder.BuildMethod(operation.Method, existing,
					methodsUseAuth, errors, prefix);
				if (replacement is not null) {
					schema.Methods.Items.Remove(existing.UId.ToString());
					schema.Methods.Items[replacement.UId.ToString()] = replacement;
					changedMethods.Add(existing.Name);
				}
				break;
			case "removeMethod":
				ServiceMethodDto removed = FindMethod(schema, operation.MethodCode);
				if (removed is null) {
					errors.Add($"removeMethod: method '{operation.MethodCode}' does not exist.");
					break;
				}
				schema.Methods.Items.Remove(removed.UId.ToString());
				changedMethods.Add(removed.Name);
				break;
			case "setBaseUri":
				schema.BaseUri = WebServiceDefinitionBuilder.NormalizeBaseUri(operation.BaseUri, errors);
				break;
			case "setRetryCount":
				schema.RetryCount = Math.Max(operation.RetryCount ?? 0, 0);
				break;
			case "setAuth":
				schema.AuthInfo = WebServiceDefinitionBuilder.BuildAuth(operation.Auth, FindOAuthApp(operation.Auth), errors);
				break;
			default:
				errors.Add($"op '{operation?.Op}' must be addMethod, setMethod, removeMethod, setBaseUri, setRetryCount or setAuth.");
				break;
		}
	}

	private static void AddMethod(ServiceSchemaDto schema, WebServiceMethodArg arg, string prefix, bool useAuth,
		WebServiceDefinitionBuilder.Errors errors) {
		if (FindMethod(schema, arg?.Code) is not null) {
			errors.Add($"method code '{arg?.Code}' is used twice.");
			return;
		}
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(arg, null, useAuth, errors, prefix);
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
				return null; // the caller refuses it as an unknown application
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

	private IReadOnlyCollection<string> RequireSysSettings(ServiceSchemaDto schema,
		WebServiceDefinitionBuilder.Errors errors) {
		IReadOnlyCollection<string> codes = WebServiceDefinitionBuilder.CollectSysSettingCodes(schema);
		foreach (string code in codes) {
			bool exists = client.Select("SysSettings", ["Code"], new Dictionary<string, string> { ["Code"] = code })
				.Count > 0;
			if (!exists) {
				// A default bound to a missing setting is dropped from the request without any error at
				// run time (measured on 10.2.565), so the reference is refused here instead.
				errors.Add($"system setting '{code}' does not exist; create it with create-sys-setting and no value "
					+ "(value-type-name SecureText for a key, token or password), then ask the user to fill it. "
					+ "The agent never sets its value.");
			}
		}
		return codes;
	}

	private static IReadOnlyList<string> Notices(IReadOnlyCollection<string> settings, string packageName) {
		var notices = new List<string>();
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
