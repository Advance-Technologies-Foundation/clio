using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SelectQueryHelper = Clio.Package.SelectQueryHelper;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Answers an <c>execute-esq</c> call that carries SQL text, or top-level <c>schema-name</c>/<c>columns</c>
/// instead of the <c>query</c> SelectQuery object, with the exact shape and, where the mapping is trivially
/// safe, the equivalent <c>query</c> object ready to resend (ENG-103025).
/// </summary>
/// <remarks>
/// Nothing here executes anything. A translated query is only RETURNED: the caller sends it, which keeps the
/// decision (and the check that the schema and columns are what was meant) with the caller. Only the shapes that
/// have one obvious reading are translated - a single table, a plain list of column names, equality filters on
/// text or GUID literals joined by AND, and a row cap. A number or a date literal is not: it cannot tell an
/// Integer column from a Boolean or Decimal one, or a Text column from a DateTime one. Everything else (joins, functions, OR, ranges,
/// <c>*</c>, ordering, aliases, dotted paths) gets the shape and no suggestion, because a wrong guess would run a
/// different read than the one intended.
/// </remarks>
internal static class ExecuteEsqQueryHints {

	/// <summary>Row cap used when the caller gave none, kept small so a first read stays cheap.</summary>
	internal const int DefaultRowCount = 100;

	/// <summary>Largest row cap a translation will carry over.</summary>
	internal const int MaxRowCount = 10_000;

	// Key()-normalized names of the SelectQuery members this class reads.
	private const string WhereKey = "where";
	private const string LimitKey = "limit";
	private const string RootSchemaNameKey = "rootschemaname";
	private const string ColumnsKey = "columns";
	private const string FiltersKey = "filters";
	private const string RowCountKey = "rowcount";
	private const string OperationTypeKey = "operationtype";
	private const string AllColumnsKey = "allcolumns";

	private static readonly string[] SchemaKeys = ["schemaname", RootSchemaNameKey, "entity", "entityname", "table", "tablename", "from", "object"];
	private static readonly string[] ColumnKeys = [ColumnsKey, "select", "fields"];
	private static readonly string[] FilterKeys = [FiltersKey, "filter", WhereKey];
	private static readonly string[] LimitKeys = [RowCountKey, "top", LimitKey, "take", "maxrows"];
	private static readonly string[] SqlKeys = ["sql", "sqlquery", "sqltext", "statement"];

	// Every alias of a query part (schema, columns, filters, row cap, SQL text), Key()-normalized.
	private static readonly HashSet<string> QueryPartKeys =
		new(SchemaKeys.Concat(ColumnKeys).Concat(FilterKeys).Concat(LimitKeys).Concat(SqlKeys), StringComparer.Ordinal);

	// The members of Creatio's SelectQuery data contract, Key()-normalized, plus the serializer's type hints. SOURCE:
	// the public properties of Terrasoft.Nui.ServiceModel.DataContract.SelectQuery and its BaseFilterableQuery and
	// BaseQuery bases in Terrasoft.Nui.ServiceModel.dll, read with System.Reflection.Metadata on 2026-10-13 from
	// 8.1.0.6716 (the assembly clio ships) and 10.2.47 (a current platform build); the union of both is listed.
	// Re-read it the same way when the platform adds a member. The list is NOT the refusal: a member missing from it
	// only earns a warning (see ShapeProblem), so a gap here never turns a working query into a hard failure.
	private static readonly HashSet<string> SelectQueryMembers = new(StringComparer.Ordinal) {
		"queryid", "querykind", "columnvalues", RootSchemaNameKey, OperationTypeKey, "includeprocessexecutiondata",
		"forcebackgroundmode", "processexecutionoptions",
		"querytype", FiltersKey, ColumnsKey, AllColumnsKey, "serveresqcacheparameters", "ispageable", "isdistinct",
		"uselocalization", "userecorddeactivation", RowCountKey, "conditionalvalues", "ishierarchical",
		"hierarchicalmaxdepth", "hierarchicalcolumnname", "hierarchicalcolumnvalue", "hierarchicalfulldataload",
		"hierarchicalcolumnfiltersvalue", "rowsoffset", "chunksize",
		"queryoptimize", "usemetrics", "adminunitrolesources", "querysource", "ignoredisplayvalues", "type", "$type"
	};

