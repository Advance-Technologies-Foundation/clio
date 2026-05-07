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
		BusinessRule rule,
		string entitySchemaName) {
		string ruleUId = Guid.NewGuid().ToString();
		BusinessRuleCaseMetadataDto @case = BuildCase(columnMap, rule, entitySchemaName);
		List<BusinessRuleTriggerMetadataDto> triggers = BuildTriggers(columnMap, rule);
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
		BusinessRule rule,
		string entitySchemaName) {
		return new BusinessRuleCaseMetadataDto {
			TypeName = BusinessRuleCaseTypeName,
			UId = Guid.NewGuid().ToString(),
			Condition = BuildConditionGroup(columnMap, rule.Condition),
			Actions = rule.Actions.Select(action => BuildAction(columnMap, action, entitySchemaName)).ToList()
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

	private static FieldSelectionBusinessRuleActionMetadataDto BuildAction(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRuleAction action,
		string entitySchemaName) {
		if (string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			return BuildSetValuesAction(columnMap, action, entitySchemaName);
		}

		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = SupportedActionTypeNames[action.ActionType],
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = string.Join(",", action.FieldSelectionItems)
		};
	}

	private static FieldSelectionBusinessRuleActionMetadataDto BuildSetValuesAction(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRuleAction action,
		string entitySchemaName) {
		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = BusinessRuleSetValuesElementTypeName,
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = action.SetValueItems.Select(item => BuildSetValueItem(columnMap, item, entitySchemaName)).ToList()
		};
	}

	private static BusinessRuleSetValueItemMetadataDto BuildSetValueItem(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRuleSetValueItem item,
		string entitySchemaName) {
		string targetPath = item.Expression.Path!;
		EntitySchemaColumnDto targetDescriptor = columnMap[targetPath];
		string dataValueTypeName = MapDataValueTypeName(targetDescriptor.DataValueType);
		BusinessRuleExpressionMetadataDto valueExpression = BuildSetValueItemValueExpression(
			columnMap,
			item,
			entitySchemaName,
			targetPath,
			targetDescriptor,
			dataValueTypeName);
		return new BusinessRuleSetValueItemMetadataDto {
			TypeName = BusinessRuleSetValueItemTypeName,
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Expression = BuildAttributeExpression(targetDescriptor, targetPath, dataValueTypeName),
			Value = valueExpression
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildSetValueItemValueExpression(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRuleSetValueItem item,
		string entitySchemaName,
		string targetPath,
		EntitySchemaColumnDto targetDescriptor,
		string dataValueTypeName) {
		if (BusinessRuleFormulaBuilder.IsFormulaExpression(item.Value)) {
			return BusinessRuleFormulaBuilder.BuildValueExpression(
				entitySchemaName,
				columnMap,
				targetPath,
				BusinessRuleFormulaBuilder.GetRequiredFormulaText(item.Value),
				dataValueTypeName);
		}
		object? value = ConvertJsonElement(item.Value.Value!.Value, dataValueTypeName);
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleValueExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "Const",
			DataValueTypeName = dataValueTypeName,
			ReferenceSchemaName = targetDescriptor.ReferenceSchema?.Name,
			Value = value
		};
	}

	private static List<BusinessRuleTriggerMetadataDto> BuildTriggers(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRule rule) {
		List<BusinessRuleTriggerMetadataDto> triggers = rule.Condition.Conditions
			.SelectMany(EnumerateTriggerNames)
			.Concat(rule.Actions.SelectMany(action => EnumerateFormulaTriggerNames(action, columnMap)))
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

	private static IEnumerable<string> EnumerateFormulaTriggerNames(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap) {
		if (!string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			yield break;
		}
		foreach (var item in action.SetValueItems) {
			if (!BusinessRuleFormulaBuilder.IsFormulaExpression(item.Value)) {
				continue;
			}
			foreach (var path in BusinessRuleFormulaBuilder.GetFormulaSourcePaths(
				BusinessRuleFormulaBuilder.GetRequiredFormulaText(item.Value),
				columnMap)) {
				yield return path;
			}
		}
	}

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];
}
