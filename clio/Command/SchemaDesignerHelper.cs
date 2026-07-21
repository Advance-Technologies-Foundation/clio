namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
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

/// <summary>
/// One layer of a (possibly multi-package) schema chain: a single <c>SysSchema</c> row for a schema name,
/// carrying the owning package and its hierarchy level. Layers are ordered base-&gt;top by hierarchy level.
/// </summary>
internal sealed record SchemaLayer(string UId, string Name, string PackageName, int HierarchyLevel);

/// <summary>One culture value of a merged localizable string.</summary>
public sealed record MergedLocalizableStringValue(
	[property: System.Text.Json.Serialization.JsonPropertyName("cultureName")] string CultureName,
	[property: System.Text.Json.Serialization.JsonPropertyName("value")] string Value);

/// <summary>
/// A localizable string from the full-hierarchy-merged schema, carrying the schema that contributed it
/// (<c>parentSchemaUId</c> provenance) and its per-culture values. This is the honest content a
/// <c>--full-hierarchy</c> read delivers (the merge folds localization + metadata, not the view body).
/// </summary>
public sealed record MergedLocalizableString(
	[property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
	[property: System.Text.Json.Serialization.JsonPropertyName("parentSchemaUId")] string ParentSchemaUId,
	[property: System.Text.Json.Serialization.JsonPropertyName("uId")] string UId,
	[property: System.Text.Json.Serialization.JsonPropertyName("values")] IReadOnlyList<MergedLocalizableStringValue> Values);

internal static class SchemaDesignerHelper {

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
	private const string ValueKey = "value";
	private const string ExpressionTypeKey = "expressionType";

	internal static string ValidateCreateInput(string schemaName, string packageName) {
		if (string.IsNullOrWhiteSpace(schemaName))
			return "schema-name is required";
		if (!PageSchemaMetadataHelper.IsValidSchemaName(schemaName))
			return PageSchemaMetadataHelper.SchemaNameFormatError;
		if (string.IsNullOrWhiteSpace(packageName))
			return "package-name is required";
		return null;
	}

	internal static (string uId, string error) ResolveSchemaUId(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string schemaName,
		SchemaDesignerKind kind) {
		(IReadOnlyList<SchemaLayer> layers, string error) = EnumerateSchemaLayers(client, urlBuilder, schemaName, kind);
		if (error != null)
			return (null, error);
		if (layers.Count == 0)
			return (null, $"Schema '{schemaName}' not found (ManagerName='{kind.ManagerName}')");
		// Layers are ordered base->top; the top (most-derived) layer wins for a single-schema resolve, so a
		// multi-layer classic name always resolves to the same UId instead of a DB-order-dependent random layer.
		string uId = layers[layers.Count - 1].UId;
		if (string.IsNullOrWhiteSpace(uId))
			return (null, $"Schema '{schemaName}' metadata is missing UId");
		return (uId, null);
	}

	/// <summary>
	/// Enumerates every same-named schema layer (one <c>SysSchema</c> row per package that defines or replaces
	/// the schema) ordered base-&gt;top by the owning package's hierarchy level, with a stable package-name
	/// tiebreaker so equal levels order deterministically. This is the layer chain the Classic-&gt;Freedom
	/// migration bundle folds; the last element is the effective top (most-derived) layer.
	/// </summary>
	internal static (IReadOnlyList<SchemaLayer> layers, string error) EnumerateSchemaLayers(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string schemaName,
		SchemaDesignerKind kind) {
		var query = BuildSelectLayersByName(schemaName, kind.ManagerName);
		string url = urlBuilder.Build(SelectQueryRoute);
		string responseJson = client.ExecutePostRequest(url, query.ToString(Formatting.None));
		JObject selectResponse = JObject.Parse(responseJson);
		// Surface an explicit DataService failure instead of masking it as an empty result — otherwise the
		// caller reports a misleading "not found". Route through the shared SelectQuery detector so this keys
		// failure off the same three signals as ReadRows (success:false / an errorInfo object / a
		// responseStatus error), not the weaker success-only check that misses errorInfo/responseStatus-only
		// failures (e.g. restricted SysSchema access) and throws on a "success":null token.
		if (DataServiceSelectResponse.TryGetFailure(selectResponse, out string failure)) {
			return ([], $"SelectQuery for schema '{schemaName}' failed: {failure}");
		}
		var rows = selectResponse["rows"] as JArray ?? [];
		// Sort client-side as the authoritative order so the result is deterministic regardless of the row
		// order the DataService returns (the query also requests this order server-side).
		var layers = rows
			.Select(row => new SchemaLayer(
				row["UId"]?.ToString(),
				row["Name"]?.ToString(),
				row["PackageName"]?.ToString(),
				row["HierarchyLevel"]?.Value<int?>() ?? 0))
			.OrderBy(layer => layer.HierarchyLevel)
			.ThenBy(layer => layer.PackageName, StringComparer.OrdinalIgnoreCase)
			.ToList();
		return (layers, null);
	}

	/// <summary>
	/// Enumerates the layer chains of MANY schema names in a single DataService round-trip (an
	/// <c>In</c> filter over <c>Name</c>), grouping rows client-side. Every requested name gets an
	/// entry in the result — an empty list when the schema does not exist — so callers can memoize
	/// "not found" without re-querying. Ordering per name matches <see cref="EnumerateSchemaLayers"/>.
	/// </summary>
	internal static (IReadOnlyDictionary<string, IReadOnlyList<SchemaLayer>> layersByName, string error)
		EnumerateSchemaLayersBatch(
			IApplicationClient client,
			IServiceUrlBuilder urlBuilder,
			IReadOnlyCollection<string> schemaNames,
			SchemaDesignerKind kind) {
		var layersByName = new Dictionary<string, IReadOnlyList<SchemaLayer>>(StringComparer.OrdinalIgnoreCase);
		foreach (string name in schemaNames) {
			layersByName[name] = [];
		}
		if (layersByName.Count == 0) {
			return (layersByName, null);
		}
		var query = BuildSelectLayersByNames(layersByName.Keys, kind.ManagerName);
		string url = urlBuilder.Build(SelectQueryRoute);
		string responseJson = client.ExecutePostRequest(url, query.ToString(Formatting.None));
		JObject selectResponse = JObject.Parse(responseJson);
		// Same shared SelectQuery failure detection as EnumerateSchemaLayers: a batch failure must not be
		// read as "every requested name is empty", which PrimeLayerBatch would then memoize for the whole run.
		if (DataServiceSelectResponse.TryGetFailure(selectResponse, out string failure)) {
			return (layersByName, $"SelectQuery for schema layer batch failed: {failure}");
		}
		var rows = selectResponse["rows"] as JArray ?? [];
		foreach (var group in rows
			.Select(row => new SchemaLayer(
				row["UId"]?.ToString(),
				row["Name"]?.ToString(),
				row["PackageName"]?.ToString(),
				row["HierarchyLevel"]?.Value<int?>() ?? 0))
			.Where(layer => !string.IsNullOrEmpty(layer.Name) && layersByName.ContainsKey(layer.Name))
			.GroupBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)) {
			layersByName[group.Key] = group
				.OrderBy(layer => layer.HierarchyLevel)
				.ThenBy(layer => layer.PackageName, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		return (layersByName, null);
	}

	/// <summary>
	/// Extracts the merged localizable strings from a schema loaded with <c>useFullHierarchy:true</c>, each with
	/// its <c>parentSchemaUId</c> provenance and per-culture values. Returns an empty list when the schema has none.
	/// This is the honest content behind <c>--full-hierarchy</c> and the source of the migration bundle's resources.
	/// </summary>
	internal static IReadOnlyList<MergedLocalizableString> ExtractMergedLocalizableStrings(JObject schema) {
		var result = new List<MergedLocalizableString>();
		if (schema?["localizableStrings"] is not JArray strings) {
			return result;
		}
		foreach (JToken entry in strings) {
			var values = new List<MergedLocalizableStringValue>();
			if (entry["values"] is JArray valueArray) {
				foreach (JToken value in valueArray) {
					values.Add(new MergedLocalizableStringValue(
						value["cultureName"]?.ToString(),
						value["value"]?.ToString()));
				}
			}
			result.Add(new MergedLocalizableString(
				entry["name"]?.ToString(),
				entry["parentSchemaUId"]?.ToString(),
				entry["uId"]?.ToString(),
				values));
		}
		return result;
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
		string schemaName = null,
		bool useFullHierarchy = false) {
		var request = new JObject {
			["schemaUId"] = schemaUId,
			["useFullHierarchy"] = useFullHierarchy
		};
		string designerUrl = urlBuilder.Build(kind.GetRoute);
		string json = client.ExecutePostRequest(designerUrl, request.ToString(Formatting.None));
		JObject response = JObject.Parse(json);
		if (response["schema"] is not JObject loaded) {
			string label = schemaName ?? schemaUId;
			// Carry the designer service's own reason (permission, locked package, invalid UId) so a
			// failed load is diagnosable instead of a generic message — parity with EnumerateSchemaLayers.
			// `as JObject` keeps a JSON `errorInfo:null` (a JValue of type Null, not C# null) from throwing an
			// opaque JValue-indexing error when the reason is read.
			string failure = (response["errorInfo"] as JObject)?["message"]?.ToString();
			return (null, string.IsNullOrWhiteSpace(failure)
				? $"Failed to load schema '{label}' via {kind.ServiceName}"
				: $"Failed to load schema '{label}' via {kind.ServiceName}: {failure}");
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
		JObject schema, string name, string caption, string description, string cultureName = null) {
		// Anchor captions to the effective culture (override > profile > en-US). A null cultureName
		// preserves the legacy en-US default; the host CultureInfo.CurrentCulture is never read.
		string effectiveCulture = string.IsNullOrWhiteSpace(cultureName) ? "en-US" : cultureName;
		// ENG-91044: reject caption/description text whose script does not match the effective culture
		// (e.g. Cyrillic under en-US). Shared by create-sql-schema and create-source-code-schema.
		CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(effectiveCulture, caption, "caption");
		CaptionCultureScriptGuard.EnsureCaptionMatchesCulture(effectiveCulture, description, "description");
		schema["name"] = name;
		schema["caption"] = new JArray(new JObject { ["cultureName"] = effectiveCulture, [ValueKey] = caption });
		if (!string.IsNullOrWhiteSpace(description))
			schema["description"] = new JArray(
				new JObject { ["cultureName"] = effectiveCulture, [ValueKey] = description });
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

	private static JObject BuildSelectLayersByName(string schemaName, string managerName) {
		return new JObject {
			["rootSchemaName"] = "SysSchema",
			["operationType"] = 0,
			["columns"] = new JObject {
				["items"] = new JObject {
					["UId"] = new JObject {
						["expression"] = new JObject { [ExpressionTypeKey] = 0, ["columnPath"] = "UId" }
					},
					["Name"] = new JObject {
						["expression"] = new JObject { [ExpressionTypeKey] = 0, ["columnPath"] = "Name" }
					},
					["PackageName"] = new JObject {
						// Secondary, stable tiebreaker so packages at the same hierarchy level order deterministically.
						["orderDirection"] = 1,
						["orderPosition"] = 1,
						["expression"] = new JObject { [ExpressionTypeKey] = 0, ["columnPath"] = "SysPackage.Name" }
					},
					["HierarchyLevel"] = new JObject {
						// Package hierarchy level orders the replacing chain base (lowest) -> top (highest), so a
						// multi-layer classic schema enumerates/resolves deterministically instead of by DB order.
						["orderDirection"] = 1,
						["orderPosition"] = 0,
						["expression"] = new JObject { [ExpressionTypeKey] = 0, ["columnPath"] = "SysPackage.HierarchyLevel" }
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
			// -1 = no limit: return every layer so a multi-package replacing chain enumerates in full.
			["rowCount"] = -1
		};
	}

	// Same projection/order as BuildSelectLayersByName, but filtering Name with an In filter
	// (filterType 4 + rightExpressions) so one round-trip enumerates many names at once.
	private static JObject BuildSelectLayersByNames(IEnumerable<string> schemaNames, string managerName) {
		var nameExpressions = new JArray();
		foreach (string schemaName in schemaNames) {
			nameExpressions.Add(new JObject {
				[ExpressionTypeKey] = 2,
				["parameter"] = new JObject { ["dataValueType"] = 1, [ValueKey] = schemaName }
			});
		}
		JObject query = BuildSelectLayersByName(string.Empty, managerName);
		query["filters"]["items"]["byName"] = new JObject {
			["filterType"] = 4,
			["comparisonType"] = 3,
			["isEnabled"] = true,
			["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, ["columnPath"] = "Name" },
			["rightExpressions"] = nameExpressions
		};
		return query;
	}
}
