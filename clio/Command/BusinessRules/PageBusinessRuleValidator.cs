using System;
using System.Collections.Generic;
using System.Linq;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal interface IPageBusinessRuleValidator {
	/// <summary>
	/// Validates a page business-rule definition against page attributes and elements.
	/// </summary>
	/// <param name="rule">Business rule to validate.</param>
	/// <param name="attributeMap">Page business-rule attributes keyed by payload path (or <c>scopeId::path</c> for a scoped operand).</param>
	/// <param name="elementNames">Available page element names.</param>
	void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames);
}

internal sealed class PageBusinessRuleValidator(
	IBusinessRuleValidator businessRuleValidator,
	IFeatureToggleService featureToggleService)
	: IPageBusinessRuleValidator {

	public void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames) {
		bool allowConditionSources = featureToggleService.IsFeatureEnabled(PageConditionSourcesFeatureName);
		if (allowConditionSources) {
			// The feature is on: scoped operands are legitimate. Reject an unknown scope early with the
			// available scopes; a bare datasource path (`.`) without a scopeId is still rejected downstream
			// by the shared validator's direct-path check.
			ValidateConditionScopes(rule, attributeMap);
		} else {
			// The feature is off: preserve the prior page-rule behaviour. A `.` path must be a declared page
			// attribute name; a non-empty scopeId or a SysSetting operand is rejected by the shared validator.
			RejectDatasourcePaths(rule);
		}

		try {
			businessRuleValidator.Validate(rule, attributeMap, ValidatePageAction(elementNames), allowConditionSources);
		} catch (ArgumentException exception) {
			throw new ArgumentException(AppendCandidateHint(exception.Message, attributeMap, elementNames), exception);
		}
	}

	private static Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> ValidatePageAction(
		IReadOnlySet<string> elementNames) =>
		(action, _) => {
			if (string.IsNullOrEmpty(action.ActionType)) {
				throw new ArgumentException("rule.actions[*].type is required.");
			}

			if (!SupportedPageActionTypeNames.ContainsKey(action.ActionType)) {
				throw new ArgumentException(
					$"Unsupported rule.actions[*].type '{action.ActionType}'. Supported values: {SupportedPageActionTypesDescription}.");
			}

			List<string> items = action.FieldSelectionItems;
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
		};

	/// <summary>
	/// Validates every condition operand's <c>scopeId</c> against the scopes available on the page
	/// (empty root scope, <c>PageParameters</c>, or a DataSource name from <c>modelConfig.dataSources</c>)
	/// so an unknown scope fails early with the list of valid scopes rather than as an opaque
	/// unknown-attribute error.
	/// </summary>
	private static void ValidateConditionScopes(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) {
		if (attributeMap is not PageScopedBusinessRuleAttributeMap scopedMap) {
			return;
		}

		foreach (BusinessRuleCondition condition in rule.Condition?.Conditions ?? []) {
			ValidateConditionOperandScope(condition.LeftExpression, scopedMap);
			ValidateConditionOperandScope(condition.RightExpression, scopedMap);
		}
	}

	private static void ValidateConditionOperandScope(
		BusinessRuleExpression? expression,
		PageScopedBusinessRuleAttributeMap scopedMap) {
		if (expression is null
			|| !string.Equals(expression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)
			|| string.IsNullOrEmpty(expression.ScopeId)
			|| scopedMap.IsKnownScope(expression.ScopeId)) {
			return;
		}

		string availableScopes = FormatCandidates(
			new[] { PageParametersScope }.Concat(scopedMap.DataSourceScopes));
		throw new ArgumentException(
			$"Unknown scopeId '{expression.ScopeId}' in rule.condition.conditions[*]. Use an empty scope for a root page attribute, 'PageParameters' for a page parameter, or a DataSource name. Available scopes: {availableScopes}.");
	}

	private static void RejectDatasourcePaths(BusinessRule rule) {
		foreach (BusinessRuleCondition condition in rule.Condition?.Conditions ?? []) {
			RejectDatasourcePath(condition.LeftExpression, "rule.condition.conditions[*].leftExpression.path");
			RejectDatasourcePath(condition.RightExpression, "rule.condition.conditions[*].rightExpression.path");
		}
	}

	private static void RejectDatasourcePath(BusinessRuleExpression? expression, string fieldName) {
		if (expression is null
			|| !string.Equals(expression.Type, AttributeValueExpressionType, StringComparison.OrdinalIgnoreCase)) {
			return;
		}

		if (expression.Path?.Contains('.', StringComparison.Ordinal) == true) {
			throw new ArgumentException(
				$"{fieldName} must use the declared page attribute name from bundle.viewModelConfig.attributes, not datasource path '{expression.Path}'. Enable the '{PageConditionSourcesFeatureName}' feature to reference DataSource fields and page parameters via scopeId.");
		}
	}

	private static string AppendCandidateHint(
		string message,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames) {
		// Keep the scoped "Unknown attribute '…' in scope '…'" phrasing intact; only the plain root-scope
		// unknown-attribute error is rewritten to the datasource-bound-attribute wording.
		string result = message.Contains(" in scope '", StringComparison.Ordinal)
			? message
			: message.Replace(
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
