using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.BusinessRules.Filters.Schema;

namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Builds the platform ESQ envelope JSON string from a validated <see cref="StaticFilterGroup"/>.
/// Ported in spirit from CrtCopilot LlmEsqFiltersConverter, scoped to this iteration's coverage.
/// </summary>
internal sealed class LocalEsqFilterBuilder {

	private static readonly Regex GuidRegex = new(
		@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
		RegexOptions.Compiled);

	private static readonly JsonSerializerOptions SerializerOptions = new() {
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = false
	};

	private readonly IFilterSchemaProvider _schemaProvider;
	private readonly ILookupValueResolver? _lookupResolver;
	private readonly SchemaAwareFilterValidator _schemaValidator;

	public LocalEsqFilterBuilder(
		IFilterSchemaProvider schemaProvider,
		ILookupValueResolver? lookupResolver) {
		_schemaProvider = schemaProvider ?? throw new ArgumentNullException(nameof(schemaProvider));
		_lookupResolver = lookupResolver;
		_schemaValidator = new SchemaAwareFilterValidator(schemaProvider);
	}

	public string Build(StaticFilterGroup group, string rootSchemaName) {
		EsqRootEnvelopeDto envelope = new() {
			RootSchemaName = rootSchemaName,
			LogicalOperation = MapLogicalOperation(group.LogicalOperation),
			Items = BuildGroupItems(group, rootSchemaName, indexBase: 0)
		};

		return JsonSerializer.Serialize(envelope, SerializerOptions);
	}

	private Dictionary<string, object> BuildGroupItems(StaticFilterGroup group, string currentSchema, int indexBase) {
		Dictionary<string, object> items = [];
		int leafIndex = 0;
		foreach (StaticFilterLeaf leaf in group.Filters) {
			string key = $"Filter_{indexBase + leafIndex}";
			items[key] = BuildLeaf(leaf, currentSchema);
			leafIndex++;
		}

		int groupIndex = 0;
		foreach (StaticFilterGroup nested in group.Groups) {
			string key = $"Group_{indexBase + groupIndex}";
			EsqNestedGroupDto nestedDto = new() {
				LogicalOperation = MapLogicalOperation(nested.LogicalOperation),
				Items = BuildGroupItems(nested, currentSchema, indexBase: (indexBase + groupIndex + 1) * 100)
			};
			items[key] = nestedDto;
			groupIndex++;
		}

		int backwardIndex = 0;
		foreach (StaticFilterBackwardReference backward in group.BackwardReferenceFilters) {
			string key = $"BackwardReferenceFilter_{indexBase + backwardIndex}";
			items[key] = BuildBackwardReference(backward, currentSchema, (indexBase + backwardIndex + 1) * 100);
			backwardIndex++;
		}

		return items;
	}

	private object BuildLeaf(StaticFilterLeaf leaf, string currentSchema) {
		FilterSchemaColumn column = _schemaValidator.ResolveColumnPath(leaf.ColumnPath, currentSchema, "filter");
		string normalizedColumnPath = NormalizeColumnPath(leaf.ColumnPath, column);
		string comparison = leaf.ComparisonType.ToUpperInvariant();

		if (StaticFilterConstants.UnaryComparisons.Contains(comparison)) {
			return BuildIsNullFilter(comparison, normalizedColumnPath);
		}

		JsonElement value = leaf.Value!.Value;
		bool isLookup = string.Equals(column.DataValueTypeName, "Lookup", StringComparison.OrdinalIgnoreCase);
		if (isLookup) {
			return BuildLookupInFilter(comparison, normalizedColumnPath, column, value);
		}

		return BuildCompareFilter(comparison, normalizedColumnPath, column, value);
	}

	private static EsqIsNullFilterDto BuildIsNullFilter(string comparison, string columnPath) {
		bool isNull = comparison == StaticFilterConstants.IsNull;
		return new EsqIsNullFilterDto {
			ComparisonType = (int)(isNull ? EsqComparisonType.IsNull : EsqComparisonType.IsNotNull),
			IsNull = isNull,
			LeftExpression = new EsqColumnExpressionDto { ColumnPath = columnPath }
		};
	}

