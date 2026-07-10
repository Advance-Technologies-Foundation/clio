using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Validates a business-rule definition.
/// </summary>
internal interface IBusinessRuleValidator {
	/// <summary>
	/// Validates an entity business-rule definition against entity column metadata.
	/// </summary>
	/// <param name="rule">Business rule to validate.</param>
	/// <param name="columnMap">Entity columns keyed by column name.</param>
	void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap);

	/// <summary>
	/// Validates an entity business-rule definition against business-rule attributes.
	/// </summary>
	/// <param name="rule">Business rule to validate.</param>
	/// <param name="attributeMap">Business-rule attributes keyed by payload path.</param>
	void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap);

	/// <summary>
	/// Validates a business-rule definition with a custom action validator.
	/// </summary>
	/// <param name="rule">Business rule to validate.</param>
	/// <param name="attributeMap">Business-rule attributes keyed by payload path.</param>
	/// <param name="validateAction">Action validator for the rule scope.</param>
	void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> validateAction);

	/// <summary>
	/// Validates an entity business-rule definition with schema-aware checks for apply-static-filter.
	/// </summary>
	/// <param name="rule">Business rule to validate.</param>
	/// <param name="attributeMap">Business-rule attributes keyed by payload path.</param>
	/// <param name="filterSchemaProvider">Schema provider used for apply-static-filter validation. Optional for non-static-filter rules.</param>
	void ValidateEntity(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		Filters.Schema.IFilterSchemaProvider? filterSchemaProvider);
}

