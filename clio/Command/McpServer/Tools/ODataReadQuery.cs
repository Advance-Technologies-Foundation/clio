using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Query construction, argument validation and response parsing shared by the two OData read tools.
/// </summary>
/// <remarks>
/// <c>odata-read</c> stays a read-only, idempotent tool; <c>odata-read-to-file</c> is the narrowly
/// write-capable sibling that persists a large response. Splitting the TOOLS but keeping ONE
/// implementation here is what stops the two surfaces drifting: filters, paging rules and the
/// response shapes are settled in a single place and both tools call into it.
/// <para>
/// Pure functions only - no file access, no remote calls - so this stays a static helper rather than a
/// DI-registered service (same category as <see cref="ODataKeyFormatter"/>).
/// </para>
/// </remarks>
internal static class ODataReadQuery {

	/// <summary>Smallest accepted value for the <c>top</c> argument.</summary>
	internal const int MinTop = 1;

	/// <summary>Largest accepted value for the <c>top</c> argument.</summary>
	internal const int MaxTop = 100;

	/// <summary>Number of records returned when <c>top</c> is omitted.</summary>
	internal const int DefaultTop = 25;

	internal static readonly IReadOnlyDictionary<string, string> SharedArgumentAliases =
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

	/// <summary>
	/// Runs every argument-level check both read tools share, in the order the caller-facing messages assume.
	/// </summary>
	/// <param name="args">Bound arguments.</param>
	/// <param name="argumentAliases">Tool-specific camelCase / snake_case spellings mapped to the kebab-case member.</param>
	/// <param name="validArgumentsHint">Tool-specific list of accepted argument names quoted back on an unbound member.</param>
	/// <returns><c>null</c> when the arguments are usable, otherwise the caller-facing message.</returns>
	internal static string ValidateArguments(
		ODataReadArgs args,
		IReadOnlyDictionary<string, string> argumentAliases,
		string validArgumentsHint) {
		if (args.ExtensionData?.ContainsKey("filter") == true) {
			return "Argument 'filter' is unsupported because raw filter strings are not accepted. " +
				"Use a structured filter, for example: " +
				"filters: {\"all\":[{\"field\":\"Name\",\"op\":\"eq\",\"value\":\"Acme\"}]}.";
		}
		string argumentError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData,
			argumentAliases,
			".",
			validArgumentsHint);
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

