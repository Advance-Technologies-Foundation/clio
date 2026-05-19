using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Builds the Creatio ESQ filter envelope nodes for relative-date macros — values like
/// <c>PREVIOUS_WEEK</c>, <c>CURRENT_QUARTER</c>, <c>NEXT_YEAR()</c>, <c>WITHIN_PREV_HOURS(N)</c>,
/// <c>ANNIVERSARY_WITHIN_NEXTDAYS(N)</c>, <c>DAY_OF_WEEK(N)</c>, <c>EXACT_YEAR(N)</c>, etc.
/// Ported and adapted from CrtCopilot's <c>LlmEsqFiltersConverter</c> macro paths
/// (<c>GenerateMacroConfig</c> / <c>GenerateMacroWithParameterConfig</c> /
/// <c>GenerateRelativeDateParameterConfig</c>).
///
/// Caller sends the macro as a JSON string in the leaf <c>value</c>; this builder detects the
/// pattern, maps it to the matching <see cref="EsqEntitySchemaQueryMacrosType"/> or
/// <see cref="EsqDatePart"/>, and emits the equivalent <see cref="SerializableFilter"/> the
/// platform runtime evaluates against time-of-fire (so the filter remains "static" in shape
/// even though its effective set of rows changes over time).
/// </summary>
internal static class EsqMacroBuilder {

	internal static readonly Regex SimpleMacroRegex =
		new(@"^(PREVIOUS|CURRENT|NEXT)_(HOUR|DAY|WEEK|MONTH|QUARTER|HALFYEAR|YEAR)(\(\))?$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
			TimeSpan.FromMilliseconds(100));

	internal static readonly Regex ParameterizedMacroRegex =
		new(@"^(WITHIN_PREV_HOURS|WITHIN_NEXT_HOURS|WITHIN_PREV_DAYS|WITHIN_NEXT_DAYS|" +
			@"ANNIVERSARY_WITHIN_NEXTDAYS|ANNIVERSARY_WITHIN_PREVDAYS|ANNIVERSARY_EXACTLY_IN_DAYS)" +
			@"(\(\d+\))?$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
			TimeSpan.FromMilliseconds(100));

	internal static readonly Regex AnniversaryTodayRegex =
		new(@"^ANNIVERSARY_TODAY(\(\))?$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
			TimeSpan.FromMilliseconds(100));

	internal static readonly Regex DatePartMacroRegex =
		new(@"^(DAY_OF_WEEK|DAY_OF_MONTH|MONTH|EXACT_YEAR)\((\d+)\)$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
			TimeSpan.FromMilliseconds(100));

	internal static bool IsMacroValue(JsonElement? value) {
		if (!value.HasValue || value.Value.ValueKind != JsonValueKind.String) {
			return false;
		}
		string? raw = value.Value.GetString();
		return !string.IsNullOrEmpty(raw)
			&& (SimpleMacroRegex.IsMatch(raw)
				|| ParameterizedMacroRegex.IsMatch(raw)
				|| AnniversaryTodayRegex.IsMatch(raw)
				|| DatePartMacroRegex.IsMatch(raw));
	}

	internal static SerializableFilter Build(StaticFilterLeaf leaf, EntitySchemaColumnDto column) {
		string raw = leaf.Value!.Value.GetString()!.Trim();
		Match simple = SimpleMacroRegex.Match(raw);
		if (simple.Success) {
			return BuildSimpleMacroFilter(leaf, simple);
		}
		if (AnniversaryTodayRegex.IsMatch(raw)) {
			return BuildSimpleMacroByType(leaf, EsqEntitySchemaQueryMacrosType.DayOfYearToday);
		}
		Match parameterized = ParameterizedMacroRegex.Match(raw);
		if (parameterized.Success) {
			return BuildParameterizedMacroFilter(leaf, parameterized);
		}
		Match datePart = DatePartMacroRegex.Match(raw);
		if (datePart.Success) {
			return BuildDatePartFilter(leaf, datePart);
		}
		throw new InvalidOperationException(
			$"Macro value '{raw}' passed IsMacroValue check but matched no concrete pattern.");
	}

	private static SerializableFilter BuildSimpleMacroFilter(StaticFilterLeaf leaf, Match match) {
		string position = match.Groups[1].Value.ToUpperInvariant();
		string unit = match.Groups[2].Value.ToUpperInvariant();
		EsqEntitySchemaQueryMacrosType macroType = MapSimpleMacro(position, unit);
		return BuildSimpleMacroByType(leaf, macroType);
	}

