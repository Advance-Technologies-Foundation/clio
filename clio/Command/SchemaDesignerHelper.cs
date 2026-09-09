namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
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

/// <summary>
/// How a schema-name resolution ended. Callers that must tell "the schema is absent" apart from
/// "the check could not be answered" branch on this instead of matching the error text: the error
/// prose is authored in several places and a wording change would silently flip the branch, which is
/// how a transport failure gets read as "the schema does not exist".
/// </summary>
internal enum SchemaResolveStatus {

	/// <summary>The schema was found and its UId is available.</summary>
	Resolved = 0,

	/// <summary>The query was answered and reported no such schema — an observation about the schema.</summary>
	NotFound = 1,

	/// <summary>
	/// The query could not be answered (unusable response body, DataService failure envelope), or it was
	/// answered but carried no UId. Neither says the schema is absent, so a caller must abort rather than
	/// treat it as a licence to create.
	/// </summary>
	Unanswerable = 2
}

/// <summary>
/// Outcome of resolving a schema name to its UId: the UId when resolved, the discriminated
/// <see cref="SchemaResolveStatus"/>, and the failure text for the two non-resolved statuses.
/// </summary>
/// <param name="UId">Resolved schema UId, or <see langword="null"/> when not resolved.</param>
/// <param name="Status">Which of the three outcomes occurred.</param>
/// <param name="Error">Failure text, or <see langword="null"/> when resolved.</param>
internal readonly record struct SchemaResolveResult(string UId, SchemaResolveStatus Status, string Error) {

	/// <summary>Builds a resolved outcome.</summary>
	/// <param name="uId">The resolved schema UId.</param>
	/// <returns>The resolved outcome.</returns>
	internal static SchemaResolveResult Resolved(string uId) => new(uId, SchemaResolveStatus.Resolved, null);

	/// <summary>Builds the "answered, and there is no such schema" outcome.</summary>
	/// <param name="error">Failure text naming the schema and manager.</param>
	/// <returns>The not-found outcome.</returns>
	internal static SchemaResolveResult NotFound(string error) => new(null, SchemaResolveStatus.NotFound, error);

	/// <summary>Builds the "the check could not be answered" outcome.</summary>
	/// <param name="error">Failure text describing why the check is unanswerable.</param>
	/// <returns>The unanswerable outcome.</returns>
	internal static SchemaResolveResult Unanswerable(string error) =>
		new(null, SchemaResolveStatus.Unanswerable, error);

	/// <summary>True when the schema was found.</summary>
	internal bool IsResolved => Status == SchemaResolveStatus.Resolved;

	/// <summary>True when the query was answered and reported no such schema.</summary>
	internal bool IsNotFound => Status == SchemaResolveStatus.NotFound;

	/// <summary>
	/// Deconstructs into the legacy <c>(uId, error)</c> shape used by callers that treat every
	/// non-resolved outcome as a plain failure.
	/// </summary>
	/// <param name="uId">Resolved schema UId, or <see langword="null"/>.</param>
	/// <param name="error">Failure text, or <see langword="null"/>.</param>
	internal void Deconstruct(out string uId, out string error) {
		uId = UId;
		error = Error;
	}
}

internal static class SchemaDesignerHelper {

	private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
	private const string ValueKey = "value";
	private const string ExpressionTypeKey = "expressionType";

	// ESQ payload keys, single-sourced so the select builders below cannot drift on a key name.
	private const string ItemsKey = "items";
	private const string ExpressionKey = "expression";
	private const string ColumnPathKey = "columnPath";
	private const string FilterTypeKey = "filterType";
	private const string IsEnabledKey = "isEnabled";
	private const string ComparisonTypeKey = "comparisonType";
	private const string LeftExpressionKey = "leftExpression";
	private const string RightExpressionKey = "rightExpression";
	private const string ParameterKey = "parameter";
	private const string DataValueTypeKey = "dataValueType";