		string groupError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.Filters.ExtensionData,
			FilterGroupAliases,
			".",
			"Valid filter groups: all, any.");
		if (groupError is not null) {
			return $"filters: {groupError}";
		}

		List<(string Path, ODataFilterCondition Condition)> conditions = [];
		AddConditions(conditions, "filters.all", args.Filters.All);
		AddConditions(conditions, "filters.any", args.Filters.Any);
		if (conditions.Count == 0) {
			return "filters must contain at least one condition in all or any.";
		}
		foreach ((string path, ODataFilterCondition condition) in conditions) {
			string conditionError = ValidateCondition(path, condition);
			if (conditionError is not null) {
				return conditionError;
			}
		}
		return null;
	}

	/// <summary>
	/// Validates the members neither tool can build a URL without: the entity set name and the paging bound.
	/// </summary>
	/// <param name="args">Bound arguments.</param>
	/// <returns><c>null</c> when the target is usable, otherwise the caller-facing message.</returns>
	internal static string ValidateTarget(ODataReadArgs args) {
		if (string.IsNullOrWhiteSpace(args.Entity)) {
			return "entity is required.";
		}
		if (!ODataKeyFormatter.IsValidEntityName(args.Entity)) {
			return "entity must be a valid OData entity set name (letters, digits, underscore).";
		}
		if (args.Top is { } requestedTop && (requestedTop < MinTop || requestedTop > MaxTop)) {
			// An out-of-range top must NOT silently fall through to the default (which would
			// return a page when the caller asked for 0, or be misread as "all" on negatives).
			return $"top must be between {MinTop} and {MaxTop} (got {requestedTop}). Omit top to use the default of {DefaultTop}.";
		}
		return null;
	}

	private static void AddConditions(
		ICollection<(string Path, ODataFilterCondition Condition)> destination,
		string path,
		IReadOnlyList<ODataFilterCondition> conditions) {
		if (conditions is null) {
			return;
		}
		for (int index = 0; index < conditions.Count; index++) {
			destination.Add(($"{path}[{index}]", conditions[index]));
		}
	}

	private static string ValidateCondition(string path, ODataFilterCondition condition) {
		if (condition is null) {
			return $"{path} must be a filter condition object; null is not supported.";
		}
		string memberError = McpToolArgumentSupport.BuildLegacyAliasError(
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

	private static string JoinConditions(IReadOnlyList<string> conditions, string separator) {
		return conditions.Count switch {
			0 => null,
			1 => conditions[0],
			_ => $"({string.Join(separator, conditions)})"
		};
	}

	private static List<string> BuildConditions(IEnumerable<ODataFilterCondition> conditions) {
		if (conditions is null) {
			return [];
		}
		return conditions
			.Select(BuildCondition)
			.Where(condition => condition is not null)
			.ToList();
	}

	private static string BuildCondition(ODataFilterCondition c) {
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

	private static string BuildFilterFromStructured(ODataFilters filters) {
		List<string> andParts = BuildConditions(filters.All);
		List<string> orParts = BuildConditions(filters.Any);
		var parts = new List<string>();
		string allFilter = JoinConditions(andParts, " and ");
		if (allFilter is not null) {
			parts.Add(allFilter);
		}
		string anyFilter = JoinConditions(orParts, " or ");
		if (anyFilter is not null) {
			parts.Add(anyFilter);
		}
		return parts.Count > 0 ? string.Join(" and ", parts) : null;
	}

	/// <summary>Builds the OData query string (including the leading '?') for validated arguments.</summary>
	internal static string BuildQueryString(ODataReadArgs args) {
		var parts = new List<string>();

		string effectiveFilter = args.Filters is not null ? BuildFilterFromStructured(args.Filters) : null;
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
			parts.Add($"$orderby={Uri.EscapeDataString(args.OrderBy.Trim())}");
		}

		if (args.Skip is { } skip) {
			parts.Add($"$skip={skip}");
		}

		if (args.Count) {
			parts.Add("$count=true");
		}

		// ValidateTarget rejects an out-of-range top before reaching here, so top is either unset (default)
		// or already validated to be within [MinTop, MaxTop].
		int top = args.Top ?? DefaultTop;
		parts.Add($"$top={top}");

		return $"?{string.Join("&", parts)}";
	}

	/// <summary>Builds the environment-relative OData request path for validated arguments.</summary>
	internal static string BuildRequestPath(ODataReadArgs args) =>
		$"odata/{args.Entity.Trim()}{BuildQueryString(args)}";

	/// <summary>Parses a raw OData response body into the inline read response.</summary>
	internal static ODataReadResponse ParseODataResponse(string json, bool countRequested) {
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			if (ODataResponseError.TryDetect(root, out string serverError)) {
				// Redact like the sibling error paths: a routing Message can embed the absolute request
				// URI (host/port/app path), which must not leak into the MCP transcript or logs.
				return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact(serverError));
			}

			// A bare top-level array — some endpoints and $expand projections return one — is a COLLECTION,
			// not a single entity. Counting it as 1 reported a page of n rows as one record. The kind is also
			// checked before TryGetProperty, which throws on anything that is not an object.
			if (root.ValueKind == JsonValueKind.Array) {
				return ParseCollectionResponse(root, root, countRequested);
			}

			// Anything that is NOT a JSON object is rejected here rather than reported as one entity.
			// A scalar body — null, true, 42, "Unauthorized" — is what a proxy, an auth redirect or a
			// misrouted request returns; falling through to the single-entity branch reported those as
			// success with count=1, and with a file destination the scalar was persisted as OData output.
			if (root.ValueKind != JsonValueKind.Object) {
				return ODataReadResponse.Failure(
					$"OData response is a JSON {DescribeKind(root.ValueKind)}, not a record or a collection. " +
					"The endpoint did not answer with OData content; check the environment and the entity name.");
			}

			if (root.TryGetProperty("value", out JsonElement valueEl)) {
				return ParseCollectionResponse(root, valueEl, countRequested);
			}

			// Single-entity response (no value wrapper)
			return new ODataReadResponse(true, null, 1, root.Clone(), null);
		} catch (JsonException ex) {
			string preview = string.IsNullOrWhiteSpace(json) ? "<empty>" : json;
			if (preview.Length > 500) {
				preview = preview[..500] + "...";
			}
			return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact($"Failed to parse OData response: {ex.Message} | Response: {preview}"));
		}
	}

	/// <summary>Names a JSON kind the way the caller-facing rejection messages quote it.</summary>
	internal static string DescribeKind(JsonValueKind kind) => kind switch {
		JsonValueKind.Null => "null",
		JsonValueKind.True or JsonValueKind.False => "boolean",
		JsonValueKind.Number => "number",
		JsonValueKind.String => "string",
		_ => kind.ToString().ToLowerInvariant()
	};

	private static ODataReadResponse ParseCollectionResponse(
		JsonElement root,
		JsonElement valueElement,
		bool countRequested) {
		int count = valueElement.ValueKind == JsonValueKind.Array ? valueElement.GetArrayLength() : 1;
		// A bare top-level array carries no envelope, and TryGetProperty throws on a non-object, so the
		// annotations are read only when there is an object to read them from.
		bool hasEnvelope = root.ValueKind == JsonValueKind.Object;
		long? totalCount = hasEnvelope
			&& root.TryGetProperty("@odata.count", out JsonElement totalCountElement)
			&& totalCountElement.TryGetInt64(out long parsedTotalCount)
			? parsedTotalCount
			: null;
		if (countRequested && !totalCount.HasValue) {
			return ODataReadResponse.Failure(
				"Creatio did not return @odata.count for count=true; total count cannot be verified.");
		}
		string nextLink = hasEnvelope
			&& root.TryGetProperty("@odata.nextLink", out JsonElement nextLinkElement)
			&& nextLinkElement.ValueKind == JsonValueKind.String
			? nextLinkElement.GetString()
			: null;
		return new ODataReadResponse(true, null, count, valueElement.Clone(), nextLink, totalCount);
	}
}
