using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
	IToolCommandResolver commandResolver,
	IOperationCorrelationIdProvider correlationIds,
	ILogger logger) {

	internal const string ToolName = "odata-read";

	/// <summary>Smallest accepted value for the <c>top</c> argument.</summary>
	internal const int MinTop = 1;

	/// <summary>Largest accepted value for the <c>top</c> argument.</summary>
	internal const int MaxTop = 100;

	/// <summary>Number of records returned when <c>top</c> is omitted.</summary>
	internal const int DefaultTop = 25;

	/// <summary>The lookup-column suffix the navigation-path hint keys on.</summary>
	private const string IdSuffix = "Id";

	private const string ValidArgumentsHint =
		"Valid: entity, environment-name, filters, select, expand, order-by, top, skip, count. " +
		"Raw filter strings are not supported; use the structured filters object. " +
		"To keep a large response on disk, call odata-read-to-file instead.";

	/// <summary>Alias map shared with <see cref="ODataReadToFileTool"/>, which extends it with its own keys.</summary>
	internal static readonly IReadOnlyDictionary<string, string> ArgumentAliases =
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
		McpToolDescriptions.CorrelationIdOnEveryResponse +
		"A failure also carries a machine-readable error-code: argument, entity-not-found, invalid-query, server-reported-error, non-json-response, incomplete-response, or transport. " +
		"error-code invalid-query usually means the request shape is at fault, and the message lists the field, select, expand and order-by names YOU sent; when a filter field looks like a raw lookup column (a name ending in Id) it names the navigation path to use instead, for example AccountId -> Account/Id. " +
		"The one exception is a member added moments ago: the same rejection is how a column or entity that exists but is not published to OData yet reports itself, so wait for the rebuild and retry once before changing the query - and never re-run a schema write in response. " +
		"Creatio's own error wording is never reproduced in error; it is written to clio's debug log, which requires clio to be running in-process with --debug and a log sink, so in an MCP worker there is no such line and the correlation-id serves to match this response to clio's own logs. " +
		"Call get-tool-contract for odata-read to see usage examples and discovery workflow hints.")]
	public ODataReadResponse Read(
		[Description("Parameters: entity, environment-name (required); filters, select, expand, order-by, top, skip, count (optional).")]
		[Required]
		ODataReadArgs args) {
		//One place mints the id and one place stamps it, so no failure path can return a response
		//without the correlation-id core-rules promises on EVERY response.
		string correlationId = correlationIds.New();
		return ReadCore(args, correlationId) with { CorrelationId = correlationId };
	}

	private ODataReadResponse ReadCore(ODataReadArgs args, string correlationId) {
		try {
			string? argumentError = ValidateAndNormalizeArguments(args, ArgumentAliases, ValidArgumentsHint,
				out string[]? selectColumns, out string[]? expandColumns);
			if (argumentError is not null) {
				return ODataReadResponse.Failure(argumentError, ODataReadErrorCodes.Argument);
			}
			string? targetError = ValidateTarget(args);
			if (targetError is not null) {
				//entity travels only once the NAME ITSELF has been accepted. The contract on the member
				//says so - "an argument-level rejection (a bad or missing entity ...) is refused before
				//that point and carries no entity" - so a rejected name must not be echoed back as the
				//entity this failure is about. Only the top-range refusal reaches here with a usable one.
				return ODataReadResponse.Failure(targetError, ODataReadErrorCodes.Argument,
					entity: IsEntityNameAccepted(args) ? args.Entity.Trim() : null);
			}

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			string url = urlBuilder.Build(BuildRequestPath(args, selectColumns, expandColumns));

			string responseJson = client.ExecuteGetRequest(url, 30_000);
			return ParseODataResponse(responseJson, args, correlationId);
		} catch (Exception ex) {
			//The transport swallows connection failures into an empty body, so what reaches here is
			//mostly environment resolution and client construction - still a transport-class failure
			//from the caller's point of view, and never a query-shape one.
			return ODataReadResponse.Failure(
				SensitiveErrorTextRedactor.Redact(ex.Message), ODataReadErrorCodes.Transport,
				entity: string.IsNullOrWhiteSpace(args.Entity) ? null : args.Entity.Trim());
		}
	}

	/// <summary>
	/// Writes the server's own error text to the ONE channel allowed to carry it - <c>WriteDebug</c>,
	/// which the console drops unless --debug was passed - fenced as observed data, and tagged with the
	/// correlation ID that also appears on the failure envelope. That ID is the only bridge between the
	/// locally authored sentence a caller reads and the server excerpt an operator can look up.
	/// </summary>
	private void WriteServerDetailToDebugChannel(string correlationId, string serverDetail) {
		string? safeDetail = UntrustedText.Fenced(serverDetail);
		if (safeDetail is null) {
			return;
		}
		logger.WriteDebug($"(correlation-id: {correlationId}) odata-read server detail: {safeDetail}");
	}

	/// <summary>
	/// Validates the READ TARGET - the entity set name and the page size - for either read path.
	/// </summary>
	/// <remarks>
	/// Shared with <see cref="ODataReadToFileTool"/> so the two read paths refuse the same targets with
	/// the same wording; a second copy drifted the moment one of them was edited.
	/// </remarks>
	/// <param name="args">The bound tool arguments.</param>
	/// <returns>The contract message when the target is not accepted; otherwise null.</returns>
	internal static string? ValidateTarget(ODataReadArgs args) {
		if (!IsEntityNameAccepted(args)) {
			return string.IsNullOrWhiteSpace(args.Entity)
				? "entity is required."
				: "entity must be a valid OData entity set name (letters, digits, underscore).";
		}
		if (args.Top is { } requestedTop && (requestedTop < MinTop || requestedTop > MaxTop)) {
			// An out-of-range top must NOT silently fall through to the default (which would
			// return a page when the caller asked for 0, or be misread as "all" on negatives).
			return $"top must be between {MinTop} and {MaxTop} (got {requestedTop}). "
				+ $"Omit top to use the default of {DefaultTop}.";
		}
		return null;
	}

	/// <summary>
	/// Whether the requested entity set NAME was supplied and is well formed - which is what decides
	/// whether a failure may name an entity at all.
	/// </summary>
	/// <param name="args">The bound tool arguments.</param>
	internal static bool IsEntityNameAccepted(ODataReadArgs args) =>
		!string.IsNullOrWhiteSpace(args.Entity) && ODataKeyFormatter.IsValidEntityName(args.Entity);

	/// <summary>
	/// Validates the supplied arguments and hands back the normalized column lists.
	/// </summary>
	/// <remarks>
	/// The normalized lists are OUT parameters rather than members written back onto
	/// <see cref="ODataReadArgs"/>: the args record is the caller's request, and a validator that
	/// mutates it makes the bound request and the request that was executed two different things -
	/// with no way for a test, a log or a second validation pass to tell which one it is looking at.
	/// </remarks>
	/// <param name="args">The bound tool arguments.</param>
	/// <param name="argumentAliases">Alias map for the CALLING tool, whose accepted argument set may be wider.</param>
	/// <param name="validArgumentsHint">The calling tool's list of accepted arguments, quoted in the rejection.</param>
	/// <param name="selectColumns">The normalized $select list, or null when select was omitted.</param>
	/// <param name="expandColumns">The normalized $expand list, or null when expand was omitted.</param>
	/// <returns>The contract message when an argument is not accepted; otherwise null.</returns>
	internal static string? ValidateAndNormalizeArguments(ODataReadArgs args,
			IReadOnlyDictionary<string, string> argumentAliases, string validArgumentsHint,
			out string[]? selectColumns, out string[]? expandColumns) {
		selectColumns = null;
		expandColumns = null;
		if (args.ExtensionData?.ContainsKey("filter") == true) {
			return "Argument 'filter' is unsupported because raw filter strings are not accepted. " +
				"Use a structured filter, for example: " +
				"filters: {\"all\":[{\"field\":\"Name\",\"op\":\"eq\",\"value\":\"Acme\"}]}.";
		}
		string? argumentError = McpToolArgumentSupport.BuildLegacyAliasError(
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
		if (!TryNormalizeColumnList(args.Select, "select", out selectColumns, out string? selectError)) {
			return selectError;
		}
		if (!TryNormalizeColumnList(args.Expand, "expand", out expandColumns, out string? expandError)) {
			return expandError;
		}
		//Validated on the NORMALIZED lists so both accepted shapes - the array and the comma-separated
		//string - are held to the same member-path rule.
		string? projectionError = ValidateMemberList("select", selectColumns)
			?? ValidateMemberList("expand", expandColumns)
			?? ValidateOrderBy(args.OrderBy);
		if (projectionError is not null) {
			return projectionError;
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

	/// <summary>
	/// Applies the filter-field rule to <c>select</c> and <c>expand</c>: every element must be a plain
	/// OData member path.
	/// </summary>
	/// <remarks>
	/// These were the only caller-supplied names that reached the query string unchecked, and since
	/// GH-1407 they are also echoed back in an invalid-query failure. Rejecting them here means one rule
	/// covers every name the tool names back, and an embedded query option or grammar fragment fails
	/// locally - with error-code argument - instead of being pasted into a URL for the server to reject
	/// with a message the caller is not allowed to read. It runs on the NORMALIZED list, so the
	/// comma-separated string form is split into names first and each name is then held to the same rule.
	/// </remarks>
	private static string? ValidateMemberList(string argumentName, IReadOnlyList<string>? members) {
		if (members is null) {
			return null;
		}
		for (int index = 0; index < members.Count; index++) {
			if (!ODataKeyFormatter.IsValidMemberPath(members[index])) {
				return $"{argumentName}[{index}] must be an OData member path containing only letters, digits, "
					+ "underscores, and '/' separators; nested query options are not supported.";
			}
		}
		return null;
	}

	/// <summary>Accepted sort directions in an <c>order-by</c> clause.</summary>
	private static readonly HashSet<string> SortDirections = new(StringComparer.OrdinalIgnoreCase) { "asc", "desc" };

	/// <summary>
	/// Validates <c>order-by</c> as a comma-separated list of <c>&lt;member path&gt; [asc|desc]</c>.
	/// </summary>
	private static string? ValidateOrderBy(string? orderBy) {
		if (string.IsNullOrWhiteSpace(orderBy)) {
			return null;
		}
		foreach (string clause in orderBy.Split(',')) {
			string[] parts = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			bool isWellFormed = parts.Length is 1 or 2
				&& ODataKeyFormatter.IsValidMemberPath(parts[0])
				&& (parts.Length == 1 || SortDirections.Contains(parts[1]));
			if (!isWellFormed) {
				return "order-by must be a comma-separated list of '<field> [asc|desc]' clauses, where each "
					+ "field is an OData member path containing only letters, digits, underscores, and '/' separators.";
			}
		}
		return null;
	}

	/// <summary>
	/// Accepts a column list in either shape the caller may reasonably send: a JSON array of strings,
	/// or the comma-separated form OData itself uses in <c>$select</c>/<c>$expand</c>. Blank entries are
	/// dropped so a trailing comma is not turned into an empty column.
	/// </summary>
	/// <remarks>
	/// The argument is bound as a <see cref="JsonElement"/> rather than a <c>string[]</c> on purpose.
	/// A typed array made the serializer reject the string form before the tool ever ran, so the caller
	/// got "The JSON value could not be converted to System.String[]" - a serializer message about a
	/// .NET type, not a statement about this tool's contract. Binding loosely and validating here is
	/// what lets the contract message be written locally. Doing the split in a custom converter would
	/// reintroduce the same defect, because a converter can only reject by throwing a JsonException.
	/// </remarks>
	/// <param name="value">The raw JSON value supplied for the argument, if any.</param>
	/// <param name="argumentName">The argument name used in the contract message.</param>
	/// <param name="columns">The normalized column list, or null when the argument was omitted or empty.</param>
	/// <param name="error">The contract message when the shape is not accepted.</param>
	/// <returns><see langword="false"/> when the supplied shape is not accepted.</returns>
	internal static bool TryNormalizeColumnList(JsonElement? value, string argumentName,
			out string[]? columns, out string? error) {
		columns = null;
		error = null;
		if (value is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) {
			return true;
		}
		List<string> parsed = [];
		switch (element.ValueKind) {
			case JsonValueKind.String:
				string? raw = element.GetString();
				if (!string.IsNullOrWhiteSpace(raw)) {
					parsed.AddRange(raw.Split(',',
						StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
				}
				break;
			case JsonValueKind.Array:
				//An array element is taken as ONE column name, never split. The caller who writes
				//["Id,Name"] has already chosen the array shape, so a comma inside an element is part of
				//the name they asked for; splitting it would silently rewrite the request instead of
				//letting the server reject the name the caller actually wrote.
				foreach (JsonElement item in element.EnumerateArray()) {
					if (item.ValueKind != JsonValueKind.String) {
						error = ColumnListContractError(argumentName);
						return false;
					}
					string? name = item.GetString();
					if (!string.IsNullOrWhiteSpace(name)) {
						parsed.Add(name.Trim());
					}
				}
				break;
			default:
				error = ColumnListContractError(argumentName);
				return false;
		}
		columns = parsed.Count > 0 ? parsed.ToArray() : null;
		return true;
	}

	/// <summary>
	/// The contract message for a select/expand value that is neither an array of names nor a
	/// comma-separated string. Exposed to the tests so the wording is asserted from its single source
	/// instead of being retyped and drifting on the next rewording.
	/// </summary>
	internal static string ColumnListContractError(string argumentName) =>
		$"{argumentName} must be an array of column names or a comma-separated string, "
		+ $"for example [\"Id\",\"Name\"] or \"Id,Name\".";

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

	/// <summary>Builds the OData request path both read paths issue, including the query string.</summary>
	/// <param name="args">The bound tool arguments.</param>
	/// <param name="selectColumns">Normalized $select list, or null.</param>
	/// <param name="expandColumns">Normalized $expand list, or null.</param>
	internal static string BuildRequestPath(ODataReadArgs args, string[]? selectColumns, string[]? expandColumns) =>
		$"odata/{args.Entity.Trim()}{BuildQueryString(args, selectColumns, expandColumns)}";

	private static string BuildQueryString(ODataReadArgs args, string[]? selectColumns,
			string[]? expandColumns) {
		var parts = new List<string>();

		string? effectiveFilter = args.Filters is not null ? BuildFilterFromStructured(args.Filters) : null;
		if (effectiveFilter is not null) {
			parts.Add($"$filter={Uri.EscapeDataString(effectiveFilter)}");
		}

		if (selectColumns is { Length: > 0 }) {
			parts.Add($"$select={Uri.EscapeDataString(string.Join(",", selectColumns))}");
		}

		if (expandColumns is { Length: > 0 }) {
			parts.Add($"$expand={Uri.EscapeDataString(string.Join(",", expandColumns))}");
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

	/// <summary>
	/// The read path's wording for an HTML error page, composed from the two facts the classifier
	/// returns: whether the body is such a page, and the HTTP status its title states.
	/// </summary>
	/// <remarks>
	/// The text is built here rather than in <see cref="CreatioResponseError"/> because it is this
	/// tool's contract - it names the entity the caller asked for and steers to execute-esq - while the
	/// pre-write probe has to say something different about the very same page.
	/// </remarks>
	internal static string DescribeMarkupError(string entityName, int? statusCode) {
		string entity = string.IsNullOrWhiteSpace(entityName) ? "<unnamed>" : entityName;
		return statusCode switch {
			(int)HttpStatusCode.NotFound =>
				$"Entity '{entity}' is not exposed over OData on this environment (HTTP "
				+ $"{(int)HttpStatusCode.NotFound}). {CreatioResponseError.UnregisteredEntityHint} Use "
				+ "execute-esq to read schemas that never get an OData entity set.",
			{ } knownStatus =>
				$"The OData request for entity '{entity}' was answered with an "
				+ $"{CreatioResponseError.MarkupStatusPhrase(knownStatus)} instead of an OData response. Verify the "
				+ "environment URL, the authentication and any proxy.",
			//No status in the title means no diagnosis beyond "this was not an OData response". Creatio's
			//own outage page (<title>Request Error</title>) and an SSO/proxy login page both land here,
			//and neither says anything about whether the entity has an OData controller - claiming it
			//"may not be exposed" and steering the caller onto execute-esq would be a guess that costs
			//them the actual cause (an outage, an expired session).
			_ => CreatioResponseError.DescribeNonJsonReadResponse()
		};
	}

	private ODataReadResponse ParseODataResponse(string json, ODataReadArgs args, string correlationId) {
		string entityName = args.Entity.Trim();
		bool countRequested = args.Count;
		//Every failure from here on refers to a specific entity set, and a caller correlating several
		//reads needs to know which one - so entity is stamped once, at the single exit point, instead of
		//being remembered at each of the seven of them.
		ODataReadResponse Fail(string message, string errorCode) =>
			ODataReadResponse.Failure(message, errorCode, entity: entityName);
		// ExecuteGetRequest may return null (the interface permits it; reauth and proxy failures do produce
		// it). The absence of a body has to be classified HERE: the IIS-404 probe below dereferences the
		// string, so a null would raise an NRE that escapes the body-suppression invariant and reaches
		// Read()'s outer catch as an opaque message. An empty body already resolved to this same failure.
		if (string.IsNullOrWhiteSpace(json)) {
			return Fail(CreatioResponseError.DescribeEmptyReadResponse(), ODataReadErrorCodes.Transport);
		}
		if (CreatioResponseError.TryClassifyMarkupError(json, out int? markupStatusCode)) {
			//The status travels as its own member, not only inside the prose: the documented async-gap
			//retry after create-entity-schema/create-lookup has to key off "404" programmatically, and a
			//caller cannot reliably do that by matching on a message it does not own.
			return ODataReadResponse.Failure(DescribeMarkupError(entityName, markupStatusCode),
				markupStatusCode == (int)HttpStatusCode.NotFound
					? ODataReadErrorCodes.EntityNotFound
					: ODataReadErrorCodes.Transport,
				markupStatusCode, entityName);
		}

		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			//A response whose @odata.context proves it IS the requested top-level entity is that entity,
			//whatever its columns happen to be called. Running the error-member heuristics first rejected
			//a genuine record with a legal persisted column named ExceptionMessage, ExceptionType or
			//StackTrace as a server error. A real OData error envelope carries no matching context, so it
			//loses nothing by being classified second.
			bool hasMatchingIdentity = HasMatchingODataIdentity(root, entityName);

			//The detected text is server-controlled prose and is dropped, not redacted: the redactor
			//removes known secret shapes, but arbitrary instructions, opaque tokens, tenant data and
			//line breaks survive it, and this transcript is read as trusted content by a model.
			if (!hasMatchingIdentity && CreatioResponseError.TryClassify(root,
					CreatioResponseContext.ODataPayload, out ODataErrorKind kind, out string serverDetail)) {
				WriteServerDetailToDebugChannel(correlationId, serverDetail);
				//entity travels even though status-code cannot: Creatio serves the JSON routing 404 with
				//HTTP 200, so there is no status to report, but the failure still refers to a specific
				//entity set and a caller correlating several reads needs to know which one.
				return Fail(
					CreatioResponseError.DescribeServerReportedReadError(kind, DescribeCallerQuery(args, kind)),
					ErrorCodeFor(kind));
			}

			//A collection response carries `value` as an ARRAY. Accepting any `value` meant a proxy or
			//auth body such as {"value":"private response marker"} came back as success:true with the
			//marker as the payload, and clio-run then forwarded it without failure redaction.
			if (root.TryGetProperty("value", out JsonElement valueEl)) {
				return valueEl.ValueKind == JsonValueKind.Array
					&& IsCollectionResponse(root, entityName)
					? ParseCollectionResponse(root, valueEl, entityName, countRequested)
					: Fail(CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse);
			}

			//Single-entity response (no value wrapper). Only OData identifies itself as one: the
			//@odata.context annotation ends with "/$entity". Without that check ANY parsed JSON object
			//was a successful record - {"detail":"private response marker"} included.
			return IsSingleEntityResponse(root, entityName)
				? new ODataReadResponse(true, null, 1, root.Clone(), null)
				: Fail(CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse);
		} catch (Exception) {
			// EVERY parse failure gets the same fixed diagnostic, carrying no fragment of the body. Testing
			// the first character was not enough: a malformed body that still starts with '{' or '[' — a
			// truncated proxy response, say — fell through to a preview that copied arbitrary server or
			// proxy content into the MCP transcript. The redactor strips known secret shapes, not tenant
			// data it has never seen, so the body cannot be quoted at all. The exception message is
			// dropped with it: a parse position is of no use to a caller who cannot see the body anyway.
			return Fail(CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse);
		}
	}

	/// <summary>
	/// True when the body identifies itself as an OData single-entity response. Creatio serves reads
	/// under the default metadata level, so a genuine entity always carries an @odata.context whose
	/// value ends with "/$entity"; nothing else may be treated as a record.
	/// </summary>
	/// <summary>
	/// True when the body identifies itself as an OData COLLECTION response for the entity that was
	/// requested: the <c>@odata.context</c> annotation names that entity set, optionally followed by a
	/// projection such as <c>(Id,Name)</c> from $select/$expand.
	/// </summary>
	/// <remarks>
	/// An array-valued <c>value</c> alone was not enough. A proxy or auth body shaped as
	/// <c>{"value":[{"detail":"private response marker"}]}</c> satisfied it, came back as
	/// <c>success:true</c>, and clio-run forwarded the marker as a read result. Creatio's
	/// default-metadata responses always carry the context, which is what the single-entity path
	/// already requires.
	/// </remarks>
	internal static bool IsCollectionResponse(JsonElement root, string entityName) =>
		MatchesTopLevelContext(root, entityName, singleEntity: false);

	/// <summary>
	/// Reads the entity-set name out of the <c>@odata.context</c> annotation: the fragment after
	/// <c>#</c>, cut at a projection such as <c>(Id,Name)</c> or at a trailing segment such as
	/// <c>/$entity</c>.
	/// </summary>
	private static bool MatchesTopLevelContext(JsonElement root, string entityName, bool singleEntity) {
		if (!TryGetContextFragment(root, out string fragment)) {
			return false;
		}
		if (singleEntity) {
			if (!fragment.EndsWith(SingleEntitySuffix, StringComparison.Ordinal)) {
				return false;
			}
			fragment = fragment[..^SingleEntitySuffix.Length];
		} else if (fragment.EndsWith(SingleEntitySuffix, StringComparison.Ordinal)) {
			//A collection response never terminates in /$entity.
			return false;
		}
		//What may remain is the entity set plus at most one parenthesised projection. Anything after the
		//closing parenthesis - a navigation segment, a second predicate - is not a top-level read of this
		//set.
		int projectionStart = fragment.IndexOf('(', StringComparison.Ordinal);
		string entitySet = projectionStart < 0 ? fragment : fragment[..projectionStart];
		if (projectionStart >= 0 && !IsBalancedTrailingProjection(fragment, projectionStart)) {
			return false;
		}
		//A containment or navigation suffix leaves a '/' behind once the projection is accounted for.
		return entitySet.Length > 0
			&& entitySet.IndexOf('/', StringComparison.Ordinal) < 0
			&& string.Equals(entitySet, entityName, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// True when the projection that starts at <paramref name="projectionStart"/> is balanced and closes
	/// on the last character of <paramref name="fragment"/>.
	/// </summary>
	/// <remarks>
	/// The projection's own grammar is deliberately left opaque. $expand makes Creatio answer with a
	/// nested list - <c>#Contact(Id,Name,AccountId,Account())</c> for <c>$expand=Account</c> - and OData
	/// also allows nested select lists and navigation paths in there, so rejecting a parenthesis or a
	/// slash anywhere inside discarded genuine rows. What still has to be rejected is everything the
	/// projection is not: an unbalanced fragment, and any suffix after it such as a navigation segment
	/// or a second predicate, which is what the "closes on the last character" requirement covers.
	/// </remarks>
	private static bool IsBalancedTrailingProjection(string fragment, int projectionStart) {
		int depth = 0;
		for (int index = projectionStart; index < fragment.Length; index++) {
			switch (fragment[index]) {
				case '(':
					depth++;
					break;
				case ')':
					depth--;
					if (depth < 0) {
						return false;
					}
					if (depth == 0) {
						//An empty projection names nothing, and anything past the closing parenthesis
						//puts this fragment outside a top-level read of the set.
						return index > projectionStart + 1 && index == fragment.Length - 1;
					}
					break;
			}
		}
		return false;
	}

	private const string SingleEntitySuffix = "/$entity";

	/// <summary>True when the body is a top-level read of the requested set, in either shape.</summary>
	internal static bool HasMatchingODataIdentity(JsonElement root, string entityName) =>
		root.ValueKind == JsonValueKind.Object
		&& (MatchesTopLevelContext(root, entityName, singleEntity: true)
			|| MatchesTopLevelContext(root, entityName, singleEntity: false));

	/// <summary>
	/// Reads the whole <c>@odata.context</c> fragment - everything after the last <c>#</c> - without
	/// cutting it at the first separator.
	/// </summary>
	private static bool TryGetContextFragment(JsonElement root, out string fragment) {
		fragment = string.Empty;
		if (!root.TryGetProperty("@odata.context", out JsonElement context)
			|| context.ValueKind != JsonValueKind.String
			|| context.GetString() is not { } contextValue) {
			return false;
		}
		//$metadata itself sits behind a '#' ("...$metadata#Contact"), so the LAST '#' opens the fragment
		//that names the set.
		int fragmentStart = contextValue.LastIndexOf('#');
		if (fragmentStart < 0) {
			return false;
		}
		fragment = contextValue[(fragmentStart + 1)..];
		return fragment.Length > 0;
	}

	/// <summary>
	/// True when the body identifies itself as an OData single-entity response for the entity that
	/// was REQUESTED. The <c>/$entity</c> suffix alone was not enough: a body answering
	/// <c>...#$metadata#Account/$entity</c> to a read of <c>Contact</c> came back as
	/// <c>success:true</c>, which forwards an unrelated - possibly proxy-controlled - record into the
	/// MCP transcript as the requested data. The collection branch already checks the entity set; so
	/// does this one now.
	/// </summary>
	internal static bool IsSingleEntityResponse(JsonElement root, string entityName) =>
		root.ValueKind == JsonValueKind.Object
		&& MatchesTopLevelContext(root, entityName, singleEntity: true);

	/// <summary>Maps a classified server error onto this tool's machine-readable error-code.</summary>
	private static string ErrorCodeFor(ODataErrorKind kind) => kind switch {
		//A routing miss IS a missing entity set - the same condition the IIS 404 page reports - so the
		//two paths must not hand the caller two different codes for one cause.
		ODataErrorKind.UnregisteredEntity => ODataReadErrorCodes.EntityNotFound,
		ODataErrorKind.InvalidQuery => ODataReadErrorCodes.InvalidQuery,
		_ => ODataReadErrorCodes.ServerReportedError
	};

	/// <summary>
	/// Restates the CALLER's own query members - the filter fields, select, expand and order-by names
	/// that arrived in this request - and adds the lookup-column hint when one of the filter fields
	/// looks like a raw foreign-key column.
	/// </summary>
	/// <remarks>
	/// Every character here comes from the request, never from the response, which is what makes it
	/// legal to print: reproducing the server's own wording would put third-party prose into an MCP
	/// transcript. It is also what makes it USEFUL - the server's text is withheld, so without this the
	/// caller cannot tell which of several names the server rejected. Filter FIELDS have additionally
	/// passed <c>ODataKeyFormatter.IsValidMemberPath</c> (letters, digits, underscore and '/') before
	/// reaching here; select, expand and order-by are echoed exactly as the caller sent them, which is
	/// safe for the same reason it is useful - they are the caller's own text going back to the caller.
	/// </remarks>
	private static string? DescribeCallerQuery(ODataReadArgs args, ODataErrorKind kind) {
		//ONLY a classified query-shape failure earns any of this. On an unregistered entity set the query
		//was never the problem, and on an unclassified server fault - a measured
		//{"Message":"An error has occurred.","ExceptionMessage":"Object reference ..."} body, say - the
		//query is very probably correct: listing its members reads as an accusation, and a field that
		//"looks like" a lookup column may be an ordinary persisted column (TaxId, ExternalId) the caller
		//would then rewrite as Tax/Id over a failure that had nothing to do with it.
		if (kind != ODataErrorKind.InvalidQuery) {
			return null;
		}
		IReadOnlyList<string> filterFields = CollectFilterFields(args);
		List<string> sentParts = [];
		if (filterFields.Count > 0) {
			sentParts.Add($"filter fields: {string.Join(", ", filterFields)}");
		}
		//Read back through the same normalizer the request used, so the names echoed here are the names
		//that actually reached $select/$expand - whichever of the two accepted shapes the caller sent.
		if (TryNormalizeColumnList(args.Select, "select", out string[]? select, out _) && select is { Length: > 0 }) {
			sentParts.Add($"select: {string.Join(", ", select)}");
		}
		if (TryNormalizeColumnList(args.Expand, "expand", out string[]? expand, out _) && expand is { Length: > 0 }) {
			sentParts.Add($"expand: {string.Join(", ", expand)}");
		}
		if (!string.IsNullOrWhiteSpace(args.OrderBy)) {
			sentParts.Add($"order-by: {args.OrderBy.Trim()}");
		}
		List<string> described = [];
		if (sentParts.Count > 0) {
			described.Add($"Names this request sent - {string.Join("; ", sentParts)}.");
		}
		string? lookupHint = BuildLookupColumnHint(filterFields);
		if (lookupHint is not null) {
			described.Add(lookupHint);
		}
		return described.Count > 0 ? string.Join(" ", described) : null;
	}

	/// <summary>
	/// The hint for the commonest cause of a rejected filter: a raw foreign-key column such as
	/// <c>SysSettingsId</c>, which OData does not expose as a property - the reference is reachable only
	/// through the navigation path <c>SysSettings/Id</c>.
	/// </summary>
	private static string? BuildLookupColumnHint(IReadOnlyList<string> filterFields) {
		List<string> suspects = filterFields
			//IsIdish, not a bare EndsWith("Id"): the suffix has to sit on a word boundary, or Paid, Void
			//and Grid each earn a hint telling the caller to filter on "Pa/Id". The length test excludes
			//the primary key itself, which is not a lookup - "Id" is not LONGER than "Id".
			.Where(field => field.Length > IdSuffix.Length
				&& ODataKeyFormatter.IsIdish(field)
				//A path that already traverses a navigation property is the correct shape already.
				&& field.IndexOf('/', StringComparison.Ordinal) < 0)
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (suspects.Count == 0) {
			return null;
		}
		string rewrites = string.Join(", ", suspects.Select(field => $"'{field}' -> '{field[..^IdSuffix.Length]}/Id'"));
		return $"{string.Join(", ", suspects.Select(field => $"'{field}'"))} "
			+ (suspects.Count == 1 ? "looks" : "look")
			+ " like a lookup column; OData exposes lookups as navigation paths, so filter on the "
			+ $"navigation path instead: {rewrites}.";
	}

	/// <summary>Collects the field names of every condition the caller supplied, in request order.</summary>
	private static IReadOnlyList<string> CollectFilterFields(ODataReadArgs args) {
		if (args.Filters is null) {
			return [];
		}
		return new[] { args.Filters.All, args.Filters.Any }
			.Where(group => group is not null)
			.SelectMany(group => group)
			.Where(condition => condition is not null && !string.IsNullOrWhiteSpace(condition.Field))
			.Select(condition => condition.Field.Trim())
			.Distinct(StringComparer.Ordinal)
			.ToList();
	}

	private static ODataReadResponse ParseCollectionResponse(
		JsonElement root,
		JsonElement valueElement,
		string entityName,
		bool countRequested) {
		int count = valueElement.ValueKind == JsonValueKind.Array ? valueElement.GetArrayLength() : 1;
		long? totalCount = root.TryGetProperty("@odata.count", out JsonElement totalCountElement)
			&& totalCountElement.TryGetInt64(out long parsedTotalCount)
			? parsedTotalCount
			: null;
		if (countRequested && !totalCount.HasValue) {
			return ODataReadResponse.Failure(
				"Creatio did not return @odata.count for count=true; total count cannot be verified.",
				ODataReadErrorCodes.IncompleteResponse, entity: entityName);
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
//NOT sealed: ODataReadToFileArgs inherits it, so the two read paths bind the SAME query members and
//one validator covers both. A second declaration of the query surface would drift on the first edit.
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
		"Accepts an array or a comma-separated string. " +
		"Example: [\"Id\",\"Name\",\"AccountId\"] or \"Id,Name,AccountId\"")]
	public JsonElement? Select { get; init; }

	/// <summary>Navigation properties to expand ($expand).</summary>
	[JsonPropertyName("expand")]
	[Description(
		"Navigation properties to expand ($expand). " +
		"Remove 'Id' suffix from a lookup field to get the navigation name: AccountId → Account. " +
		"Accepts an array or a comma-separated string. " +
		"Example: [\"Account\",\"Owner\"] or \"Account,Owner\"")]
	public JsonElement? Expand { get; init; }

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
	int? Count = null,

	[property: JsonPropertyName("value")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Records returned by the OData query.")]
	JsonElement? Value = null,

	[property: JsonPropertyName("next-link")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("OData next-link URL when more records are available beyond the requested top.")]
	string? NextLink = null,

	[property: JsonPropertyName("total-count")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Total number of records matching the filter before top/skip paging, present when count=true.")]
	long? TotalCount = null,

	[property: JsonPropertyName("status-code")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("HTTP status behind the failure, present only when the response was an HTML error page that names its status in the title, for example 404 when the entity is not exposed over OData. A JSON routing 404 is served with HTTP 200 and therefore sets no status-code - it carries the wait-and-retry hint in error instead, so handle both.")]
	int? StatusCode = null,

	[property: JsonPropertyName("entity")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("The OData entity set the failure refers to. Present on every failure raised once the requested entity name is known; an argument-level rejection (a bad or missing entity, an unsupported argument) is refused before that point and carries no entity.")]
	string? Entity = null,

	[property: JsonPropertyName("error-code")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Machine-readable failure classification when success is false: argument, entity-not-found, invalid-query, server-reported-error, non-json-response, incomplete-response, or transport. Null on success.")]
	string? ErrorCode = null,

	[property: JsonPropertyName("correlation-id")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Identifier for this read, present on success and on failure. The same id tags the debug line carrying the server's own error text.")]
	string? CorrelationId = null,

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

	/// <summary>Creates a failure response carrying its machine-readable classification.</summary>
	/// <param name="message">The locally authored failure text; never server prose.</param>
	/// <param name="errorCode">One of <see cref="ODataReadErrorCodes"/>.</param>
	/// <param name="statusCode">The HTTP status when one could be established; otherwise null.</param>
	/// <param name="entity">The requested OData entity set name, when known.</param>
	/// <remarks>
	/// The code is a REQUIRED argument rather than a defaulted one on purpose: every failure path has to
	/// name its classification, and a default would let a new path ship with success:false and no code -
	/// exactly the shape issue #1407 reports.
	/// </remarks>
	public static ODataReadResponse Failure(string message, string errorCode, int? statusCode = null,
			string? entity = null) =>
		new(false, message, null, null, StatusCode: statusCode, Entity: entity, ErrorCode: errorCode);
}

/// <summary>
/// The machine-readable <c>error-code</c> values <see cref="ODataReadTool"/> reports. They are part of
/// the tool's published contract (<c>get-tool-contract</c> for odata-read lists the same set), so a
/// value may be added but must not be renamed or repurposed.
/// </summary>
internal static class ODataReadErrorCodes {

	/// <summary>The request was rejected by local validation; nothing was sent to Creatio.</summary>
	internal const string Argument = "argument";

	/// <summary>
	/// The entity set could not be reached: an IIS 404 page, or the ASP.NET routing miss that follows an
	/// entity whose OData controller is not registered or is still rebuilding.
	/// </summary>
	internal const string EntityNotFound = "entity-not-found";

	/// <summary>
	/// Creatio rejected the query shape - an unknown property, a column absent from the schema, or an
	/// unparsable query option. Retrying the same query cannot succeed.
	/// </summary>
	internal const string InvalidQuery = "invalid-query";

	/// <summary>Creatio reported an error that could not be narrowed to a more specific code.</summary>
	internal const string ServerReportedError = "server-reported-error";

	/// <summary>The body was not a JSON OData response for the requested entity set.</summary>
	internal const string NonJsonResponse = "non-json-response";

	/// <summary>
	/// The response parsed as the requested payload but omitted something the request asked for, such as
	/// <c>@odata.count</c> for count=true.
	/// </summary>
	internal const string IncompleteResponse = "incomplete-response";

	/// <summary>
	/// The request did not produce a usable response body at all, or failed before it was sent. The
	/// shared GET transport reports a connection failure, a DNS failure and a timeout all as an empty
	/// body, so they arrive here rather than as an exception.
	/// </summary>
	internal const string Transport = "transport";
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