	/// <summary>
	/// Locally authored tail appended to a designer-service response that was empty or not JSON. It names
	/// the two causes actually observed on a live stand: the designer <c>.svc</c> route is not present on
	/// the target Creatio version (an unrouted <c>.svc</c> answers 404 with a zero-length body, which is
	/// what produced the bare Newtonsoft parser message in issue #1322), and the ordinary
	/// package/permission causes. No HTTP status is quoted because the synchronous client this helper
	/// calls through does not expose one (issue #1317).
	/// </summary>
	internal const string DesignerServiceHint =
		"If this repeats, the designer route may not be served by this Creatio version at all; otherwise "
		+ "verify that the target package exists and is unlocked and editable, that the connected user may "
		+ "manage configuration, and that the session is still valid (healthcheck).";

	/// <summary>
	/// Locally authored tail a caller appends when <c>SaveSchema</c> reported <c>outcomeUnknown</c> and the
	/// caller cannot read the schema back: the write was neither observed to succeed nor observed to fail,
	/// so the error must not be reported as an observed failure.
	/// </summary>
	internal const string SaveOutcomeUnknownNote =
		"The save outcome is unknown - the request may have been applied and only the answer lost - so "
		+ "verify the schema in the environment before retrying.";

	// One label per designer call, so the failure names the service and the operation
	// (for example "ScriptSchemaDesignerService CreateNewSchema") instead of a bare parser message.
	private static string DesignerOperation(SchemaDesignerKind kind, string operation) =>
		$"{kind.ServiceName} {operation}";

	private static (JObject parsed, string error) ParseServiceResponse(
		string operationName, string url, string responseBody, string hint = null) =>
		ServiceResponseJsonGuard.TryParseJObject(operationName, url, responseBody, hint,
			out JObject parsed, out string error)
			? (parsed, null)
			: (null, error);

	internal static string ValidateCreateInput(string schemaName, string packageName) {
		if (string.IsNullOrWhiteSpace(schemaName))
			return "schema-name is required";
		if (!PageSchemaMetadataHelper.IsValidSchemaName(schemaName))
			return PageSchemaMetadataHelper.SchemaNameFormatError;
		if (string.IsNullOrWhiteSpace(packageName))
			return "package-name is required";
		return null;
	}

	internal static SchemaResolveResult ResolveSchemaUId(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string schemaName,
		SchemaDesignerKind kind) {
		// The deterministic top-layer (most-derived) resolution is SCOPED to ClientUnit — the only kind the
		// Classic->Freedom migration path needs it for. SqlScript/SourceCode keep the pre-PR single-row pick:
		// ResolveSchemaUId is a shared, kind-generic helper also used by SqlSchemaUpdate/SqlSchemaInstall
		// (which executes raw SQL against the DB) and SourceCodeSchemaUpdate, none of which are covered by
		// multi-layer resolution tests. Silently redirecting which physical layer those commands write to /
		// execute against is out of scope for this PR (see PR #937 review); modernize those kinds separately.
		if (kind != SchemaDesignerKind.ClientUnit) {
			return ResolveSchemaUIdSingle(client, urlBuilder, schemaName, kind);
		}
		(IReadOnlyList<SchemaLayer> layers, string error) = EnumerateSchemaLayers(client, urlBuilder, schemaName, kind);
		if (error != null)
			return SchemaResolveResult.Unanswerable(error);
		if (layers.Count == 0)
			return SchemaResolveResult.NotFound(SchemaNotFoundError(schemaName, kind));
		// Layers are ordered base->top; the top (most-derived) layer wins for a single-schema resolve, so a
		// multi-layer classic name always resolves to the same UId instead of a DB-order-dependent random layer.
		string uId = layers[layers.Count - 1].UId;
		// A row with a blank UId is NOT evidence that the schema is absent - the row exists, only its
		// identifier is unusable - so this is classified as unanswerable, not as not-found. A caller that
		// treated it as "absent" would create a second schema over an existing one.
		if (string.IsNullOrWhiteSpace(uId))
			return SchemaResolveResult.Unanswerable(MissingUIdError(schemaName));
		return SchemaResolveResult.Resolved(uId);
	}

