using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal static class BusinessRuleAddonMetadata {

	internal static IReadOnlyList<BusinessRuleBatchItemResult> ApplyUpdateBatch(
		IBusinessRuleAddonService addon,
		AddonGetRequestDto request,
		IReadOnlyList<BusinessRule> inputRules,
		Func<BusinessRule, JsonObject, IReadOnlyList<BusinessRuleMetadataDto>> convert) {
		ArgumentNullException.ThrowIfNull(addon);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(inputRules);
		ArgumentNullException.ThrowIfNull(convert);

		AddonSchemaDto schema = addon.GetSchema(request);
		JsonObject metadata = ParseMetadata(schema.MetaData);
		JsonArray rules = GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = NormalizeResourceKeys(schema.Resources.ToList());

		var results = new BusinessRuleBatchItemResult[inputRules.Count];
		var pending = new List<(int Index, string Name)>();
		HashSet<string> batchNames = new(StringComparer.OrdinalIgnoreCase);

		for (int index = 0; index < inputRules.Count; index++) {
			BusinessRule rule = inputRules[index];
			string identifier = string.IsNullOrWhiteSpace(rule?.Name)
				? rule?.Caption ?? string.Empty
				: rule.Name.Trim();
			try {
				ArgumentNullException.ThrowIfNull(rule);
				if (string.IsNullOrWhiteSpace(rule.Name)) {
					throw new ArgumentException("name is required to update a business rule.");
				}

				string name = rule.Name.Trim();
				if (!batchNames.Add(name)) {
					throw new InvalidOperationException(
						$"Business rule '{name}' appears more than once in the update batch.");
				}

				int ruleIndex = FindSingleRuleIndexByName(rules, name);
				if (ruleIndex < 0) {
					throw new InvalidOperationException($"Business rule '{name}' was not found.");
				}

				JsonObject existing = (JsonObject)rules[ruleIndex]!;
				bool effectiveEnabled = rule.Enabled ?? GetBool(existing, "enabled", defaultValue: true);
				BusinessRule effectiveRule = rule with { Enabled = effectiveEnabled };

				IReadOnlyList<BusinessRuleMetadataDto> generated = convert(effectiveRule, existing);
				BusinessRuleMetadataDto parent = generated[0];
				RemoveChildRules(rules, resources, parent.UId);
				ruleIndex = FindSingleRuleIndexByName(rules, name);
				rules[ruleIndex] = SerializeCreatedRule(parent);
				foreach (BusinessRuleMetadataDto child in generated.Skip(1)) {
					rules.Add(SerializeCreatedRule(child));
				}

				foreach (BusinessRuleMetadataDto generatedRule in generated) {
					if (!string.IsNullOrWhiteSpace(generatedRule.Caption)) {
						UpsertCaptionResource(resources, generatedRule.UId, generatedRule.Caption.Trim());
					}
				}

				pending.Add((index, name));
			} catch (Exception exception) {
				results[index] = new BusinessRuleBatchItemResult(identifier, false, null, exception.Message);
			}
		}

		if (pending.Count == 0) {
			return results;
		}

		schema.MetaData = metadata.ToJsonString(JsonOptions);
		schema.Resources = resources;
		try {
			addon.SaveSchema(schema);
			foreach ((int index, string name) in pending) {
				results[index] = new BusinessRuleBatchItemResult(name, true, name, null);
			}
		} catch (Exception exception) {
			foreach ((int index, string name) in pending) {
				results[index] = new BusinessRuleBatchItemResult(name, false, null, exception.Message);
			}
		}

		return results;
	}

	internal static void EnsureUniqueRuleNames(JsonArray existingRules, IReadOnlyList<BusinessRuleMetadataDto> createdRules) {
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonNode? node in existingRules) {
			if (node is JsonObject ruleObject
				&& ruleObject["name"] is JsonValue nameValue
				&& nameValue.TryGetValue(out string? existingName)
				&& !string.IsNullOrWhiteSpace(existingName)) {
				names.Add(existingName);
			}
		}

		foreach (BusinessRuleMetadataDto createdRule in createdRules) {
			if (!string.IsNullOrWhiteSpace(createdRule.Name) && !names.Add(createdRule.Name)) {
				throw new InvalidOperationException(
					$"Business rule name '{createdRule.Name}' already exists on the target schema. " +
					"Rule names must be unique; use the update tool to change an existing rule.");
			}
		}
	}

	internal static int FindSingleRuleIndexByName(JsonArray rules, string ruleName) {
		int foundIndex = -1;
		for (int index = 0; index < rules.Count; index++) {
			if (rules[index] is not JsonObject ruleObject) {
				continue;
			}

			if (ruleObject["parentUId"] is JsonValue parentValue
				&& parentValue.TryGetValue(out string? parentUId)
				&& !string.IsNullOrWhiteSpace(parentUId)) {
				continue;
			}

			if (ruleObject["name"] is not JsonValue nameValue
				|| !nameValue.TryGetValue(out string? name)
				|| !string.Equals(name, ruleName, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			if (foundIndex >= 0) {
				throw new InvalidOperationException(
					$"Business rule name '{ruleName}' matches more than one rule on the schema; " +
					"rename the duplicates in the Creatio designer before using name-based operations.");
			}

			foundIndex = index;
		}

		return foundIndex;
	}

	internal static string? GetRuleUId(JsonObject ruleObject) =>
		ruleObject["uId"] is JsonValue uIdValue && uIdValue.TryGetValue(out string? uId) ? uId : null;

	internal static void RemoveChildRules(JsonArray rules, List<AddonResourceDto> resources, string parentUId) {
		for (int index = rules.Count - 1; index >= 0; index--) {
			if (rules[index] is not JsonObject ruleObject
				|| ruleObject["parentUId"] is not JsonValue parentValue
				|| !parentValue.TryGetValue(out string? candidateParentUId)
				|| !string.Equals(candidateParentUId, parentUId, StringComparison.OrdinalIgnoreCase)) {
				continue;
			}

			string? childUId = GetRuleUId(ruleObject);
			rules.RemoveAt(index);
			if (!string.IsNullOrWhiteSpace(childUId)) {
				RemoveCaptionResource(resources, childUId);
			}
		}
	}

	internal static void RemoveCaptionResource(List<AddonResourceDto> resources, string ruleUId) {
		string key = $"{ruleUId}.Caption";
		resources.RemoveAll(resource => string.Equals(resource.Key, key, StringComparison.OrdinalIgnoreCase));
	}

	internal static JsonObject ParseMetadata(string? metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return CreateEmptyMetadata();
		}

		try {
			return JsonNode.Parse(metaData) as JsonObject
				?? throw new InvalidOperationException("Business-rule add-on metadata root must be a JSON object.");
		} catch (JsonException exception) {
			throw new InvalidOperationException("Business-rule add-on metadata is not valid JSON.", exception);
		}
	}

	internal static JsonObject CreateEmptyMetadata() =>
		new() {
			["typeName"] = BusinessRulesMetadataTypeName,
			["rules"] = new JsonArray()
		};

	internal static JsonArray GetOrCreateRules(JsonObject metadata) {
		if (!metadata.TryGetPropertyValue("rules", out JsonNode? rulesNode) || rulesNode is null) {
			JsonArray createdRules = [];
			metadata["rules"] = createdRules;
			return createdRules;
		}

		return rulesNode as JsonArray
			?? throw new InvalidOperationException("Business-rule add-on metadata 'rules' property must be a JSON array.");
	}

	internal static JsonNode SerializeCreatedRule(BusinessRuleMetadataDto createdRule) =>
		JsonSerializer.SerializeToNode(createdRule, JsonOptions)
		?? throw new InvalidOperationException("Generated business-rule metadata could not be serialized.");

	/// <summary>
	/// Normalizes resource keys received from GetSchema to the format expected by SaveSchema.
	/// The server returns 4-part keys like <c>AddonConfig.Rules.{guid}.Caption</c>;
	/// this method extracts parts [2] and [3] to produce <c>{guid}.Caption</c>.
	/// Mirrors the frontend logic in <c>AddonInfo._setAddonResources</c>
	/// (libs/studio-enterprise/util/schema-designer-utils/src/lib/models/addon-info.ts).
	/// </summary>
	internal static List<AddonResourceDto> NormalizeResourceKeys(List<AddonResourceDto> resources) {
		for (int i = 0; i < resources.Count; i++) {
			string[] parts = resources[i].Key.Split('.');
			if (parts.Length == 4
				&& string.Equals(parts[0], "AddonConfig", StringComparison.Ordinal)
				&& string.Equals(parts[1], "Rules", StringComparison.Ordinal)) {
				resources[i].Key = $"{parts[2]}.{parts[3]}";
			}
		}
		return resources;
	}

	internal static void UpsertCaptionResource(List<AddonResourceDto> resources, string ruleUId, string caption) {
		string key = $"{ruleUId}.Caption";
		AddonResourceDto? existing = resources.FirstOrDefault(resource =>
			string.Equals(resource.Key, key, StringComparison.OrdinalIgnoreCase));
		AddonResourceValueDto enUsValue = new() {
			Key = EntitySchemaDesignerSupport.DefaultCultureName,
			Value = caption
		};
		if (existing is null) {
			resources.Add(new AddonResourceDto {
				Key = key,
				Value = [enUsValue]
			});
			return;
		}

		existing.Value = [enUsValue];
	}

	private static bool GetBool(JsonObject source, string propertyName, bool defaultValue) =>
		source[propertyName] is JsonValue value && value.TryGetValue(out bool result) ? result : defaultValue;
}