	private EsqInFilterDto BuildLookupInFilter(string comparison, string columnPath, FilterSchemaColumn column,
		JsonElement value) {
		List<EsqParameterExpressionDto> rightExpressions = [];
		if (value.ValueKind == JsonValueKind.Array) {
			foreach (JsonElement item in value.EnumerateArray()) {
				rightExpressions.Add(BuildLookupParameter(item.GetString() ?? string.Empty, column));
			}
		} else if (value.ValueKind == JsonValueKind.String) {
			rightExpressions.Add(BuildLookupParameter(value.GetString() ?? string.Empty, column));
		} else {
			throw new ArgumentException(
				$"filter: Lookup column '{columnPath}' expects string or array of strings.");
		}

		return new EsqInFilterDto {
			ComparisonType = MapEqualityComparisonToEsq(comparison),
			LeftExpression = new EsqColumnExpressionDto { ColumnPath = columnPath },
			RightExpressions = rightExpressions
		};
	}

	private EsqParameterExpressionDto BuildLookupParameter(string rawValue, FilterSchemaColumn column) {
		Guid id = ResolveLookupId(rawValue, column);
		return new EsqParameterExpressionDto {
			Parameter = new EsqParameterDto {
				DataValueType = (int)EsqDataValueType.Lookup,
				Value = new EsqLookupValueDto {
					Value = id.ToString("D"),
					DisplayValue = GuidRegex.IsMatch(rawValue) ? null : rawValue
				}
			}
		};
	}

	private Guid ResolveLookupId(string rawValue, FilterSchemaColumn column) {
		if (GuidRegex.IsMatch(rawValue)) {
			return Guid.Parse(rawValue);
		}

		if (_lookupResolver is null) {
			throw new ArgumentException(
				$"filter: Lookup value '{rawValue}' is not a GUID and no lookup-value resolver is configured.");
		}

		if (string.IsNullOrEmpty(column.ReferenceSchemaName)) {
			throw new ArgumentException(
				"filter: Lookup column reference schema is missing; cannot resolve display name.");
		}

		return _lookupResolver.ResolveIdByDisplayName(column.ReferenceSchemaName, rawValue);
	}

	private static EsqCompareFilterDto BuildCompareFilter(string comparison, string columnPath, FilterSchemaColumn column,
		JsonElement value) {
		EsqDataValueType valueDataType = MapColumnDatatypeToParameterType(column.DataValueTypeName);
		object parameterValue = ConvertScalarValue(value, valueDataType, column.DataValueTypeName);
		bool trimDate = valueDataType == EsqDataValueType.DateTime;
		return new EsqCompareFilterDto {
			ComparisonType = MapLeafComparisonToEsq(comparison),
			TrimDateTimeParameterToDate = trimDate ? true : null,
			LeftExpression = new EsqColumnExpressionDto { ColumnPath = columnPath },
			RightExpression = new EsqParameterExpressionDto {
				Parameter = new EsqParameterDto {
					DataValueType = (int)valueDataType,
					Value = parameterValue
				}
			}
		};
	}

	private object BuildBackwardReference(StaticFilterBackwardReference backward, string currentSchema, int indexBase) {
		_ = currentSchema; // currentSchema unused at envelope build (path validated upstream); left for parity.
		(string childSchema, _) = ParseBackwardReference(backward.ReferenceColumnPath);
		StaticFilterGroup subFilter = backward.Filter ?? new StaticFilterGroup {
			LogicalOperation = StaticFilterConstants.LogicalAnd
		};

		EsqNestedGroupDto subFilters = new() {
			RootSchemaName = childSchema,
			LogicalOperation = MapLogicalOperation(subFilter.LogicalOperation),
			Items = BuildGroupItems(subFilter, childSchema, indexBase)
		};

		bool isExists = string.Equals(backward.ComparisonType, StaticFilterConstants.Exists,
			StringComparison.OrdinalIgnoreCase);
		return new EsqExistsFilterDto {
			ComparisonType = (int)(isExists ? EsqComparisonType.Exists : EsqComparisonType.NotExists),
			LeftExpression = new EsqColumnExpressionDto { ColumnPath = backward.ReferenceColumnPath },
			SubFilters = subFilters
		};
	}

