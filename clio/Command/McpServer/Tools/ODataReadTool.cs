using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for querying Creatio records via OData v4.
/// </summary>
[McpServerToolType]
public sealed class ODataReadTool(IToolCommandResolver commandResolver) {

	internal const string ToolName = "odata-read";

	/// <summary>Smallest accepted value for the <c>top</c> argument.</summary>
	internal const int MinTop = 1;

	/// <summary>Largest accepted value for the <c>top</c> argument.</summary>
	internal const int MaxTop = 100;

	/// <summary>Number of records returned when <c>top</c> is omitted.</summary>
	internal const int DefaultTop = 25;

	private const string ValidArgumentsHint =
		"Valid: entity, environment-name, filters, select, expand, order-by, top, skip, count. " +
		"Raw filter strings are not supported; use the structured filters object.";

	private static readonly IReadOnlyDictionary<string, string> ArgumentAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["environmentName"] = "environment-name",
			["environment_name"] = "environment-name",
			["orderBy"] = "order-by",
			["order_by"] = "order-by",
			["limit"] = "top"
		};

	private static readonly IReadOnlyDictionary<string, string> FilterGroupAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["and"] = "all",
			["or"] = "any"
		};

	private static readonly IReadOnlyDictionary<string, string> FilterConditionAliases =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["column"] = "field",
			["operator"] = "op",
			["values"] = "in"
		};

	private static readonly HashSet<string> SupportedFilterOperators = new(StringComparer.Ordinal) {
		"eq", "ne", "gt", "ge", "lt", "le", "contains", "startswith", "endswith"
	};

	/// <summary>Reads Creatio records using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Query Creatio records via OData v4. " +
		"Supports structured filters, select, expand, order by, top, skip, and total-count requests. " +
		"top must be between 1 and 100 (default 25); an out-of-range top (including 0 or negative) is rejected, never silently widened. " +
		"skip must be zero or greater; use order-by with skip for stable paging. " +
		"Unknown arguments and malformed filter conditions fail before any Creatio request; raw filter strings are not supported. " +
		"Call get-tool-contract for odata-read to see usage examples and discovery workflow hints.")]
	public ODataReadResponse Read(
		[Description("Parameters: entity, environment-name (required); filters, select, expand, order-by, top, skip, count (optional).")]
		[Required]
		ODataReadArgs args) {
		try {
			string? argumentError = ValidateArguments(args);
			if (argumentError is not null) {
				return ODataReadResponse.Failure(argumentError);
			}
			if (string.IsNullOrWhiteSpace(args.Entity)) {
				return ODataReadResponse.Failure("entity is required.");
			}
			if (!ODataKeyFormatter.IsValidEntityName(args.Entity)) {
				return ODataReadResponse.Failure("entity must be a valid OData entity set name (letters, digits, underscore).");
			}
			if (args.Top is { } requestedTop && (requestedTop < MinTop || requestedTop > MaxTop)) {
				// An out-of-range top must NOT silently fall through to the default (which would
				// return a page when the caller asked for 0, or be misread as "all" on negatives).
				return ODataReadResponse.Failure(
					$"top must be between {MinTop} and {MaxTop} (got {requestedTop}). Omit top to use the default of {DefaultTop}.");
			}

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			string queryString = BuildQueryString(args);
			string path = $"odata/{args.Entity.Trim()}{queryString}";
			string url = urlBuilder.Build(path);

			string responseJson = client.ExecuteGetRequest(url, 30_000);
			return ParseODataResponse(responseJson, args.Count);
		} catch (Exception ex) {
			return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

	private static string? ValidateArguments(ODataReadArgs args) {
		if (args.ExtensionData?.ContainsKey("filter") == true) {
			return "Argument 'filter' is unsupported because raw filter strings are not accepted. " +
				"Use a structured filter, for example: " +
				"filters: {\"all\":[{\"field\":\"Name\",\"op\":\"eq\",\"value\":\"Acme\"}]}.";
		}
		string? argumentError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData,
			ArgumentAliases,
			".",
			ValidArgumentsHint);
		if (argumentError is not null) {
			return argumentError;
		}
		if (args.Skip is < 0) {
			return $"skip must be zero or greater (got {args.Skip}).";
		}
		if (args.FiltersProvided && args.Filters is null) {
			return "filters must be a structured object containing at least one condition in all or any; null is not supported.";
		}
		if (args.Filters is null) {
			return null;
		}

		string? groupError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.Filters.ExtensionData,
			FilterGroupAliases,
			".",
			"Valid filter groups: all, any.");
		if (groupError is not null) {
			return $"filters: {groupError}";
		}

		List<(string Path, ODataFilterCondition? Condition)> conditions = [];
		AddConditions(conditions, "filters.all", args.Filters.All);
		AddConditions(conditions, "filters.any", args.Filters.Any);
		if (conditions.Count == 0) {
			return "filters must contain at least one condition in all or any.";
		}
		foreach ((string path, ODataFilterCondition? condition) in conditions) {
			string? conditionError = ValidateCondition(path, condition);
			if (conditionError is not null) {
				return conditionError;
			}
		}
		return null;
	}

	private static void AddConditions(
		ICollection<(string Path, ODataFilterCondition? Condition)> destination,
		string path,
		IReadOnlyList<ODataFilterCondition?>? conditions) {
		if (conditions is null) {
			return;
		}
		for (int index = 0; index < conditions.Count; index++) {
			destination.Add(($"{path}[{index}]", conditions[index]));
		}
	}

	private static string? ValidateCondition(string path, ODataFilterCondition? condition) {
		if (condition is null) {
			return $"{path} must be a filter condition object; null is not supported.";
		}
		string? memberError = McpToolArgumentSupport.BuildLegacyAliasError(
			condition.ExtensionData,
			FilterConditionAliases,
			".",
			"Valid filter condition members: field, op, value, in.");
		if (memberError is not null) {
			return $"{path}: {memberError}";
		}
		if (string.IsNullOrWhiteSpace(condition.Field)) {
			return $"{path}.field is required.";
		}
		if (!ODataKeyFormatter.IsValidMemberPath(condition.Field)) {
			return $"{path}.field must be an OData member path containing only letters, digits, underscores, and '/' separators.";
		}
		bool hasValue = condition.Value.ValueKind != JsonValueKind.Undefined;
		bool hasInValues = condition.InValues.ValueKind != JsonValueKind.Undefined;
		if (hasValue == hasInValues) {
			return $"{path} must provide exactly one of value or in.";
		}
		if (hasInValues) {
			JsonElement inValues = condition.InValues;
			if (inValues.ValueKind != JsonValueKind.Array || inValues.GetArrayLength() == 0) {
				return $"{path}.in must be a non-empty array.";
			}
			if (!string.IsNullOrWhiteSpace(condition.Op)) {
				return $"{path}.op must be omitted when in is provided; in expands to equality conditions.";
			}
			return null;
		}
		string operation = string.IsNullOrWhiteSpace(condition.Op) ? "eq" : condition.Op;
		return SupportedFilterOperators.Contains(operation)
			? null
			: $"{path}.op must be one of: {string.Join(", ", SupportedFilterOperators)} (got {operation}).";
	}

	private static string LiteralFor(string field, JsonElement value) =>
		ODataKeyFormatter.LiteralFor(field, value);

	private static string? JoinConditions(IReadOnlyList<string> conditions, string separator) {
		return conditions.Count switch {
			0 => null,
			1 => conditions[0],
			_ => $"({string.Join(separator, conditions)})"
		};
	}

	private static List<string> BuildConditions(IEnumerable<ODataFilterCondition>? conditions) {
		if (conditions is null) {
			return [];
		}
		return conditions
			.Select(BuildCondition)
			.Where(condition => condition is not null)
			.Cast<string>()
			.ToList();
	}

	private static string? BuildCondition(ODataFilterCondition c) {
		if (string.IsNullOrWhiteSpace(c.Field)) {
			return null;
		}
		string field = c.Field;
		if (c.InValues.ValueKind == JsonValueKind.Array) {
			List<string> inParts = c.InValues.EnumerateArray()
				.Select(v => $"{field} eq {LiteralFor(field, v)}")
				.ToList();
			return JoinConditions(inParts, " or ");
		}
		if (c.Value.ValueKind == JsonValueKind.Undefined) {
			return null;
		}
		string op = string.IsNullOrWhiteSpace(c.Op) ? "eq" : c.Op;
		JsonElement val = c.Value;
		if (op is "contains" or "startswith" or "endswith") {
			return $"{op}({field},{LiteralFor(field, val)})";
		}
		if (val.ValueKind == JsonValueKind.Null && op is "eq" or "ne") {
			return $"{field} {op} null";
		}
		return $"{field} {op} {LiteralFor(field, val)}";
	}

	private static string? BuildFilterFromStructured(ODataFilters filters) {
		List<string> andParts = BuildConditions(filters.All);
		List<string> orParts = BuildConditions(filters.Any);
		var parts = new List<string>();
		string? allFilter = JoinConditions(andParts, " and ");
		if (allFilter is not null) {
			parts.Add(allFilter);
		}
		string? anyFilter = JoinConditions(orParts, " or ");
		if (anyFilter is not null) {
			parts.Add(anyFilter);
		}
		return parts.Count > 0 ? string.Join(" and ", parts) : null;
	}

	private static string BuildQueryString(ODataReadArgs args) {
		var parts = new List<string>();

		string? effectiveFilter = args.Filters is not null ? BuildFilterFromStructured(args.Filters) : null;
		if (effectiveFilter is not null) {
			parts.Add($"$filter={Uri.EscapeDataString(effectiveFilter)}");
		}

		if (args.Select is { Length: > 0 }) {
			parts.Add($"$select={Uri.EscapeDataString(string.Join(",", args.Select))}");
		}

		if (args.Expand is { Length: > 0 }) {
			parts.Add($"$expand={Uri.EscapeDataString(string.Join(",", args.Expand))}");
		}

		if (!string.IsNullOrWhiteSpace(args.OrderBy)) {
			parts.Add($"$orderby={Uri.EscapeDataString(args.OrderBy!.Trim())}");
		}

		if (args.Skip is { } skip) {
			parts.Add($"$skip={skip}");
		}

		if (args.Count) {
			parts.Add("$count=true");
		}

		// Read() rejects out-of-range top before reaching here, so top is either unset (default)
		// or already validated to be within [MinTop, MaxTop].
		int top = args.Top ?? DefaultTop;
		parts.Add($"$top={top}");

		return $"?{string.Join("&", parts)}";
	}

	private static ODataReadResponse ParseODataResponse(string json, bool countRequested) {
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			if (CreatioResponseError.TryDetect(root, CreatioResponseContext.ODataPayload, out string serverError)) {
				// Redact like the sibling error paths: a routing Message can embed the absolute request
				// URI (host/port/app path), which must not leak into the MCP transcript or logs.
				return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact(serverError));
			}

			if (root.TryGetProperty("value", out JsonElement valueEl)) {
				return ParseCollectionResponse(root, valueEl, countRequested);
			}

			// Single-entity response (no value wrapper)
			return new ODataReadResponse(true, null, 1, root.Clone(), null);
		} catch (Exception ex) {
			string preview = string.IsNullOrWhiteSpace(json) ? "<empty>" : json;
			if (preview.Length > 500) {
				preview = preview[..500] + "...";
			}
			return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact($"Failed to parse OData response: {ex.Message} | Response: {preview}"));
		}
	}

	private static ODataReadResponse ParseCollectionResponse(
		JsonElement root,
		JsonElement valueElement,
		bool countRequested) {
		int count = valueElement.ValueKind == JsonValueKind.Array ? valueElement.GetArrayLength() : 1;
		long? totalCount = root.TryGetProperty("@odata.count", out JsonElement totalCountElement)
			&& totalCountElement.TryGetInt64(out long parsedTotalCount)
			? parsedTotalCount
			: null;
		if (countRequested && !totalCount.HasValue) {
			return ODataReadResponse.Failure(
				"Creatio did not return @odata.count for count=true; total count cannot be verified.");
		}
		string? nextLink = root.TryGetProperty("@odata.nextLink", out JsonElement nextLinkElement)
			&& nextLinkElement.ValueKind == JsonValueKind.String
			? nextLinkElement.GetString()
			: null;
		return new ODataReadResponse(true, null, count, valueElement.Clone(), nextLink, totalCount);
	}

}

