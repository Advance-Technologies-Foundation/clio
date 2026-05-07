using System;
using System.Collections.Generic;
using System.Linq;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal static class PageBusinessRuleValidator {

	private const string ApplyStaticFilterAtPageScopeMessage =
		"rule.actions[*].type 'apply-static-filter' is not supported for page-level rules. " +
		"Static lookup filters apply at the entity level, so every page and editable list bound to the entity " +
		"sees the narrowed lookup. Use create-entity-business-rule with apply-static-filter instead.";

	internal static void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames) {
		// Reject apply-static-filter at page scope before any condition / element validation
		// runs, so callers don't have to fix unrelated condition errors only to be told the
		// action is not supported on pages. Static lookup filters belong on the entity rule.
		RejectApplyStaticFilterAtPageScope(rule);
		RejectDatasourcePaths(rule);
		try {
			BusinessRuleValidator.Validate(rule, attributeMap, ValidatePageAction(elementNames));
		} catch (ArgumentException exception) {
			throw new ArgumentException(AppendCandidateHint(exception.Message, attributeMap, elementNames), exception);
		}
	}

	private static void RejectApplyStaticFilterAtPageScope(BusinessRule rule) {
		bool hasApplyStaticFilter = (rule.Actions ?? [])
			.OfType<ApplyStaticFilterBusinessRuleAction>()
			.Any();
		if (hasApplyStaticFilter) {
			throw new ArgumentException(ApplyStaticFilterAtPageScopeMessage);
		}
	}

	private static Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> ValidatePageAction(
		IReadOnlySet<string> elementNames) =>
		(action, _) => ValidateSinglePageAction(action, elementNames);

	private static void ValidateSinglePageAction(BusinessRuleAction action, IReadOnlySet<string> elementNames) {
		if (string.IsNullOrEmpty(action.ActionType)) {
			throw new ArgumentException("rule.actions[*].type is required.");
		}
		if (action is ApplyStaticFilterBusinessRuleAction) {
			throw new ArgumentException(ApplyStaticFilterAtPageScopeMessage);
		}
		if (!SupportedPageActionTypeNames.ContainsKey(action.ActionType)) {
			throw new ArgumentException(
				$"Unsupported rule.actions[*].type '{action.ActionType}'. Supported values: {SupportedPageActionTypesDescription}.");
		}
		ValidatePageActionItems(action.FieldSelectionItems, elementNames);
	}

	private static void ValidatePageActionItems(List<string> items, IReadOnlySet<string> elementNames) {
		if (items.Count == 0) {
			throw new ArgumentException("rule.actions[*].items must contain at least one page element name.");
		}
		foreach (string target in items) {
			if (string.IsNullOrWhiteSpace(target)) {
				throw new ArgumentException("rule.actions[*].items cannot contain empty page element names.");
			}
			if (!elementNames.Contains(target)) {
				throw new ArgumentException($"Unknown page element '{target}' in rule.actions[*].items.");
			}
		}
	}

	private static void RejectDatasourcePaths(BusinessRule rule) {
		foreach (BusinessRuleCondition condition in rule.Condition?.Conditions ?? []) {
			RejectDatasourcePath(condition.LeftExpression, "rule.condition.conditions[*].leftExpression.path");
			if (condition.RightExpression is not null
				&& string.Equals(condition.RightExpression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
				RejectDatasourcePath(condition.RightExpression, "rule.condition.conditions[*].rightExpression.path");
			}
		}
	}

	private static void RejectDatasourcePath(BusinessRuleExpression? expression, string fieldName) {
		if (expression?.Path?.Contains('.', StringComparison.Ordinal) == true) {
			throw new ArgumentException(
				$"{fieldName} must use the declared page attribute name from bundle.viewModelConfig.attributes, not datasource path '{expression.Path}'.");
		}
	}

	private static string AppendCandidateHint(
		string message,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames) {
		string result = message.Replace(
			"Unknown attribute",
			"Unknown or unsupported datasource-bound page attribute",
			StringComparison.Ordinal);
		if (result.Contains("rule.condition.conditions", StringComparison.Ordinal)) {
			result += $" Available condition attributes: {FormatCandidates(attributeMap.Keys)}.";
		}
		if (result.Contains("rule.actions", StringComparison.Ordinal)) {
			result += $" Available page elements: {FormatCandidates(elementNames)}.";
		}
		return result;
	}

	private static string FormatCandidates(IEnumerable<string> candidates) {
		string[] values = candidates
			.Where(candidate => !string.IsNullOrWhiteSpace(candidate))
			.OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
			.Take(20)
			.ToArray();
		return values.Length == 0 ? "<none>" : string.Join(", ", values);
	}
}
