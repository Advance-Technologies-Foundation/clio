using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.BusinessRules.Filters;
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
		IEsqFilterConverterClient? esqConverterClient) =>
		ToMetadata(BuildAttributeDescriptorMap(columnMap), rule, esqConverterClient);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule) =>
		ToMetadata(attributeMap, rule, esqConverterClient: null);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		IEsqFilterConverterClient? esqConverterClient) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: true, esqConverterClient);

	internal static BusinessRuleMetadataDto ToPageMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: false, esqConverterClient: null);

	private static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		bool includeAttributeReferenceSchemaName,
		IEsqFilterConverterClient? esqConverterClient) {
		string ruleUId = Guid.NewGuid().ToString();
		BusinessRuleCaseMetadataDto @case = BuildCase(attributeMap, rule, includeAttributeReferenceSchemaName, esqConverterClient);
		List<BusinessRuleTriggerMetadataDto> triggers = BuildTriggers(rule.Condition?.Conditions ?? []);
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
		IEsqFilterConverterClient? esqConverterClient) {
		return new BusinessRuleCaseMetadataDto {
			TypeName = BusinessRuleCaseTypeName,
			UId = Guid.NewGuid().ToString(),
			Condition = rule.Condition is null
				? null
				: BuildConditionGroup(attributeMap, rule.Condition, includeAttributeReferenceSchemaName),
			Actions = rule.Actions
				.Select(action => BuildAction(attributeMap, action, includeAttributeReferenceSchemaName, esqConverterClient))
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
		if (string.Equals(right.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
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
			Type = ConstExpressionType,
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
			Type = AttributeValueExpressionType,
			DataValueTypeName = dataValueTypeName ?? descriptor.DataValueTypeName,
			ReferenceSchemaName = includeAttributeReferenceSchemaName ? descriptor.ReferenceSchemaName : null,
			Path = path,
		};
	}

	private static FieldSelectionBusinessRuleActionMetadataDto BuildAction(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleAction action,
		bool includeAttributeReferenceSchemaName,
		IEsqFilterConverterClient? esqConverterClient) {
		if (action is ApplyStaticFilterBusinessRuleAction setFilter) {
			if (!includeAttributeReferenceSchemaName) {
				throw new InvalidOperationException(
					"apply-static-filter is not supported in page-level business rules.");
			}
			return BuildApplyStaticFilterAction(attributeMap, setFilter, esqConverterClient);
		}

		if (string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			return BuildSetValuesAction(attributeMap, action, includeAttributeReferenceSchemaName);
		}

		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = GetActionTypeName(action.ActionType),
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = string.Join(",", action.FieldSelectionItems)
		};
	}

	private static SetFilterBusinessRuleActionMetadataDto BuildApplyStaticFilterAction(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		ApplyStaticFilterBusinessRuleAction action,
		IEsqFilterConverterClient? esqConverterClient) {
		if (esqConverterClient is null) {
			throw new InvalidOperationException(
				"BusinessRuleMetadataConverter.ToMetadata was invoked without an IEsqFilterConverterClient; apply-static-filter actions require the ESQ filter converter client to be wired through EntityBusinessRuleService.");
		}
		BusinessRuleAttributeDescriptor target = attributeMap[action.TargetAttribute];
		string rootSchemaName = target.ReferenceSchemaName
			?? throw new InvalidOperationException(
				$"Lookup target attribute '{action.TargetAttribute}' has no resolved reference schema; validator must run first.");
		StaticFilterGroup friendly = action.Filter.Deserialize<StaticFilterGroup>(JsonOptions)
			?? throw new InvalidOperationException(
				$"rule.actions[*].filter could not be deserialized as a friendly filter group for targetAttribute '{action.TargetAttribute}'.");
		string esqEnvelopeJson = esqConverterClient.ConvertToEsqFilter(rootSchemaName, friendly);
		// BusinessRuleValueExpression.value (compressed: BVE1) is stored as a JSON STRING — the
		// envelope is escaped once. STJ writes a string property as a JSON string literal, so
		// passing the raw envelope text directly is correct: storage gets BVE1: "<escaped>".
		return new SetFilterBusinessRuleActionMetadataDto {
			TypeName = BusinessRuleActionSetFilterTypeName,
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Expression = new BusinessRuleExpressionMetadataDto {
				TypeName = BusinessRuleAttributeExpressionTypeName,
				UId = Guid.NewGuid().ToString(),
				Type = AttributeValueExpressionType,
				Path = action.TargetAttribute
			},
			Value = new BusinessRuleExpressionMetadataDto {
				TypeName = BusinessRuleValueExpressionTypeName,
				UId = Guid.NewGuid().ToString(),
				Value = esqEnvelopeJson
			}
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
		bool includeAttributeReferenceSchemaName) {
		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = BusinessRuleSetValuesElementTypeName,
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Items = action.SetValueItems
				.Select(item => BuildSetValueItem(attributeMap, item, includeAttributeReferenceSchemaName))
				.ToList()
		};
	}

	private static BusinessRuleSetValueItemMetadataDto BuildSetValueItem(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleSetValueItem item,
		bool includeAttributeReferenceSchemaName) {
		string targetPath = item.Expression.Path!;
		BusinessRuleAttributeDescriptor targetDescriptor = attributeMap[targetPath];
		string dataValueTypeName = targetDescriptor.DataValueTypeName;
		object? value = ConvertJsonElement(item.Value.Value!.Value, dataValueTypeName);
		return new BusinessRuleSetValueItemMetadataDto {
			TypeName = BusinessRuleSetValueItemTypeName,
			UId = Guid.NewGuid().ToString(),
			Enabled = true,
			Expression = BuildAttributeExpression(
				targetDescriptor,
				targetPath,
				dataValueTypeName,
				includeAttributeReferenceSchemaName),
			Value = new BusinessRuleExpressionMetadataDto {
				TypeName = BusinessRuleValueExpressionTypeName,
				UId = Guid.NewGuid().ToString(),
				Type = ConstExpressionType,
				DataValueTypeName = dataValueTypeName,
				ReferenceSchemaName = targetDescriptor.ReferenceSchemaName,
				Value = value
			}
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
			&& string.Equals(condition.RightExpression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(condition.RightExpression.Path)) {
			yield return condition.RightExpression.Path;
		}
	}

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];
}