	private static int MapLogicalOperation(string logicalOperation) =>
		string.Equals(logicalOperation, StaticFilterConstants.LogicalOr, StringComparison.OrdinalIgnoreCase)
			? (int)EsqLogicalOperation.Or
			: (int)EsqLogicalOperation.And;

	private static int MapLeafComparisonToEsq(string comparison) => comparison switch {
		StaticFilterConstants.Equal => (int)EsqComparisonType.Equal,
		StaticFilterConstants.NotEqual => (int)EsqComparisonType.NotEqual,
		StaticFilterConstants.Less => (int)EsqComparisonType.Less,
		StaticFilterConstants.LessOrEqual => (int)EsqComparisonType.LessOrEqual,
		StaticFilterConstants.Greater => (int)EsqComparisonType.Greater,
		StaticFilterConstants.GreaterOrEqual => (int)EsqComparisonType.GreaterOrEqual,
		StaticFilterConstants.StartWith => (int)EsqComparisonType.StartWith,
		StaticFilterConstants.NotStartWith => (int)EsqComparisonType.NotStartWith,
		StaticFilterConstants.Contain => (int)EsqComparisonType.Contain,
		StaticFilterConstants.NotContain => (int)EsqComparisonType.NotContain,
		StaticFilterConstants.EndWith => (int)EsqComparisonType.EndWith,
		StaticFilterConstants.NotEndWith => (int)EsqComparisonType.NotEndWith,
		_ => throw new ArgumentException($"Unsupported comparison '{comparison}'.")
	};

	private static int MapEqualityComparisonToEsq(string comparison) => comparison switch {
		StaticFilterConstants.Equal => (int)EsqComparisonType.Equal,
		StaticFilterConstants.NotEqual => (int)EsqComparisonType.NotEqual,
		_ => throw new ArgumentException(
			$"InFilter on Lookup supports only EQUAL or NOT_EQUAL (got '{comparison}').")
	};

	private static EsqDataValueType MapColumnDatatypeToParameterType(string dataValueTypeName) =>
		dataValueTypeName switch {
			"Boolean" => EsqDataValueType.Boolean,
			"Integer" => EsqDataValueType.Integer,
			"Float" or "Money" or "Float0" or "Float1" or "Float2" or "Float3" or "Float4" or "Float8" =>
				EsqDataValueType.Float,
			"DateTime" => EsqDataValueType.DateTime,
			"Date" => EsqDataValueType.Date,
			"Time" => EsqDataValueType.Time,
			"Guid" => EsqDataValueType.Guid,
			"Lookup" => EsqDataValueType.Lookup,
			_ => EsqDataValueType.MediumText
		};

	private static object ConvertScalarValue(JsonElement value, EsqDataValueType target, string columnDatatype) {
		switch (target) {
			case EsqDataValueType.Boolean:
				return value.GetBoolean();
			case EsqDataValueType.Integer:
				return value.GetInt64();
			case EsqDataValueType.Float:
				return value.GetDecimal();
			case EsqDataValueType.Guid:
				return value.GetString() ?? string.Empty;
			case EsqDataValueType.DateTime:
			case EsqDataValueType.Date:
			case EsqDataValueType.Time:
				return value.GetString() ?? string.Empty;
			default:
				return value.ValueKind switch {
					JsonValueKind.String => value.GetString() ?? string.Empty,
					JsonValueKind.Number => value.GetDecimal().ToString(CultureInfo.InvariantCulture),
					JsonValueKind.True => true,
					JsonValueKind.False => false,
					_ => throw new ArgumentException(
						$"Unsupported JSON value kind '{value.ValueKind}' for column type '{columnDatatype}'.")
				};
		}
	}

	private static string NormalizeColumnPath(string columnPath, FilterSchemaColumn finalColumn) {
		// strip trailing "Id" segment if it points to the same column with a synthetic Id suffix (parity with platform behavior)
		if (columnPath.EndsWith(".Id", StringComparison.Ordinal)
			&& string.Equals(finalColumn.DataValueTypeName, "Guid", StringComparison.OrdinalIgnoreCase)) {
			return columnPath[..^3];
		}

		return columnPath;
	}

	private static (string childSchema, string childColumn) ParseBackwardReference(string referenceColumnPath) {
		string inner = referenceColumnPath.Trim('[', ']');
		string[] parts = inner.Split(':');
		return (parts[0], parts[1]);
	}
}
