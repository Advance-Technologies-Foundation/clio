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
/// text, GUID or integer literals joined by AND, and a row cap. Everything else (joins, functions, OR, ranges,
/// <c>*</c>, ordering, aliases, dotted paths) gets the shape and no suggestion, because a wrong guess would run a
/// different read than the one intended.
/// </remarks>
internal static class ExecuteEsqQueryHints {

	/// <summary>Row cap used when the caller gave none, kept small so a first read stays cheap.</summary>
	internal const int DefaultRowCount = 100;

	/// <summary>Largest row cap a translation will carry over.</summary>
	internal const int MaxRowCount = 10_000;

	private static readonly string[] SchemaKeys = ["schemaname", "rootschemaname", "entity", "entityname", "table", "tablename", "from", "object"];
	private static readonly string[] ColumnKeys = ["columns", "select", "fields"];
	private static readonly string[] FilterKeys = ["filters", "filter", "where"];
	private static readonly string[] LimitKeys = ["rowcount", "top", "limit", "take", "maxrows"];
	private static readonly string[] SqlKeys = ["sql", "sqlquery", "sqltext", "statement"];

	private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex GuidLiteral = new(
		@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex SqlStart = new(@"^\s*(select|with)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex SelectShape = new(
		@"^\s*select\s+(?:top\s+(?<top>\d+)\s+)?(?<cols>.+?)\s+from\s+(?<table>\S+?)"
		+ @"(?:\s+where\s+(?<where>.+?))?(?:\s+limit\s+(?<limit>\d+))?\s*;?\s*$",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

	private static readonly Regex Unsupported = new(
		@"\b(join|group\s+by|order\s+by|having|union|distinct|offset|fetch|into)\b|[()]|--|/\*|;\s*\S",
		RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex EqualityTerm = new(
		@"^\s*\[?(?<col>[A-Za-z_][A-Za-z0-9_]*)\]?\s*=\s*(?<val>'(?:[^']|'')*'|-?\d+)\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
				if (!SqlStart.IsMatch(text)) {
					return null;
				}
				sql = text;
				break;
			case JsonValueKind.Object:
				if (HasRootSchemaName(query)) {
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
		return recognised ? FromFlat(flat) : null;
	}

	private static bool HasRootSchemaName(JsonElement query) =>
		query.TryGetProperty("rootSchemaName", out JsonElement root)
		&& root.ValueKind == JsonValueKind.String
		&& !string.IsNullOrWhiteSpace(root.GetString());

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

	private static Hint FromSql(string sql) {
		if (Unsupported.IsMatch(sql) || SelectShape.Match(sql) is not { Success: true } match) {
			return new Hint(SqlPrefix + " This statement is not a single-table SELECT of plain columns with simple "
				+ "equality filters, so no query is suggested: build the SelectQuery from the shape below.", null);
		}
		string columnsText = match.Groups["cols"].Value.Trim();
		if (columnsText == "*") {
			return new Hint(SqlPrefix + " 'SELECT *' has no safe SelectQuery equivalent: list the columns you need "
				+ "(allColumns pulls binary and unrelated fields).", null);
		}
		string? table = ReadTable(match.Groups["table"].Value);
		List<string> columns = columnsText.Split(',')
			.Select(part => part.Trim().Trim('[', ']', '"', '`'))
			.ToList();
		if (table is null || columns.Any(column => !Identifier.IsMatch(column))) {
			return new Hint(SqlPrefix + " Aliases, expressions and dotted column paths are not translated.", null);
		}
		List<(string Column, JsonElement Value)> filters = [];
		if (match.Groups["where"].Success) {
			foreach (string term in Regex.Split(match.Groups["where"].Value, @"\s+and\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
				Match equality = EqualityTerm.Match(term);
				if (!equality.Success) {
					return new Hint(SqlPrefix + " Only 'column = value' terms joined by AND are translated.", null);
				}
				string literal = equality.Groups["val"].Value;
				filters.Add((equality.Groups["col"].Value, literal.StartsWith('\'')
					? JsonSerializer.SerializeToElement(literal[1..^1].Replace("''", "'", StringComparison.Ordinal))
					: JsonSerializer.SerializeToElement(long.Parse(literal, System.Globalization.CultureInfo.InvariantCulture))));
			}
		}
		int? rowCount = match.Groups["top"].Success ? int.Parse(match.Groups["top"].Value, System.Globalization.CultureInfo.InvariantCulture)
			: match.Groups["limit"].Success ? int.Parse(match.Groups["limit"].Value, System.Globalization.CultureInfo.InvariantCulture)
			: null;
		return Suggest(SqlPrefix, table, columns, filters, rowCount);
	}

	private static string? ReadTable(string raw) {
		string name = raw.Trim();
		if (name.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("[dbo].", StringComparison.OrdinalIgnoreCase)) {
			name = name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..];
		}
		name = name.Trim('[', ']', '"', '`');
		return Identifier.IsMatch(name) ? name : null;
	}

	private static Hint FromFlat(Dictionary<string, JsonElement> flat) {
		string? schema = SchemaKeys.Where(flat.ContainsKey)
			.Select(key => flat[key].ValueKind == JsonValueKind.String ? flat[key].GetString() : null)
			.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
		string? columnKey = ColumnKeys.FirstOrDefault(flat.ContainsKey);
		List<string>? columns = null;
		if (columnKey is not null && flat[columnKey].ValueKind == JsonValueKind.Array) {
			columns = flat[columnKey].EnumerateArray()
				.Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : string.Empty)
				.ToList();
		}
		List<(string Column, JsonElement Value)>? filters = [];
		if (FilterKeys.FirstOrDefault(flat.ContainsKey) is { } filterKey) {
			filters = flat[filterKey].ValueKind == JsonValueKind.Object
				? ReadEqualityObject(flat[filterKey])
				: null;
		}
		int? rowCount = null;
		if (LimitKeys.FirstOrDefault(flat.ContainsKey) is { } limitKey
			&& flat[limitKey].ValueKind == JsonValueKind.Number && flat[limitKey].TryGetInt32(out int limit)) {
			rowCount = limit;
		}
		bool safe = schema is not null && Identifier.IsMatch(schema)
			&& columns is { Count: > 0 } && columns.All(column => Identifier.IsMatch(column))
			&& filters is not null;
		return safe
			? Suggest(FlatPrefix, schema!, columns!, filters!, rowCount)
			: new Hint(FlatPrefix + " The top-level values are not a single schema name with a plain list of column "
				+ "names and simple equality filters, so no query is suggested.", null);
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

	private static Hint Suggest(
		string prefix, string schema, List<string> columns, List<(string Column, JsonElement Value)> filters, int? requestedRows) {
		if (requestedRows is <= 0 or > MaxRowCount) {
			return new Hint(prefix + $" The row cap must be 1-{MaxRowCount}, so no query is suggested.", null);
		}
		int rowCount = requestedRows ?? DefaultRowCount;
		List<SelectQueryHelper.SelectQueryColumnDefinition> columnDefinitions =
			columns.Distinct(StringComparer.Ordinal).Select(column => new SelectQueryHelper.SelectQueryColumnDefinition(column, column)).ToList();
		List<SelectQueryHelper.SelectQueryFilterDefinition> filterDefinitions = filters
			.Select(filter => filter.Value.ValueKind == JsonValueKind.Number
				? new SelectQueryHelper.SelectQueryFilterDefinition(
					filter.Column, filter.Value.GetInt64(), SelectQueryHelper.IntDataValueType)
				: new SelectQueryHelper.SelectQueryFilterDefinition(
					filter.Column, filter.Value.GetString()!,
					GuidLiteral.IsMatch(filter.Value.GetString()!)
						? SelectQueryHelper.GuidDataValueType
						: SelectQueryHelper.TextDataValueType))
			.ToList();
		JsonElement query = JsonSerializer.SerializeToElement(
			SelectQueryHelper.BuildSelectQuery(schema, columnDefinitions, filterDefinitions, rowCount));
		return new Hint(prefix + " The equivalent query is returned in 'suggested-query' - send it as 'query' to run "
			+ $"it. Check that '{schema}' is the Creatio schema name, and that the filter values' types (inferred from the "
			+ $"literals) fit their columns. The row cap is {rowCount}"
			+ (requestedRows is null ? " (default; none was given)." : "."), query);
	}
}
