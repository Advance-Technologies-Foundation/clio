using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleHelpers {
	private static readonly Regex ExplicitTimeZoneSuffixRegex = new(
		@"(?:Z|[+-]\d{2}:\d{2})$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);


	internal static IReadOnlyDictionary<string, EntitySchemaColumnDto> BuildColumnMap(EntityDesignSchemaDto entitySchema) {
		ArgumentNullException.ThrowIfNull(entitySchema);

		Dictionary<string, EntitySchemaColumnDto> columns = new(StringComparer.Ordinal);

		foreach (EntitySchemaColumnDto column in entitySchema.Columns
			         .Concat(entitySchema.InheritedColumns)
			         .Where(column => !string.IsNullOrWhiteSpace(column.Name))) {
			columns[column.Name] = column;
		}

		return columns;
	}

	internal static string MapDataValueTypeName(int? dataValueType) {
		if (!dataValueType.HasValue) {
			throw new InvalidOperationException("Entity schema column dataValueType is required.");
		}

		if (!DataValueTypeNames.TryGetValue(dataValueType.Value, out string? value)) {
			throw new InvalidOperationException($"Unsupported entity schema dataValueType '{dataValueType.Value}'.");
		}

		return value;
	}

	internal static int MapComparisonType(string comparisonType) {
		if (!SupportedComparisonTypeValues.TryGetValue(comparisonType, out int value)) {
			throw new InvalidOperationException($"Unsupported business-rule comparisonType '{comparisonType}'.");
		}

		return value;
	}

	internal static bool IsSupportedComparisonType(string comparisonType) =>
		!string.IsNullOrWhiteSpace(comparisonType)
		&& SupportedComparisonTypeValues.ContainsKey(comparisonType);

	internal static bool IsUnaryComparisonType(string comparisonType) =>
		!string.IsNullOrWhiteSpace(comparisonType)
		&& UnaryComparisonTypeNames.Contains(comparisonType);

	internal static bool IsRelationalComparisonType(string comparisonType) =>
		!string.IsNullOrWhiteSpace(comparisonType)
		&& RelationalComparisonTypeNames.Contains(comparisonType);

	internal static bool RequiresRightExpression(string comparisonType) =>
		!IsUnaryComparisonType(comparisonType);

	internal static bool IsTextDataValueType(string dataValueTypeName) =>
		SupportedTextDataValueTypeNames.Contains(dataValueTypeName);

	internal static bool IsNumericDataValueType(string dataValueTypeName) =>
		SupportedNumericDataValueTypeNames.Contains(dataValueTypeName);

	internal static bool IsTemporalDataValueType(string dataValueTypeName) =>
		SupportedTemporalDataValueTypeNames.Contains(dataValueTypeName);

	internal static bool IsRelationalDataValueType(string dataValueTypeName) =>
		IsNumericDataValueType(dataValueTypeName) || IsTemporalDataValueType(dataValueTypeName);

	internal static string GetTemporalConstantValidationMessage(string dataValueTypeName) =>
		dataValueTypeName switch {
			"Date" => "rule.condition.conditions[*].rightExpression.value must be a JSON string in 'yyyy-MM-dd' format when the left attribute is Date.",
			"DateTime" => "rule.condition.conditions[*].rightExpression.value must be a JSON string in ISO 8601 date-time format with a timezone suffix ('Z' or '+/-HH:mm') when the left attribute is DateTime.",
			"Time" => "rule.condition.conditions[*].rightExpression.value must be a JSON string in ISO 8601 time format with a timezone suffix ('Z' or '+/-HH:mm') when the left attribute is Time.",
			_ => "rule.condition.conditions[*].rightExpression.value must be a valid JSON string temporal constant."
		};

	internal static bool TryConvertTemporalConstant(
		JsonElement element,
		string dataValueTypeName,
		out DateTime normalizedValue) {
		normalizedValue = default;
		if (element.ValueKind != JsonValueKind.String) {
			return false;
		}

		string? rawValue = element.GetString();
		if (string.IsNullOrWhiteSpace(rawValue)) {
			return false;
		}
		rawValue = rawValue.Trim();

		switch (dataValueTypeName) {
			case "Date":
				if (!DateOnly.TryParseExact(rawValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
					    out DateOnly dateValue)) {
					return false;
				}

				normalizedValue = new DateTime(dateValue.Year, dateValue.Month, dateValue.Day, 0, 0, 0, DateTimeKind.Utc);
				return true;
			case "DateTime":
				if (!HasExplicitTimeZone(rawValue)
					|| !DateTimeOffset.TryParse(
						rawValue,
						CultureInfo.InvariantCulture,
						DateTimeStyles.AllowWhiteSpaces,
						out DateTimeOffset dateTimeOffsetValue)) {
					return false;
				}

				normalizedValue = dateTimeOffsetValue.UtcDateTime;
				return true;
			case "Time":
				if (!HasExplicitTimeZone(rawValue)
					|| !DateTimeOffset.TryParse(
						$"2000-01-01T{rawValue}",
						CultureInfo.InvariantCulture,
						DateTimeStyles.AllowWhiteSpaces,
						out DateTimeOffset timeWithOffset)) {
					return false;
				}

				TimeOnly utcTimeValue = TimeOnly.FromDateTime(timeWithOffset.UtcDateTime);
				normalizedValue = new DateTime(DateOnly.MinValue, utcTimeValue, DateTimeKind.Utc);
				return true;
			default:
				return false;
		}
	}

	private static bool HasExplicitTimeZone(string rawValue) =>
		ExplicitTimeZoneSuffixRegex.IsMatch(rawValue);

	internal static object? ConvertJsonElement(JsonElement element, string? dataValueTypeName = null) {
		if (!string.IsNullOrWhiteSpace(dataValueTypeName)
			&& IsTemporalDataValueType(dataValueTypeName)
			&& TryConvertTemporalConstant(element, dataValueTypeName, out DateTime temporalValue)) {
			return temporalValue;
		}

		return element.ValueKind switch {
			JsonValueKind.Null => null,
			JsonValueKind.String => element.GetString(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number when element.TryGetInt64(out long intValue) => intValue,
			JsonValueKind.Number when element.TryGetDecimal(out decimal decimalValue) => decimalValue,
			JsonValueKind.Array or JsonValueKind.Object => JsonNode.Parse(element.GetRawText()),
			_ => element.ToString()
		};
	}
}
