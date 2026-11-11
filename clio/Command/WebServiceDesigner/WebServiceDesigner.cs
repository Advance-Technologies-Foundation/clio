using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.WebServiceDesigner;

/// <summary>Registers, reads and changes REST web services the way Studio > Web services does.</summary>
public interface IWebServiceDesigner {
	IReadOnlyList<WebServiceListItem> List();
	WebServiceToolResult Describe(string code, bool includeUsages);
	WebServiceToolResult Register(string packageName, string code, string caption, string baseUri, int? retryCount,
		WebServiceAuthArg auth, IReadOnlyList<WebServiceMethodArg> methods);
	WebServiceToolResult Modify(string code, IReadOnlyList<WebServiceOperationArg> operations,
		IReadOnlyList<string> acknowledgedProcesses);
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
			AddMethod(schema, method, prefix, errors);
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
		foreach (WebServiceOperationArg operation in operations) {
			Apply(schema, operation, prefix, changedMethods, errors);
		}
		IReadOnlyCollection<string> settings = RequireSysSettings(schema, errors);
		errors.ThrowIfAny();
		IReadOnlyList<WebServiceUsage> affected = changedMethods.Count == 0
			? []
			: FindUsages(schema.UId).Where(u => changedMethods.Contains(u.Method ?? string.Empty)).ToList();
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
		IReadOnlyList<string> secrets =
			WebServiceResponseMasker.SentSecrets(method, result.RequestRawData, result.RequestBody);
		// Masked before truncation, so a value cut at the boundary cannot survive half-visible.
		string body = WebServiceResponseMasker.Apply(result.ResponseBody, secrets, out bool bodyMasked);
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

	internal static IEnumerable<(string Element, string Method)> FindElements(string metadata, string uid) {
		JsonNode root;
		try {
			root = JsonNode.Parse(metadata);
		} catch (JsonException) {
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

	private void Apply(ServiceSchemaDto schema, WebServiceOperationArg operation, string prefix,
		ISet<string> changedMethods, WebServiceDefinitionBuilder.Errors errors) {
		WebServiceDefinitionBuilder.RejectUnknownFields(operation?.ExtensionData, $"op '{operation?.Op}'", errors);
		switch (operation?.Op) {
			case "addMethod":
				AddMethod(schema, operation.Method, prefix, errors);
				break;
			case "setMethod":
				ServiceMethodDto existing = FindMethod(schema, operation.Method?.Code);
				if (existing is null) {
					errors.Add($"setMethod: method '{operation.Method?.Code}' does not exist; use addMethod.");
					break;
				}
				ServiceMethodDto replacement = WebServiceDefinitionBuilder.BuildMethod(operation.Method, existing,
					HasAuth(schema), errors, prefix);
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

	private static void AddMethod(ServiceSchemaDto schema, WebServiceMethodArg arg, string prefix,
		WebServiceDefinitionBuilder.Errors errors) {
		if (FindMethod(schema, arg?.Code) is not null) {
			errors.Add($"method code '{arg?.Code}' is used twice.");
			return;
		}
		ServiceMethodDto method = WebServiceDefinitionBuilder.BuildMethod(arg, null, HasAuth(schema), errors, prefix);
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
			|| string.IsNullOrWhiteSpace(auth.OAuthApp)) {
			return null;
		}
		JsonObject row = client.Select("OAuthApplications", ["Id"],
			new Dictionary<string, string> { ["Name"] = auth.OAuthApp }).FirstOrDefault();
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
