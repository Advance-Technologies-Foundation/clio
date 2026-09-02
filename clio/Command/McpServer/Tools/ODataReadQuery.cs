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
	internal static ODataReadResponse ParseODataResponse(string json, string entityName, bool countRequested) {
		// ExecuteGetRequest may return null (the interface permits it; reauth and proxy failures do produce
		// it). The absence of a body has to be classified HERE: the IIS-404 probe below dereferences the
		// string, so a null would raise an NRE that escapes the body-suppression invariant and reaches
		// Read()'s outer catch as an opaque message. An empty body already resolved to this same failure.
		if (string.IsNullOrWhiteSpace(json)) {
			return ODataReadResponse.Failure(CreatioResponseError.DescribeNonJsonReadResponse());
		}
		if (CreatioResponseError.TryDescribeMissingEntitySet(json, entityName, out string missingEntitySetError)) {
			return ODataReadResponse.Failure(missingEntitySetError);
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
					CreatioResponseContext.ODataPayload, out bool isUnregisteredEntity)) {
				return ODataReadResponse.Failure(
					CreatioResponseError.DescribeServerReportedReadError(isUnregisteredEntity));
			}

			//A collection response carries `value` as an ARRAY. Accepting any `value` meant a proxy or
			//auth body such as {"value":"private response marker"} came back as success:true with the
			//marker as the payload, and clio-run then forwarded it without failure redaction.
			if (root.TryGetProperty("value", out JsonElement valueEl)) {
				return valueEl.ValueKind == JsonValueKind.Array
					&& IsCollectionResponse(root, entityName)
					? ParseCollectionResponse(root, valueEl, countRequested)
					: ODataReadResponse.Failure(CreatioResponseError.DescribeNonJsonReadResponse());
			}

			//Single-entity response (no value wrapper). Only OData identifies itself as one: the
			//@odata.context annotation ends with "/$entity". Without that check ANY parsed JSON object
			//was a successful record - {"detail":"private response marker"} included.
			return IsSingleEntityResponse(root, entityName)
				? new ODataReadResponse(true, null, 1, root.Clone(), null)
				: ODataReadResponse.Failure(CreatioResponseError.DescribeNonJsonReadResponse());
		} catch (Exception) {
			// EVERY parse failure gets the same fixed diagnostic, carrying no fragment of the body. Testing
			// the first character was not enough: a malformed body that still starts with '{' or '[' — a
			// truncated proxy response, say — fell through to a preview that copied arbitrary server or
			// proxy content into the MCP transcript. The redactor strips known secret shapes, not tenant
			// data it has never seen, so the body cannot be quoted at all. The exception message is
			// dropped with it: a parse position is of no use to a caller who cannot see the body anyway.
			return ODataReadResponse.Failure(CreatioResponseError.DescribeNonJsonReadResponse());
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
	internal static bool MatchesTopLevelContext(JsonElement root, string entityName, bool singleEntity) {
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
	internal static bool IsBalancedTrailingProjection(string fragment, int projectionStart) {
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

	internal const string SingleEntitySuffix = "/$entity";

	/// <summary>True when the body is a top-level read of the requested set, in either shape.</summary>
	internal static bool HasMatchingODataIdentity(JsonElement root, string entityName) =>
		root.ValueKind == JsonValueKind.Object
		&& (MatchesTopLevelContext(root, entityName, singleEntity: true)
			|| MatchesTopLevelContext(root, entityName, singleEntity: false));

	/// <summary>
	/// Reads the whole <c>@odata.context</c> fragment - everything after the last <c>#</c> - without
	/// cutting it at the first separator.
	/// </summary>
	internal static bool TryGetContextFragment(JsonElement root, out string fragment) {
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

	/// <summary>
	/// The rejection for a body that is not OData content. Owned here, and used verbatim by the file-mode
	/// contract too: the two read paths must give the caller the SAME diagnostic for the same body, and
	/// two copies of the sentence drifted the moment one of them was edited.
	/// </summary>
	/// <param name="kind">Kind of the response root.</param>
	internal static string DescribeNonODataContent(JsonValueKind kind) =>
		$"OData response is a JSON {DescribeKind(kind)}, not a record or a collection. "
		+ "The endpoint did not answer with OData content; check the environment and the entity name.";

	/// <summary>The rejection for count=true answered without the annotation. Shared with file mode.</summary>
	internal const string MissingCountMessage =
		"Creatio did not return @odata.count for count=true; total count cannot be verified.";

	/// <summary>
	/// Whether a response property is an OData control annotation rather than a data column. Any
	/// <c>@odata.*</c> member belongs to the envelope, and a single-entity response carries
	/// <c>@odata.context</c> alongside the real fields.
	/// </summary>
	/// <param name="name">Property name from the response object.</param>
	internal static bool IsODataAnnotation(string name) =>
		name.StartsWith("@odata.", StringComparison.Ordinal);

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
			return ODataReadResponse.Failure(MissingCountMessage);
		}
		string nextLink = hasEnvelope
			&& root.TryGetProperty("@odata.nextLink", out JsonElement nextLinkElement)
			&& nextLinkElement.ValueKind == JsonValueKind.String
			? nextLinkElement.GetString()
			: null;
		return new ODataReadResponse(true, null, count, valueElement.Clone(), nextLink, totalCount);
	}
}
