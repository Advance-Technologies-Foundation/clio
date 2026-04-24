namespace Clio.Command;

using System.Linq;
using Clio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal sealed record SchemaDesignerKind(
	string ManagerName,
	string ServiceName,
	string GetRoute,
	string SaveRoute,
	string CreateRoute = null) {

	internal static readonly SchemaDesignerKind SourceCode = new(
		"SourceCodeSchemaManager",
		"SourceCodeSchemaDesignerService",
		"ServiceModel/SourceCodeSchemaDesignerService.svc/GetSchema",
		"ServiceModel/SourceCodeSchemaDesignerService.svc/SaveSchema",
		"ServiceModel/SourceCodeSchemaDesignerService.svc/CreateNewSchema");

	internal static readonly SchemaDesignerKind SqlScript = new(
		"ScriptSchemaManager",
		"ScriptSchemaDesignerService",
		"ServiceModel/ScriptSchemaDesignerService.svc/GetSchema",
		"ServiceModel/ScriptSchemaDesignerService.svc/SaveSchema",
		"ServiceModel/ScriptSchemaDesignerService.svc/CreateNewSchema");

	internal static readonly SchemaDesignerKind ClientUnit = new(
		"ClientUnitSchemaManager",
		"ClientUnitSchemaDesignerService",
		"/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema",
		"/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema",
		"/ServiceModel/ClientUnitSchemaDesignerService.svc/CreateNewSchema");
}

internal static class SchemaDesignerHelper {

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
	private const string ValueKey = "value";
	private const string ExpressionTypeKey = "expressionType";

	internal static string ValidateCreateInput(string schemaName, string packageName) {
		if (string.IsNullOrWhiteSpace(schemaName))
			return "schema-name is required";
		if (!PageSchemaMetadataHelper.IsValidSchemaName(schemaName))
			return "schema-name must start with a letter and contain only letters, digits, or underscores";
		if (string.IsNullOrWhiteSpace(packageName))
			return "package-name is required";
		return null;
	}

	internal static (string uId, string error) ResolveSchemaUId(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string schemaName,
		SchemaDesignerKind kind) {
		var query = BuildSelectUIdByName(schemaName, kind.ManagerName);
		string url = urlBuilder.Build(SelectQueryRoute);
		string responseJson = client.ExecutePostRequest(url, query.ToString(Formatting.None));
		JObject selectResponse = JObject.Parse(responseJson);
		var rows = selectResponse["rows"] as JArray ?? [];
		if (rows.Count == 0)
			return (null, $"Schema '{schemaName}' not found (ManagerName='{kind.ManagerName}')");
		string uId = rows[0]["UId"]?.ToString();
		if (string.IsNullOrWhiteSpace(uId))
			return (null, $"Schema '{schemaName}' metadata is missing UId");
		return (uId, null);
	}

	internal static bool SchemaNameExists(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string schemaName,
		SchemaDesignerKind kind) {
		(string uId, _) = ResolveSchemaUId(client, urlBuilder, schemaName, kind);
		return uId != null;
	}

	internal static (JObject schema, string error) LoadSchema(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string schemaUId,
		SchemaDesignerKind kind,
		string schemaName = null) {
		var request = new JObject {
			["schemaUId"] = schemaUId,
			["useFullHierarchy"] = false
		};
		string designerUrl = urlBuilder.Build(kind.GetRoute);
		string json = client.ExecutePostRequest(designerUrl, request.ToString(Formatting.None));
		JObject response = JObject.Parse(json);
		if (response["schema"] is not JObject loaded) {
			string label = schemaName ?? schemaUId;
			return (null, $"Failed to load schema '{label}' via {kind.ServiceName}");
		}
		return (loaded, null);
	}

	internal static string SaveSchema(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		JObject schema,
		SchemaDesignerKind kind) {
		string saveUrl = urlBuilder.Build(kind.SaveRoute);
		string json = client.ExecutePostRequest(saveUrl, schema.ToString(Formatting.None));
		JObject response = JObject.Parse(json);
		if (response["success"]?.Value<bool>() ?? false)
			return null;
		return PageSchemaMetadataHelper.ParseSaveErrorMessage(response, "Failed to save schema");
	}

	internal static (JObject schema, string error) CreateNewSchema(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string packageUId,
		SchemaDesignerKind kind) {
		string createUrl = urlBuilder.Build(kind.CreateRoute);
		var request = new JObject { ["packageUId"] = packageUId };
		string json = client.ExecutePostRequest(createUrl, request.ToString(Formatting.None));
		JObject response = JObject.Parse(json);
		if (!(response["success"]?.Value<bool>() ?? false))
			return (null, response["errorInfo"]?["message"]?.ToString() ?? "CreateNewSchema failed");
		if (response["schema"] is not JObject created)
			return (null, "CreateNewSchema did not return a schema payload.");
		return (created, null);
	}

	internal static void ApplySchemaMetadata(
		JObject schema, string name, string caption, string description) {
		schema["name"] = name;
		schema["caption"] = new JArray(new JObject { ["cultureName"] = "en-US", [ValueKey] = caption });
		if (!string.IsNullOrWhiteSpace(description))
			schema["description"] = new JArray(
				new JObject { ["cultureName"] = "en-US", [ValueKey] = description });
	}

	internal static string ExtractCaption(JObject schema) {
		if (schema["caption"] is JArray captions && captions.Count > 0)
			return captions[0]?[ValueKey]?.ToString();
		return schema["caption"]?.ToString();
	}

	internal static (string body, string error) ResolveBody(string body, string bodyFile) {
		if (!string.IsNullOrWhiteSpace(bodyFile)) {
			if (!System.IO.File.Exists(bodyFile))
				return (null, $"body-file not found: '{bodyFile}'");
			body = System.IO.File.ReadAllText(bodyFile);
		}
		if (string.IsNullOrWhiteSpace(body))
			return (null, "body (or body-file) is required and must not be empty");
		return (body, null);
	}

	private static JObject BuildSelectUIdByName(string schemaName, string managerName) {
		return new JObject {
			["rootSchemaName"] = "SysSchema",
			["operationType"] = 0,
			["columns"] = new JObject {
				["items"] = new JObject {
					["UId"] = new JObject {
						["expression"] = new JObject { [ExpressionTypeKey] = 0, ["columnPath"] = "UId" }
					}
				}
			},
			["filters"] = new JObject {
				["filterType"] = 6,
				["logicalOperation"] = 0,
				["isEnabled"] = true,
				["items"] = new JObject {
					["byName"] = new JObject {
						["filterType"] = 1,
						["comparisonType"] = 3,
						["isEnabled"] = true,
						["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, ["columnPath"] = "Name" },
						["rightExpression"] = new JObject {
							[ExpressionTypeKey] = 2,
							["parameter"] = new JObject { ["dataValueType"] = 1, [ValueKey] = schemaName }
						}
					},
					["byManager"] = new JObject {
						["filterType"] = 1,
						["comparisonType"] = 3,
						["isEnabled"] = true,
						["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, ["columnPath"] = "ManagerName" },
						["rightExpression"] = new JObject {
							[ExpressionTypeKey] = 2,
							["parameter"] = new JObject { ["dataValueType"] = 1, [ValueKey] = managerName }
						}
					}
				}
			},
			["rowCount"] = 1
		};
	}
}
