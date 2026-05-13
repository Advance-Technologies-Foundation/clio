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
		BusinessRule rule) =>
		ToMetadata(BuildAttributeDescriptorMap(columnMap), rule);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRule rule,
		string entitySchemaName) =>
		ToMetadata(BuildAttributeDescriptorMap(columnMap), rule, entitySchemaName);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: true, entitySchemaName: null);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string entitySchemaName) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: true, entitySchemaName: entitySchemaName);

	internal static BusinessRuleMetadataDto ToPageMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: false, entitySchemaName: null);

	private static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		bool includeAttributeReferenceSchemaName,
		string? entitySchemaName) {
		string ruleUId = Guid.NewGuid().ToString();
		BusinessRuleCaseMetadataDto @case = BuildCase(attributeMap, rule, includeAttributeReferenceSchemaName, entitySchemaName);
		List<BusinessRuleTriggerMetadataDto> triggers = BuildTriggers(attributeMap, rule, entitySchemaName);
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
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		bool includeAttributeReferenceSchemaName,
		string? entitySchemaName) {
		return new BusinessRuleCaseMetadataDto {
			TypeName = BusinessRuleCaseTypeName,
			UId = Guid.NewGuid().ToString(),
			Condition = BuildConditionGroup(attributeMap, rule.Condition, includeAttributeReferenceSchemaName),
			Actions = rule.Actions
				.Select(action => BuildAction(attributeMap, action, includeAttributeReferenceSchemaName, entitySchemaName))
				.ToList()
		};
	}

	private static BusinessRuleGroupConditionMetadataDto BuildConditionGroup(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleConditionGroup group,
		bool includeAttributeReferenceSchemaName) {
		return new BusinessRuleGroupConditionMetadataDto {
			TypeName = BusinessRuleGroupConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			LogicalOperation = string.Equals(group.LogicalOperation, "OR", StringComparison.OrdinalIgnoreCase) ? LogicalOr : LogicalAnd,
			Conditions = group.Conditions
				.Select(condition => BuildCondition(attributeMap, condition, includeAttributeReferenceSchemaName))
				.ToList()
		};
	}

	private static BusinessRuleConditionMetadataDto BuildCondition(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleCondition condition,
		bool includeAttributeReferenceSchemaName) {
		string leftPath = condition.LeftExpression.Path!;
		BusinessRuleAttributeDescriptor leftDescriptor = attributeMap[leftPath];
		string leftDataValueTypeName = leftDescriptor.DataValueTypeName;
		return new BusinessRuleConditionMetadataDto {
			TypeName = BusinessRuleConditionTypeName,
			UId = Guid.NewGuid().ToString(),
			ComparisonType = MapComparisonType(condition.ComparisonType),
			LeftExpression = BuildAttributeExpression(
				leftDescriptor,
				leftPath,
				leftDataValueTypeName,
				includeAttributeReferenceSchemaName),
			RightExpression = RequiresRightExpression(condition.ComparisonType)
				? BuildRightExpression(
					attributeMap,
					condition.RightExpression!,
					leftDescriptor,
					leftDataValueTypeName,
					includeAttributeReferenceSchemaName)
				: null
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildRightExpression(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleExpression right,
		BusinessRuleAttributeDescriptor leftDescriptor,
		string leftDataValueTypeName,
		bool includeAttributeReferenceSchemaName) {
		if (string.Equals(right.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase)) {
			string rightPath = right.Path!;
			BusinessRuleAttributeDescriptor rightDescriptor = attributeMap[rightPath];
			return BuildAttributeExpression(
				rightDescriptor,
				rightPath,
				includeAttributeReferenceSchemaName: includeAttributeReferenceSchemaName);
		}

		object? value = ConvertJsonElement(right.Value!.Value, leftDataValueTypeName);

		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleValueExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "Const",
			DataValueTypeName = leftDataValueTypeName,
			ReferenceSchemaName = leftDescriptor.ReferenceSchemaName,
			Value = value
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildAttributeExpression(
		BusinessRuleAttributeDescriptor descriptor,
		string path,
		string? dataValueTypeName = null,
		bool includeAttributeReferenceSchemaName = true) {
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleAttributeExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "AttributeValue",
			DataValueTypeName = dataValueTypeName ?? descriptor.DataValueTypeName,
			ReferenceSchemaName = includeAttributeReferenceSchemaName ? descriptor.ReferenceSchemaName : null,
			Path = path,
		};
	}

	private static FieldSelectionBusinessRuleActionMetadataDto BuildAction(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleAction action,
		bool includeAttributeReferenceSchemaName,
		string? entitySchemaName) {
		if (string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			return BuildSetValuesAction(attributeMap, action, includeAttributeReferenceSchemaName, entitySchemaName);
		}

		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = GetActionTypeName(action.ActionType),
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = string.Join(",", action.FieldSelectionItems)
		};
	}

	private static string GetActionTypeName(string actionType) {
		if (SupportedActionTypeNames.TryGetValue(actionType, out string? entityActionTypeName)) {
			return entityActionTypeName;
		}
		if (SupportedPageActionTypeNames.TryGetValue(actionType, out string? pageActionTypeName)) {
			return pageActionTypeName;
		}
		throw new InvalidOperationException($"Unsupported business-rule action type '{actionType}'.");
	}

	private static FieldSelectionBusinessRuleActionMetadataDto BuildSetValuesAction(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleAction action,
		bool includeAttributeReferenceSchemaName,
		string? entitySchemaName) {
		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = BusinessRuleSetValuesElementTypeName,
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = action.SetValueItems
				.Select(item => BuildSetValueItem(attributeMap, item, includeAttributeReferenceSchemaName, entitySchemaName))
				.ToList()
		};
	}

	private static BusinessRuleSetValueItemMetadataDto BuildSetValueItem(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleSetValueItem item,
		bool includeAttributeReferenceSchemaName,
		string? entitySchemaName) {
		string targetPath = item.Expression.Path!;
		BusinessRuleAttributeDescriptor targetDescriptor = attributeMap[targetPath];
		string dataValueTypeName = targetDescriptor.DataValueTypeName;
		BusinessRuleExpressionMetadataDto valueExpression = BuildSetValueItemValueExpression(
			attributeMap,
			item,
			entitySchemaName,
			includeAttributeReferenceSchemaName,
			targetPath,
			targetDescriptor,
			dataValueTypeName);
		return new BusinessRuleSetValueItemMetadataDto {
			TypeName = BusinessRuleSetValueItemTypeName,
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Expression = BuildAttributeExpression(
				targetDescriptor,
				targetPath,
				dataValueTypeName,
				includeAttributeReferenceSchemaName),
			Value = valueExpression
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildSetValueItemValueExpression(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleSetValueItem item,
		string? entitySchemaName,
		bool includeAttributeReferenceSchemaName,
		string targetPath,
		BusinessRuleAttributeDescriptor targetDescriptor,
		string dataValueTypeName) {
		if (BusinessRuleFormulaBuilder.IsFormulaExpression(item.Value)) {
			if (string.IsNullOrWhiteSpace(entitySchemaName)) {
				throw new InvalidOperationException(
					"Formula set-values items are only supported for entity business rules.");
			}
			return BusinessRuleFormulaBuilder.BuildValueExpression(
				entitySchemaName,
				attributeMap,
				targetPath,
				BusinessRuleFormulaBuilder.GetRequiredFormulaText(item.Value),
				dataValueTypeName);
		}

		if (IsAttributeValueExpression(item.Value)) {
			string sourcePath = item.Value.Path!;
			BusinessRuleAttributeDescriptor sourceDescriptor = attributeMap[sourcePath];
			return BuildAttributeExpression(
				sourceDescriptor,
				sourcePath,
				sourceDescriptor.DataValueTypeName,
				includeAttributeReferenceSchemaName);
		}

		object? value = ConvertJsonElement(item.Value.Value!.Value, dataValueTypeName);
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleValueExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = "Const",
			DataValueTypeName = dataValueTypeName,
			ReferenceSchemaName = targetDescriptor.ReferenceSchemaName,
			Value = value
		};
	}

	private static List<BusinessRuleTriggerMetadataDto> BuildTriggers(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string? entitySchemaName) {
		IEnumerable<string> conditionTriggers = rule.Condition.Conditions.SelectMany(EnumerateTriggerNames);
		IEnumerable<string> formulaTriggers = string.IsNullOrWhiteSpace(entitySchemaName)
			? Enumerable.Empty<string>()
			: rule.Actions.SelectMany(action => EnumerateFormulaTriggerNames(action, attributeMap));
		IEnumerable<string> attributeValueSourceTriggers =
			rule.Actions.SelectMany(EnumerateSetValuesAttributeSourceTriggerNames);
		List<BusinessRuleTriggerMetadataDto> triggers = conditionTriggers
			.Concat(formulaTriggers)
			.Concat(attributeValueSourceTriggers)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(triggerName => new BusinessRuleTriggerMetadataDto {
				TypeName = BusinessRuleTriggerTypeName,
				UId = Guid.NewGuid().ToString(),
				Name = triggerName,
				Type = ChangeAttributeValueTriggerType
			})
			.ToList();
		triggers.Add(new BusinessRuleTriggerMetadataDto {
			TypeName = BusinessRuleTriggerTypeName,
			UId = Guid.NewGuid().ToString(),
			Name = string.Empty,
			Type = DataLoadedTriggerType
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
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (!string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			yield break;
		}
		foreach (var item in action.SetValueItems) {
			if (!BusinessRuleFormulaBuilder.IsFormulaExpression(item.Value)) {
				continue;
			}
			foreach (var path in BusinessRuleFormulaBuilder.GetFormulaSourcePaths(
				BusinessRuleFormulaBuilder.GetRequiredFormulaText(item.Value),
				attributeMap)) {
				yield return path;
			}
		}
	}

	private static IEnumerable<string> EnumerateSetValuesAttributeSourceTriggerNames(BusinessRuleAction action) {
		if (!string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			yield break;
		}

		foreach (BusinessRuleSetValueItem item in action.SetValueItems) {
			if (IsAttributeValueExpression(item.Value)
				&& !string.IsNullOrWhiteSpace(item.Value.Path)) {
				yield return GetRootAttributePath(item.Value.Path);
			}
		}
	}

	private static bool IsAttributeValueExpression(BusinessRuleExpression? expression) =>
		string.Equals(expression?.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase);

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];
}


