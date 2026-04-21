namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json.Linq;

/// <summary>
/// Freedom UI schema types exposed by <c>schema.template.api/templates</c>.
/// </summary>
public enum PageSchemaType {
	FreedomUIPage = 9,
	MobilePage = 10
}

/// <summary>
/// Provides access to the live Freedom UI page template catalog.
/// </summary>
public interface ISchemaTemplateCatalog {
	IReadOnlyList<PageTemplateInfo> GetTemplates(PageSchemaType? schemaType = null);
	PageTemplateInfo FindTemplate(string templateNameOrUId);
}

/// <inheritdoc cref="ISchemaTemplateCatalog" />
public sealed class SchemaTemplateCatalog : ISchemaTemplateCatalog {
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
		combined.AddRange(LoadForSchemaType((int)PageSchemaType.FreedomUIPage));
		combined.AddRange(LoadForSchemaType((int)PageSchemaType.MobilePage));
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
			_cache[schemaType] = result;
			return result;
		}
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
		string snippet = string.IsNullOrWhiteSpace(responseBody)
			? "<empty body>"
			: responseBody.Length > 200 ? responseBody[..200] + "…" : responseBody;
		return $"Page template catalog endpoint returned an unparseable response (URL: {url}). "
			+ $"Parser error: {parseException.Message}. Response preview: {snippet}";
	}
}