/// <summary>
/// Arguments for <see cref="ODataReadTool"/>.
/// </summary>
public sealed record ODataReadArgs {
	private ODataFilters? _filters;

	/// <summary>Creatio OData entity set name (e.g., Contact, Account, Activity).</summary>
	[JsonPropertyName("entity")]
	[Description("Creatio OData entity set name (e.g., Contact, Account, Activity). Call dataforge-find-tables to discover available names.")]
	[Required]
	public required string Entity { get; init; }

	/// <summary>Fields to return ($select).</summary>
	[JsonPropertyName("select")]
	[Description(
		"Fields to return ($select). Strongly recommended for performance. " +
		"Include all fields used in filter. " +
		"Use dataforge-get-table-columns to discover field names. " +
		"Example: [\"Id\",\"Name\",\"AccountId\"]")]
	public string[]? Select { get; init; }

	/// <summary>Navigation properties to expand ($expand).</summary>
	[JsonPropertyName("expand")]
	[Description(
		"Navigation properties to expand ($expand). " +
		"Remove 'Id' suffix from a lookup field to get the navigation name: AccountId → Account. " +
		"Example: [\"Account\",\"Owner\"]")]
	public string[]? Expand { get; init; }

	/// <summary>OData $orderby clause.</summary>
	[JsonPropertyName("order-by")]
	[Description("OData $orderby clause. Example: \"CreatedOn desc\" or \"Name asc, Amount desc\".")]
	public string? OrderBy { get; init; }