	// Pre-PR single-row resolution preserved verbatim for the non-ClientUnit kinds (SqlScript/SourceCode):
	// a UId-by-name query capped at one row, taking that row's UId. Kept deliberately unchanged so the layer
	// the Sql/SourceCode update/install commands target is not altered by this PR (see ResolveSchemaUId).
	private static SchemaResolveResult ResolveSchemaUIdSingle(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		string schemaName,
		SchemaDesignerKind kind) {
		var query = BuildSelectUIdByName(schemaName, kind.ManagerName);
		string url = urlBuilder.Build(SelectQueryRoute);
		string responseJson = client.ExecutePostRequest(url, query.ToString(Formatting.None));
		(JObject selectResponse, string parseError) = ParseServiceResponse("SelectQuery", url, responseJson);
		if (parseError != null)
			return SchemaResolveResult.Unanswerable(parseError);
		// DataService returns HTTP 200 even on failure (restricted SysSchema access, auth, invalid column). Key
		// failure off the same authoritative detector the ClientUnit layer path uses, so a failure envelope is
		// surfaced as the real error instead of an empty-rows "not found" — which would also silently corrupt
		// the duplicate-name check (a permission failure read as "schema does not exist").
		if (DataServiceSelectResponse.TryGetFailure(selectResponse, out string failure))
			return SchemaResolveResult.Unanswerable($"SelectQuery for schema '{schemaName}' failed: {failure}");
		var rows = selectResponse["rows"] as JArray ?? [];
		if (rows.Count == 0)
			return SchemaResolveResult.NotFound(SchemaNotFoundError(schemaName, kind));
		string uId = rows[0]["UId"]?.ToString();
		// See ResolveSchemaUId: a blank UId leaves the question unanswered rather than answering "absent".
		if (string.IsNullOrWhiteSpace(uId))
			return SchemaResolveResult.Unanswerable(MissingUIdError(schemaName));
		return SchemaResolveResult.Resolved(uId);
	}

	private static string SchemaNotFoundError(string schemaName, SchemaDesignerKind kind) =>
		$"Schema '{schemaName}' not found (ManagerName='{kind.ManagerName}')";

	private static string MissingUIdError(string schemaName) =>
		$"Schema '{schemaName}' metadata is missing UId";

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
		(JObject selectResponse, string parseError) = ParseServiceResponse("SelectQuery", url, responseJson);
		if (parseError != null) {
			return ([], parseError);
		}
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
		(JObject selectResponse, string parseError) = ParseServiceResponse("SelectQuery", url, responseJson);
		if (parseError != null) {
			return (layersByName, parseError);
		}
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
		(JObject response, string parseError) = ParseServiceResponse(
			DesignerOperation(kind, "GetSchema"), designerUrl, json, DesignerServiceHint);
		if (parseError != null)
			return (null, parseError);
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

