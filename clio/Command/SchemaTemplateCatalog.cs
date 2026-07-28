namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json.Linq;

/// <summary>
/// Provides access to the live Freedom UI page template catalog.
/// </summary>
public interface ISchemaTemplateCatalog {
	IReadOnlyList<PageTemplateInfo> GetTemplates(PageSchemaType? schemaType = null);
	PageTemplateInfo FindTemplate(string templateNameOrUId);
}

/// <inheritdoc cref="ISchemaTemplateCatalog" />
public sealed class SchemaTemplateCatalog : ISchemaTemplateCatalog {
	/// <summary>
	/// Name of the base Freedom UI dashboard page template.
	/// </summary>
	internal const string DashboardTemplateName = "BaseDashboardTemplate";

	/// <summary>
	/// UId of the base <c>BaseDashboardTemplate</c> schema (owned by the <c>CrtUIPlatform</c> base
	/// package, so the GUID is a fixed platform constant across environments).
	/// </summary>
	internal const string DashboardTemplateUId = "eb4d4a67-25d8-fcfa-7851-c4c91efb7b9c";

	/// <summary>
	/// Name of the platform desktop page template (parent for desktop pages).
	/// </summary>
	internal const string DesktopTemplateName = "CentralAreaDesktopTemplate";

	/// <summary>
	/// UId of the <c>CentralAreaDesktopTemplate</c> schema (owned by the <c>CrtUIPlatform</c> base
	/// package, so the GUID is a fixed platform constant across environments).
	/// </summary>
	internal const string DesktopTemplateUId = "fbc98c89-0691-479c-bc25-59c11ac2365f";

	/// <summary>
	/// Schema group that marks a client-unit schema as a desktop. The platform
	/// <c>DesktopAppEventListener</c> (CrtUIv2) keys the automatic <c>Desktop</c> entity
	/// registration off this group (Angular schema + group <c>Desktop</c>), NOT off the parent
	/// template — so the group value is the invariant that must never drift.
	/// </summary>
	internal const string DesktopGroupName = "Desktop";

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly Dictionary<int, IReadOnlyList<PageTemplateInfo>> _cache = new();
	private readonly object _sync = new();