	/// <summary>Maximum number of records to return (1-100, default 25).</summary>
	[JsonPropertyName("top")]
	[Description("Maximum number of records to return. Range: 1-100. Default: 25. An out-of-range value (including 0 or negative) is rejected with a validation error, not silently changed.")]
	public int? Top { get; init; }

	/// <summary>Number of matching records to skip before returning the page.</summary>
	[JsonPropertyName("skip")]
	[Description("Number of matching records to skip. Must be zero or greater. Use order-by for stable paging.")]
	public int? Skip { get; init; }

	/// <summary>Whether Creatio should return the total number of matching records.</summary>
	[JsonPropertyName("count")]
	[Description("When true, requests the total number of matching records before top/skip paging; returned as total-count. Response count remains the number of records in this page.")]
	public bool Count { get; init; }

	/// <summary>Structured filter used to narrow matching records.</summary>
	[JsonPropertyName("filters")]
	[Description(
		"Structured filter used to narrow matching records. Raw filter strings are not supported. " +
		"all conditions join with AND; any conditions join with OR. " +
		"GUID values in Id-suffixed fields and navigation paths ending in Id are automatically unquoted; strings are single-quoted. " +
		"in array expands to OR-joined equality clauses. " +
		"Example: { \"all\": [{ \"field\": \"Account/Id\", \"op\": \"eq\", \"value\": \"8ecab4a1-0ca3-4515-9399-efe0a19390bd\" }] }")]
	public ODataFilters? Filters {
		get => _filters;
		init {
			_filters = value;
			FiltersProvided = true;
		}
	}