	// Keys that are recognisably a query PART written the SQL/OData way (where, orderBy, select, limit, ...). They are
	// never a SelectQuery member on any platform version, and the DataService would drop them silently - running
	// the query without its filter, order or cap - so a query carrying one is refused rather than warned about.
	private static readonly HashSet<string> MisplacedPartKeys = new(
		QueryPartKeys.Concat(["orderby", "order", "sort", "sortby", "sorting", "groupby", "having", "join", "joins",
			"offset", "skip", "page", "pagesize", "distinct"]),
		StringComparer.Ordinal);

	private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

	private static readonly Regex GuidLiteral = new(
		@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
		RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

	private static readonly Regex SqlStart = new(@"^\s*(select|with)\b", RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex WriteSqlStart = new(
		@"^\s*(update|delete|insert|merge|upsert|replace|drop|truncate|alter|create|exec|execute|grant|revoke)\b",
		RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	// Top-level members of a real SelectQuery whose meaning a translation keeps: anything else on a malformed query
	// (isDistinct, rowsOffset, isPageable, ...) would be dropped by a suggestion, so none is made.
	private static readonly HashSet<string> TranslatableSelectQueryKeys = new(StringComparer.Ordinal) {
		RootSchemaNameKey, OperationTypeKey, AllColumnsKey, ColumnsKey, FiltersKey, RowCountKey
	};

	private static readonly Regex SelectShape = new(
		@"^\s*select\s+(?:top\s+(?<top>\d+)\s+)?(?<cols>.+?)\s+from\s+(?<table>\S+?)"
		+ @"(?:\s+where\s+(?<where>.+?))?(?:\s+limit\s+(?<limit>\d+))?\s*;?\s*$",
		RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

	private static readonly Regex Unsupported = new(
		@"\b(join|group\s+by|order\s+by|having|union|distinct|offset|fetch|into)\b|[()]|--|/\*|;\s*\S",
		RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex EqualityTerm = new(
		@"^\s*\[?(?<col>[A-Za-z_][A-Za-z0-9_]*)\]?\s*=\s*(?<val>'(?:[^']|'')*'|-?\d+)\s*$",
		RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

	/// <summary>A hint to return in place of running the call.</summary>
	/// <param name="Message">What was wrong and what the shape is; goes ahead of the usual shape example.</param>
	/// <param name="SuggestedQuery">The equivalent <c>query</c> object, or null when the mapping is not trivially safe.</param>
	internal sealed record Hint(string Message, JsonElement? SuggestedQuery);

	/// <summary>
	/// Builds the hint for a call whose <c>query</c> is absent or is not a SelectQuery, or returns null when the
	/// call carries nothing this class recognises (the caller then keeps its ordinary refusal).
	/// </summary>
	/// <param name="query">The bound <c>query</c> argument.</param>
	/// <param name="extensionData">Top-level keys the binder could not bind, such as <c>sql</c> or <c>schema-name</c>.</param>
	internal static Hint? TryBuild(JsonElement query, IReadOnlyDictionary<string, JsonElement>? extensionData) {
		string? sql = null;
		Dictionary<string, JsonElement> flat = Normalized(extensionData);
		switch (query.ValueKind) {
			case JsonValueKind.String:
				string text = query.GetString() ?? string.Empty;
				if (WriteSqlStart.IsMatch(text)) {
					return ReadOnlyRefusal();
				}
				if (!SqlStart.IsMatch(text)) {
					return null;
				}
				sql = text;
				break;
			case JsonValueKind.Object:
				if (HasRootSchemaName(query) || HasSelectQueryColumns(query)) {
					// A real SelectQuery (columns.items), even one missing rootSchemaName, keeps the ordinary refusal,
					// which names the missing rootSchemaName instead of claiming the fields sat at the top level.
					return null;
				}
				// A SelectQuery missing only its root schema, written the flat way: {"schema-name":..,"columns":[..]}.
				foreach (JsonProperty property in query.EnumerateObject()) {
					flat[Key(property.Name)] = property.Value;
				}
				break;
			case JsonValueKind.Undefined:
			case JsonValueKind.Null:
				break;
			default:
				return null;
		}
		if (sql is null && SqlKeys.FirstOrDefault(flat.ContainsKey) is { } sqlKey
			&& flat[sqlKey].ValueKind == JsonValueKind.String) {
			sql = flat[sqlKey].GetString();
		}
		if (sql is not null) {
			return FromSql(sql);
		}
		bool recognised = SchemaKeys.Concat(ColumnKeys).Concat(FilterKeys).Any(flat.ContainsKey);
		return recognised ? FromFlat(flat, FlatPrefix) : null;
	}

	private static bool HasSelectQueryColumns(JsonElement query) =>
		query.TryGetProperty(ColumnsKey, out JsonElement columns) && columns.ValueKind == JsonValueKind.Object;

	private static bool HasRootSchemaName(JsonElement query) =>
		query.TryGetProperty("rootSchemaName", out JsonElement root)
		&& root.ValueKind == JsonValueKind.String
		&& !string.IsNullOrWhiteSpace(root.GetString());

	/// <summary>
	/// Checks a <c>query</c> object that names its root schema against the SelectQuery shape, and the call's
	/// top-level keys for query parts sent next to it. Returns a refusal (with the equivalent query where the
	/// mapping is safe) when any part would otherwise be silently dropped by the server - for example
	/// <c>columns</c> sent as an array, which Creatio ignores and answers with Id-only rows - or null when the
	/// query is well-formed.
	/// </summary>
	/// <param name="query">The normalized <c>query</c> object (it has a non-empty <c>rootSchemaName</c>).</param>
	/// <param name="extensionData">Top-level keys the binder could not bind.</param>
	/// <param name="warnings">
	/// Members the query carries that clio does not know as SelectQuery members but that are not a misplaced query
	/// part either - most likely a member a newer platform added. The query still runs; the caller reports them.
	/// </param>
	internal static Hint? CheckSelectQueryShape(JsonElement query, IReadOnlyDictionary<string, JsonElement>? extensionData,
		out IReadOnlyList<string> warnings) {
		List<string> unknown = query.EnumerateObject()
			.Where(property => IsUnknownMember(Key(property.Name)))
			.Select(property => $"'{McpToolArgumentSupport.DescribeCallerKey(property.Name)}'")
			.ToList();
		warnings = unknown.Count == 0
			? []
			: [$"query carries {string.Join(", ", unknown)}, which clio does not know as a SelectQuery member. It was "
				+ "sent as is: if this Creatio version does not know it either, the DataService ignored it and the rows "
				+ "were read without it."];
		List<string> stray = (extensionData?.Keys ?? Enumerable.Empty<string>())
			.Where(key => QueryPartKeys.Contains(Key(key)))
			.Select(key => $"'{McpToolArgumentSupport.DescribeCallerKey(key)}'")
			.ToList();
		if (stray.Count > 0) {
			return new Hint("query parts were also sent at the top level (" + string.Join(", ", stray)
				+ "), where execute-esq ignores them, so running 'query' would drop them; nothing was executed. Put every "
				+ "part inside the 'query' SelectQuery object and send the call once.", null);
		}
		List<string> problems = query.EnumerateObject().Select(ShapeProblem).OfType<string>().ToList();
		if (problems.Count == 0) {
			return null;
		}
		string prefix = "query is not a valid SelectQuery: " + string.Join("; ", problems)
			+ ". Creatio would ignore those parts and run the rest, so nothing was executed.";
		Dictionary<string, JsonElement> flat = new(StringComparer.Ordinal);
		foreach (JsonProperty property in query.EnumerateObject()) {
			flat[Key(property.Name)] = property.Value;
		}
		if (!IsTranslatable(flat)) {
			return new Hint(prefix + " It also carries parts a translation would drop or misread, so no query is "
				+ "suggested.", null);
		}
		flat.Remove(OperationTypeKey);
		flat.Remove(AllColumnsKey);
		return FromFlat(flat, prefix);
	}

	/// <summary>
	/// A malformed SelectQuery is translated only when every member it has is one the translation keeps, so the
	/// suggestion reads exactly what was asked: no allColumns, a plain select, and filters (if any) written as a
	/// flat column-equality object rather than a filter group.
	/// </summary>
	private static bool IsTranslatable(Dictionary<string, JsonElement> flat) {
		if (!flat.Keys.All(key => TranslatableSelectQueryKeys.Contains(key) || QueryPartKeys.Contains(key))) {
			return false;
		}
		if (flat.TryGetValue(AllColumnsKey, out JsonElement all) && all.ValueKind == JsonValueKind.True) {
			return false;
		}
		if (flat.TryGetValue(OperationTypeKey, out JsonElement operation)
			&& !(operation.ValueKind == JsonValueKind.Number && operation.TryGetInt32(out int kind) && kind == 0)) {
			return false;
		}
		return !FilterKeys.Any(key => flat.TryGetValue(key, out JsonElement filters) && IsFilterGroup(filters));
	}

	private static bool IsFilterGroup(JsonElement filters) =>
		filters.ValueKind == JsonValueKind.Object
		&& (filters.TryGetProperty("items", out _) || filters.TryGetProperty("filterType", out _));

	// Stems of the parts that change WHAT is read. An unrecognised key carrying one (a misspelled 'column', 'rowCnt',
	// 'filterGroup', 'criteria') is far likelier a part sent under the wrong name than a new platform member, and
	// running without it returns the wrong rows with success:true - so it is refused like a misplaced part.
	private static readonly string[] ReadShapingStems =
		["column", "filter", "row", WhereKey, "order", "sort", LimitKey, "select", "criteri", "condition"];

	/// <summary>A query part under a non-SelectQuery name: refused, because the DataService would silently drop it.</summary>
	private static bool IsMisplacedPart(string key) =>
		!SelectQueryMembers.Contains(key)
		&& (MisplacedPartKeys.Contains(key) || ReadShapingStems.Any(stem => key.Contains(stem, StringComparison.Ordinal)));

	/// <summary>A member clio does not know that shapes nothing it can name: sent as is, with a warning.</summary>
	private static bool IsUnknownMember(string key) =>
		!SelectQueryMembers.Contains(key) && !IsMisplacedPart(key);

	/// <summary>
	/// Why one top-level member of a SelectQuery would be ignored or misread, or null when it is fine or merely
	/// unknown to clio (that one is a warning, not a refusal: see <see cref="SelectQueryMembers"/>).
	/// </summary>
	private static string? ShapeProblem(JsonProperty property) {
		string key = Key(property.Name);
		JsonElement value = property.Value;
		string name = McpToolArgumentSupport.DescribeCallerKey(property.Name);
		if (value.ValueKind == JsonValueKind.Null && SelectQueryMembers.Contains(key)) {
			// An explicit null asks for nothing, so nothing is lost.
			return null;
		}
		switch (key) {
			case ColumnsKey:
				return value.ValueKind == JsonValueKind.Object && value.TryGetProperty("items", out JsonElement items)
					&& items.ValueKind == JsonValueKind.Object
					? null
					: $"'{name}' must be an object {{\"items\":{{\"<alias>\":{{\"expression\":{{\"expressionType\":0,\"columnPath\":\"<column>\"}}}}}}}}, not {Describe(value)}";
			case FiltersKey:
				return IsFilterGroup(value)
					&& (!value.TryGetProperty("items", out JsonElement filterItems) || filterItems.ValueKind == JsonValueKind.Object)
					? null
					: $"'{name}' must be a filter object {{\"filterType\":6,\"items\":{{\"<key>\":{{...}}}}}}, not {Describe(value)}";
			case RowCountKey:
				return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)
					? null
					: $"'{name}' must be a whole number, not {Describe(value)}";
			default:
				return IsMisplacedPart(key) ? $"'{name}' is not a SelectQuery member" : null;
		}
	}

	private static string Describe(JsonElement value) => value.ValueKind switch {
		JsonValueKind.Array => "an array",
		JsonValueKind.String => "a string",
		JsonValueKind.Number => "a number",
		JsonValueKind.Object => "an object without an 'items' map",
		JsonValueKind.True or JsonValueKind.False => "a boolean",
		_ => "null"
	};

	private static string Key(string name) =>
		name.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal)
			.ToLowerInvariant();

	private static Dictionary<string, JsonElement> Normalized(IReadOnlyDictionary<string, JsonElement>? data) {
		Dictionary<string, JsonElement> result = new(StringComparer.Ordinal);
		foreach ((string key, JsonElement value) in data ?? new Dictionary<string, JsonElement>()) {
			result[Key(key)] = value;
		}
		return result;
	}

	private const string SqlPrefix =
		"query holds SQL text, but execute-esq runs a SelectQuery JSON object, never SQL; nothing was executed.";

	private const string FlatPrefix =
		"query is missing: the schema, columns and filters belong inside the 'query' SelectQuery object, not at the top "
		+ "level; nothing was executed.";

	private static Hint ReadOnlyRefusal() =>
		new(ReadOnlyMessage, null);

	private const string ReadOnlyMessage =
		"execute-esq is read-only: it runs a SELECT as a SelectQuery JSON object, and never UPDATE, DELETE, INSERT or any "
		+ "other write or DDL statement; nothing was executed. To change data use odata-update, odata-delete or "
		+ "odata-create (or the matching data tool); to read, send a SelectQuery in 'query'.";

	private static Hint FromSql(string sql) {
		if (WriteSqlStart.IsMatch(sql)) {
			return ReadOnlyRefusal();
		}
		if (Unsupported.IsMatch(sql) || SelectShape.Match(sql) is not { Success: true } match) {
			return new Hint(SqlPrefix + " This statement is not a single-table SELECT of plain columns with simple "
				+ "equality filters, so no query is suggested: build the SelectQuery from the shape below.", null);
		}
		string columnsText = match.Groups["cols"].Value.Trim();
		if (columnsText == "*") {
			return new Hint(SqlPrefix + " 'SELECT *' has no safe SelectQuery equivalent: list the columns you need "
				+ "(allColumns pulls binary and unrelated fields).", null);
		}
		if (match.Groups["top"].Success && match.Groups[LimitKey].Success) {
			return new Hint(SqlPrefix + " TOP and LIMIT were both given, so the row cap is ambiguous and no query is "
				+ "suggested.", null);
		}
		string? table = ReadTable(match.Groups["table"].Value);
		List<string> columns = columnsText.Split(',')
			.Select(part => part.Trim().Trim('[', ']', '"', '`'))
			.ToList();
		if (table is null || columns.Any(column => !Identifier.IsMatch(column))) {
			return new Hint(SqlPrefix + " Aliases, expressions and dotted column paths are not translated.", null);
		}
		List<(string Column, JsonElement Value)> filters = [];
		if (match.Groups[WhereKey].Success) {
			foreach (string term in AndSeparator.Split(match.Groups[WhereKey].Value)) {
				if (TryReadEqualityTerm(term) is not { } filter) {
					return new Hint(SqlPrefix + " Only 'column = value' terms joined by AND are translated.", null);
				}
				filters.Add(filter);
			}
		}
		return Suggest(SqlPrefix, table, columns, filters, ReadRowCount(match));
	}

	private static readonly Regex AndSeparator = new(
		@"\s+and\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

	private static (string Column, JsonElement Value)? TryReadEqualityTerm(string term) {
		Match equality = EqualityTerm.Match(term);
		if (!equality.Success) {
			return null;
		}
		string literal = equality.Groups["val"].Value;
		if (literal.StartsWith('\'')) {
			return (equality.Groups["col"].Value,
				JsonSerializer.SerializeToElement(literal[1..^1].Replace("''", "'", StringComparison.Ordinal)));
		}
		// An integer literal outside the long range is not translated rather than throwing.
		return long.TryParse(literal, System.Globalization.NumberStyles.AllowLeadingSign,
			System.Globalization.CultureInfo.InvariantCulture, out long number)
			? (equality.Groups["col"].Value, JsonSerializer.SerializeToElement(number))
			: null;
	}

	private static int? ReadRowCount(Match match) {
		Group group = match.Groups["top"].Success ? match.Groups["top"] : match.Groups[LimitKey];
		if (!group.Success) {
			return null;
		}
		// A cap beyond int is out of range like any other cap above MaxRowCount: Suggest refuses -1.
		return int.TryParse(group.Value, System.Globalization.NumberStyles.None,
			System.Globalization.CultureInfo.InvariantCulture, out int rows) ? rows : -1;
	}

	private static string? ReadTable(string raw) {
		string name = raw.Trim();
		if (name.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("[dbo].", StringComparison.OrdinalIgnoreCase)) {
			name = name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..];
		}
		name = name.Trim('[', ']', '"', '`');
		return Identifier.IsMatch(name) ? name : null;
	}

	private static Hint FromFlat(Dictionary<string, JsonElement> flat, string prefix) {
		// Two keys for one part (schema-name and table, filters and where, ...) have no single reading: using one
		// would silently drop the other, for example a filter, and read more rows than meant.
		string[][] groups = [SchemaKeys, ColumnKeys, FilterKeys, LimitKeys];
		if (groups.Any(group => group.Count(flat.ContainsKey) > 1)) {
			return new Hint(prefix + " Two keys were given for the same part of the query (for example "
				+ "'filters' and 'where'), so no query is suggested.", null);
		}
		string? schema = SchemaKeys.Where(flat.ContainsKey)
			.Select(key => flat[key].ValueKind == JsonValueKind.String ? flat[key].GetString() : null)
			.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
		List<string>? columns = ReadColumnNames(flat);
		List<(string Column, JsonElement Value)>? filters = [];
		if (FilterKeys.FirstOrDefault(flat.ContainsKey) is { } filterKey) {
			filters = flat[filterKey].ValueKind == JsonValueKind.Object
				? ReadEqualityObject(flat[filterKey])
				: null;
		}
		int? rowCount = null;
		if (LimitKeys.FirstOrDefault(flat.ContainsKey) is { } limitKey) {
			// A cap that is not a whole number in int range is refused (-1), never replaced by the default.
			rowCount = flat[limitKey].ValueKind == JsonValueKind.Number && flat[limitKey].TryGetInt32(out int limit)
				? limit
				: -1;
		}
		bool safe = schema is not null && Identifier.IsMatch(schema)
			&& columns is { Count: > 0 } && columns.All(column => Identifier.IsMatch(column))
			&& filters is not null;
		return safe
			? Suggest(prefix, schema, columns, filters, rowCount)
			: new Hint(prefix + " The values are not a single schema name with a plain list of column "
				+ "names and simple equality filters, so no query is suggested.", null);
	}

	private static List<string>? ReadColumnNames(Dictionary<string, JsonElement> flat) {
		string? columnKey = ColumnKeys.FirstOrDefault(flat.ContainsKey);
		if (columnKey is null || flat[columnKey].ValueKind != JsonValueKind.Array) {
			return null;
		}
		return flat[columnKey].EnumerateArray()
			.Select(item => item.ValueKind == JsonValueKind.String ? (item.GetString() ?? string.Empty).Trim() : string.Empty)
			.ToList();
	}

	// Only text and GUID values reach here: Suggest refuses a number or a date before building any filter, because
	// the literal cannot tell the column's type. Numbers are still PARSED, so that refusal can name the filter.
	private static SelectQueryHelper.SelectQueryFilterDefinition ToFilterDefinition((string Column, JsonElement Value) filter) {
		string text = filter.Value.GetString() ?? string.Empty;
		int dataType = GuidLiteral.IsMatch(text) ? SelectQueryHelper.GuidDataValueType : SelectQueryHelper.TextDataValueType;
		return new SelectQueryHelper.SelectQueryFilterDefinition(filter.Column, text, dataType);
	}

	private static List<(string Column, JsonElement Value)>? ReadEqualityObject(JsonElement filters) {
		List<(string Column, JsonElement Value)> result = [];
		foreach (JsonProperty property in filters.EnumerateObject()) {
			bool scalar = property.Value.ValueKind == JsonValueKind.String
				|| (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out _));
			if (!scalar || !Identifier.IsMatch(property.Name)) {
				return null;
			}
			result.Add((property.Name, property.Value.Clone()));
		}
		return result;
	}

	private static readonly Regex DateLiteral = new(
		@"^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?$",
		RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);

	/// <summary>
	/// A filter value whose column type the literal cannot tell: a number (Integer, Decimal or Boolean - 'IsActive = 1')
	/// or a date ('CreatedOn = 2024-01-01', which as Text would never match a DateTime column).
	/// </summary>
	private static bool IsTypeAmbiguous(JsonElement value) =>
		value.ValueKind == JsonValueKind.Number
		|| (value.ValueKind == JsonValueKind.String && DateLiteral.IsMatch(value.GetString() ?? string.Empty));

	private static Hint Suggest(
		string prefix, string schema, List<string> columns, List<(string Column, JsonElement Value)> filters, int? requestedRows) {
		if (requestedRows is <= 0 or > MaxRowCount) {
			return new Hint(prefix + $" The row cap must be 1-{MaxRowCount}, so no query is suggested.", null);
		}
		if (filters.FirstOrDefault(filter => IsTypeAmbiguous(filter.Value)) is { Column: { } ambiguousColumn }) {
			// Agents resend suggested-query as is, so a filter typed from the literal (an Integer filter on a Boolean
			// column, a Text filter on a DateTime one) would come back as one more failed call or an empty read.
			return new Hint(prefix + $" The filter on '{ambiguousColumn}' compares with a number or a date, and the "
				+ "literal cannot tell its column type (Integer, Decimal, Boolean or DateTime), so no query is "
				+ "suggested: build the filter with the column's dataValueType (get-guidance name=esq-filters).", null);
		}
		int rowCount = requestedRows ?? DefaultRowCount;
		List<SelectQueryHelper.SelectQueryColumnDefinition> columnDefinitions =
			columns.Distinct(StringComparer.Ordinal).Select(column => new SelectQueryHelper.SelectQueryColumnDefinition(column, column)).ToList();
		List<SelectQueryHelper.SelectQueryFilterDefinition> filterDefinitions = filters.Select(ToFilterDefinition).ToList();
		JsonElement query = JsonSerializer.SerializeToElement(
			SelectQueryHelper.BuildSelectQuery(schema, columnDefinitions, filterDefinitions, rowCount));
		return new Hint(prefix + " The equivalent query is returned in 'suggested-query' - send it as 'query' to run "
			+ $"it. Check that '{schema}' is the Creatio schema name, that each column is an ESQ column name (a lookup is "
			+ "'Account' or 'Account.Id', not the physical 'AccountId'), and that each filter fits its column (a quoted "
			+ $"value is sent as Text, a GUID as Guid; a date-shaped one gets no suggestion). The row cap is {rowCount}"
			+ (requestedRows is null ? " (default; none was given)." : "."), query);
	}
}
