using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleToDtoConverter {

	internal static IReadOnlyDictionary<string, EntityColumnDescriptor> BuildColumnIndex(EntityDesignSchemaDto entitySchema) {
		Dictionary<string, EntityColumnDescriptor> exact = new(StringComparer.OrdinalIgnoreCase);

		foreach (EntitySchemaColumnDto column in entitySchema.Columns
			         .Concat(entitySchema.InheritedColumns)
			         .Where(column => !string.IsNullOrWhiteSpace(column.Name))) {
			EntityColumnDescriptor descriptor = new(
				column.Name,
				MapDataValueTypeName(column.DataValueType),
				column.ReferenceSchema?.Name,
				column.ReferenceSchema?.PrimaryDisplayColumn?.Name);
			exact[column.Name] = descriptor;
		}

		return exact;
	}

	internal static BusinessRuleMetadataDto BuildRule(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		BusinessRule rule) {
		string ruleUId = Guid.NewGuid().ToString();
		BusinessRuleCaseMetadataDto @case = BuildCase(columnIndex, rule);
		List<BusinessRuleTriggerMetadataDto> triggers = BuildTriggers(rule.ConditionGroup.Conditions);
		return new BusinessRuleMetadataDto {
			TypeName = BusinessRuleTypeName,
			UId = ruleUId,
			Name = GenerateBusinessRuleName(),
			Enabled = rule.Enabled ?? true,
			Caption = rule.Caption.Trim(),
			Cases = [@case],
			Triggers = triggers
		};
	}

	private static BusinessRuleCaseMetadataDto BuildCase(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		BusinessRule rule) {
		return new BusinessRuleCaseMetadataDto {
			TypeName = BusinessRuleCaseTypeName,
			UId = Guid.NewGuid().ToString(),
			Condition = BuildConditionGroup(columnIndex, rule.ConditionGroup),
			Actions = rule.Actions.Select(BuildAction).ToList()
		};
	}

	private static BusinessRuleGroupConditionMetadataDto BuildConditionGroup(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		BusinessRuleConditionGroup group) {
		return new BusinessRuleGroupConditionMetadataDto {
			TypeName = BusinessRuleGroupConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			LogicalOperation = string.Equals(group.Operator, "OR", StringComparison.OrdinalIgnoreCase) ? LogicalOr : LogicalAnd,
			Conditions = group.Conditions.Select(condition => BuildCondition(columnIndex, condition)).ToList()
		};
	}

	private static BusinessRuleConditionMetadataDto BuildCondition(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		BusinessRuleCondition condition) {
		EntityColumnDescriptor leftDescriptor = ResolveAttributeDescriptor(
			columnIndex,
			condition.Left.Path!,
			"rule.if.conditions[*].left.path");
		return new BusinessRuleConditionMetadataDto {
			TypeName = BusinessRuleConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			ComparisonType = string.Equals(condition.Comparison, "not-equal", StringComparison.OrdinalIgnoreCase)
				? ComparisonNotEqual
				: ComparisonEqual,
			LeftExpression = BuildAttributeExpression(leftDescriptor, condition.Left.Path!),
			RightExpression = BuildRightExpression(columnIndex, condition.Right, leftDescriptor)
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildRightExpression(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		BusinessRuleOperand right,
		EntityColumnDescriptor leftDescriptor) {
		if (string.Equals(right.Kind, "attribute", StringComparison.OrdinalIgnoreCase)) {
			EntityColumnDescriptor rightDescriptor = ResolveAttributeDescriptor(
				columnIndex,
				right.Path!,
				"rule.if.conditions[*].right.path");
			return BuildAttributeExpression(rightDescriptor, right.Path!);
		}

		object? value = ConvertJsonElement(right.Value!.Value);

		return new BusinessRuleValueExpressionMetadataDto {
			TypeName = BusinessRuleValueExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "Const",
			DataValueTypeName = leftDescriptor.DataValueTypeName,
			ReferenceSchemaName = leftDescriptor.ReferenceSchemaName,
			Value = value
		};
	}

	private static BusinessRuleAttributeExpressionMetadataDto BuildAttributeExpression(
		EntityColumnDescriptor descriptor,
		string path) {
		return new BusinessRuleAttributeExpressionMetadataDto {
			TypeName = BusinessRuleAttributeExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "AttributeValue",
			DataValueTypeName = descriptor.DataValueTypeName,
			ReferenceSchemaName = descriptor.ReferenceSchemaName,
			Path = path,
		};
	}

	private static FieldSelectionBusinessRuleActionMetadataDto BuildAction(BusinessRuleAction action) {
		string normalizedAction = NormalizeActionName(action.Action);
		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = SupportedActionTypeNames[normalizedAction],
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = string.Join(",", action.Targets.Select(target => target.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
		};
	}

	private static List<BusinessRuleTriggerMetadataDto> BuildTriggers(
		IReadOnlyList<BusinessRuleCondition> conditions) {
		return conditions
			.SelectMany(EnumerateTriggerNames)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(triggerName => new BusinessRuleTriggerMetadataDto {
				TypeName = BusinessRuleTriggerTypeName,
				UId = Guid.NewGuid().ToString(),
				Name = triggerName,
				Type = ChangeAttributeValueTriggerType,
			})
			.ToList();
	}

	private static IEnumerable<string> EnumerateTriggerNames(BusinessRuleCondition condition) {
		yield return condition.Left.Path!.Trim();
		if (string.Equals(condition.Right.Kind, "attribute", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(condition.Right.Path)) {
			yield return condition.Right.Path.Trim();
		}
	}

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];
}
