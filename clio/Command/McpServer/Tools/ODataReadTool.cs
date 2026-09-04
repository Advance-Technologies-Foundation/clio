using System;
using System.Collections.Generic;
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
	internal const int MinTop = ODataReadQuery.MinTop;

	/// <summary>Largest accepted value for the <c>top</c> argument.</summary>
	internal const int MaxTop = ODataReadQuery.MaxTop;

	/// <summary>Number of records returned when <c>top</c> is omitted.</summary>
	internal const int DefaultTop = ODataReadQuery.DefaultTop;

	private const string ValidArgumentsHint =
		"Valid: entity, environment-name, filters, select, expand, order-by, top, skip, count. " +
		"Raw filter strings are not supported; use the structured filters object. " +
		"To keep a large response on disk, call odata-read-to-file instead.";

	/// <summary>Reads Creatio records using OData v4.</summary>
	// ReadOnly and Idempotent are TRUE: this tool performs a GET and writes nothing, locally or remotely.
	// The file destination lives in odata-read-to-file precisely so THIS tool keeps the ordinary read
	// contract - raw-name compatibility, and the bounded retry-safe read semantics the MCP read-deadline
	// pipeline (McpReadResponseDeadline) only applies to a ReadOnly tool.
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Query Creatio records via OData v4. " +
		"Supports structured filters, select, expand, order by, top, skip, and total-count requests. " +
		"Read-only and retry-safe: it never writes, locally or remotely. " +
		"For a response too large to return inline, call odata-read-to-file, which takes the same arguments plus output-file. " +
		"top must be between 1 and 100 (default 25); an out-of-range top (including 0 or negative) is rejected, never silently widened. " +
		"skip must be zero or greater; use order-by with skip for stable paging. " +
		"Unknown arguments and malformed filter conditions fail before any Creatio request; raw filter strings are not supported. " +
		"Call get-tool-contract for odata-read to see usage examples and discovery workflow hints.")]
	public ODataReadResponse Read(
		[Description("Parameters: entity, environment-name (required); filters, select, expand, order-by, top, skip, count (optional).")]
		[Required]
		ODataReadArgs args) {
		try {
			string argumentError = ODataReadQuery.ValidateArguments(args, ODataReadQuery.SharedArgumentAliases, ValidArgumentsHint)
				?? ODataReadQuery.ValidateTarget(args);
			if (argumentError is not null) {
				return ODataReadResponse.Failure(argumentError);
			}

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			string url = urlBuilder.Build(ODataReadQuery.BuildRequestPath(args));
			string responseJson = client.ExecuteGetRequest(url, 30_000);
			return ODataReadQuery.ParseODataResponse(responseJson, args.Entity.Trim(), args.Count);
		} catch (Exception ex) {
			return ODataReadResponse.Failure(SensitiveErrorTextRedactor.Redact(ex.Message));
		}
	}

}

/// <summary>
/// Arguments for <see cref="ODataReadTool"/>.
/// </summary>
public record ODataReadArgs {
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
	long? TotalCount = null,

	[property: JsonPropertyName("output-file")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Absolute path to the raw OData response written to disk.")]
	string? OutputFile = null,

	[property: JsonPropertyName("row-count")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Number of object rows written to output-file.")]
	int? RowCount = null,

	[property: JsonPropertyName("column-sizes")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("UTF-8 byte totals by column for rows written to output-file.")]
	IReadOnlyDictionary<string, long>? ColumnSizes = null) {

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
