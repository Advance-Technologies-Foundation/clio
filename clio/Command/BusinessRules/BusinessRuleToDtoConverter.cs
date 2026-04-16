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
		List<BusinessRuleTriggerMetadataDto> triggers = BuildTriggers(rule.Condition.Conditions);
		return new BusinessRuleMetadataDto {
			TypeName = BusinessRuleTypeName,
			UId = ruleUId,
			Name = GenerateBusinessRuleName(),
			Enabled = true,
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
			Condition = BuildConditionGroup(columnIndex, rule.Condition),
			Actions = rule.Actions.Select(BuildAction).ToList()
		};
	}

	private static BusinessRuleGroupConditionMetadataDto BuildConditionGroup(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		BusinessRuleConditionGroup group) {
		return new BusinessRuleGroupConditionMetadataDto {
			TypeName = BusinessRuleGroupConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			LogicalOperation = string.Equals(group.LogicalOperation, "OR", StringComparison.OrdinalIgnoreCase) ? LogicalOr : LogicalAnd,
			Conditions = group.Conditions.Select(condition => BuildCondition(columnIndex, condition)).ToList()
		};
	}

	private static BusinessRuleConditionMetadataDto BuildCondition(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		BusinessRuleCondition condition) {
		EntityColumnDescriptor leftDescriptor = ResolveAttributeDescriptor(
			columnIndex,
			condition.LeftExpression.Path!,
			"rule.condition.conditions[*].leftExpression.path");
		return new BusinessRuleConditionMetadataDto {
			TypeName = BusinessRuleConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			ComparisonType = string.Equals(condition.ComparisonType, "not-equal", StringComparison.OrdinalIgnoreCase)
				? ComparisonNotEqual
				: ComparisonEqual,
			LeftExpression = BuildAttributeExpression(leftDescriptor, condition.LeftExpression.Path!),
			RightExpression = BuildRightExpression(columnIndex, condition.RightExpression, leftDescriptor)
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildRightExpression(
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex,
		BusinessRuleExpression right,
		EntityColumnDescriptor leftDescriptor) {
		if (string.Equals(right.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			EntityColumnDescriptor rightDescriptor = ResolveAttributeDescriptor(
				columnIndex,
				right.Path!,
				"rule.condition.conditions[*].rightExpression.path");
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
		string normalizedAction = NormalizeActionName(action.Type);
		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = SupportedActionTypeNames[normalizedAction],
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = string.Join(",", action.Items.Select(target => target.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
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
		yield return condition.LeftExpression.Path!.Trim();
		if (string.Equals(condition.RightExpression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(condition.RightExpression.Path)) {
			yield return condition.RightExpression.Path.Trim();
		}
	}

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];
}