	/// <summary>
	/// Saves a designer schema, additionally reporting whether the failure leaves the outcome UNKNOWN.
	/// </summary>
	/// <remarks>
	/// A save whose response was empty or not JSON says nothing about whether the schema was written:
	/// the request may have been applied and the answer lost, or never have reached the service at all.
	/// Callers that can read the schema back (see <c>create-sql-schema</c>) must verify instead of
	/// reporting a failure they did not observe.
	/// </remarks>
	/// <param name="client">Application client used for the request.</param>
	/// <param name="urlBuilder">Builder for the designer save route.</param>
	/// <param name="schema">Schema payload to save.</param>
	/// <param name="kind">Which designer service to save through.</param>
	/// <param name="outcomeUnknown">
	/// <see langword="true"/> when the service answer was unusable, so the save was neither observed to
	/// succeed nor observed to fail.
	/// </param>
	/// <returns>The failure message, or <see langword="null"/> when the service reported success.</returns>
	internal static string SaveSchema(
		IApplicationClient client,
		IServiceUrlBuilder urlBuilder,
		JObject schema,
		SchemaDesignerKind kind,
		out bool outcomeUnknown) {
		outcomeUnknown = false;
		string saveUrl = urlBuilder.Build(kind.SaveRoute);
		string json = client.ExecutePostRequest(saveUrl, schema.ToString(Formatting.None));
		(JObject response, string parseError) = ParseServiceResponse(
			DesignerOperation(kind, "SaveSchema"), saveUrl, json, DesignerServiceHint);
		if (parseError != null) {
			outcomeUnknown = true;
			return parseError;
		}
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
		(JObject response, string parseError) = ParseServiceResponse(
			DesignerOperation(kind, "CreateNewSchema"), createUrl, json, DesignerServiceHint);
		if (parseError != null)
			return (null, parseError);
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

	// The SysSchema filter group both selects share: Name == <schemaName> AND ManagerName == <managerName>.
	// Single-sourced so the UId lookup and the layer enumeration cannot drift on the filter shape or a dataValueType
	// and start resolving different rows.
	private static JObject BuildNameAndManagerFilters(string schemaName, string managerName) => new() {
		[FilterTypeKey] = 6,
		["logicalOperation"] = 0,
		[IsEnabledKey] = true,
		[ItemsKey] = new JObject {
			["byName"] = BuildTextEqualsFilter("Name", schemaName),
			["byManager"] = BuildTextEqualsFilter("ManagerName", managerName)
		}
	};

	// An ESQ equality filter over a text column (dataValueType 1): <columnPath> == <value>.
	private static JObject BuildTextEqualsFilter(string columnPath, string value) => new() {
		[FilterTypeKey] = 1,
		[ComparisonTypeKey] = 3,
		[IsEnabledKey] = true,
		[LeftExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = columnPath },
		[RightExpressionKey] = new JObject {
			[ExpressionTypeKey] = 2,
			[ParameterKey] = new JObject { [DataValueTypeKey] = 1, [ValueKey] = value }
		}
	};

	// Pre-PR UId-by-name query (single row) used by ResolveSchemaUIdSingle for the non-ClientUnit kinds.
	private static JObject BuildSelectUIdByName(string schemaName, string managerName) {
		return new JObject {
			["rootSchemaName"] = "SysSchema",
			["operationType"] = 0,
			["columns"] = new JObject {
				[ItemsKey] = new JObject {
					["UId"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "UId" }
					}
				}
			},
			["filters"] = BuildNameAndManagerFilters(schemaName, managerName),
			["rowCount"] = 1
		};
	}

	private static JObject BuildSelectLayersByName(string schemaName, string managerName) {
		return new JObject {
			["rootSchemaName"] = "SysSchema",
			["operationType"] = 0,
			["columns"] = new JObject {
				[ItemsKey] = new JObject {
					["UId"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "UId" }
					},
					["Name"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "Name" }
					},
					["PackageName"] = new JObject {
						// Secondary, stable tiebreaker so packages at the same hierarchy level order deterministically.
						["orderDirection"] = 1,
						["orderPosition"] = 1,
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "SysPackage.Name" }
					},
					["HierarchyLevel"] = new JObject {
						// Package hierarchy level orders the replacing chain base (lowest) -> top (highest), so a
						// multi-layer classic schema enumerates/resolves deterministically instead of by DB order.
						["orderDirection"] = 1,
						["orderPosition"] = 0,
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "SysPackage.HierarchyLevel" }
					}
				}
			},
			["filters"] = BuildNameAndManagerFilters(schemaName, managerName),
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
				[ParameterKey] = new JObject { [DataValueTypeKey] = 1, [ValueKey] = schemaName }
			});
		}
		JObject query = BuildSelectLayersByName(string.Empty, managerName);
		query["filters"][ItemsKey]["byName"] = new JObject {
			[FilterTypeKey] = 4,
			[ComparisonTypeKey] = 3,
			[IsEnabledKey] = true,
			[LeftExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "Name" },
			["rightExpressions"] = nameExpressions
		};
		return query;
	}
}
