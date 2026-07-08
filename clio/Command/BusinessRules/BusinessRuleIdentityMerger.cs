using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleIdentityMerger {

	internal static IReadOnlyList<BusinessRuleMetadataDto> Merge(
		JsonObject existingRule,
		IReadOnlyList<BusinessRuleMetadataDto> generatedRules,
		bool? requestedEnabled) {
		ArgumentNullException.ThrowIfNull(existingRule);
		ArgumentNullException.ThrowIfNull(generatedRules);
		if (generatedRules.Count == 0) {
			throw new ArgumentException("At least one generated business rule is required.", nameof(generatedRules));
		}

		BusinessRuleMetadataDto parent = generatedRules[0];
		string generatedUId = parent.UId;
		string existingUId = GetString(existingRule, "uId")
			?? throw new InvalidOperationException("Existing business rule has no uId.");

		parent.UId = existingUId;
		parent.Enabled = requestedEnabled ?? GetBool(existingRule, "enabled", defaultValue: true);
		MergeCaseIdentity(existingRule, parent);
		MergeTriggerIdentity(existingRule, parent);
		ReanchorChildRules(generatedRules, generatedUId, existingUId);
		return generatedRules;
	}

	private static void MergeCaseIdentity(JsonObject existingRule, BusinessRuleMetadataDto parent) {
		if (parent.Cases.Count != 1
			|| existingRule["cases"] is not JsonArray existingCases
			|| existingCases.Count != 1
			|| existingCases[0] is not JsonObject existingCase) {
			return;
		}

		string? existingCaseUId = GetString(existingCase, "uId");
		if (!string.IsNullOrWhiteSpace(existingCaseUId)) {
			parent.Cases[0].UId = existingCaseUId;
		}

		if (parent.Cases[0].Condition is BusinessRuleGroupConditionMetadataDto generatedGroup
			&& existingCase["condition"] is JsonObject existingCondition
			&& string.Equals(GetString(existingCondition, "typeName"), BusinessRuleGroupConditionTypeName,
				StringComparison.Ordinal)) {
			string? existingGroupUId = GetString(existingCondition, "uId");
			if (!string.IsNullOrWhiteSpace(existingGroupUId)) {
				generatedGroup.UId = existingGroupUId;
			}
		}
	}

	private static void MergeTriggerIdentity(JsonObject existingRule, BusinessRuleMetadataDto parent) {
		if (existingRule["triggers"] is not JsonArray existingTriggers) {
			return;
		}

		List<(string Name, int Type, string UId)> unconsumed = existingTriggers
			.OfType<JsonObject>()
			.Select(trigger => (
				Name: GetString(trigger, "name") ?? string.Empty,
				Type: GetInt(trigger, "type", ChangeAttributeValueTriggerType),
				UId: GetString(trigger, "uId") ?? string.Empty))
			.Where(trigger => !string.IsNullOrWhiteSpace(trigger.UId))
			.ToList();

		foreach (BusinessRuleTriggerMetadataDto generatedTrigger in parent.Triggers) {
			int matchIndex = unconsumed.FindIndex(candidate =>
				candidate.Type == generatedTrigger.Type
				&& string.Equals(candidate.Name, generatedTrigger.Name ?? string.Empty,
					StringComparison.OrdinalIgnoreCase));
			if (matchIndex < 0) {
				continue;
			}

			generatedTrigger.UId = unconsumed[matchIndex].UId;
			unconsumed.RemoveAt(matchIndex);
		}
	}

	private static void ReanchorChildRules(
		IReadOnlyList<BusinessRuleMetadataDto> generatedRules,
		string generatedParentUId,
		string existingParentUId) {
		foreach (BusinessRuleMetadataDto child in generatedRules.Skip(1)) {
			if (!string.Equals(child.ParentUId, generatedParentUId, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			child.ParentUId = existingParentUId;
			child.Name = child.Name.Replace(generatedParentUId, existingParentUId, StringComparison.OrdinalIgnoreCase);
			if (!string.IsNullOrWhiteSpace(child.Caption)) {
				child.Caption = child.Caption.Replace(generatedParentUId, existingParentUId,
					StringComparison.OrdinalIgnoreCase);
			}
		}
	}

	private static string? GetString(JsonObject source, string propertyName) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

	private static bool GetBool(JsonObject source, string propertyName, bool defaultValue) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out bool result) ? result : defaultValue;

	private static int GetInt(JsonObject source, string propertyName, int defaultValue) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out int result) ? result : defaultValue;
}
