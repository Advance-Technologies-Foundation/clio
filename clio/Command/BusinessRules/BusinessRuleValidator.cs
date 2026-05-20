using System;
using System.Collections.Generic;
using System.Text.Json;
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
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap) =>
		Validate(rule, BuildAttributeDescriptorMap(columnMap));

	internal static void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) =>
		Validate(rule, attributeMap, ValidateEntityAction);

	internal static void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> validateAction) {
		ArgumentNullException.ThrowIfNull(rule);
		bool isApplyFilterRule = IsApplyFilterOnlyRule(rule);

		if (string.IsNullOrWhiteSpace(rule.Caption)) {
			throw new ArgumentException("rule.caption is required.");
		}

		if (rule.Condition is null) {
			throw new ArgumentException("rule.condition is required.");
		}

		if (rule.Actions is null || rule.Actions.Count == 0) {
			throw new ArgumentException("rule.actions must contain at least one action.");
		}
		if (rule.Actions.Exists(action =>
			    string.Equals(action?.ActionType, ApplyFilterActionTypeName, StringComparison.OrdinalIgnoreCase))
			&& !isApplyFilterRule) {
			throw new ArgumentException(
				"apply-filter rules support exactly one action and cannot be combined with other entity business-rule actions.");
		}
		if (string.IsNullOrWhiteSpace(rule.Condition.LogicalOperation)) {
			throw new ArgumentException("rule.condition.logicalOperation is required.");
		}

		if (!string.Equals(rule.Condition.LogicalOperation, "AND", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(rule.Condition.LogicalOperation, "OR", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Unsupported rule.condition.logicalOperation '{rule.Condition.LogicalOperation}'. Use AND or OR.");
		}

		if (!isApplyFilterRule && (rule.Condition.Conditions is null || rule.Condition.Conditions.Count == 0)) {
			throw new ArgumentException("rule.condition.conditions must contain at least one condition.");
		}

		foreach (BusinessRuleCondition condition in rule.Condition.Conditions ?? []) {
			if (condition is null) {
				throw new ArgumentException("rule.condition.conditions[*] is required.");
			}

			ValidateCondition(condition, attributeMap);
		}

		foreach (BusinessRuleAction action in rule.Actions) {
			if (action is null) {
				throw new ArgumentException("rule.actions[*].type is required.");
			}

			validateAction(action, attributeMap);
		}
	}

	private static void ValidateCondition(
		BusinessRuleCondition condition,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (condition.LeftExpression is null || !string.Equals(condition.LeftExpression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
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
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (string.IsNullOrEmpty(action.ActionType)) {
			throw new ArgumentException("rule.actions[*].type is required.");
		}

		if (string.Equals(action.ActionType, ApplyFilterActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			ValidateApplyFilterAction(action, attributeMap);
			return;
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
			|| !string.Equals(item.Expression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
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
			ValidateSetValueFormula(item.Value, attributeMap, item.Expression.Path, targetDataValueTypeName);
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
		string targetPath,
		string targetDataValueTypeName) {
		string formula = BusinessRuleFormulaBuilder.GetRequiredFormulaText(value);
		BusinessRuleFormulaBuilder.ValidateFormulaScope(formula, attributeMap, targetPath, targetDataValueTypeName);
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
		if (string.Equals(rightExpression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			ValidateAttributeRightExpression(rightExpression, attributeMap, leftPath, leftDataValueTypeName);
			return;
		}

		if (!string.Equals(rightExpression.Type, "Const", StringComparison.OrdinalIgnoreCase)) {
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

	private static bool IsApplyFilterOnlyRule(BusinessRule rule) =>
		rule.Actions.Count == 1
		&& string.Equals(rule.Actions[0]?.ActionType, ApplyFilterActionTypeName, StringComparison.OrdinalIgnoreCase);

	private static void ValidateApplyFilterAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (action is not ApplyFilterBusinessRuleAction applyFilterAction) {
			throw new ArgumentException("rule.actions[*] apply-filter payload is invalid.");
		}

		if (string.IsNullOrWhiteSpace(applyFilterAction.Target)) {
			throw new ArgumentException("rule.actions[*].target is required when type is 'apply-filter'.");
		}

		ValidateDirectAttributePath(applyFilterAction.Target, "rule.actions[*].target");
		BusinessRuleAttributeDescriptor targetDescriptor = ResolveAttribute(
			attributeMap,
			applyFilterAction.Target,
			"rule.actions[*].target");
		EnsureLookupDescriptor(targetDescriptor, applyFilterAction.Target, "rule.actions[*].target");

		if (string.IsNullOrWhiteSpace(applyFilterAction.Source)) {
			throw new ArgumentException("rule.actions[*].source is required when type is 'apply-filter'.");
		}

		ValidateDirectAttributePath(applyFilterAction.Source, "rule.actions[*].source");
		BusinessRuleAttributeDescriptor sourceDescriptor = ResolveAttribute(
			attributeMap,
			applyFilterAction.Source,
			"rule.actions[*].source");
		EnsureLookupDescriptor(sourceDescriptor, applyFilterAction.Source, "rule.actions[*].source");

		if (string.IsNullOrWhiteSpace(applyFilterAction.TargetFilterPath)) {
			throw new ArgumentException("rule.actions[*].targetFilterPath is required when type is 'apply-filter'.");
		}

		BusinessRuleAttributeDescriptor leftDescriptor = ResolveRelativeLookupPath(
			attributeMap,
			applyFilterAction.Target,
			applyFilterAction.TargetFilterPath,
			"rule.actions[*].targetFilterPath");

		BusinessRuleAttributeDescriptor rightDescriptor = string.IsNullOrWhiteSpace(applyFilterAction.SourceFilterPath)
			? sourceDescriptor
			: ResolveRelativeLookupPath(
				attributeMap,
				applyFilterAction.Source,
				applyFilterAction.SourceFilterPath,
				"rule.actions[*].sourceFilterPath");

		ValidateCompatibleDescriptors(leftDescriptor, rightDescriptor);

		if (!string.IsNullOrWhiteSpace(applyFilterAction.SourceFilterPath) && applyFilterAction.PopulateValue) {
			throw new ArgumentException(
				"rule.actions[*].populateValue is not supported when rule.actions[*].sourceFilterPath is set for apply-filter.");
		}
	}

	private static BusinessRuleAttributeDescriptor ResolveRelativeLookupPath(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string rootPath,
		string relativePath,
		string fieldName) {
		string combinedPath = $"{rootPath}.{relativePath.Trim()}";
		return ResolveAttribute(attributeMap, combinedPath, fieldName);
	}

	private static void EnsureLookupDescriptor(
		BusinessRuleAttributeDescriptor descriptor,
		string path,
		string fieldName) {
		if (!string.Equals(descriptor.DataValueTypeName, "Lookup", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Attribute '{path}' in {fieldName} must be a Lookup.");
		}

		if (string.IsNullOrWhiteSpace(descriptor.ReferenceSchemaName)) {
			throw new ArgumentException($"Lookup attribute '{path}' in {fieldName} must declare a reference schema.");
		}
	}

	private static void ValidateCompatibleDescriptors(
		BusinessRuleAttributeDescriptor leftDescriptor,
		BusinessRuleAttributeDescriptor rightDescriptor) {
		if (!string.Equals(leftDescriptor.DataValueTypeName, rightDescriptor.DataValueTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"apply-filter compares target path '{leftDescriptor.Path}' ({leftDescriptor.DataValueTypeName}) to source path '{rightDescriptor.Path}' ({rightDescriptor.DataValueTypeName}). Both sides must have the same data value type.");
		}

		if (string.Equals(leftDescriptor.DataValueTypeName, "Lookup", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(leftDescriptor.ReferenceSchemaName, rightDescriptor.ReferenceSchemaName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"apply-filter compares lookup path '{leftDescriptor.Path}' ({leftDescriptor.ReferenceSchemaName}) to '{rightDescriptor.Path}' ({rightDescriptor.ReferenceSchemaName}). Both lookup sides must reference the same schema.");
		}
	}
}
