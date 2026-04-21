namespace Clio.Command;

using System;
using System.Collections.Generic;
using Clio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal sealed class PageDesignerHierarchyClient : IPageDesignerHierarchyClient {
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	public PageDesignerHierarchyClient(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	public string GetDesignPackageUId(string schemaUId) {
		var request = new JObject {
			["schemaUId"] = schemaUId,
			["userLevelSchema"] = false
		};
		string url = _serviceUrlBuilder.Build("/ServiceModel/ApplicationPackagesService.svc/GetDesignPackageUId");
		string responseJson = _applicationClient.ExecutePostRequest(url, request.ToString(Formatting.None));
		var response = JObject.Parse(responseJson);
		if (!(response["success"]?.Value<bool>() ?? false)) {
			string errorDetail = response["errorInfo"]?.ToString()
				?? response["message"]?.ToString()
				?? response["error"]?.ToString();
			string message = string.IsNullOrWhiteSpace(errorDetail)
				? "Failed to resolve design package"
				: $"Failed to resolve design package: {errorDetail}";
			throw new InvalidOperationException(message);
		}
		string uId = response["uId"]?.ToString();
		if (string.IsNullOrWhiteSpace(uId)) {
			throw new InvalidOperationException("Design package response did not return a uId");
		}
		return uId;
	}

	public IReadOnlyList<PageDesignerHierarchySchema> GetParentSchemas(string schemaUId, string packageUId) {
		var request = new JObject {
			["schemaUId"] = schemaUId,
			["packageUId"] = packageUId,
			["useFullHierarchy"] = true,
			["userLevelSchema"] = false
		};
		string designerUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetParentSchemas");
		string responseJson = _applicationClient.ExecutePostRequest(designerUrl, request.ToString(Formatting.None));
		var response = JObject.Parse(responseJson);
		if (!(response["success"]?.Value<bool>() ?? false)) {
			string errorDetail = response["errorInfo"]?.ToString()
				?? response["message"]?.ToString()
				?? response["error"]?.ToString();
			string message = string.IsNullOrWhiteSpace(errorDetail)
				? "Failed to load page schema hierarchy"
				: $"Failed to load page schema hierarchy: {errorDetail}";
			throw new InvalidOperationException(message);
		}

		if (response["values"] is not JArray values) {
			throw new InvalidOperationException("Page schema hierarchy response does not contain values");
		}

		var result = new List<PageDesignerHierarchySchema>(values.Count);
		foreach (JObject value in values.Children<JObject>()) {
			string body = value["body"]?.ToString();
			if (string.IsNullOrWhiteSpace(body)) {
				continue;
			}

			JToken package = value["package"];
			result.Add(new PageDesignerHierarchySchema {
				UId = value["uId"]?.ToString() ?? value["id"]?.ToString(),
				Name = value["name"]?.ToString(),
				PackageUId = package?["uId"]?.ToString() ?? value["packageUId"]?.ToString(),
				PackageName = package?["name"]?.ToString() ?? value["packageName"]?.ToString(),
				SchemaVersion = value["schemaVersion"]?.Value<int>() ?? 0,
				Body = body,
				Parameters = value["parameters"] as JArray ?? new JArray(),
				LocalizableStrings = value["localizableStrings"] as JArray ?? new JArray(),
				OptionalProperties = value["optionalProperties"] as JArray ?? new JArray()
			});
		}

		return result;
	}
}
