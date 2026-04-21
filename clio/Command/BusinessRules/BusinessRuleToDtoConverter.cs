using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleToDtoConverter {

	internal static IReadOnlyDictionary<string, EntitySchemaColumnDto> BuildColumnMap(EntityDesignSchemaDto entitySchema) {
		Dictionary<string, EntitySchemaColumnDto> exact = new(StringComparer.OrdinalIgnoreCase);

		foreach (EntitySchemaColumnDto column in entitySchema.Columns
			         .Concat(entitySchema.InheritedColumns)
			         .Where(column => !string.IsNullOrWhiteSpace(column.Name))) {
			exact[column.Name] = column;
		}

		return exact;
	}

	internal static BusinessRuleMetadataDto BuildRule(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRule rule) {
		string ruleUId = Guid.NewGuid().ToString();
		BusinessRuleCaseMetadataDto @case = BuildCase(columnMap, rule);
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
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRule rule) {
		return new BusinessRuleCaseMetadataDto {
			TypeName = BusinessRuleCaseTypeName,
			UId = Guid.NewGuid().ToString(),
			Condition = BuildConditionGroup(columnMap, rule.Condition),
			Actions = rule.Actions.Select(BuildAction).ToList()
		};
	}

	private static BusinessRuleGroupConditionMetadataDto BuildConditionGroup(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRuleConditionGroup group) {
		return new BusinessRuleGroupConditionMetadataDto {
			TypeName = BusinessRuleGroupConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			LogicalOperation = string.Equals(group.LogicalOperation, "OR", StringComparison.OrdinalIgnoreCase) ? LogicalOr : LogicalAnd,
			Conditions = group.Conditions.Select(condition => BuildCondition(columnMap, condition)).ToList()
		};
	}

	private static BusinessRuleConditionMetadataDto BuildCondition(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRuleCondition condition) {
		string leftPath = condition.LeftExpression.Path!;
		EntitySchemaColumnDto leftDescriptor = columnMap[leftPath];
		return new BusinessRuleConditionMetadataDto {
			TypeName = BusinessRuleConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			ComparisonType = string.Equals(condition.ComparisonType, "not-equal", StringComparison.OrdinalIgnoreCase)
				? ComparisonNotEqual
				: ComparisonEqual,
			LeftExpression = BuildAttributeExpression(leftDescriptor, leftPath),
			RightExpression = BuildRightExpression(columnMap, condition.RightExpression, leftDescriptor)
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildRightExpression(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRuleExpression right,
		EntitySchemaColumnDto leftDescriptor) {
		if (string.Equals(right.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			string rightPath = right.Path!;
			EntitySchemaColumnDto rightDescriptor = columnMap[rightPath];
			return BuildAttributeExpression(rightDescriptor, rightPath);
		}

		object? value = ConvertJsonElement(right.Value!.Value);

		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleValueExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "Const",
			DataValueTypeName = MapDataValueTypeName(leftDescriptor.DataValueType),
			ReferenceSchemaName = leftDescriptor.ReferenceSchema?.Name,
			Value = value
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildAttributeExpression(
		EntitySchemaColumnDto descriptor,
		string path) {
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleAttributeExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "AttributeValue",
			DataValueTypeName = MapDataValueTypeName(descriptor.DataValueType),
			ReferenceSchemaName = descriptor.ReferenceSchema?.Name,
			Path = path,
		};
	}

	private static FieldSelectionBusinessRuleActionMetadataDto BuildAction(BusinessRuleAction action) {
		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = SupportedActionTypeNames[action.Type],
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = string.Join(",", action.Items.Select(target => target.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
		};
	}

	private static List<BusinessRuleTriggerMetadataDto> BuildTriggers(
		IReadOnlyList<BusinessRuleCondition> conditions) {
		List<BusinessRuleTriggerMetadataDto> triggers = conditions
			.SelectMany(EnumerateTriggerNames)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(triggerName => new BusinessRuleTriggerMetadataDto {
				TypeName = BusinessRuleTriggerTypeName,
				UId = Guid.NewGuid().ToString(),
				Name = triggerName,
				Type = ChangeAttributeValueTriggerType,
			})
			.ToList();
		triggers.Add(new BusinessRuleTriggerMetadataDto {
			TypeName = BusinessRuleTriggerTypeName,
			UId = Guid.NewGuid().ToString(),
			Name = string.Empty,
			Type = DataLoadedTriggerType,
		});
		return triggers;
	}

	private static IEnumerable<string> EnumerateTriggerNames(BusinessRuleCondition condition) {
		yield return condition.LeftExpression.Path!;
		if (string.Equals(condition.RightExpression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(condition.RightExpression.Path)) {
			yield return condition.RightExpression.Path;
		}
	}

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];
}
