using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Converts validated business-rule definitions into add-on metadata DTOs.
/// </summary>
internal static class BusinessRuleMetadataConverter {

	internal static BusinessRuleMetadataDto ToMetadata(
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
		string leftDataValueTypeName = MapDataValueTypeName(leftDescriptor.DataValueType);
		return new BusinessRuleConditionMetadataDto {
			TypeName = BusinessRuleConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			ComparisonType = MapComparisonType(condition.ComparisonType),
			LeftExpression = BuildAttributeExpression(leftDescriptor, leftPath, leftDataValueTypeName),
			RightExpression = RequiresRightExpression(condition.ComparisonType)
				? BuildRightExpression(columnMap, condition.RightExpression!, leftDescriptor, leftDataValueTypeName)
				: null
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildRightExpression(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRuleExpression right,
		EntitySchemaColumnDto leftDescriptor,
		string leftDataValueTypeName) {
		if (string.Equals(right.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			string rightPath = right.Path!;
			EntitySchemaColumnDto rightDescriptor = columnMap[rightPath];
			return BuildAttributeExpression(rightDescriptor, rightPath);
		}

		object? value = ConvertJsonElement(right.Value!.Value, leftDataValueTypeName);

		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleValueExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "Const",
			DataValueTypeName = leftDataValueTypeName,
			ReferenceSchemaName = leftDescriptor.ReferenceSchema?.Name,
			Value = value
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildAttributeExpression(
		EntitySchemaColumnDto descriptor,
		string path,
		string? dataValueTypeName = null) {
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleAttributeExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "AttributeValue",
			DataValueTypeName = dataValueTypeName ?? MapDataValueTypeName(descriptor.DataValueType),
			ReferenceSchemaName = descriptor.ReferenceSchema?.Name,
			Path = path,
		};
	}

	private static FieldSelectionBusinessRuleActionMetadataDto BuildAction(BusinessRuleAction action) {
		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = SupportedActionTypeNames[action.Type],
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = string.Join(",", action.Items)
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
		if (condition.RightExpression is not null
			&& string.Equals(condition.RightExpression.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(condition.RightExpression.Path)) {
			yield return condition.RightExpression.Path;
		}
	}

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];
}