	/// <summary>Whether the JSON request explicitly supplied the filters member.</summary>
	[JsonIgnore]
	internal bool FiltersProvided { get; private set; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }

	/// <summary>Unbound JSON members, rejected before any Creatio request.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Response returned by <see cref="ODataReadTool"/>.
/// </summary>
public sealed record ODataReadResponse(
	[property: JsonPropertyName("success")]
	[property: Description("Whether the OData read succeeded.")]
	bool Success,

	[property: JsonPropertyName("error")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Error message when success is false.")]
	string? Error,

	[property: JsonPropertyName("count")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Number of records returned.")]
	int? Count,

	[property: JsonPropertyName("value")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Records returned by the OData query.")]
	JsonElement? Value,

	[property: JsonPropertyName("next-link")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("OData next-link URL when more records are available beyond the requested top.")]
	string? NextLink = null,

	[property: JsonPropertyName("total-count")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Total number of records matching the filter before top/skip paging, present when count=true.")]
	long? TotalCount = null) {

	/// <summary>Creates a failure response.</summary>
	public static ODataReadResponse Failure(string message) =>
		new(false, message, null, null);
}

/// <summary>
/// A single condition in a structured OData filter.
/// </summary>
public sealed record ODataFilterCondition {
	/// <summary>OData field name to filter on.</summary>
	[JsonPropertyName("field")]
	[Description("OData field name. Id-suffixed fields and navigation paths ending in Id, such as Id, Account/Id, receive automatic GUID unquoting.")]
	[Required]
	public required string Field { get; init; }

	/// <summary>Comparison operator.</summary>
	[JsonPropertyName("op")]
	[Description("Comparison operator: eq, ne, gt, ge, lt, le, contains, startswith, endswith. Default: eq.")]
	public string? Op { get; init; }

	/// <summary>Value to compare against.</summary>
	[JsonPropertyName("value")]
	[Description("Comparison value. GUIDs in Id-suffixed fields and navigation paths ending in Id are automatically unquoted. Strings get single-quoted. Numbers and booleans are unquoted.")]
	public JsonElement Value { get; init; }

	/// <summary>Array of values for in-list OR expansion.</summary>
	[JsonPropertyName("in")]
	[Description("Array of values that expand to OR-joined equality clauses: field eq v1 or field eq v2.")]
	public JsonElement InValues { get; init; }

	/// <summary>Unbound condition members, rejected before any Creatio request.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Structured filter object for <see cref="ODataReadArgs.Filters"/>.
/// </summary>
public sealed record ODataFilters {
	/// <summary>Conditions joined with AND.</summary>
	[JsonPropertyName("all")]
	[Description("Conditions that must ALL match (AND-joined).")]
	public ODataFilterCondition?[]? All { get; init; }

	/// <summary>Conditions joined with OR.</summary>
	[JsonPropertyName("any")]
	[Description("Conditions where ANY must match (OR-joined).")]
	public ODataFilterCondition?[]? Any { get; init; }

	/// <summary>Unbound filter-group members, rejected before any Creatio request.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
