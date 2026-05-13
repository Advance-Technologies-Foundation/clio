using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Validates a business-rule definition.
/// </summary>
internal static class BusinessRuleValidator {

	internal static void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		IFilterSchemaProvider? filterSchemaProvider = null) =>
		Validate(rule, BuildAttributeDescriptorMap(columnMap), filterSchemaProvider);

	internal static void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IFilterSchemaProvider? filterSchemaProvider = null) =>
		Validate(rule, attributeMap,
			(action, map) => ValidateEntityAction(action, map, filterSchemaProvider));

	internal static void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> validateAction) {
		ArgumentNullException.ThrowIfNull(rule);

		if (string.IsNullOrWhiteSpace(rule.Caption)) {
			throw new ArgumentException("rule.caption is required.");
		}
		if (rule.Actions is null || rule.Actions.Count == 0) {
			throw new ArgumentException("rule.actions must contain at least one action.");
		}

		ValidateOptionalConditionGroup(rule.Condition, attributeMap);
		ValidateActions(rule.Actions, validateAction, attributeMap);
	}

	// `condition` is optional — when omitted the rule applies unconditionally
	// (used for apply-static-filter where the lookup must always be narrowed).
	private static void ValidateOptionalConditionGroup(
		BusinessRuleConditionGroup? condition,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (condition is null) {
			return;
		}
		if (string.IsNullOrWhiteSpace(condition.LogicalOperation)) {
			throw new ArgumentException("rule.condition.logicalOperation is required.");
		}
		if (!string.Equals(condition.LogicalOperation, "AND", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(condition.LogicalOperation, "OR", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Unsupported rule.condition.logicalOperation '{condition.LogicalOperation}'. Use AND or OR.");
		}
		if (condition.Conditions is null || condition.Conditions.Count == 0) {
			throw new ArgumentException("rule.condition.conditions must contain at least one condition.");
		}
		foreach (BusinessRuleCondition entry in condition.Conditions) {
			if (entry is null) {
				throw new ArgumentException("rule.condition.conditions[*] is required.");
			}
			ValidateCondition(entry, attributeMap);
		}
	}

	private static void ValidateActions(
		List<BusinessRuleAction> actions,
		Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> validateAction,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		foreach (BusinessRuleAction action in actions) {
			if (action is null) {
				throw new ArgumentException("rule.actions[*].type is required.");
			}
			validateAction(action, attributeMap);
		}
	}

	private static void ValidateCondition(
		BusinessRuleCondition condition,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (condition.LeftExpression is null || !string.Equals(condition.LeftExpression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.condition.conditions[*].leftExpression.type must be 'AttributeValue'.");
		}

		if (string.IsNullOrWhiteSpace(condition.LeftExpression.Path)) {
			throw new ArgumentException("rule.condition.conditions[*].leftExpression.path is required.");
		}

		string leftPath = condition.LeftExpression.Path;
		ValidateDirectAttributePath(
			leftPath,
			"rule.condition.conditions[*].leftExpression.path");
		BusinessRuleAttributeDescriptor leftDescriptor = ResolveAttribute(
			attributeMap,
			leftPath,
			"rule.condition.conditions[*].leftExpression.path");
		string comparisonType = GetSupportedComparisonType(condition.ComparisonType);
		string leftDataValueTypeName = leftDescriptor.DataValueTypeName;
		ValidateComparisonOperands(
			condition.RightExpression,
			comparisonType,
			leftPath,
			leftDataValueTypeName,
			attributeMap);
	}

	private static void ValidateEntityAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) =>
		ValidateEntityAction(action, attributeMap, filterSchemaProvider: null);

	private static void ValidateEntityAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IFilterSchemaProvider? filterSchemaProvider) {
		if (action is ApplyStaticFilterBusinessRuleAction setFilter) {
			ValidateSetFilterAction(setFilter, attributeMap, filterSchemaProvider);
			return;
		}

		if (string.IsNullOrEmpty(action.ActionType)) {
			throw new ArgumentException("rule.actions[*].type is required.");
		}

		if (string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			ValidateSetValuesAction(action, attributeMap);
			return;
		}

		if (!SupportedActionTypeNames.ContainsKey(action.ActionType)) {
			throw new ArgumentException(
				$"Unsupported rule.actions[*].type '{action.ActionType}'. Supported values: {SupportedActionTypesDescription}.");
		}

		List<string> fieldSelectionItems = action.FieldSelectionItems;
		if (fieldSelectionItems.Count == 0) {
			throw new ArgumentException("rule.actions[*].items must contain at least one attribute.");
		}

		foreach (string target in fieldSelectionItems) {
			if (string.IsNullOrWhiteSpace(target)) {
				throw new ArgumentException("rule.actions[*].items cannot contain empty attribute names.");
			}

			ValidateDirectAttributePath(target, "rule.actions[*].items");
			ResolveAttribute(attributeMap, target, "rule.actions[*].items");
		}
	}

	private static void ValidateSetFilterAction(
		ApplyStaticFilterBusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IFilterSchemaProvider? filterSchemaProvider) {

		if (action.ExtensionData is not null && action.ExtensionData.ContainsKey("items")) {
			throw new ArgumentException(
				$"{BusinessRuleFilterErrorCodes.ItemsNotAllowed}: rule.actions[*].items is not allowed when type is 'apply-static-filter'; use targetAttribute and filter only.");
		}

		if (string.IsNullOrWhiteSpace(action.TargetAttribute)) {
			throw new ArgumentException(
				$"{BusinessRuleFilterErrorCodes.TargetAttributeRequired}: rule.actions[*].targetAttribute is required for apply-static-filter.");
		}

		if (!attributeMap.TryGetValue(action.TargetAttribute, out BusinessRuleAttributeDescriptor? targetDescriptor)) {
			throw new ArgumentException(
				$"{BusinessRuleFilterErrorCodes.TargetAttributeUnknown}: targetAttribute '{action.TargetAttribute}' was not found on the entity schema.");
		}

		if (!string.Equals(targetDescriptor.DataValueTypeName, "Lookup", StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(targetDescriptor.ReferenceSchemaName)) {
			throw new ArgumentException(
				$"{BusinessRuleFilterErrorCodes.TargetAttributeNotLookup}: targetAttribute '{action.TargetAttribute}' must be a Lookup column with a reference schema.");
		}

		if (action.Filter.ValueKind == JsonValueKind.Undefined || action.Filter.ValueKind == JsonValueKind.Null) {
			throw new ArgumentException(
				$"{BusinessRuleFilterErrorCodes.FilterRequired}: rule.actions[*].filter is required for apply-static-filter.");
		}

		StaticFilterGroup friendly;
		try {
			friendly = action.Filter.Deserialize<StaticFilterGroup>(JsonOptions)
				?? throw new ArgumentException(
					$"{BusinessRuleFilterErrorCodes.FilterRequired}: rule.actions[*].filter could not be deserialized as a friendly filter group.");
		} catch (JsonException ex) {
			throw new ArgumentException(
				$"{BusinessRuleFilterErrorCodes.FilterRequired}: rule.actions[*].filter is not valid JSON: {ex.Message}",
				ex);
		}

		try {
			StaticFilterStructuralValidator.Validate(friendly);
		} catch (BusinessRuleFilterException ex) {
			throw new ArgumentException($"{ex.ErrorCode}: {ex.Message} (path={ex.FieldPath})", ex);
		}

		if (filterSchemaProvider is null) {
			return;
		}

		try {
			new SchemaAwareFilterValidator(filterSchemaProvider).Validate(
				friendly,
				targetDescriptor.ReferenceSchemaName!,
				StaticFilterStructuralValidator.DefaultFieldPathPrefix);
		} catch (BusinessRuleFilterException ex) {
			throw new ArgumentException($"{ex.ErrorCode}: {ex.Message} (path={ex.FieldPath})", ex);
		}
	}

	private static void ValidateSetValuesAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		List<BusinessRuleSetValueItem> setValueItems = action.SetValueItems;
		if (setValueItems.Count == 0) {
			throw new ArgumentException("rule.actions[*].items must contain at least one set-values item.");
		}

		foreach (BusinessRuleSetValueItem item in setValueItems) {
			if (item is null) {
				throw new ArgumentException("rule.actions[*].items[*] is required.");
			}

			ValidateSetValueItem(item, attributeMap);
		}
	}

	private static void ValidateSetValueItem(
		BusinessRuleSetValueItem item,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (item.Expression is null
			|| !string.Equals(item.Expression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.actions[*].items[*].expression.type must be 'AttributeValue'.");
		}

		if (string.IsNullOrWhiteSpace(item.Expression.Path)) {
			throw new ArgumentException("rule.actions[*].items[*].expression.path is required.");
		}

		ValidateDirectAttributePath(
			item.Expression.Path,
			"rule.actions[*].items[*].expression.path");
		BusinessRuleAttributeDescriptor targetDescriptor = ResolveAttribute(
			attributeMap,
			item.Expression.Path,
			"rule.actions[*].items[*].expression.path");
		string targetDataValueTypeName = targetDescriptor.DataValueTypeName;

		if (item.Value is null) {
			throw new ArgumentException("rule.actions[*].items[*].value is required.");
		}

		if (string.Equals(item.Value.Type, ConstExpressionType, StringComparison.OrdinalIgnoreCase)) {
			if (item.Value.Value is null) {
				throw new ArgumentException("rule.actions[*].items[*].value.value is required when value.type is 'Const'.");
			}

			ValidateSetValueConstant(item.Value.Value.Value, targetDataValueTypeName);
			return;
		}

		if (string.Equals(item.Value.Type, FormulaExpressionType, StringComparison.OrdinalIgnoreCase)) {
			ValidateSetValueFormula(item.Value, attributeMap, item.Expression.Path);
			return;
		}

		if (string.Equals(item.Value.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			ValidateSetValueAttributeSource(
				item.Value,
				attributeMap,
				item.Expression.Path,
				targetDataValueTypeName);
			return;
		}

		throw new ArgumentException("rule.actions[*].items[*].value.type must be 'Const', 'Formula', or 'AttributeValue'.");
	}

	private static void ValidateSetValueFormula(
		BusinessRuleExpression value,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string targetPath) {
		string formula = BusinessRuleFormulaBuilder.GetRequiredFormulaText(value);
		BusinessRuleFormulaBuilder.ValidateFormulaScope(formula, attributeMap, targetPath);
	}

	private static void ValidateSetValueAttributeSource(
		BusinessRuleExpression value,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string targetPath,
		string targetDataValueTypeName) {
		if (string.IsNullOrWhiteSpace(value.Path)) {
			throw new ArgumentException(
				"rule.actions[*].items[*].value.path is required when value.type is 'AttributeValue'.");
		}

		string sourcePath = value.Path;
		BusinessRuleAttributeDescriptor sourceDescriptor = ResolveAttribute(
			attributeMap,
			sourcePath,
			"rule.actions[*].items[*].value.path");
		string sourceDataValueTypeName = sourceDescriptor.DataValueTypeName;
		if (!string.Equals(targetDataValueTypeName, sourceDataValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"rule.actions[*].items[*] assigns source attribute '{sourcePath}' ({sourceDataValueTypeName}) to target attribute '{targetPath}' ({targetDataValueTypeName}). Both attributes must have the same data value type.");
		}
	}

	private static void ValidateSetValueConstant(JsonElement value, string targetDataValueTypeName) {
		Action<JsonElement> validator = ResolveSetValueConstantValidator(targetDataValueTypeName);
		validator(value);
	}

	private static Action<JsonElement> ResolveSetValueConstantValidator(string targetDataValueTypeName) {
		if (string.Equals(targetDataValueTypeName, "Boolean", StringComparison.Ordinal)) {
			return ValidateBooleanSetValueConstant;
		}

		if (IsDateTimeDataValueType(targetDataValueTypeName)) {
			return value => ValidateTemporalSetValueConstant(value, targetDataValueTypeName);
		}

		if (IsTextDataValueType(targetDataValueTypeName)) {
			return ValidateTextSetValueConstant;
		}

		if (IsNumericDataValueType(targetDataValueTypeName)) {
			return ValidateNumericSetValueConstant;
		}

		return _ => throw new ArgumentException(
			$"Const set-values is not supported for target attribute type '{targetDataValueTypeName}'.");
	}

	private static void ValidateBooleanSetValueConstant(JsonElement value) {
		if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) {
			throw new ArgumentException(
				"rule.actions[*].items[*].value.value must be a JSON boolean when the target attribute is Boolean.");
		}
	}

	private static void ValidateTemporalSetValueConstant(JsonElement value, string targetDataValueTypeName) {
		if (TryConvertDateTimeConstant(value, targetDataValueTypeName, out _)) {
			return;
		}

		throw new ArgumentException(GetSetValueTemporalValidationMessage(targetDataValueTypeName));
	}

	private static string GetSetValueTemporalValidationMessage(string targetDataValueTypeName) =>
		targetDataValueTypeName switch {
			"DateTime" =>
				"rule.actions[*].items[*].value.value must be a JSON string in ISO 8601 date-time format with a timezone suffix ('Z' or '+/-HH:mm') when the target attribute is DateTime.",
			"Date" =>
				"rule.actions[*].items[*].value.value must be a JSON string in 'yyyy-MM-dd' format when the target attribute is Date.",
			"Time" =>
				"rule.actions[*].items[*].value.value must be a JSON string in ISO 8601 time format with a timezone suffix ('Z' or '+/-HH:mm') when the target attribute is Time.",
			_ => throw new ArgumentException(
				$"Const set-values is not supported for target attribute type '{targetDataValueTypeName}'.")
		};

	private static void ValidateTextSetValueConstant(JsonElement value) {
		if (value.ValueKind != JsonValueKind.String) {
			throw new ArgumentException(
				"rule.actions[*].items[*].value.value must be a JSON string when the target attribute is a text type.");
		}
	}

	private static void ValidateNumericSetValueConstant(JsonElement value) {
		if (value.ValueKind != JsonValueKind.Number) {
			throw new ArgumentException(
				"rule.actions[*].items[*].value.value must be a JSON number when the target attribute is a numeric type.");
		}

		if (!TryConvertSupportedNumericConstant(value, out _)) {
			throw new ArgumentException(
				"rule.actions[*].items[*].value.value must be a JSON number representable as Int64 or Decimal when the target attribute is a numeric type.");
		}
	}

	private static string GetSupportedComparisonType(string comparisonType) {
		if (!IsSupportedComparisonType(comparisonType)) {
			throw new ArgumentException(
				$"Unsupported rule.condition.conditions[*].comparisonType '{comparisonType}'. Supported values: {SupportedComparisonTypesDescription}.");
		}

		return comparisonType.Trim();
	}

	private static void ValidateComparisonOperands(
		BusinessRuleExpression? rightExpression,
		string comparisonType,
		string leftPath,
		string leftDataValueTypeName,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (IsUnaryComparisonType(comparisonType)) {
			if (rightExpression is not null) {
				throw new ArgumentException(
					$"rule.condition.conditions[*].rightExpression must be omitted when comparisonType is '{comparisonType}'.");
			}

			return;
		}

		if (rightExpression is null) {
			throw new ArgumentException(
				$"rule.condition.conditions[*].rightExpression is required when comparisonType is '{comparisonType}'.");
		}

		if (IsRelationalComparisonType(comparisonType) && !IsRelationalDataValueType(leftDataValueTypeName)) {
			throw new ArgumentException(
				$"rule.condition.conditions[*].comparisonType '{comparisonType}' is only supported for numeric and date/time left attributes. Left attribute '{leftPath}' has type {leftDataValueTypeName}.");
		}

		if (IsEqualityComparisonType(comparisonType) && IsUnsupportedEqualityDataValueType(leftDataValueTypeName)) {
			throw new ArgumentException(
				$"rule.condition.conditions[*].comparisonType '{comparisonType}' is not supported for left attribute '{leftPath}' with type {leftDataValueTypeName}. RichText and Image attributes do not support equal or not-equal business-rule conditions.");
		}

		ValidateRightExpression(rightExpression, attributeMap, leftPath, leftDataValueTypeName);
	}

	private static void ValidateRightExpression(
		BusinessRuleExpression rightExpression,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string leftPath,
		string leftDataValueTypeName) {
		if (string.Equals(rightExpression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			ValidateAttributeRightExpression(rightExpression, attributeMap, leftPath, leftDataValueTypeName);
			return;
		}

		if (!string.Equals(rightExpression.Type, ConstExpressionType, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.type must be 'AttributeValue' or 'Const'.");
		}

		ValidateConstantRightExpression(rightExpression, leftDataValueTypeName);
	}

	private static void ValidateAttributeRightExpression(
		BusinessRuleExpression rightExpression,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string leftPath,
		string leftDataValueTypeName) {
		if (string.IsNullOrWhiteSpace(rightExpression.Path)) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.path is required when rightExpression.type is 'AttributeValue'.");
		}

		string rightPath = rightExpression.Path;
		ValidateDirectAttributePath(
			rightPath,
			"rule.condition.conditions[*].rightExpression.path");
		BusinessRuleAttributeDescriptor rightDescriptor = ResolveAttribute(
			attributeMap,
			rightPath,
			"rule.condition.conditions[*].rightExpression.path");
		string rightDataValueTypeName = rightDescriptor.DataValueTypeName;
		if (!string.Equals(leftDataValueTypeName, rightDataValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"rule.condition.conditions[*] compares left attribute '{leftPath}' ({leftDataValueTypeName}) to right attribute '{rightPath}' ({rightDataValueTypeName}). Both attributes must have the same data value type.");
		}
	}

	private static void ValidateConstantRightExpression(
		BusinessRuleExpression rightExpression,
		string leftDataValueTypeName) {
		if (rightExpression.Value is null) {
			throw new ArgumentException("rule.condition.conditions[*].rightExpression.value is required when rightExpression.type is 'Const'.");
		}

		JsonElement rightValue = rightExpression.Value.Value;
		switch (leftDataValueTypeName) {
			case "Lookup":
				ValidateGuidString(rightValue,
					"rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is a Lookup.");
				return;
			case "Guid":
				ValidateGuidString(rightValue,
					"rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is Guid.");
				return;
			case "Boolean":
				if (rightValue.ValueKind != JsonValueKind.True && rightValue.ValueKind != JsonValueKind.False) {
					throw new ArgumentException(
						"rule.condition.conditions[*].rightExpression.value must be a JSON boolean when the left attribute is Boolean.");
				}
				return;
		}

		if (IsTextDataValueType(leftDataValueTypeName) && rightValue.ValueKind != JsonValueKind.String) {
			throw new ArgumentException(
				"rule.condition.conditions[*].rightExpression.value must be a JSON string when the left attribute is a text type.");
		}

		if (IsNumericDataValueType(leftDataValueTypeName) && rightValue.ValueKind != JsonValueKind.Number) {
			throw new ArgumentException(
				"rule.condition.conditions[*].rightExpression.value must be a JSON number when the left attribute is a numeric type.");
		}

		if (IsNumericDataValueType(leftDataValueTypeName)
			&& !TryConvertSupportedNumericConstant(rightValue, out _)) {
			throw new ArgumentException(
				"rule.condition.conditions[*].rightExpression.value must be a JSON number representable as Int64 or Decimal when the left attribute is a numeric type.");
		}

		if (IsDateTimeDataValueType(leftDataValueTypeName)
			&& !TryConvertDateTimeConstant(rightValue, leftDataValueTypeName, out _)) {
			throw new ArgumentException(GetDateTimeConstantValidationMessage(leftDataValueTypeName));
		}

		if (!IsTextDataValueType(leftDataValueTypeName)
			&& !IsNumericDataValueType(leftDataValueTypeName)
			&& !IsDateTimeDataValueType(leftDataValueTypeName)) {
			throw new ArgumentException(
				$"Const rightExpression is not supported for left attribute type '{leftDataValueTypeName}'.");
		}
	}

	private static void ValidateGuidString(JsonElement value, string errorMessage) {
		if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out _)) {
			throw new ArgumentException(errorMessage);
		}
	}

	private static BusinessRuleAttributeDescriptor ResolveAttribute(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string path,
		string fieldName) {
		if (!attributeMap.TryGetValue(path, out BusinessRuleAttributeDescriptor? descriptor)) {
			throw new ArgumentException($"Unknown attribute '{path}' in {fieldName}.");
		}

		return descriptor;
	}

	private static void ValidateDirectAttributePath(string path, string fieldName) {
		if (IsForwardReferencePath(path)) {
			throw new ArgumentException(
				$"{fieldName} must reference a direct entity attribute. Forward reference paths are supported only in rule.actions[*].items[*].value.path.");
		}
	}
}
