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
public sealed class ODataReadTool(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IToolCommandResolver commandResolver) {

	internal const string ToolName = "odata-read";

	/// <summary>Reads Creatio records using OData v4.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description(
		"Query Creatio records via OData v4. " +
		"Supports $filter, $select, $expand, $orderby, and $top. " +
		"Workflow: 1) call dataforge-find-tables to discover entity names, " +
		"2) call dataforge-get-table-columns to inspect fields, " +
		"3) call odata-read with select, filter, expand, order-by, and top. " +
		"When using $filter with $select, always include filtered fields in select. " +
		"GUID fields use no quotes: AccountId eq 8ecab4a1-0ca3-4515-9399-efe0a19390bd. " +
		"String fields use single quotes: Name eq 'Acme'. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap or emergency fallback flows.")]
	public ODataReadResponse Read(
		[Description("Parameters: entity (required), plus optional filter/select/expand/order-by/top and environment connection args.")]
		[Required]
		ODataReadArgs args) {
		try {
			if (string.IsNullOrWhiteSpace(args.Entity)) {
				return ODataReadResponse.Failure("entity is required.");
			}

			IApplicationClient client;
			IServiceUrlBuilder urlBuilder;

			if (HasExplicitTarget(args)) {
				EnvironmentOptions options = ToEnvironmentOptions(args);
				client = commandResolver.Resolve<IApplicationClient>(options);
				urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);
			} else {
				client = applicationClient;
				urlBuilder = serviceUrlBuilder;
			}

			string queryString = BuildQueryString(args);
			string path = $"odata/{args.Entity.Trim()}{queryString}";
			string url = urlBuilder.Build(path);

			string responseJson = client.ExecuteGetRequest(url, 30_000);
			return ParseODataResponse(responseJson);
		} catch (Exception ex) {
			return ODataReadResponse.Failure(ex.Message);
		}
	}

	private static string BuildQueryString(ODataReadArgs args) {
		var parts = new List<string>();

		if (!string.IsNullOrWhiteSpace(args.Filter)) {
			parts.Add($"$filter={Uri.EscapeDataString(args.Filter!.Trim())}");
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

		int top = args.Top is > 0 and <= 1000 ? args.Top.Value : 25;
		parts.Add($"$top={top}");

		return parts.Count > 0 ? $"?{string.Join("&", parts)}" : string.Empty;
	}

	private static ODataReadResponse ParseODataResponse(string json) {
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

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
			return ODataReadResponse.Failure($"Failed to parse OData response: {ex.Message}");
		}
	}

	private static EnvironmentOptions ToEnvironmentOptions(ODataReadArgs args) =>
		new() {
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};

	private static bool HasExplicitTarget(ODataReadArgs args) =>
		!string.IsNullOrWhiteSpace(args.EnvironmentName)
		|| !string.IsNullOrWhiteSpace(args.Uri)
		|| !string.IsNullOrWhiteSpace(args.Login)
		|| !string.IsNullOrWhiteSpace(args.Password);
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

	/// <summary>Raw OData $filter clause.</summary>
	[JsonPropertyName("filter")]
	[Description(
		"OData $filter clause. " +
		"GUID fields (Id, AccountId, etc.): no quotes — AccountId eq 8ecab4a1-0ca3-4515-9399-efe0a19390bd. " +
		"String fields: single quotes — Name eq 'Acme'. " +
		"Operators: eq, ne, gt, ge, lt, le, and, or, not, contains, startswith, endswith. " +
		"When $select is also set, include all filtered fields in select.")]
	public string? Filter { get; init; }

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

	/// <summary>Maximum number of records to return (1–1000, default 25).</summary>
	[JsonPropertyName("top")]
	[Description("Maximum number of records to return. Range: 1–1000. Default: 25.")]
	public int? Top { get; init; }

	/// <summary>Registered clio environment name.</summary>
	[JsonPropertyName("environment-name")]
	[Description("Registered clio environment name, e.g. 'dev_5001'. Preferred for normal MCP work.")]
	public string? EnvironmentName { get; init; }

	/// <summary>Direct Creatio URL (emergency fallback).</summary>
	[JsonPropertyName("uri")]
	[Description("Direct Creatio URL. Use only when bootstrap is broken or before the environment can be registered through reg-web-app.")]
	public string? Uri { get; init; }

	/// <summary>Direct login (emergency fallback).</summary>
	[JsonPropertyName("login")]
	[Description("Direct Creatio login paired with `uri`. Emergency fallback only.")]
	public string? Login { get; init; }

	/// <summary>Direct password (emergency fallback).</summary>
	[JsonPropertyName("password")]
	[Description("Direct Creatio password paired with `uri`. Emergency fallback only.")]
	public string? Password { get; init; }
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
