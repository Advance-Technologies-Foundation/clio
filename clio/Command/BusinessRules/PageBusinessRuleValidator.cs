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
	void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames,
		IReadOnlyDictionary<string, SysSettingOperandDescriptor>? sysSettingMap = null);
}

internal sealed class PageBusinessRuleValidator(IBusinessRuleValidator businessRuleValidator)
	: IPageBusinessRuleValidator {

	public void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames,
		IReadOnlyDictionary<string, SysSettingOperandDescriptor>? sysSettingMap = null) {
		try {
			businessRuleValidator.Validate(rule, attributeMap, ValidatePageAction(elementNames), sysSettingMap);
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
