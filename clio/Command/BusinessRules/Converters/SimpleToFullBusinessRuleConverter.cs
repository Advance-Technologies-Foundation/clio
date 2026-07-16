using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.BusinessRules.Filters;
using Clio.Command.BusinessRules.Filters.Esq;
using Clio.Command.BusinessRules.Filters.Schema;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using static Clio.Command.BusinessRules.BusinessRuleHelpers;

namespace Clio.Command.BusinessRules.Converters;

/// <summary>
/// Converts validated business-rule definitions into add-on metadata DTOs.
/// </summary>
internal static class SimpleToFullBusinessRuleConverter {

	internal static IReadOnlyList<BusinessRuleMetadataDto> ToEntityMetadata(
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap,
		BusinessRule rule,
		string entitySchemaName) =>
		ToEntityMetadata(BuildAttributeDescriptorMap(columnMap), rule, entitySchemaName);

	internal static IReadOnlyList<BusinessRuleMetadataDto> ToEntityMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string entitySchemaName) =>
		ToEntityMetadata(attributeMap, rule, entitySchemaName,
			filterSchemaProvider: null, lookupValueResolver: null);

	internal static IReadOnlyList<BusinessRuleMetadataDto> ToEntityMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string entitySchemaName,
		IFilterSchemaProvider? filterSchemaProvider,
		ILookupValueResolver? lookupValueResolver,
		JsonObject? existingRule = null) {
		ExistingRuleIdentity? identity = existingRule is null ? null : new ExistingRuleIdentity(existingRule);
		if (TryGetApplyFilterAction(rule, out ApplyFilterBusinessRuleAction? applyFilterAction)) {
			return BuildApplyFilterRules(attributeMap, rule, applyFilterAction!, identity);
		}

		if (TryGetApplyStaticFilterAction(rule, out ApplyStaticFilterBusinessRuleAction? applyStaticFilterAction)) {
			return [BuildApplyStaticFilterRule(
				attributeMap, rule, applyStaticFilterAction!, filterSchemaProvider, lookupValueResolver, identity)];
		}

		return [ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: true, entitySchemaName: entitySchemaName, identity)];
	}

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
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: true, entitySchemaName: null, identity: null);

	internal static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string entitySchemaName) =>
		ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: true, entitySchemaName: entitySchemaName, identity: null);

	internal static BusinessRuleMetadataDto ToPageMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		JsonObject? existingRule = null) {
		ExistingRuleIdentity? identity = existingRule is null ? null : new ExistingRuleIdentity(existingRule);
		return ToMetadata(attributeMap, rule, includeAttributeReferenceSchemaName: false, entitySchemaName: null, identity);
	}

	private static BusinessRuleMetadataDto ToMetadata(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		bool includeAttributeReferenceSchemaName,
		string? entitySchemaName,
		ExistingRuleIdentity? identity) {
		string ruleUId = identity?.RuleUId ?? Guid.NewGuid().ToString();
		BusinessRuleCaseMetadataDto @case = BuildCase(attributeMap, rule, includeAttributeReferenceSchemaName, entitySchemaName, identity);
		List<BusinessRuleTriggerMetadataDto> triggers = BuildTriggers(attributeMap, rule, entitySchemaName, identity);
		return new BusinessRuleMetadataDto {
			TypeName = BusinessRuleTypeName,
			UId = ruleUId,
			Name = ResolveRuleName(rule),
			Enabled = rule.Enabled ?? true,
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
		ExistingRuleIdentity? identity = null) {
		return new BusinessRuleCaseMetadataDto {
			TypeName = BusinessRuleCaseTypeName,
			UId = identity?.CaseUId ?? Guid.NewGuid().ToString(),
			Condition = BuildConditionGroup(attributeMap, rule.Condition, includeAttributeReferenceSchemaName, identity),
			Actions = rule.Actions
				.Select(action => BuildAction(attributeMap, action, includeAttributeReferenceSchemaName, entitySchemaName))
				.ToList()
		};
	}

	private static BusinessRuleGroupConditionMetadataDto BuildConditionGroup(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleConditionGroup group,
		bool includeAttributeReferenceSchemaName,
		ExistingRuleIdentity? identity = null) {
		return new BusinessRuleGroupConditionMetadataDto {
			TypeName = BusinessRuleGroupConditionTypeName,
			UId = identity?.GroupConditionUId ?? Guid.NewGuid().ToString(),
			LogicalOperation = string.Equals(group.LogicalOperation, "OR", StringComparison.OrdinalIgnoreCase) ? LogicalOr : LogicalAnd,
			Conditions = (group.Conditions ?? [])
				.Select(condition => BuildCondition(attributeMap, condition, includeAttributeReferenceSchemaName))
				.ToList()
		};
	}

	private static BusinessRuleConditionMetadataDto BuildCondition(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleCondition condition,
		bool includeAttributeReferenceSchemaName) {
		bool hasRight = RequiresRightExpression(condition.ComparisonType);

		// Each operand can be an AttributeValue, Const, or SysValue. The data value type and
		// reference schema of a Const operand are inferred from the OTHER (typed) operand, so
		// resolve both sides first.
		OperandTypeContext? leftType = ResolveOperandTypeContext(attributeMap, condition.LeftExpression);
		OperandTypeContext? rightType = hasRight
			? ResolveOperandTypeContext(attributeMap, condition.RightExpression!)
			: null;
		OperandTypeContext fallback = leftType?.AsValueType() ?? rightType?.AsValueType() ?? OperandTypeContext.Text;

		return new BusinessRuleConditionMetadataDto {
			TypeName = BusinessRuleConditionTypeName,
			UId = ResolveBlockUId(condition.UId),
			ComparisonType = MapComparisonType(condition.ComparisonType),
			LeftExpression = BuildOperandExpression(
				attributeMap,
				condition.LeftExpression,
				rightType?.AsValueType() ?? fallback,
				includeAttributeReferenceSchemaName),
			RightExpression = hasRight
				? BuildOperandExpression(
					attributeMap,
					condition.RightExpression!,
					leftType?.AsValueType() ?? fallback,
					includeAttributeReferenceSchemaName)
				: null
		};
	}

	/// <summary>
	/// Resolves the data value type and reference schema carried by a typed operand
	/// (AttributeValue or SysValue). Returns <c>null</c> for a Const operand, which has no
	/// intrinsic type and inherits its type from the operand it is compared against.
	/// </summary>
	private static OperandTypeContext? ResolveOperandTypeContext(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleExpression expression) {
		if (string.Equals(expression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			BusinessRuleAttributeDescriptor descriptor = attributeMap[expression.Path!];
			return new OperandTypeContext(descriptor.DataValueTypeName, descriptor.ReferenceSchemaName);
		}

		if (string.Equals(expression.Type, SysValueExpressionType, StringComparison.OrdinalIgnoreCase)
			&& SupportedSystemVariables.TryGetValue(expression.SysValueName!, out SystemVariableDescriptor? sysValue)) {
			return new OperandTypeContext(sysValue.DataValueTypeName, sysValue.ReferenceSchemaName);
		}

		return null;
	}

	private static BusinessRuleExpressionMetadataDto BuildOperandExpression(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleExpression expression,
		OperandTypeContext constValueType,
		bool includeAttributeReferenceSchemaName) {
		if (string.Equals(expression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			string path = expression.Path!;
			BusinessRuleAttributeDescriptor descriptor = attributeMap[path];
			return BuildAttributeExpression(
				descriptor,
				path,
				descriptor.DataValueTypeName,
				includeAttributeReferenceSchemaName,
				expression.UId);
		}

		if (string.Equals(expression.Type, SysValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			// Persist the canonical catalog name rather than the caller-supplied casing: the
			// validator accepts the name case-insensitively, but the platform resolves system
			// variables by exact name at runtime, so a casing variant must be normalized here.
			OperandTypeContext sysValueType =
				ResolveOperandTypeContext(attributeMap, expression) ?? OperandTypeContext.Text;
			string sysValueName =
				SupportedSystemVariables.TryGetValue(expression.SysValueName!, out SystemVariableDescriptor? descriptor)
					? descriptor.SysValueName
					: expression.SysValueName;
			return new BusinessRuleExpressionMetadataDto {
				TypeName = BusinessRuleSysValueExpressionTypeName,
				UId = ResolveBlockUId(expression.UId),
				Type = SysValueExpressionType,
				DataValueTypeName = sysValueType.DataValueTypeName,
				ReferenceSchemaName = sysValueType.ReferenceSchemaName,
				SysValueName = sysValueName
			};
		}

		// Const: inherit the data value type and reference schema from the compared operand.
		object? value = ConvertJsonElement(expression.Value!.Value, constValueType.DataValueTypeName);
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleValueExpressionTypeName,
			UId = ResolveBlockUId(expression.UId),
			Type = ConstExpressionType,
			DataValueTypeName = constValueType.DataValueTypeName,
			ReferenceSchemaName = constValueType.ReferenceSchemaName,
			Value = value
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildAttributeExpression(
		BusinessRuleAttributeDescriptor descriptor,
		string path,
		string? dataValueTypeName = null,
		bool includeAttributeReferenceSchemaName = true,
		string? requestedUId = null) {
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleAttributeExpressionTypeName,
			UId = ResolveBlockUId(requestedUId),
			Type = "AttributeValue",
			DataValueTypeName = dataValueTypeName ?? descriptor.DataValueTypeName,
			ReferenceSchemaName = includeAttributeReferenceSchemaName ? descriptor.ReferenceSchemaName : null,
			Path = path,
		};
	}

	private static BusinessRuleExpressionMetadataDto BuildMinimalAttributeExpression(string path) {
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleAttributeExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = AttributeValueExpressionType,
			Path = path
		};
	}

	private static BaseBusinessRuleActionMetadataDto BuildAction(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRuleAction action,
		bool includeAttributeReferenceSchemaName,
		string? entitySchemaName) {
		if (string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			return BuildSetValuesAction(attributeMap, action, includeAttributeReferenceSchemaName, entitySchemaName);
		}

		return new FieldSelectionBusinessRuleActionMetadataDto {
			TypeName = GetActionTypeName(action.ActionType),
			UId = ResolveBlockUId(action.UId),
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
			UId = ResolveBlockUId(action.UId),
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
			UId = ResolveBlockUId(item.UId),
			Enabled = true,
			Expression = BuildAttributeExpression(
				targetDescriptor,
				targetPath,
				dataValueTypeName,
				includeAttributeReferenceSchemaName,
				item.Expression.UId),
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
				includeAttributeReferenceSchemaName,
				item.Value.UId);
		}

		object? value = ConvertJsonElement(item.Value.Value!.Value, dataValueTypeName);
		return new BusinessRuleExpressionMetadataDto {
			TypeName = BusinessRuleValueExpressionTypeName,
			UId = ResolveBlockUId(item.Value.UId),
			Type = "Const",
			DataValueTypeName = dataValueTypeName,
			ReferenceSchemaName = targetDescriptor.ReferenceSchemaName,
			Value = value
		};
	}

	private static List<BusinessRuleTriggerMetadataDto> BuildTriggers(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		string? entitySchemaName,
		ExistingRuleIdentity? identity = null) {
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
		identity?.ApplyTriggerIdentities(triggers);
		return triggers;
	}

	private static IReadOnlyList<BusinessRuleMetadataDto> BuildApplyFilterRules(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		ApplyFilterBusinessRuleAction action,
		ExistingRuleIdentity? identity = null) {
		string normalizedTargetFilterPath = NormalizeRelativeFilterPath(action.TargetFilterPath);
		string? normalizedSourceFilterPath = NormalizeOptionalRelativeFilterPath(action.SourceFilterPath);
		string parentRuleUId = identity?.RuleUId ?? Guid.NewGuid().ToString();
		string parentActionUId = ResolveBlockUId(action.UId);

		BusinessRuleMetadataDto parentRule = new() {
			TypeName = BusinessRuleTypeName,
			UId = parentRuleUId,
			Name = ResolveRuleName(rule),
			Enabled = rule.Enabled ?? true,
			Caption = rule.Caption.Trim(),
			Cases = [BuildApplyFilterParentCase(
				attributeMap,
				rule,
				action,
				normalizedTargetFilterPath,
				normalizedSourceFilterPath,
				parentActionUId,
				identity)],
			Triggers = BuildApplyFilterParentTriggers(rule, action, identity)
		};

		List<BusinessRuleMetadataDto> rules = [parentRule];
		if (action.ClearValue) {
			rules.Add(BuildApplyFilterClearValueRule(
				attributeMap,
				action,
				normalizedTargetFilterPath,
				normalizedSourceFilterPath,
				parentRuleUId,
				parentActionUId));
		}

		if (action.PopulateValue) {
			rules.Add(BuildApplyFilterPopulateValueRule(
				attributeMap,
				action,
				normalizedTargetFilterPath,
				parentRuleUId,
				parentActionUId));
		}

		return rules;
	}

	private static BusinessRuleCaseMetadataDto BuildApplyFilterParentCase(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		ApplyFilterBusinessRuleAction action,
		string normalizedTargetFilterPath,
		string? normalizedSourceFilterPath,
		string parentActionUId,
		ExistingRuleIdentity? identity = null) {
		BusinessRuleAttributeDescriptor targetDescriptor = attributeMap[action.Target];
		BusinessRuleAttributeDescriptor sourceDescriptor = attributeMap[action.Source];
		return new BusinessRuleCaseMetadataDto {
			TypeName = BusinessRuleCaseTypeName,
			UId = identity?.CaseUId ?? Guid.NewGuid().ToString(),
			Condition = BuildConditionGroup(attributeMap, rule.Condition, includeAttributeReferenceSchemaName: true, identity),
			Actions = [
				new BusinessRuleFilterLookupActionMetadataDto {
					TypeName = BusinessRuleFilterLookupElementTypeName,
					UId = parentActionUId,
					Enabled = true,
					ClearValue = action.ClearValue,
					PopulateValue = action.PopulateValue,
					LeftExpression = BuildFilterLookupExpression(targetDescriptor, action.Target, normalizedTargetFilterPath),
					RightExpression = BuildFilterLookupExpression(sourceDescriptor, action.Source, normalizedSourceFilterPath)
				}
			]
		};
	}

	private static BusinessRuleMetadataDto BuildApplyFilterClearValueRule(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		ApplyFilterBusinessRuleAction action,
		string normalizedTargetFilterPath,
		string? normalizedSourceFilterPath,
		string parentRuleUId,
		string parentActionUId) {
		string caption = $"ChildRule-{parentRuleUId}-ClearValue";
		string targetRelatedPath = $"{action.Target}.{normalizedTargetFilterPath}";
		string sourceComparisonPath = BuildApplyFilterSourceComparisonPath(action.Source, normalizedSourceFilterPath);
		BusinessRuleAttributeDescriptor targetDescriptor = attributeMap[action.Target];
		return new BusinessRuleMetadataDto {
			TypeName = BusinessRuleTypeName,
			UId = Guid.NewGuid().ToString(),
			Name = $"Autogenerated_{parentRuleUId}_ClearValue",
			Enabled = true,
			Caption = caption,
			ParentUId = parentRuleUId,
			ParentActionUId = parentActionUId,
			Cases = [
				new BusinessRuleCaseMetadataDto {
					TypeName = BusinessRuleCaseTypeName,
					UId = Guid.NewGuid().ToString(),
					Condition = new BusinessRuleConditionMetadataDto {
						TypeName = BusinessRuleConditionTypeName,
						UId = Guid.NewGuid().ToString(),
						ComparisonType = ComparisonNotEqual,
						LeftExpression = BuildMinimalAttributeExpression(sourceComparisonPath),
						RightExpression = BuildMinimalAttributeExpression(targetRelatedPath)
					},
					Actions = [
						new FieldSelectionBusinessRuleActionMetadataDto {
							TypeName = BusinessRuleSetValuesElementTypeName,
							UId = Guid.NewGuid().ToString(),
							Enabled = true,
							Items = new List<BusinessRuleSetValueItemMetadataDto> {
								new() {
									TypeName = BusinessRuleSetValueItemTypeName,
									UId = Guid.NewGuid().ToString(),
									Enabled = true,
									Expression = BuildAttributeExpression(targetDescriptor, action.Target, targetDescriptor.DataValueTypeName, true),
									Value = BuildEmptyLookupExpression()
								}
							}
						}
					]
				}
			],
			Triggers = [BuildChangeTrigger(action.Source)]
		};
	}

	private static BusinessRuleMetadataDto BuildApplyFilterPopulateValueRule(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		ApplyFilterBusinessRuleAction action,
		string normalizedTargetFilterPath,
		string parentRuleUId,
		string parentActionUId) {
		string caption = $"ChildRule-{parentRuleUId}-PopulateValue";
		string targetRelatedPath = $"{action.Target}.{normalizedTargetFilterPath}";
		BusinessRuleAttributeDescriptor sourceDescriptor = attributeMap[action.Source];
		return new BusinessRuleMetadataDto {
			TypeName = BusinessRuleTypeName,
			UId = Guid.NewGuid().ToString(),
			Name = $"Autogenerated_{parentRuleUId}_PopulateValue",
			Enabled = true,
			Caption = caption,
			ParentUId = parentRuleUId,
			ParentActionUId = parentActionUId,
			Cases = [
				new BusinessRuleCaseMetadataDto {
					TypeName = BusinessRuleCaseTypeName,
					UId = Guid.NewGuid().ToString(),
					Condition = new BusinessRuleConditionMetadataDto {
						TypeName = BusinessRuleConditionTypeName,
						UId = Guid.NewGuid().ToString(),
						ComparisonType = ComparisonIsFilledIn,
						LeftExpression = BuildMinimalAttributeExpression(action.Target)
					},
					Actions = [
						new FieldSelectionBusinessRuleActionMetadataDto {
							TypeName = BusinessRuleSetValuesElementTypeName,
							UId = Guid.NewGuid().ToString(),
							Enabled = true,
							Items = new List<BusinessRuleSetValueItemMetadataDto> {
								new() {
									TypeName = BusinessRuleSetValueItemTypeName,
									UId = Guid.NewGuid().ToString(),
									Enabled = true,
									Expression = BuildAttributeExpression(sourceDescriptor, action.Source, sourceDescriptor.DataValueTypeName, true),
									Value = BuildAttributeExpression(attributeMap[targetRelatedPath], targetRelatedPath)
								}
							}
						}
					]
				}
			],
			Triggers = [BuildChangeTrigger(action.Target)]
		};
	}

	private static List<BusinessRuleTriggerMetadataDto> BuildApplyFilterParentTriggers(
		BusinessRule rule,
		ApplyFilterBusinessRuleAction action,
		ExistingRuleIdentity? identity = null) {
		List<BusinessRuleTriggerMetadataDto> triggers = (rule.Condition.Conditions ?? [])
			.SelectMany(EnumerateTriggerNames)
			.Append(action.Source)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(BuildChangeTrigger)
			.ToList();
		triggers.Add(BuildDataLoadedTrigger());
		identity?.ApplyTriggerIdentities(triggers);
		return triggers;
	}

	private static BusinessRuleTriggerMetadataDto BuildChangeTrigger(string name) =>
		new() {
			TypeName = BusinessRuleTriggerTypeName,
			UId = Guid.NewGuid().ToString(),
			Name = name,
			Type = ChangeAttributeValueTriggerType
		};

	private static BusinessRuleTriggerMetadataDto BuildDataLoadedTrigger() =>
		new() {
			TypeName = BusinessRuleTriggerTypeName,
			UId = Guid.NewGuid().ToString(),
			Name = string.Empty,
			Type = DataLoadedTriggerType
		};

	private static BusinessRuleFilterLookupExpressionMetadataDto BuildFilterLookupExpression(
		BusinessRuleAttributeDescriptor descriptor,
		string path,
		string? filterExpression) =>
		new() {
			TypeName = BusinessRuleFilterLookupExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = ConstExpressionType,
			DataValueTypeName = "Lookup",
			ReferenceSchemaName = descriptor.ReferenceSchemaName,
			Path = path,
			FilterExpression = string.IsNullOrWhiteSpace(filterExpression) ? "null" : filterExpression
		};

	private static BusinessRuleExpressionMetadataDto BuildEmptyLookupExpression() =>
		new() {
			TypeName = BusinessRuleEmptyValueExpressionTypeName,
			UId = Guid.NewGuid().ToString(),
			Type = ConstExpressionType,
			DataValueTypeName = "Lookup",
			Value = Guid.Empty.ToString()
		};

	private static string NormalizeRelativeFilterPath(string path) => path.Trim();

	private static string? NormalizeOptionalRelativeFilterPath(string? path) =>
		string.IsNullOrWhiteSpace(path) ? null : path.Trim();

	private static string BuildApplyFilterSourceComparisonPath(string sourcePath, string? sourceFilterPath) =>
		string.IsNullOrWhiteSpace(sourceFilterPath) ? sourcePath : $"{sourcePath}.{sourceFilterPath}";

	private static IEnumerable<string> EnumerateTriggerNames(BusinessRuleCondition condition) {
		// Only attribute operands drive change triggers; Const and SysValue operands have no
		// attribute path, so a condition with no attribute operand (for example
		// CurrentUserRoles CONTAIN <role>) contributes only the DataLoaded trigger.
		if (IsTriggerAttributeExpression(condition.LeftExpression)) {
			yield return condition.LeftExpression.Path!;
		}

		if (condition.RightExpression is not null && IsTriggerAttributeExpression(condition.RightExpression)) {
			yield return condition.RightExpression.Path!;
		}
	}

	private static bool IsTriggerAttributeExpression(BusinessRuleExpression expression) =>
		string.Equals(expression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)
		&& !string.IsNullOrWhiteSpace(expression.Path);

	private static IEnumerable<string> EnumerateFormulaTriggerNames(
		BusinessRuleAction action,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (!string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			return [];
		}

		return action.SetValueItems
			.Where(item => BusinessRuleFormulaBuilder.IsFormulaExpression(item.Value))
			.SelectMany(item => BusinessRuleFormulaBuilder.GetFormulaSourcePaths(
				BusinessRuleFormulaBuilder.GetRequiredFormulaText(item.Value),
				attributeMap));
	}

	private static IEnumerable<string> EnumerateSetValuesAttributeSourceTriggerNames(BusinessRuleAction action) {
		if (!string.Equals(action.ActionType, SetValuesActionTypeName, StringComparison.OrdinalIgnoreCase)) {
			return [];
		}

		return action.SetValueItems
			.Where(item => IsAttributeValueExpression(item.Value) && !string.IsNullOrWhiteSpace(item.Value.Path))
			.Select(item => GetRootAttributePath(item.Value.Path));
	}

	private static bool IsAttributeValueExpression(BusinessRuleExpression? expression) =>
		string.Equals(expression?.Type, "AttributeValue", StringComparison.OrdinalIgnoreCase);

	private static string GenerateBusinessRuleName() => $"BusinessRule_{Guid.NewGuid():N}"[..20];

	private static string ResolveRuleName(BusinessRule rule) =>
		string.IsNullOrWhiteSpace(rule.Name) ? GenerateBusinessRuleName() : rule.Name.Trim();

	private static string ResolveBlockUId(string? requestedUId) {
		if (string.IsNullOrWhiteSpace(requestedUId)) {
			return Guid.NewGuid().ToString();
		}

		if (!Guid.TryParse(requestedUId, out Guid parsedUId)) {
			throw new InvalidOperationException($"Block uId '{requestedUId}' is not a valid GUID.");
		}

		return parsedUId.ToString();
	}

	private static bool TryGetApplyFilterAction(BusinessRule rule, out ApplyFilterBusinessRuleAction? action) {
		action = null;
		if (rule.Actions.Count != 1) {
			return false;
		}

		action = rule.Actions[0] as ApplyFilterBusinessRuleAction;
		return action is not null;
	}

	private static bool TryGetApplyStaticFilterAction(BusinessRule rule, out ApplyStaticFilterBusinessRuleAction? action) {
		action = null;
		if (rule.Actions.Count != 1) {
			return false;
		}

		action = rule.Actions[0] as ApplyStaticFilterBusinessRuleAction;
		return action is not null;
	}

	private static BusinessRuleMetadataDto BuildApplyStaticFilterRule(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		BusinessRule rule,
		ApplyStaticFilterBusinessRuleAction action,
		IFilterSchemaProvider? filterSchemaProvider,
		ILookupValueResolver? lookupValueResolver,
		ExistingRuleIdentity? identity = null) {
		BusinessRuleAttributeDescriptor targetDescriptor = attributeMap[action.TargetAttribute];
		string rootSchemaName = targetDescriptor.ReferenceSchemaName!;
		StaticFilterGroup filterGroup = StaticFilterDeserializer.Deserialize(action.Filter);

		string esqEnvelope = BuildEsqEnvelope(filterGroup, rootSchemaName, filterSchemaProvider, lookupValueResolver);

		BusinessRuleSetFilterActionMetadataDto setFilterAction = new() {
			TypeName = BusinessRuleSetFilterElementTypeName,
			UId = ResolveBlockUId(action.UId),
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
				Type = ConstExpressionType,
				Value = esqEnvelope
			}
		};

		BusinessRuleCaseMetadataDto @case = new() {
			TypeName = BusinessRuleCaseTypeName,
			UId = identity?.CaseUId ?? Guid.NewGuid().ToString(),
			Condition = BuildConditionGroup(attributeMap, rule.Condition, includeAttributeReferenceSchemaName: true, identity),
			Actions = [setFilterAction]
		};

		List<BusinessRuleTriggerMetadataDto> triggers = (rule.Condition.Conditions ?? [])
			.SelectMany(EnumerateTriggerNames)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(BuildChangeTrigger)
			.ToList();
		triggers.Add(BuildDataLoadedTrigger());
		identity?.ApplyTriggerIdentities(triggers);

		return new BusinessRuleMetadataDto {
			TypeName = BusinessRuleTypeName,
			UId = identity?.RuleUId ?? Guid.NewGuid().ToString(),
			Name = ResolveRuleName(rule),
			Enabled = rule.Enabled ?? true,
			Caption = rule.Caption.Trim(),
			Cases = [@case],
			Triggers = triggers
		};
	}

	private static string BuildEsqEnvelope(
		StaticFilterGroup filterGroup,
		string rootSchemaName,
		IFilterSchemaProvider? filterSchemaProvider,
		ILookupValueResolver? lookupValueResolver) {
		if (filterSchemaProvider is null) {
			throw new InvalidOperationException(
				"apply-static-filter requires an IFilterSchemaProvider to resolve column metadata.");
		}

		SimpleToFullFilterConverter builder = new(filterSchemaProvider, lookupValueResolver);
		return builder.Build(filterGroup, rootSchemaName);
	}

	private sealed class ExistingRuleIdentity {
		private readonly List<(string Name, int Type, string UId)> _unconsumedTriggers;

		public ExistingRuleIdentity(JsonObject existingRule) {
			RuleUId = GetString(existingRule, "uId")
				?? throw new InvalidOperationException("Existing business rule has no uId.");
			(CaseUId, GroupConditionUId) = ReadCaseIdentity(existingRule);
			_unconsumedTriggers = ReadTriggers(existingRule);
		}

		public string RuleUId { get; }

		public string? CaseUId { get; }

		public string? GroupConditionUId { get; }

		public void ApplyTriggerIdentities(IEnumerable<BusinessRuleTriggerMetadataDto> triggers) {
			foreach (BusinessRuleTriggerMetadataDto trigger in triggers) {
				int matchIndex = _unconsumedTriggers.FindIndex(candidate =>
					candidate.Type == trigger.Type
					&& string.Equals(candidate.Name, trigger.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
				if (matchIndex < 0) {
					continue;
				}

				trigger.UId = _unconsumedTriggers[matchIndex].UId;
				_unconsumedTriggers.RemoveAt(matchIndex);
			}
		}

		private static (string? CaseUId, string? GroupConditionUId) ReadCaseIdentity(JsonObject existingRule) {
			if (existingRule["cases"] is not JsonArray cases
				|| cases.Count != 1
				|| cases[0] is not JsonObject existingCase) {
				return (null, null);
			}

			string? caseUId = GetString(existingCase, "uId");
			string? groupUId = null;
			if (existingCase["condition"] is JsonObject condition
				&& string.Equals(GetString(condition, "typeName"), BusinessRuleGroupConditionTypeName, StringComparison.Ordinal)) {
				groupUId = GetString(condition, "uId");
			}

			return (
				string.IsNullOrWhiteSpace(caseUId) ? null : caseUId,
				string.IsNullOrWhiteSpace(groupUId) ? null : groupUId);
		}

		private static List<(string Name, int Type, string UId)> ReadTriggers(JsonObject existingRule) =>
			existingRule["triggers"] is not JsonArray triggers
				? []
				: triggers
					.OfType<JsonObject>()
					.Select(trigger => (
						Name: GetString(trigger, "name") ?? string.Empty,
						Type: GetInt(trigger, "type", ChangeAttributeValueTriggerType),
						UId: GetString(trigger, "uId") ?? string.Empty))
					.Where(trigger => !string.IsNullOrWhiteSpace(trigger.UId))
					.ToList();

		private static string? GetString(JsonObject source, string propertyName) =>
			source[propertyName] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

		private static int GetInt(JsonObject source, string propertyName, int defaultValue) =>
			source[propertyName] is JsonValue value && value.TryGetValue(out int result) ? result : defaultValue;
	}

	/// <summary>
	/// Data value type and reference schema used to type a condition operand.
	/// </summary>
	/// <param name="DataValueTypeName">Resolved data value type name.</param>
	/// <param name="ReferenceSchemaName">Reference schema for lookup-typed operands, otherwise <c>null</c>.</param>
	private sealed record OperandTypeContext(string DataValueTypeName, string? ReferenceSchemaName) {
		/// <summary>Default context for a comparison between two untyped (Const) operands.</summary>
		public static readonly OperandTypeContext Text = new("Text", null);

		/// <summary>
		/// The type a constant/value uses when compared against this operand. An <c>ObjectList</c>
		/// (for example <c>CurrentUserRoles</c>) is a collection of lookup records, so the value
		/// compared against it is a single <c>Lookup</c> of the same reference schema.
		/// </summary>
		public OperandTypeContext AsValueType() =>
			string.Equals(DataValueTypeName, "ObjectList", StringComparison.OrdinalIgnoreCase)
				? new OperandTypeContext("Lookup", ReferenceSchemaName)
				: this;
	}
}

