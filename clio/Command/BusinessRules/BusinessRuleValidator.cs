using System;
using System.Collections.Generic;
using System.Text.Json;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleValidator {

	internal static void ValidateRequest(BusinessRuleCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}

		if (request.Rule is null) {
			throw new ArgumentException("rule is required.");
		}

		if (string.IsNullOrWhiteSpace(request.Rule.Caption)) {
			throw new ArgumentException("rule.caption is required.");
		}

		if (request.Rule.ConditionGroup is null) {
			throw new ArgumentException("rule.if is required.");
		}

		if (request.Rule.Actions is null || request.Rule.Actions.Count == 0) {
			throw new ArgumentException("rule.then must contain at least one action.");
		}
	}

	internal static void ValidateRuleAgainstSchema(
		BusinessRule rule,
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex) {
		if (string.IsNullOrWhiteSpace(rule.ConditionGroup.Operator)) {
			throw new ArgumentException("rule.if.operator is required.");
		}

		if (!string.Equals(rule.ConditionGroup.Operator, "AND", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(rule.ConditionGroup.Operator, "OR", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException($"Unsupported rule.if.operator '{rule.ConditionGroup.Operator}'. Use AND or OR.");
		}

		if (rule.ConditionGroup.Conditions is null || rule.ConditionGroup.Conditions.Count == 0) {
			throw new ArgumentException("rule.if.conditions must contain at least one condition.");
		}

		foreach (BusinessRuleCondition condition in rule.ConditionGroup.Conditions) {
			ValidateCondition(condition, columnIndex);
		}

		foreach (BusinessRuleAction action in rule.Actions) {
			ValidateAction(action, columnIndex);
		}
	}

	private static void ValidateCondition(
		BusinessRuleCondition condition,
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex) {
		if (condition.Left is null || !string.Equals(condition.Left.Kind, "attribute", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.if.conditions[*].left.kind must be 'attribute'.");
		}

		if (string.IsNullOrWhiteSpace(condition.Left.Path)) {
			throw new ArgumentException("rule.if.conditions[*].left.path is required.");
		}

		EntityColumnDescriptor leftDescriptor = ResolveAttributeDescriptor(
			columnIndex,
			condition.Left.Path,
			"rule.if.conditions[*].left.path");

		if (!string.Equals(condition.Comparison, "equal", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(condition.Comparison, "not-equal", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"Unsupported rule.if.conditions[*].comparison '{condition.Comparison}'. Supported values: equal, not-equal.");
		}

		if (condition.Right is null) {
			throw new ArgumentException("rule.if.conditions[*].right is required.");
		}

		if (string.Equals(condition.Right.Kind, "attribute", StringComparison.OrdinalIgnoreCase)) {
			if (string.IsNullOrWhiteSpace(condition.Right.Path)) {
				throw new ArgumentException("rule.if.conditions[*].right.path is required when right.kind is 'attribute'.");
			}

			EntityColumnDescriptor rightDescriptor = ResolveAttributeDescriptor(
				columnIndex,
				condition.Right.Path,
				"rule.if.conditions[*].right.path");
			ValidateExplicitDataValueType(condition.Right.DataValueTypeName, rightDescriptor.DataValueTypeName,
				"rule.if.conditions[*].right.dataValueTypeName");
			return;
		}

		if (!string.Equals(condition.Right.Kind, "constant", StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException("rule.if.conditions[*].right.kind must be 'attribute' or 'constant'.");
		}

		if (condition.Right.Value is null) {
			throw new ArgumentException("rule.if.conditions[*].right.value is required when right.kind is 'constant'.");
		}

		ValidateExplicitDataValueType(condition.Right.DataValueTypeName, leftDescriptor.DataValueTypeName,
			"rule.if.conditions[*].right.dataValueTypeName");
		if (string.Equals(leftDescriptor.DataValueTypeName, "Lookup", StringComparison.OrdinalIgnoreCase)) {
			object? rawValue = ConvertJsonElement(condition.Right.Value.Value);
			if (rawValue is not string guidString || !Guid.TryParse(guidString, out _)) {
				throw new ArgumentException(
					"rule.if.conditions[*].right.value must be a GUID string when the left attribute is a Lookup.");
			}
		}
	}

	private static void ValidateAction(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex) {
		string normalizedAction = NormalizeActionName(action.Action);
		if (normalizedAction.Length == 0) {
			throw new ArgumentException("rule.then[*].action is required.");
		}

		if (!SupportedActionTypeNames.ContainsKey(normalizedAction)) {
			throw new ArgumentException(
				$"Unsupported rule.then[*].action '{action.Action}'. Supported values: make-editable, make-read-only, make-required, make-optional.");
		}

		if (action.Targets is null || action.Targets.Count == 0) {
			throw new ArgumentException("rule.then[*].targets must contain at least one attribute.");
		}

		foreach (string target in action.Targets) {
			if (string.IsNullOrWhiteSpace(target)) {
				throw new ArgumentException("rule.then[*].targets cannot contain empty attribute names.");
			}

			ResolveAttributeDescriptor(columnIndex, target, "rule.then[*].targets");
		}
	}

	private static void ValidateExplicitDataValueType(
		string? explicitTypeName,
		string inferredTypeName,
		string fieldName) {
		if (string.IsNullOrWhiteSpace(explicitTypeName)) {
			return;
		}

		if (!string.Equals(explicitTypeName.Trim(), inferredTypeName, StringComparison.OrdinalIgnoreCase)) {
			throw new ArgumentException(
				$"{fieldName} '{explicitTypeName}' does not match the referenced attribute type '{inferredTypeName}'.");
		}
	}
}