	public SchemaTemplateCatalog(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	public IReadOnlyList<PageTemplateInfo> GetTemplates(PageSchemaType? schemaType = null) {
		if (schemaType.HasValue) {
			return LoadForSchemaType((int)schemaType.Value);
		}
		var combined = new List<PageTemplateInfo>();
		combined.AddRange(LoadForSchemaType((int)PageSchemaType.Web));
		combined.AddRange(LoadForSchemaType((int)PageSchemaType.Mobile));
		return combined;
	}

	public PageTemplateInfo FindTemplate(string templateNameOrUId) {
		if (string.IsNullOrWhiteSpace(templateNameOrUId)) {
			return null;
		}
		IReadOnlyList<PageTemplateInfo> templates = GetTemplates();
		return templates.FirstOrDefault(t =>
				string.Equals(t.Name, templateNameOrUId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(t.UId, templateNameOrUId, StringComparison.OrdinalIgnoreCase));
	}

	private IReadOnlyList<PageTemplateInfo> LoadForSchemaType(int schemaType) {
		lock (_sync) {
			if (_cache.TryGetValue(schemaType, out IReadOnlyList<PageTemplateInfo> cached)) {
				return cached;
			}
			string url = _serviceUrlBuilder.Build($"/rest/schema.template.api/templates?schemaType={schemaType}");
			string responseJson = _applicationClient.ExecuteGetRequest(url);
			JObject response;
			try {
				response = JObject.Parse(responseJson);
			} catch (Newtonsoft.Json.JsonReaderException ex) {
				throw new InvalidOperationException(BuildNonJsonErrorMessage(url, responseJson, ex));
			}
			if (!(response["success"]?.Value<bool>() ?? false)) {
				string detail = response["errorInfo"]?["message"]?.ToString()
					?? response["errorInfo"]?.ToString()
					?? "Failed to load page template catalog";
				throw new InvalidOperationException(detail);
			}
			if (response["items"] is not JArray items) {
				_cache[schemaType] = [];
				return _cache[schemaType];
			}
			List<PageTemplateInfo> result = [];
			foreach (JObject item in items.Children<JObject>()) {
				result.Add(new PageTemplateInfo {
					UId = item["uId"]?.ToString(),
					Name = item["name"]?.ToString(),
					Title = item["title"]?.ToString(),
					GroupName = item["groupName"]?.ToString(),
					SchemaType = schemaType
				});
			}
			AddDashboardTemplateIfMissing(schemaType, result);
			AddDesktopTemplateIfMissing(schemaType, result);
			_cache[schemaType] = result;
			return result;
		}
	}

	/// <summary>
	/// Appends the base <c>BaseDashboardTemplate</c> to the web catalog when the platform template
	/// endpoint (<c>schema.template.api/templates</c>) does not advertise it. The endpoint returns a
	/// curated subset that omits the dashboard template, yet it is the correct parent for a Freedom UI
	/// dashboard page — so clio injects it here (deduped by name) so it is reachable through both
	/// <c>list-page-templates</c> and <c>create-page</c>. Mobile catalogs are left untouched.
	/// </summary>
	private static void AddDashboardTemplateIfMissing(int schemaType, List<PageTemplateInfo> result) {
		if (schemaType != (int)PageSchemaType.Web) {
			return;
		}
		if (result.Any(t => string.Equals(t.Name, DashboardTemplateName, StringComparison.OrdinalIgnoreCase))) {
			return;
		}
		result.Add(new PageTemplateInfo {
			UId = DashboardTemplateUId,
			Name = DashboardTemplateName,
			Title = "Dashboard",
			// create-page stamps the new schema's group from this GroupName, and the platform's
			// SysFreedomDashboardQueryExecutor lists a dashboard in a crt.Dashboards element ONLY when the
			// schema group is exactly "DashboardPage" (plus matching DashboardsElementName /
			// DashboardsClientUnitSchemaUId optional properties). Any other group hides the dashboard.
			GroupName = "DashboardPage",
			SchemaType = (int)PageSchemaType.Web
		});
	}

	/// <summary>
	/// Ensures the web catalog exposes <c>CentralAreaDesktopTemplate</c> with <c>GroupName = "Desktop"</c>
	/// so create-page stamps the correct group on a new desktop schema. The platform
	/// <c>DesktopAppEventListener</c> registers a schema in the <c>Desktop</c> entity (the selector source)
	/// only when its group is exactly <c>Desktop</c>. Verified live: <c>schema.template.api/templates</c>
	/// omits this template, so clio injects it.
	/// <para>
	/// Unlike the dashboard injection, this method also CORRECTS the group when the endpoint does
	/// advertise the template: the template's own group is <c>DesktopTemplate</c> (not <c>Desktop</c>),
	/// and stamping that inherited group would create a page that never appears in the selector — the
	/// exact silent failure this feature exists to prevent. The desktop catalog entry must therefore
	/// always carry <c>GroupName = "Desktop"</c>, regardless of source. Mobile catalogs are untouched.
	/// </para>
	/// </summary>
	private static void AddDesktopTemplateIfMissing(int schemaType, List<PageTemplateInfo> result) {
		if (schemaType != (int)PageSchemaType.Web) {
			return;
		}
		PageTemplateInfo existing = result.FirstOrDefault(
			t => string.Equals(t.Name, DesktopTemplateName, StringComparison.OrdinalIgnoreCase));
		if (existing != null) {
			// The endpoint advertised the template; force the desktop group so create-page stamps
			// the group that actually registers the schema in the selector.
			existing.GroupName = DesktopGroupName;
			return;
		}
		result.Add(new PageTemplateInfo {
			UId = DesktopTemplateUId,
			Name = DesktopTemplateName,
			Title = "Desktop",
			GroupName = DesktopGroupName,
			SchemaType = (int)PageSchemaType.Web
		});
	}

	private static string BuildNonJsonErrorMessage(string url, string responseBody, Exception parseException) {
		string trimmed = (responseBody ?? string.Empty).TrimStart();
		bool looksLikeHtml = trimmed.StartsWith("<", StringComparison.Ordinal);
		if (looksLikeHtml) {
			return $"Page template catalog endpoint did not return JSON (URL: {url}). "
				+ "The server returned an HTML response — likely because the request was redirected to a login page "
				+ "or the endpoint is missing on this Creatio version. "
				+ "Verify that: 1) the environment is registered with correct credentials (reg-web-app); "
				+ "2) `IsNetCore` flag matches the target instance (omit for .NET Framework, add --IsNetCore for .NET Core); "
				+ "3) the target Creatio version exposes `/rest/schema.template.api/templates` (Creatio 7.18+).";
		}
		string snippet;
		if (string.IsNullOrWhiteSpace(responseBody)) {
			snippet = "<empty body>";
		} else if (responseBody.Length > 200) {
			snippet = responseBody[..200] + "…";
		} else {
			snippet = responseBody;
		}
		return $"Page template catalog endpoint returned an unparseable response (URL: {url}). "
			+ $"Parser error: {parseException.Message}. Response preview: {snippet}";
	}
}
