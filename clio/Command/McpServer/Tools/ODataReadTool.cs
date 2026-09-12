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
		"top must be between 1 and 100 (default 25); an out-of-range top (including 0 or negative) is rejected, never silently widened. " +
		"skip must be zero or greater; use order-by with skip for stable paging. " +
		"Unknown arguments and malformed filter conditions fail before any Creatio request; raw filter strings are not supported. " +
		"Every response - success or failure - carries a correlation-id; the same id appears on the debug line that records the server's own error text (run clio with --debug to see it). " +
		"A failure also carries a machine-readable error-code: argument, entity-not-found, invalid-query, server-reported-error, non-json-response, incomplete-response, or transport. " +
		"error-code invalid-query means the request shape is at fault and retrying it unchanged cannot succeed; the message lists the field, select, expand and order-by names YOU sent, and when a filter field looks like a raw lookup column (a name ending in Id) it names the navigation path to use instead, for example AccountId -> Account/Id. " +
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
			string? argumentError = ValidateArguments(args);
			if (argumentError is not null) {
				return ODataReadResponse.Failure(argumentError, ODataReadErrorCodes.Argument);
			}
			if (string.IsNullOrWhiteSpace(args.Entity)) {
				return ODataReadResponse.Failure("entity is required.", ODataReadErrorCodes.Argument);
			}
			if (!ODataKeyFormatter.IsValidEntityName(args.Entity)) {
				return ODataReadResponse.Failure(
					"entity must be a valid OData entity set name (letters, digits, underscore).",
					ODataReadErrorCodes.Argument);
			}
			if (args.Top is { } requestedTop && (requestedTop < MinTop || requestedTop > MaxTop)) {
				// An out-of-range top must NOT silently fall through to the default (which would
				// return a page when the caller asked for 0, or be misread as "all" on negatives).
				return ODataReadResponse.Failure(
					$"top must be between {MinTop} and {MaxTop} (got {requestedTop}). Omit top to use the default of {DefaultTop}.",
					ODataReadErrorCodes.Argument);
			}

			EnvironmentOptions options = new() { Environment = args.EnvironmentName };
			IApplicationClient client = commandResolver.Resolve<IApplicationClient>(options);
			IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);

			string queryString = BuildQueryString(args);
			string path = $"odata/{args.Entity.Trim()}{queryString}";
			string url = urlBuilder.Build(path);

			string responseJson = client.ExecuteGetRequest(url, 30_000);
			return ParseODataResponse(responseJson, args, correlationId);
		} catch (Exception ex) {
			//The transport swallows connection failures into an empty body, so what reaches here is
			//mostly environment resolution and client construction - still a transport-class failure
			//from the caller's point of view, and never a query-shape one.
			return ODataReadResponse.Failure(
				SensitiveErrorTextRedactor.Redact(ex.Message), ODataReadErrorCodes.Transport);
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

	private ODataReadResponse ParseODataResponse(string json, ODataReadArgs args, string correlationId) {
		string entityName = args.Entity.Trim();
		bool countRequested = args.Count;
		// ExecuteGetRequest may return null (the interface permits it; reauth and proxy failures do produce
		// it). The absence of a body has to be classified HERE: the IIS-404 probe below dereferences the
		// string, so a null would raise an NRE that escapes the body-suppression invariant and reaches
		// Read()'s outer catch as an opaque message. An empty body already resolved to this same failure.
		if (string.IsNullOrWhiteSpace(json)) {
			return ODataReadResponse.Failure(
				CreatioResponseError.DescribeEmptyReadResponse(), ODataReadErrorCodes.Transport);
		}
		if (CreatioResponseError.TryDescribeMissingEntitySet(json, entityName, out string missingEntitySetError)) {
			return ODataReadResponse.Failure(missingEntitySetError, ODataReadErrorCodes.EntityNotFound);
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
				return ODataReadResponse.Failure(
					CreatioResponseError.DescribeServerReportedReadError(kind, DescribeCallerQuery(args, kind)),
					ErrorCodeFor(kind));
			}

			//A collection response carries `value` as an ARRAY. Accepting any `value` meant a proxy or
			//auth body such as {"value":"private response marker"} came back as success:true with the
			//marker as the payload, and clio-run then forwarded it without failure redaction.
			if (root.TryGetProperty("value", out JsonElement valueEl)) {
				return valueEl.ValueKind == JsonValueKind.Array
					&& IsCollectionResponse(root, entityName)
					? ParseCollectionResponse(root, valueEl, countRequested)
					: ODataReadResponse.Failure(
						CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse);
			}

			//Single-entity response (no value wrapper). Only OData identifies itself as one: the
			//@odata.context annotation ends with "/$entity". Without that check ANY parsed JSON object
			//was a successful record - {"detail":"private response marker"} included.
			return IsSingleEntityResponse(root, entityName)
				? new ODataReadResponse(true, null, 1, root.Clone(), null)
				: ODataReadResponse.Failure(
					CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse);
		} catch (Exception) {
			// EVERY parse failure gets the same fixed diagnostic, carrying no fragment of the body. Testing
			// the first character was not enough: a malformed body that still starts with '{' or '[' — a
			// truncated proxy response, say — fell through to a preview that copied arbitrary server or
			// proxy content into the MCP transcript. The redactor strips known secret shapes, not tenant
			// data it has never seen, so the body cannot be quoted at all. The exception message is
			// dropped with it: a parse position is of no use to a caller who cannot see the body anyway.
			return ODataReadResponse.Failure(
				CreatioResponseError.DescribeNonJsonReadResponse(), ODataReadErrorCodes.NonJsonResponse);
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
	private static bool IsCollectionResponse(JsonElement root, string entityName) =>
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
	private static bool HasMatchingODataIdentity(JsonElement root, string entityName) =>
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
	private static bool IsSingleEntityResponse(JsonElement root, string entityName) =>
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
		if (kind == ODataErrorKind.UnregisteredEntity) {
			//The entity set, not the query, is what failed; listing the query members would misdirect.
			return null;
		}
		IReadOnlyList<string> filterFields = CollectFilterFields(args);
		List<string> sentParts = [];
		if (filterFields.Count > 0) {
			sentParts.Add($"filter fields: {string.Join(", ", filterFields)}");
		}
		if (args.Select is { Length: > 0 } select) {
			sentParts.Add($"select: {string.Join(", ", select)}");
		}
		if (args.Expand is { Length: > 0 } expand) {
			sentParts.Add($"expand: {string.Join(", ", expand)}");
		}
		if (!string.IsNullOrWhiteSpace(args.OrderBy)) {
			sentParts.Add($"order-by: {args.OrderBy!.Trim()}");
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
			.Where(field => field.Length > IdSuffix.Length
				&& field.EndsWith(IdSuffix, StringComparison.Ordinal)
				&& !string.Equals(field, IdSuffix, StringComparison.Ordinal)
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

	private const string IdSuffix = "Id";

	/// <summary>Collects the field names of every condition the caller supplied, in request order.</summary>
	private static IReadOnlyList<string> CollectFilterFields(ODataReadArgs args) {
		if (args.Filters is null) {
			return [];
		}
		return new[] { args.Filters.All, args.Filters.Any }
			.Where(group => group is not null)
			.SelectMany(group => group!)
			.Where(condition => condition is not null && !string.IsNullOrWhiteSpace(condition.Field))
			.Select(condition => condition!.Field.Trim())
			.Distinct(StringComparer.Ordinal)
			.ToList();
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
				"Creatio did not return @odata.count for count=true; total count cannot be verified.",
				ODataReadErrorCodes.IncompleteResponse);
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
	long? TotalCount = null,

	[property: JsonPropertyName("error-code")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Machine-readable failure classification when success is false: argument, entity-not-found, invalid-query, server-reported-error, non-json-response, incomplete-response, or transport. Null on success.")]
	string? ErrorCode = null,

	[property: JsonPropertyName("correlation-id")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Identifier for this read, present on success and on failure. The same id tags the debug line carrying the server's own error text.")]
	string? CorrelationId = null) {

	/// <summary>Creates a failure response carrying its machine-readable classification.</summary>
	/// <param name="message">The locally authored failure text; never server prose.</param>
	/// <param name="errorCode">One of <see cref="ODataReadErrorCodes"/>.</param>
	/// <remarks>
	/// The code is a REQUIRED argument rather than a defaulted one on purpose: every failure path has to
	/// name its classification, and a default would let a new path ship with success:false and no code -
	/// exactly the shape issue #1407 reports.
	/// </remarks>
	public static ODataReadResponse Failure(string message, string errorCode) =>
		new(false, message, null, null, ErrorCode: errorCode);
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
