using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Esq;
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
		ILocalEsqFilterBuilder? esqFilterBuilder) =>
		ToMetadata(BuildAttributeDescriptorMap(columnMap), rule, entitySchemaName: null, esqFilterBuilder: esqFilterBuilder);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRule rule,
		string? entitySchemaName,
		ILocalEsqFilterBuilder? esqFilterBuilder = null) =>
		ToMetadata(BuildAttributeDescriptorMap(columnMap), rule, entitySchemaName, esqFilterBuilder);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: true, entitySchemaName: null, esqFilterBuilder: null);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string? entitySchemaName,
		ILocalEsqFilterBuilder? esqFilterBuilder = null) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: true, entitySchemaName: entitySchemaName, esqFilterBuilder: esqFilterBuilder);

	internal static BusinessRuleMetadataDto ToPageMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: false, entitySchemaName: null, esqFilterBuilder: null);

	private static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		bool includeAttributeReferenceSchemaName,
		string? entitySchemaName,
		ILocalEsqFilterBuilder? esqFilterBuilder) {
		string ruleUId = Guid.NewGuid().ToString();
		BusinessRuleCaseMetadataDto @case = BuildCase(attributeMap, rule, includeAttributeReferenceSchemaName, entitySchemaName, esqFilterBuilder);
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
		string? entitySchemaName,
		ILocalEsqFilterBuilder? esqFilterBuilder) {
		return new BusinessRuleCaseMetadataDto {
			TypeName = BusinessRuleCaseTypeName,
			UId = Guid.NewGuid().ToString(),
			Condition = rule.Condition is null
				? null
				: BuildConditionGroup(attributeMap, rule.Condition, includeAttributeReferenceSchemaName),
			Actions = rule.Actions
				.Select(action => BuildAction(attributeMap, action, includeAttributeReferenceSchemaName, entitySchemaName, esqFilterBuilder))
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
		string? entitySchemaName,
		ILocalEsqFilterBuilder? esqFilterBuilder) {
		if (action is ApplyStaticFilterBusinessRuleAction setFilter) {
			if (!includeAttributeReferenceSchemaName) {
				throw new InvalidOperationException(
					"apply-static-filter is not supported in page-level business rules.");
			}
			return BuildApplyStaticFilterAction(attributeMap, setFilter, esqFilterBuilder);
		}

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

	private static SetFilterBusinessRuleActionMetadataDto BuildApplyStaticFilterAction(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		ApplyStaticFilterBusinessRuleAction action,
		ILocalEsqFilterBuilder? esqFilterBuilder) {
		if (esqFilterBuilder is null) {
			throw new InvalidOperationException(
				"BusinessRuleMetadataConverter.ToMetadata was invoked without an ILocalEsqFilterBuilder; apply-static-filter actions require the local ESQ filter builder to be wired through EntityBusinessRuleService.");
		}
		BusinessRuleAttributeDescriptor target = attributeMap[action.TargetAttribute];
		string rootSchemaName = target.ReferenceSchemaName
			?? throw new InvalidOperationException(
				$"Lookup target attribute '{action.TargetAttribute}' has no resolved reference schema; validator must run first.");
		StaticFilterGroup friendly = action.Filter.Deserialize<StaticFilterGroup>(JsonOptions)
			?? throw new InvalidOperationException(
				$"rule.actions[*].filter could not be deserialized as a friendly filter group for targetAttribute '{action.TargetAttribute}'.");
		string esqEnvelopeJson = esqFilterBuilder.ConvertToEsqFilter(rootSchemaName, friendly);
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
			Type = ConstExpressionType,
			DataValueTypeName = dataValueTypeName,
			ReferenceSchemaName = targetDescriptor.ReferenceSchemaName,
			Value = value
		};
	}

	private static List<BusinessRuleTriggerMetadataDto> BuildTriggers(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string? entitySchemaName) {
		IEnumerable<string> conditionTriggers = (rule.Condition?.Conditions ?? []).SelectMany(EnumerateTriggerNames);
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
			&& string.Equals(condition.RightExpression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)
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
		string.Equals(expression?.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase);

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];
}
