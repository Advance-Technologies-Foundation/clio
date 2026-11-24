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
	/// <param name="rejectedAttributes">Declared page attributes the attribute provider did not expose, keyed by name,
	/// with the reason. When given, a rejected attribute is explained and the message says where the condition belongs.</param>
	void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames,
		IReadOnlyDictionary<string, SysSettingOperandDescriptor>? sysSettingMap = null,
		IReadOnlyDictionary<string, string>? rejectedAttributes = null);
}

internal sealed class PageBusinessRuleValidator(IBusinessRuleValidator businessRuleValidator)
	: IPageBusinessRuleValidator {

	internal const string UnsupportedAttributeRedirect =
		"A data condition belongs in an entity business rule (create-entity-business-rules) on the object that owns "
		+ "the column; view-model state belongs in a page handler (crt.HandleViewModelAttributeChangeRequest).";

	internal const string EntityOnlyLookupFilterRedirect =
		"apply-static-filter is an entity business rule: Freedom UI page rules have no lookup-filter action. "
		+ "Create it with create-entity-business-rules on the object that owns the lookup column.";

	private const string UnknownPageAttributePrefix = "Unknown or unsupported datasource-bound page attribute";

	public void Validate(
		BusinessRule rule,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames,
		IReadOnlyDictionary<string, SysSettingOperandDescriptor>? sysSettingMap = null,
		IReadOnlyDictionary<string, string>? rejectedAttributes = null) {
		try {
			businessRuleValidator.Validate(rule, attributeMap, ValidatePageAction(elementNames), sysSettingMap);
		} catch (UnknownBusinessRuleAttributeException unknown) {
			string message = $"{UnknownPageAttributePrefix} '{unknown.Path}' in {unknown.FieldName}.";
			if (rejectedAttributes is not null) {
				string reason = rejectedAttributes.TryGetValue(unknown.Path, out string? known)
					? known
					: $"'{unknown.Path}' is not declared in bundle.viewModelConfig.attributes and is not a "
						+ "'<data source>.<column>' path of a page data source.";
				message += $" {reason} {UnsupportedAttributeRedirect}";
			}
			throw new ArgumentException(AppendCandidateHint(message, unknown.FieldName, attributeMap, elementNames), unknown);
		} catch (ArgumentException exception) {
			// Other validators (the formula builder) still report an unknown attribute in the base wording.
			string message = exception.Message.Replace("Unknown attribute", UnknownPageAttributePrefix, StringComparison.Ordinal);
			throw new ArgumentException(AppendCandidateHint(message, message, attributeMap, elementNames), exception);
		}
	}

	private static Action<BusinessRuleAction, IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor>> ValidatePageAction(
		IReadOnlySet<string> elementNames) =>
		(action, _) => {
			if (string.IsNullOrEmpty(action.ActionType)) {
				throw new ArgumentException("rule.actions[*].type is required.");
			}

			if (action is ApplyStaticFilterBusinessRuleAction) {
				throw new ArgumentException(
					$"Unsupported rule.actions[*].type '{action.ActionType}'. Supported values: {SupportedPageActionTypesDescription}. {EntityOnlyLookupFilterRedirect}");
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

	// The candidates follow the field that failed: condition attributes for a condition, page elements for an action.
	private static string AppendCandidateHint(
		string message,
		string failedField,
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap,
		IReadOnlySet<string> elementNames) {
		if (failedField.Contains("rule.condition.conditions", StringComparison.Ordinal)) {
			message += $" Available condition attributes: {FormatAttributeCandidates(attributeMap)}.";
		}
		if (failedField.Contains("rule.actions", StringComparison.Ordinal)) {
			message += $" Available page elements: {FormatCandidates(elementNames)}.";
		}
		return message;
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