	private static SerializableFilter BuildSimpleMacroByType(
		StaticFilterLeaf leaf,
		EsqEntitySchemaQueryMacrosType macroType) {
		return new SerializableFilter {
			FilterType = EsqFilterType.CompareFilter,
			ComparisonType = MapComparisonType(leaf.ComparisonType),
			IsEnabled = true,
			ClassName = EsqFilterClassNames.CompareFilter,
			Key = string.Empty,
			LeftExpression = new SerializableExpression {
				ExpressionType = EsqEntitySchemaQueryExpressionType.SchemaColumn,
				ColumnPath = LocalEsqFilterConverter.NormalizeColumnPath(leaf.ColumnPath),
				ClassName = EsqFilterClassNames.ColumnExpression
			},
			RightExpression = new SerializableExpression {
				ExpressionType = EsqEntitySchemaQueryExpressionType.Function,
				FunctionType = EsqFunctionType.Macros,
				MacrosType = macroType,
				ClassName = EsqFilterClassNames.FunctionExpression
			}
		};
	}

	private static SerializableFilter BuildParameterizedMacroFilter(StaticFilterLeaf leaf, Match match) {
		string macroToken = match.Groups[1].Value.ToUpperInvariant();
		int n = ExtractParameter(match.Groups[2].Value);
		EsqEntitySchemaQueryMacrosType macroType = MapParameterizedMacro(macroToken);
		return new SerializableFilter {
			FilterType = EsqFilterType.CompareFilter,
			ComparisonType = MapComparisonType(leaf.ComparisonType),
			IsEnabled = true,
			ClassName = EsqFilterClassNames.CompareFilter,
			Key = string.Empty,
			LeftExpression = new SerializableExpression {
				ExpressionType = EsqEntitySchemaQueryExpressionType.SchemaColumn,
				ColumnPath = LocalEsqFilterConverter.NormalizeColumnPath(leaf.ColumnPath),
				ClassName = EsqFilterClassNames.ColumnExpression
			},
			RightExpression = new SerializableExpression {
				ExpressionType = EsqEntitySchemaQueryExpressionType.Function,
				FunctionType = EsqFunctionType.Macros,
				MacrosType = macroType,
				FunctionArgument = new SerializableExpression {
					ExpressionType = EsqEntitySchemaQueryExpressionType.Parameter,
					Parameter = new SerializableParameter {
						DataValueType = EsqDataValueType.Integer,
						Value = n,
						ClassName = EsqFilterClassNames.Parameter
					},
					ClassName = EsqFilterClassNames.ParameterExpression
				},
				ClassName = EsqFilterClassNames.FunctionExpression
			}
		};
	}

	private static SerializableFilter BuildDatePartFilter(StaticFilterLeaf leaf, Match match) {
		string token = match.Groups[1].Value.ToUpperInvariant();
		int n = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
		EsqDatePart datePart = MapDatePart(token);
		return new SerializableFilter {
			FilterType = EsqFilterType.CompareFilter,
			ComparisonType = EsqFilterComparisonType.Equal,
			IsEnabled = true,
			ClassName = EsqFilterClassNames.CompareFilter,
			Key = string.Empty,
			LeftExpression = new SerializableExpression {
				ExpressionType = EsqEntitySchemaQueryExpressionType.Function,
				FunctionType = EsqFunctionType.DatePart,
				FunctionArgument = new SerializableExpression {
					ExpressionType = EsqEntitySchemaQueryExpressionType.SchemaColumn,
					ColumnPath = LocalEsqFilterConverter.NormalizeColumnPath(leaf.ColumnPath),
					ClassName = EsqFilterClassNames.ColumnExpression
				},
				DatePartType = datePart,
				ClassName = EsqFilterClassNames.FunctionExpression
			},
			RightExpression = new SerializableExpression {
				ExpressionType = EsqEntitySchemaQueryExpressionType.Parameter,
				Parameter = new SerializableParameter {
					DataValueType = EsqDataValueType.Integer,
					Value = n,
					ClassName = EsqFilterClassNames.Parameter
				},
				ClassName = EsqFilterClassNames.ParameterExpression
			}
		};
	}

	private static int ExtractParameter(string parenWithDigits) {
		// Group captures "(N)"; strip parentheses, parse the digits. Empty group means caller
		// supplied the bare token without `(N)` — that's a structural error from validation, not
		// a parser problem here.
		if (string.IsNullOrEmpty(parenWithDigits)) {
			throw new InvalidOperationException(
				"Parameterized macro is missing the '(N)' parameter; structural validator should have rejected earlier.");
		}
		string inner = parenWithDigits.Trim('(', ')');
		return int.Parse(inner, CultureInfo.InvariantCulture);
	}

