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
	/// <param name="attributeMap">Page business-rule attributes keyed by payload path.</param>
	/// <param name="elementNames">Available page element names.</param>
	/// <param name="sysSettingMap">Resolved system-setting condition operands keyed by setting code. Optional when the rule references no SysSetting operand.</param>
	/// <param name="bundle">The page bundle the attribute map was built from. When given, a rejected attribute is
	/// explained (virtual, lookup path, forward reference, not declared) and the message says where to go instead.</param>
	void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames,
		IReadOnlyDictionary<string, SysSettingOperandDescriptor>? sysSettingMap = null,
		PageBundleInfo? bundle = null);
}

internal sealed class PageBusinessRuleValidator(IBusinessRuleValidator businessRuleValidator)
	: IPageBusinessRuleValidator {

	internal const string UnsupportedAttributeRedirect =
		"A data condition belongs in an entity business rule (create-entity-business-rules) on the object that owns "
		+ "the column; view-model state belongs in a page handler (crt.HandleViewModelAttributeChangeRequest).";

	internal const string EntityOnlyLookupFilterRedirect =
		"apply-static-filter is an entity business rule: Freedom UI page rules have no lookup-filter action. "
		+ "Create it with create-entity-business-rules on the object that owns the lookup column.";

	public void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames,
		IReadOnlyDictionary<string, SysSettingOperandDescriptor>? sysSettingMap = null,
		PageBundleInfo? bundle = null) {
		try {
			businessRuleValidator.Validate(rule, attributeMap, ValidatePageAction(elementNames), sysSettingMap);
		} catch (ArgumentException exception) {
			string? reason = exception is UnknownBusinessRuleAttributeException unknown && bundle is not null
				? PageBusinessRuleAttributeProvider.DescribeUnsupportedConditionAttribute(bundle, unknown.Path)
				: null;
			throw new ArgumentException(AppendCandidateHint(exception.Message, reason, attributeMap, elementNames), exception);
		}
	}

	private static Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> ValidatePageAction(
		IReadOnlySet<string> elementNames) =>
		(action, _) => {
			if (string.IsNullOrEmpty(action.ActionType)) {
				throw new ArgumentException("rule.actions[*].type is required.");
			}

			if (!SupportedPageActionTypeNames.ContainsKey(action.ActionType)) {
				string redirect = string.Equals(action.ActionType, ApplyStaticFilterActionTypeName, StringComparison.Ordinal)
					? $" {EntityOnlyLookupFilterRedirect}"
					: string.Empty;
				throw new ArgumentException(
					$"Unsupported rule.actions[*].type '{action.ActionType}'. Supported values: {SupportedPageActionTypesDescription}.{redirect}");
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

	private static string AppendCandidateHint(
		string message,
		string? reason,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames) {
		string result = message.Replace(
			"Unknown attribute",
			"Unknown or unsupported datasource-bound page attribute",
			StringComparison.Ordinal);
		if (!string.IsNullOrEmpty(reason)) {
			result += $" {reason} {UnsupportedAttributeRedirect}";
		}
		if (result.Contains("rule.condition.conditions", StringComparison.Ordinal)) {
			result += $" Available condition attributes: {FormatAttributeCandidates(attributeMap)}.";
		}
		if (result.Contains("rule.actions", StringComparison.Ordinal)) {
			result += $" Available page elements: {FormatCandidates(elementNames)}.";
		}
		return result;
	}

	// Component attributes are few and easy to miss among the many
	// data-source columns, so they are listed first and never fall behind the cap.
	private static string FormatAttributeCandidates(
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap) =>
		FormatCandidates(
			attributeMap.Where(pair => pair.Value.IsComponentAttribute).Select(pair => pair.Key)
				.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
				.Concat(attributeMap.Where(pair => !pair.Value.IsComponentAttribute).Select(pair => pair.Key)
					.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)),
			keepOrder: true);

	private static string FormatCandidates(IEnumerable<string> candidates, bool keepOrder = false) {
		IEnumerable<string> filtered = candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate));
		string[] values = (keepOrder ? filtered : filtered.OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase))
			.Take(20)
			.ToArray();
		return values.Length == 0 ? "<none>" : string.Join(", ", values);
	}
}
