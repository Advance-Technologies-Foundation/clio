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
		RegexOptions.Compiled,
		TimeSpan.FromMilliseconds(100));

	private static readonly JsonSerializerOptions SerializerOptions = new() {
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = false
	};

	private readonly ILookupValueResolver? _lookupResolver;
	private readonly SchemaAwareFilterValidator _schemaValidator;
	private readonly Func<DateTimeOffset> _now;

	public LocalEsqFilterBuilder(
		IFilterSchemaProvider schemaProvider,
		ILookupValueResolver? lookupResolver,
		Func<DateTimeOffset>? nowProvider = null) {
		ArgumentNullException.ThrowIfNull(schemaProvider);
		_lookupResolver = lookupResolver;
		_schemaValidator = new SchemaAwareFilterValidator(schemaProvider);
		_now = nowProvider ?? (() => DateTimeOffset.Now);
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

		if (!string.IsNullOrWhiteSpace(leaf.DatePart)) {
			return BuildDatePartCompareFilter(comparison, normalizedColumnPath, column, leaf);
		}

		if (!string.IsNullOrWhiteSpace(leaf.ValueMacros)) {
			return BuildMacrosCompareFilter(comparison, normalizedColumnPath, column, leaf);
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
			IsAggregative = false,
			DataValueType = (int)EsqDataValueType.Lookup,
			ReferenceSchemaName = column.ReferenceSchemaName,
			RightExpressions = rightExpressions
		};
	}

	private EsqParameterExpressionDto BuildLookupParameter(string rawValue, FilterSchemaColumn column) {
		Guid id = ResolveLookupId(rawValue, column);
		string idString = id.ToString("D");
		string? displayName = GuidRegex.IsMatch(rawValue) ? null : rawValue;
		// When the caller passed a GUID directly we enrich Name/displayValue from the lookup so the stored value
		// matches the platform-canonical form (Name + displayValue alongside Id + value).
		if (displayName is null
			&& _lookupResolver is not null
			&& !string.IsNullOrEmpty(column.ReferenceSchemaName)
			&& _lookupResolver.TryResolveDisplayNameById(column.ReferenceSchemaName!, id, out string? resolved)) {
			displayName = resolved;
		}

		// A Lookup filter value MUST carry Name/displayValue: the Freedom UI lookup control reads them off the
		// parameter value and fails to render an Id-only value. Enrichment is therefore mandatory, not best-effort.
		if (string.IsNullOrEmpty(displayName)) {
			throw new ArgumentException(
				$"filter: could not resolve the display name for Lookup value '{idString}' on schema "
				+ $"'{column.ReferenceSchemaName}'. A Lookup filter value must carry Name/displayValue or the Freedom UI "
				+ "lookup control fails to render it. Pass the lookup's display name instead of a raw GUID (clio resolves "
				+ "the Id and keeps the name), or verify the GUID exists in that schema.");
		}

		return new EsqParameterExpressionDto {
			Parameter = new EsqParameterDto {
				DataValueType = (int)EsqDataValueType.Lookup,
				Value = new EsqLookupValueDto {
					Name = displayName,
					Id = idString,
					Value = idString,
					DisplayValue = displayName
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
		// trimDateTimeParameterToDate is intentionally left unset (null → omitted from envelope) so that
		// timestamp comparisons such as CreatedOn GREATER 2026-05-01T12:00:00Z preserve the time portion.
		return new EsqCompareFilterDto {
			ComparisonType = MapLeafComparisonToEsq(comparison),
			LeftExpression = new EsqColumnExpressionDto { ColumnPath = columnPath },
			RightExpression = new EsqParameterExpressionDto {
				Parameter = new EsqParameterDto {
					DataValueType = (int)valueDataType,
					Value = parameterValue
				}
			}
		};
	}

	private EsqCompareFilterDto BuildDatePartCompareFilter(
		string comparison, string columnPath, FilterSchemaColumn column, StaticFilterLeaf leaf) {
		DatePartCatalog.TryResolve(leaf.DatePart!, out EsqDatePartType partType,
			out DatePartCatalog.DatePartValueKind valueKind);
		JsonElement value = leaf.Value!.Value;
		EsqDatePartFunctionExpressionDto leftExpression = new() {
			DatePartType = (int)partType,
			FunctionArgument = new EsqColumnExpressionDto { ColumnPath = columnPath }
		};

		if (valueKind == DatePartCatalog.DatePartValueKind.Time) {
			// HourMinute compares the extracted time-of-day against a Time parameter. The Freedom UI lookup
			// control needs a FULL datetime, not a bare "HH:mm": it reads a local `value` (a quote-wrapped ISO
			// string) to render and a UTC `dateValue` for the query. A bare time renders as a placeholder. The
			// date is just a carrier (HourMinute extraction ignores it); we use today's date in the host TZ so
			// `value` (local) and `dateValue` (UTC) stay self-consistent.
			TimeSpan timeOfDay = ParseTimeOfDay(value.GetString());
			DateTimeOffset now = _now();
			DateTime localDateTime = now.Date + timeOfDay;
			DateTimeOffset localOffset = new(localDateTime, now.Offset);
			string localIso = localDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
			string utcIso = localOffset.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
			return new EsqCompareFilterDto {
				ComparisonType = MapLeafComparisonToEsq(comparison),
				TrimDateTimeParameterToDate = true,
				LeftExpression = leftExpression,
				IsAggregative = false,
				DataValueType = column.DataValueTypeCode > 0 ? column.DataValueTypeCode : null,
				RightExpression = new EsqParameterExpressionDto {
					Parameter = new EsqParameterDto {
						DataValueType = (int)EsqDataValueType.Time,
						DateValue = utcIso,
						// The platform stores the local datetime as a quote-wrapped JSON string literal.
						Value = "\"" + localIso + "\""
					}
				}
			};
		}

		// Calendar/clock parts (Day/Week/Month/Year/Weekday/Hour) compare the extracted integer part against an
		// Integer parameter (e.g. Year EQUAL 2021, Day EQUAL 14, Hour EQUAL 11). The leaf carries no dataValueType
		// of its own — the integer lives in the right parameter.
		return new EsqCompareFilterDto {
			ComparisonType = MapLeafComparisonToEsq(comparison),
			LeftExpression = leftExpression,
			RightExpression = new EsqParameterExpressionDto {
				Parameter = new EsqParameterDto {
					DataValueType = (int)EsqDataValueType.Integer,
					Value = value.GetInt64()
				}
			}
		};
	}

	private static EsqCompareFilterDto BuildMacrosCompareFilter(
		string comparison, string columnPath, FilterSchemaColumn column, StaticFilterLeaf leaf) {
		MacrosCatalog.TryResolve(leaf.ValueMacros!, out EsqQueryMacrosType macrosType,
			out MacrosCatalog.MacrosColumnKind kind, out bool requiresArgument);
		EsqParameterExpressionDto? argument = requiresArgument
			? new EsqParameterExpressionDto {
				Parameter = new EsqParameterDto {
					DataValueType = (int)EsqDataValueType.Integer,
					Value = leaf.ValueMacrosArgument!.Value
				}
			}
			: null;

		bool isDateColumn = column.DataValueTypeName is "Date" or "DateTime" or "Time";
		// Date macros (Today, Yesterday, NextNDays, ...) on a DateTime column require trimDateTimeParameterToDate=true
		// so the comparison ignores the time portion; without it `CreatedOn EQUAL Today` matches only 00:00:00.000.
		bool trim = kind == MacrosCatalog.MacrosColumnKind.DateTime && isDateColumn;

		return new EsqCompareFilterDto {
			ComparisonType = MapLeafComparisonToEsq(comparison),
			TrimDateTimeParameterToDate = trim ? true : null,
			LeftExpression = new EsqColumnExpressionDto { ColumnPath = columnPath },
			IsAggregative = false,
			DataValueType = column.DataValueTypeCode > 0 ? column.DataValueTypeCode : null,
			RightExpression = new EsqMacrosFunctionExpressionDto {
				MacrosType = (int)macrosType,
				FunctionArgument = argument
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

		if (!string.IsNullOrWhiteSpace(backward.AggregationType)) {
			return BuildAggregationBackwardReference(backward, subFilters);
		}

		bool isExists = string.Equals(backward.ComparisonType, StaticFilterConstants.Exists,
			StringComparison.OrdinalIgnoreCase);
		// Platform-canonical EXISTS leftExpression points at the link column's Id (e.g. [Contact:Account].Id),
		// and carries dataValueType=Integer (the aggregation-count type). The friendly contract accepts
		// `[Schema:Column]` without the `.Id` suffix; the builder appends it here.
		return new EsqExistsFilterDto {
			ComparisonType = (int)(isExists ? EsqComparisonType.Exists : EsqComparisonType.NotExists),
			LeftExpression = new EsqColumnExpressionDto { ColumnPath = backward.ReferenceColumnPath + ".Id" },
			DataValueType = (int)EsqDataValueType.Integer,
			SubFilters = subFilters
		};
	}

	private static EsqAggregationFilterDto BuildAggregationBackwardReference(
		StaticFilterBackwardReference backward, EsqNestedGroupDto subFilters) {
		EsqAggregationType aggregationType = MapAggregationType(backward.AggregationType!);
		bool isCount = aggregationType == EsqAggregationType.Count;
		// COUNT aggregates the child rows themselves (link column .Id); SUM/AVG/MIN/MAX aggregate a numeric
		// child column appended to the backward path (e.g. [Activity:Contact].Amount).
		string aggregatedColumn = isCount
			? backward.ReferenceColumnPath + ".Id"
			: backward.ReferenceColumnPath + "." + backward.AggregationColumnPath;
		// COUNT/* compares to an Integer; SUM/AVG/MIN/MAX compare to a Float threshold.
		EsqDataValueType valueType = isCount ? EsqDataValueType.Integer : EsqDataValueType.Float;
		object value = isCount ? (long)backward.AggregationValue!.Value : (decimal)backward.AggregationValue!.Value;

		return new EsqAggregationFilterDto {
			ComparisonType = MapLeafComparisonToEsq(backward.ComparisonType.ToUpperInvariant()),
			IsAggregative = true,
			LeftExpression = new EsqAggregationExpressionDto {
				AggregationType = (int)aggregationType,
				ColumnPath = aggregatedColumn,
				SubFilters = subFilters
			},
			RightExpression = new EsqParameterExpressionDto {
				Parameter = new EsqParameterDto {
					DataValueType = (int)valueType,
					Value = value
				}
			},
			SubFilters = subFilters
		};
	}

	private static EsqAggregationType MapAggregationType(string aggregationType) => aggregationType.ToUpperInvariant() switch {
		StaticFilterConstants.Count => EsqAggregationType.Count,
		StaticFilterConstants.Sum => EsqAggregationType.Sum,
		StaticFilterConstants.Avg => EsqAggregationType.Avg,
		StaticFilterConstants.Min => EsqAggregationType.Min,
		StaticFilterConstants.Max => EsqAggregationType.Max,
		_ => throw new ArgumentException($"Unsupported aggregation '{aggregationType}'.")
	};

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
			"Float" or "Money" or "Money0" or "Money1" or "Money3"
				or "Float0" or "Float1" or "Float2" or "Float3" or "Float4" or "Float8" =>
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

	private static TimeSpan ParseTimeOfDay(string? raw) {
		if (!string.IsNullOrWhiteSpace(raw)) {
			if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out TimeSpan parsed)) {
				return parsed;
			}

			if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDateTime)) {
				return parsedDateTime.TimeOfDay;
			}
		}

		throw new ArgumentException(
			$"filter: datePart HourMinute value '{raw}' is not a valid time of day (use \"HH:mm\" or \"HH:mm:ss\").");
	}
}
