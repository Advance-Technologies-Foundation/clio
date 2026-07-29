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

	/// <summary>Reads Creatio records using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Query Creatio records via OData v4. " +
		"Supports filters, select, expand, order by and top. " +
		"top must be between 1 and 100 (default 25); an out-of-range top (including 0 or negative) is rejected, never silently widened. " +
		"Call get-tool-contract for odata-read to see usage examples and discovery workflow hints.")]
	public ODataReadResponse Read(
		[Description("Parameters: entity, environment-name (required); filters, select, expand, order-by, top (optional).")]
		[Required]
		ODataReadArgs args) {
		try {
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
			return ParseODataResponse(responseJson);
		} catch (Exception ex) {
			return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
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
		if (c.InValues.HasValue && c.InValues.Value.ValueKind == JsonValueKind.Array) {
			List<string> inParts = c.InValues.Value.EnumerateArray()
				.Select(v => $"{field} eq {LiteralFor(field, v)}")
				.ToList();
			return JoinConditions(inParts, " or ");
		}
		if (!c.Value.HasValue) {
			return null;
		}
		string op = string.IsNullOrWhiteSpace(c.Op) ? "eq" : c.Op;
		JsonElement val = c.Value.Value;
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

		// Read() rejects out-of-range top before reaching here, so top is either unset (default)
		// or already validated to be within [MinTop, MaxTop].
		int top = args.Top ?? DefaultTop;
		parts.Add($"$top={top}");

		return $"?{string.Join("&", parts)}";
	}

	private static ODataReadResponse ParseODataResponse(string json) {
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			if (ODataResponseError.TryDetect(root, out string serverError)) {
				// Redact like the sibling error paths: a routing Message can embed the absolute request
				// URI (host/port/app path), which must not leak into the MCP transcript or logs.
				return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact(serverError));
			}

			if (root.TryGetProperty("value", out JsonElement valueEl)) {
				int count = valueEl.ValueKind == JsonValueKind.Array ? valueEl.GetArrayLength() : 1;
				string? nextLink = root.TryGetProperty("@odata.nextLink", out JsonElement nl)
					? nl.GetString()
					: null;
				return new ODataReadResponse(true, null, count, valueEl.Clone(), nextLink);
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

}

/// <summary>
/// Arguments for <see cref="ODataReadTool"/>.
/// </summary>
public sealed record ODataReadArgs {
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

	/// <summary>Structured filter (alternative or addition to raw filter).</summary>
	[JsonPropertyName("filters")]
	[Description(
		"Structured filter (alternative or addition to raw filter). " +
		"all conditions join with AND; any conditions join with OR. " +
		"GUID values in Id-suffixed fields and navigation paths ending in Id are automatically unquoted; strings are single-quoted. " +
		"in array expands to OR-joined equality clauses. " +
		"Example: { \"all\": [{ \"field\": \"Account/Id\", \"op\": \"eq\", \"value\": \"8ecab4a1-0ca3-4515-9399-efe0a19390bd\" }] }")]
	public ODataFilters? Filters { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	public required string EnvironmentName { get; init; }
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
	string? NextLink = null) {

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
	public JsonElement? Value { get; init; }

	/// <summary>Array of values for in-list OR expansion.</summary>
	[JsonPropertyName("in")]
	[Description("Array of values that expand to OR-joined equality clauses: field eq v1 or field eq v2.")]
	public JsonElement? InValues { get; init; }
}

/// <summary>
/// Structured filter object for <see cref="ODataReadArgs.Filters"/>.
/// </summary>
public sealed record ODataFilters {
	/// <summary>Conditions joined with AND.</summary>
	[JsonPropertyName("all")]
	[Description("Conditions that must ALL match (AND-joined).")]
	public ODataFilterCondition[]? All { get; init; }

	/// <summary>Conditions joined with OR.</summary>
	[JsonPropertyName("any")]
	[Description("Conditions where ANY must match (OR-joined).")]
	public ODataFilterCondition[]? Any { get; init; }
}