	private static EsqEntitySchemaQueryMacrosType MapSimpleMacro(string position, string unit) =>
		(position, unit) switch {
			("PREVIOUS", "HOUR") => EsqEntitySchemaQueryMacrosType.PreviousHour,
			("PREVIOUS", "DAY") => EsqEntitySchemaQueryMacrosType.Yesterday,
			("PREVIOUS", "WEEK") => EsqEntitySchemaQueryMacrosType.PreviousWeek,
			("PREVIOUS", "MONTH") => EsqEntitySchemaQueryMacrosType.PreviousMonth,
			("PREVIOUS", "QUARTER") => EsqEntitySchemaQueryMacrosType.PreviousQuarter,
			("PREVIOUS", "HALFYEAR") => EsqEntitySchemaQueryMacrosType.PreviousHalfYear,
			("PREVIOUS", "YEAR") => EsqEntitySchemaQueryMacrosType.PreviousYear,
			("CURRENT", "HOUR") => EsqEntitySchemaQueryMacrosType.CurrentHour,
			("CURRENT", "DAY") => EsqEntitySchemaQueryMacrosType.Today,
			("CURRENT", "WEEK") => EsqEntitySchemaQueryMacrosType.CurrentWeek,
			("CURRENT", "MONTH") => EsqEntitySchemaQueryMacrosType.CurrentMonth,
			("CURRENT", "QUARTER") => EsqEntitySchemaQueryMacrosType.CurrentQuarter,
			("CURRENT", "HALFYEAR") => EsqEntitySchemaQueryMacrosType.CurrentHalfYear,
			("CURRENT", "YEAR") => EsqEntitySchemaQueryMacrosType.CurrentYear,
			("NEXT", "HOUR") => EsqEntitySchemaQueryMacrosType.NextHour,
			("NEXT", "DAY") => EsqEntitySchemaQueryMacrosType.Tomorrow,
			("NEXT", "WEEK") => EsqEntitySchemaQueryMacrosType.NextWeek,
			("NEXT", "MONTH") => EsqEntitySchemaQueryMacrosType.NextMonth,
			("NEXT", "QUARTER") => EsqEntitySchemaQueryMacrosType.NextQuarter,
			("NEXT", "HALFYEAR") => EsqEntitySchemaQueryMacrosType.NextHalfYear,
			("NEXT", "YEAR") => EsqEntitySchemaQueryMacrosType.NextYear,
			_ => throw new InvalidOperationException(
				$"Unsupported simple-macro combination '{position}_{unit}'.")
		};

	private static EsqEntitySchemaQueryMacrosType MapParameterizedMacro(string token) =>
		token switch {
			"WITHIN_PREV_HOURS" => EsqEntitySchemaQueryMacrosType.PreviousNHours,
			"WITHIN_NEXT_HOURS" => EsqEntitySchemaQueryMacrosType.NextNHours,
			"WITHIN_PREV_DAYS" => EsqEntitySchemaQueryMacrosType.PreviousNDays,
			"WITHIN_NEXT_DAYS" => EsqEntitySchemaQueryMacrosType.NextNDays,
			"ANNIVERSARY_WITHIN_NEXTDAYS" => EsqEntitySchemaQueryMacrosType.NextNDaysOfYear,
			"ANNIVERSARY_WITHIN_PREVDAYS" => EsqEntitySchemaQueryMacrosType.PreviousNDaysOfYear,
			"ANNIVERSARY_EXACTLY_IN_DAYS" => EsqEntitySchemaQueryMacrosType.DayOfYearTodayPlusDaysOffset,
			_ => throw new InvalidOperationException(
				$"Unsupported parameterized-macro token '{token}'.")
		};

	private static EsqDatePart MapDatePart(string token) =>
		token switch {
			"DAY_OF_WEEK" => EsqDatePart.Weekday,
			"DAY_OF_MONTH" => EsqDatePart.Day,
			"MONTH" => EsqDatePart.Month,
			"EXACT_YEAR" => EsqDatePart.Year,
			_ => throw new InvalidOperationException($"Unsupported DatePart token '{token}'.")
		};

	private static EsqFilterComparisonType MapComparisonType(string token) =>
		token?.ToUpperInvariant() switch {
			"EQUAL" => EsqFilterComparisonType.Equal,
			"NOT_EQUAL" => EsqFilterComparisonType.NotEqual,
			"LESS" => EsqFilterComparisonType.Less,
			"LESS_OR_EQUAL" => EsqFilterComparisonType.LessOrEqual,
			"GREATER" => EsqFilterComparisonType.Greater,
			"GREATER_OR_EQUAL" => EsqFilterComparisonType.GreaterOrEqual,
			_ => throw new BusinessRuleFilterException(
				BusinessRuleFilterErrorCodes.ComparisonNotSupportedForDatatype,
				"rule.actions[*].filter.filters[*].comparisonType",
				$"Comparison '{token}' is not supported with a relative-date macro value. " +
				"Use EQUAL, NOT_EQUAL, GREATER, GREATER_OR_EQUAL, LESS, or LESS_OR_EQUAL.")
		};
}