internal sealed class BusinessRuleValidator(IBusinessRuleLookupReferenceValidator lookupReferenceValidator)
	: IBusinessRuleValidator {

	public void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap) =>
		Validate(rule, BuildAttributeDescriptorMap(columnMap));

	public void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) =>
		Validate(rule, attributeMap, ValidateEntityAction);

	public void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> validateAction) {
		ArgumentNullException.ThrowIfNull(rule);
		bool isApplyFilterRule = IsApplyFilterOnlyRule(rule);
		bool isApplyStaticFilterRule = IsApplyStaticFilterOnlyRule(rule);
		bool isUnconditionalRule = isApplyFilterRule || isApplyStaticFilterRule;

		if (string.IsNullOrWhiteSpace(rule.Caption)) {
			throw new ArgumentException("rule.caption is required.");
		}

		if (rule.Condition is null) {
			throw new ArgumentException("rule.condition is required.");
		}

		if (rule.Actions is null || rule.Actions.Count == 0) {
			throw new ArgumentException("rule.actions must contain at least one action.");
		}

		ValidateNoMixedApplyFilter(rule, isApplyFilterRule);
		ValidateNoMixedApplyStaticFilter(rule, isApplyStaticFilterRule);
		ValidateLogicalOperation(rule.Condition);

		if (rule.Condition.Conditions is null) {
			throw new ArgumentException("rule.condition.conditions is required.");
		}

		if (!isUnconditionalRule && rule.Condition.Conditions.Count == 0) {
			throw new ArgumentException("rule.condition.conditions must contain at least one condition.");
		}

		ValidateAllConditions(rule.Condition.Conditions, attributeMap);
		ValidateAllActions(rule.Actions, attributeMap, validateAction);
		lookupReferenceValidator.Validate(rule, attributeMap);
	}

	/// <summary>
	/// Validates an entity rule including schema-aware checks for apply-static-filter.
	/// </summary>
	public void ValidateEntity(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IFilterSchemaProvider? filterSchemaProvider) {
		Validate(rule, attributeMap, (action, map) => ValidateEntityAction(action, map, filterSchemaProvider));
	}

	private static void ValidateNoMixedApplyFilter(BusinessRule rule, bool isApplyFilterRule) {
		if (rule.Actions.Exists(action =>
			    string.Equals(action?.ActionType, ApplyFilterActionTypeName, StringComparison.OrdinalIgnoreCase))
			&& !isApplyFilterRule) {
			throw new ArgumentException(
				"apply-filter rules support exactly one action and cannot be combined with other entity business-rule actions.");
		}
	}

	private static void ValidateNoMixedApplyStaticFilter(BusinessRule rule, bool isApplyStaticFilterRule) {
		if (rule.Actions.Exists(action =>
			    string.Equals(action?.ActionType, ApplyStaticFilterActionTypeName, StringComparison.OrdinalIgnoreCase))
			&& !isApplyStaticFilterRule) {
			throw new ArgumentException(
				"apply-static-filter rules support exactly one action and cannot be combined with other entity business-rule actions.");
		}
	}

	private static void ValidateLogicalOperation(BusinessRuleConditionGroup condition) {
		if (string.IsNullOrWhiteSpace(condition.LogicalOperation)) {
			throw new ArgumentException("rule.condition.logicalOperation is required.");
		}

		if (!string.Equals(condition.LogicalOperation, "AND", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(condition.LogicalOperation, "OR", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Unsupported rule.condition.logicalOperation '{condition.LogicalOperation}'. Use AND or OR.");
		}
	}

	private static void ValidateAllConditions(
		List<BusinessRuleCondition> conditions,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		foreach (BusinessRuleCondition condition in conditions) {
			if (condition is null) {
				throw new ArgumentException("rule.condition.conditions[*] is required.");
			}

			ValidateCondition(condition, attributeMap);
		}
	}

	private static void ValidateAllActions(
		List<BusinessRuleAction> actions,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> validateAction) {
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
		if (condition.LeftExpression is null) {
			throw new ArgumentException("rule.condition.conditions[*].leftExpression is required.");
		}

		string comparisonType = GetSupportedComparisonType(condition.ComparisonType);
		bool requiresRight = !IsUnaryComparisonType(comparisonType);

		// Either side may be an AttributeValue, Const, or SysValue; type/reference-schema
		// compatibility is the only constraint, so resolve and structurally validate each operand.
		ConditionOperand left = ResolveOperand(
			condition.LeftExpression, attributeMap, "rule.condition.conditions[*].leftExpression");

		if (!requiresRight) {
			if (condition.RightExpression is not null) {
				throw new ArgumentException(
					$"rule.condition.conditions[*].rightExpression must be omitted when comparisonType is '{comparisonType}'.");
			}

			return;
		}

		if (condition.RightExpression is null) {
			throw new ArgumentException(
				$"rule.condition.conditions[*].rightExpression is required when comparisonType is '{comparisonType}'.");
		}

		ConditionOperand right = ResolveOperand(
			condition.RightExpression, attributeMap, "rule.condition.conditions[*].rightExpression");

		ValidateComparison(comparisonType, left, right);
	}

	private static void ValidateEntityAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) =>
		ValidateEntityAction(action, attributeMap, filterSchemaProvider: null);

	private static void ValidateEntityAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IFilterSchemaProvider? filterSchemaProvider) {
		if (string.IsNullOrEmpty(action.ActionType)) {
			throw new ArgumentException("rule.actions[*].type is required.");
		}

		if (string.Equals(action.ActionType, ApplyFilterActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			ValidateApplyFilterAction(action, attributeMap);
			return;
		}

		if (string.Equals(action.ActionType, ApplyStaticFilterActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			ValidateApplyStaticFilterAction(action, attributeMap, filterSchemaProvider);
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
		if (string.Equals(targetDataValueTypeName, "Lookup", StringComparison.Ordinal)) {
			return value => ValidateGuidString(value,
				"rule.actions[*].items[*].value.value must be a GUID string when the target attribute is a Lookup.");
		}

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

	private static ConditionOperand ResolveOperand(
		BusinessRuleExpression expression,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string fieldName) {
		if (expression is null) {
			throw new ArgumentException($"{fieldName} is required.");
		}

		if (string.Equals(expression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			if (string.IsNullOrWhiteSpace(expression.Path)) {
				throw new ArgumentException($"{fieldName}.path is required when {fieldName}.type is 'AttributeValue'.");
			}

			if (!IsScopedAttributePath(attributeMap, expression.Path)) {
				ValidateDirectAttributePath(expression.Path, $"{fieldName}.path");
			}

			BusinessRuleAttributeDescriptor descriptor = ResolveAttribute(attributeMap, expression.Path, $"{fieldName}.path");
			return new ConditionOperand(
				fieldName,
				OperandKind.Attribute,
				$"attribute '{expression.Path}'",
				new OperandType(descriptor.DataValueTypeName, descriptor.ReferenceSchemaName),
				expression);
		}

		if (string.Equals(expression.Type, SysValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			if (string.IsNullOrWhiteSpace(expression.SysValueName)) {
				throw new ArgumentException($"{fieldName}.sysValueName is required when {fieldName}.type is 'SysValue'.");
			}

			if (!SupportedSystemVariables.TryGetValue(expression.SysValueName, out SystemVariableDescriptor? sysValue)) {
				throw new ArgumentException(
					$"Unsupported {fieldName}.sysValueName '{expression.SysValueName}'. Supported values: {SupportedSystemVariablesDescription}.");
			}

			return new ConditionOperand(
				fieldName,
				OperandKind.SysValue,
				$"system variable '{sysValue.SysValueName}'",
				new OperandType(sysValue.DataValueTypeName, sysValue.ReferenceSchemaName),
				expression);
		}

		if (string.Equals(expression.Type, ConstExpressionType, StringComparison.OrdinalIgnoreCase)) {
			if (expression.Value is null) {
				throw new ArgumentException($"{fieldName}.value is required when {fieldName}.type is 'Const'.");
			}

			return new ConditionOperand(fieldName, OperandKind.Const, "constant value", null, expression);
		}

		throw new ArgumentException($"{fieldName}.type must be 'AttributeValue', 'Const', or 'SysValue'.");
	}

	private static void ValidateComparison(string comparisonType, ConditionOperand left, ConditionOperand right) {
		OperandType? leftType = left.Type;
		OperandType? rightType = right.Type;

		// The "comparison value type" drives the relational/equality family checks and the Const
		// value-kind validation. It comes from whichever operand is typed (an ObjectList collection
		// such as CurrentUserRoles compares against a single Lookup element of the same schema).
		OperandType comparisonValueType = (leftType ?? rightType)?.AsValueType() ?? OperandType.Text;

		if (leftType is not null && rightType is not null) {
			OperandType leftValue = leftType.AsValueType();
			OperandType rightValue = rightType.AsValueType();
			if (!string.Equals(leftValue.DataValueTypeName, rightValue.DataValueTypeName, StringComparison.OrdinalIgnoreCase)) {
				throw new ArgumentException(
					$"rule.condition.conditions[*] compares {left.Label} ({Describe(leftType)}) to {right.Label} ({Describe(rightType)}). Both operands must resolve to the same data value type.");
			}

			if (IsLookupValueType(leftValue)
				&& !string.Equals(leftValue.ReferenceSchemaName, rightValue.ReferenceSchemaName, StringComparison.OrdinalIgnoreCase)) {
				throw new ArgumentException(
					$"rule.condition.conditions[*] compares {left.Label} ({leftValue.ReferenceSchemaName}) to {right.Label} ({rightValue.ReferenceSchemaName}). Both lookup operands must reference the same schema.");
			}

			comparisonValueType = leftValue;
		}

		ValidateComparisonFamily(comparisonType, left, comparisonValueType);

		if (left.Kind == OperandKind.Const) {
			ValidateConstantValue(left.Expression.Value!.Value, rightType?.AsValueType() ?? OperandType.Text, left.FieldName);
		}

		if (right.Kind == OperandKind.Const) {
			ValidateConstantValue(right.Expression.Value!.Value, leftType?.AsValueType() ?? OperandType.Text, right.FieldName);
		}
	}

	private static void ValidateComparisonFamily(
		string comparisonType,
		ConditionOperand left,
		OperandType comparisonValueType) {
		if (ContainComparisonTypeNames.Contains(comparisonType)) {
			string? leftRawType = left.Type?.DataValueTypeName;
			bool leftIsCollectionOrText = leftRawType is not null
				&& (string.Equals(leftRawType, "ObjectList", StringComparison.OrdinalIgnoreCase)
					|| IsTextDataValueType(leftRawType));
			if (!leftIsCollectionOrText) {
				throw new ArgumentException(
					$"rule.condition.conditions[*].comparisonType '{comparisonType}' is only supported when the left operand is a collection (ObjectList, for example CurrentUserRoles) or a text type. Left operand is {left.Label}{(leftRawType is null ? string.Empty : $" ({leftRawType})")}.");
			}

			return;
		}

		if (IsRelationalComparisonType(comparisonType) && !IsRelationalDataValueType(comparisonValueType.DataValueTypeName)) {
			throw new ArgumentException(
				$"rule.condition.conditions[*].comparisonType '{comparisonType}' is only supported for numeric and date/time operands. The compared value type is {comparisonValueType.DataValueTypeName}.");
		}

		if (IsEqualityComparisonType(comparisonType) && IsUnsupportedEqualityDataValueType(comparisonValueType.DataValueTypeName)) {
			throw new ArgumentException(
				$"rule.condition.conditions[*].comparisonType '{comparisonType}' is not supported for {comparisonValueType.DataValueTypeName} operands. RichText and Image operands do not support equal or not-equal business-rule conditions.");
		}
	}

	private static void ValidateConstantValue(JsonElement value, OperandType valueType, string fieldName) {
		string dataValueTypeName = valueType.DataValueTypeName;
		switch (dataValueTypeName) {
			case "Lookup":
				ValidateGuidString(value, $"{fieldName}.value must be a GUID string when compared against a Lookup operand.");
				return;
			case "Guid":
				ValidateGuidString(value, $"{fieldName}.value must be a GUID string when compared against a Guid operand.");
				return;
			case "Boolean":
				if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) {
					throw new ArgumentException($"{fieldName}.value must be a JSON boolean when compared against a Boolean operand.");
				}

				return;
		}

		if (IsTextDataValueType(dataValueTypeName)) {
			if (value.ValueKind != JsonValueKind.String) {
				throw new ArgumentException($"{fieldName}.value must be a JSON string when compared against a text operand.");
			}

			return;
		}

		if (IsNumericDataValueType(dataValueTypeName)) {
			if (value.ValueKind != JsonValueKind.Number || !TryConvertSupportedNumericConstant(value, out _)) {
				throw new ArgumentException(
					$"{fieldName}.value must be a JSON number representable as Int64 or Decimal when compared against a numeric operand.");
			}

			return;
		}

		if (IsDateTimeDataValueType(dataValueTypeName)) {
			if (!TryConvertDateTimeConstant(value, dataValueTypeName, out _)) {
				throw new ArgumentException(GetDateTimeConstantValidationMessage(dataValueTypeName));
			}

			return;
		}

		throw new ArgumentException($"{fieldName}.value (Const) is not supported when compared against a '{dataValueTypeName}' operand.");
	}

	private static bool IsLookupValueType(OperandType type) =>
		string.Equals(type.DataValueTypeName, "Lookup", StringComparison.OrdinalIgnoreCase);

	private static string Describe(OperandType type) =>
		string.IsNullOrWhiteSpace(type.ReferenceSchemaName)
			? type.DataValueTypeName
			: $"{type.DataValueTypeName} -> {type.ReferenceSchemaName}";

	private enum OperandKind {
		Attribute,
		Const,
		SysValue
	}

	private sealed record ConditionOperand(
		string FieldName,
		OperandKind Kind,
		string Label,
		OperandType? Type,
		BusinessRuleExpression Expression);

	private sealed record OperandType(string DataValueTypeName, string? ReferenceSchemaName) {
		public static readonly OperandType Text = new("Text", null);

		public OperandType AsValueType() =>
			string.Equals(DataValueTypeName, "ObjectList", StringComparison.OrdinalIgnoreCase)
				? new OperandType("Lookup", ReferenceSchemaName)
				: this;
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

	private static bool IsScopedAttributePath(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		string path) =>
		attributeMap.TryGetValue(path, out BusinessRuleAttributeDescriptor? descriptor)
		&& descriptor.IsScoped;

	private static bool IsApplyFilterOnlyRule(BusinessRule rule) =>
		rule.Actions.Count == 1
		&& string.Equals(rule.Actions[0]?.ActionType, ApplyFilterActionTypeName, StringComparison.OrdinalIgnoreCase);

	private static bool IsApplyStaticFilterOnlyRule(BusinessRule rule) =>
		rule.Actions.Count == 1
		&& string.Equals(rule.Actions[0]?.ActionType, ApplyStaticFilterActionTypeName, StringComparison.OrdinalIgnoreCase);

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
		EnsureLookupDescriptor(
			leftDescriptor,
			leftDescriptor.Path,
			"rule.actions[*].targetFilterPath");

		BusinessRuleAttributeDescriptor rightDescriptor = string.IsNullOrWhiteSpace(applyFilterAction.SourceFilterPath)
			? sourceDescriptor
			: ResolveRelativeLookupPath(
				attributeMap,
				applyFilterAction.Source,
				applyFilterAction.SourceFilterPath,
				"rule.actions[*].sourceFilterPath");
		if (!string.IsNullOrWhiteSpace(applyFilterAction.SourceFilterPath)) {
			EnsureLookupDescriptor(
				rightDescriptor,
				rightDescriptor.Path,
				"rule.actions[*].sourceFilterPath");
		}

		ValidateCompatibleDescriptors(leftDescriptor, rightDescriptor);

		if (!string.IsNullOrWhiteSpace(applyFilterAction.SourceFilterPath) && applyFilterAction.PopulateValue) {
			throw new ArgumentException(
				"rule.actions[*].populateValue is not supported when rule.actions[*].sourceFilterPath is set for apply-filter.");
		}
	}

	private static void ValidateApplyStaticFilterAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IFilterSchemaProvider? filterSchemaProvider) {
		if (action is not ApplyStaticFilterBusinessRuleAction staticFilterAction) {
			throw new ArgumentException("rule.actions[*] apply-static-filter payload is invalid.");
		}

		if (string.IsNullOrWhiteSpace(staticFilterAction.TargetAttribute)) {
			throw new ArgumentException("rule.actions[*].targetAttribute is required when type is 'apply-static-filter'.");
		}

		ValidateDirectAttributePath(staticFilterAction.TargetAttribute, "rule.actions[*].targetAttribute");
		if (!attributeMap.TryGetValue(staticFilterAction.TargetAttribute, out BusinessRuleAttributeDescriptor? targetDescriptor)) {
			throw new ArgumentException(
				$"filter.target-attribute-unknown: targetAttribute '{staticFilterAction.TargetAttribute}' was not found on the entity schema.");
		}

		EnsureLookupDescriptor(
			targetDescriptor,
			staticFilterAction.TargetAttribute,
			"rule.actions[*].targetAttribute");

		if (staticFilterAction.Filter.ValueKind == JsonValueKind.Undefined
			|| staticFilterAction.Filter.ValueKind == JsonValueKind.Null) {
			throw new ArgumentException("rule.actions[*].filter is required when type is 'apply-static-filter'.");
		}

		StaticFilterGroup filterGroup = StaticFilterDeserializer.Deserialize(staticFilterAction.Filter);
		StaticFilterStructuralValidator.Validate(filterGroup);

		if (filterSchemaProvider is not null) {
			SchemaAwareFilterValidator schemaValidator = new(filterSchemaProvider);
			schemaValidator.Validate(filterGroup, targetDescriptor.ReferenceSchemaName!);
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
