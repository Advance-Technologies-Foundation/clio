using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal interface IBusinessRuleAddonService {
	BusinessRuleCreateResult AppendRule(
		AddonGetRequestDto request,
		BusinessRule rule,
		IReadOnlyList<BusinessRuleMetadataDto> createdRules);

	/// <summary>
	/// Appends the combined metadata of multiple business rules to the target add-on schema in a
	/// single round-trip: one <c>GetSchema</c>, one <c>SaveSchema</c>, one <c>ResetClientScriptCache</c>,
	/// and one <c>BuildConfiguration</c> for the whole batch. This is the batch counterpart of
	/// <see cref="AppendRule"/>, which makes those four remote calls per rule.
	/// </summary>
	/// <param name="request">Identifies the target add-on schema to fetch and save once.</param>
	/// <param name="createdRules">Flattened metadata for every rule in the batch, appended in order.</param>
	/// <returns>The generated name of the first appended rule.</returns>
	BusinessRuleCreateResult AppendRules(
		AddonGetRequestDto request,
		IReadOnlyList<BusinessRuleMetadataDto> createdRules);

	IReadOnlyList<BusinessRule> ReadRules(AddonGetRequestDto request);

	IReadOnlyList<BusinessRuleBatchItemResult> UpdateRules(
		AddonGetRequestDto request,
		IReadOnlyList<BusinessRuleUpdateItem> items);

	IReadOnlyList<BusinessRuleBatchItemResult> DeleteRules(
		AddonGetRequestDto request,
		IReadOnlyList<string> ruleNames);
}

internal sealed record BusinessRuleUpdateItem(
	string Name,
	bool? Enabled,
	IReadOnlyList<BusinessRuleMetadataDto> GeneratedRules);

internal sealed class BusinessRuleAddonService(
	IAddonSchemaDesignerClient addonSchemaDesignerClient)
	: IBusinessRuleAddonService {

	public BusinessRuleCreateResult AppendRule(
		AddonGetRequestDto request,
		BusinessRule rule,
		IReadOnlyList<BusinessRuleMetadataDto> createdRules) =>
		AppendRules(request, createdRules);

	public BusinessRuleCreateResult AppendRules(
		AddonGetRequestDto request,
		IReadOnlyList<BusinessRuleMetadataDto> createdRules) {
		ArgumentNullException.ThrowIfNull(createdRules);
		if (createdRules.Count == 0) {
			throw new ArgumentException("At least one generated business rule is required.", nameof(createdRules));
		}

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(request);
		JsonObject metadata = ParseMetadata(schema.MetaData);
		JsonArray rules = GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = NormalizeResourceKeys(schema.Resources.ToList());

		EnsureUniqueRuleNames(rules, createdRules);
		foreach (BusinessRuleMetadataDto createdRule in createdRules) {
			rules.Add(SerializeCreatedRule(createdRule));
			if (!string.IsNullOrWhiteSpace(createdRule.Caption)) {
				UpsertCaptionResource(resources, createdRule.UId, createdRule.Caption.Trim());
			}
		}

		SaveAndPublish(schema, metadata, resources);
		return new BusinessRuleCreateResult(createdRules[0].Name);
	}

	public IReadOnlyList<BusinessRule> ReadRules(AddonGetRequestDto request) {
		ArgumentNullException.ThrowIfNull(request);

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(request);
		JsonObject metadata = ParseMetadata(schema.MetaData);
		JsonArray rules = GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = NormalizeResourceKeys(schema.Resources.ToList());
		return BusinessRuleMetadataReader.Read(rules, resources);
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> UpdateRules(
		AddonGetRequestDto request,
		IReadOnlyList<BusinessRuleUpdateItem> items) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(items);
		if (items.Count == 0) {
			throw new ArgumentException("At least one business-rule update item is required.", nameof(items));
		}

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(request);
		JsonObject metadata = ParseMetadata(schema.MetaData);
		JsonArray rules = GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = NormalizeResourceKeys(schema.Resources.ToList());

		var results = new BusinessRuleBatchItemResult[items.Count];
		var pending = new List<(int Index, string Name)>();
		HashSet<string> batchNames = new(StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < items.Count; index++) {
			BusinessRuleUpdateItem item = items[index];
			try {
				if (!batchNames.Add(item.Name)) {
					results[index] = new BusinessRuleBatchItemResult(
						item.Name, false, null,
						$"Business rule '{item.Name}' appears more than once in the update batch.");
					continue;
				}

				int ruleIndex = FindSingleRuleIndexByName(rules, item.Name);
				if (ruleIndex < 0) {
					results[index] = new BusinessRuleBatchItemResult(
						item.Name, false, null, $"Business rule '{item.Name}' was not found.");
					continue;
				}

				JsonObject existingRule = (JsonObject)rules[ruleIndex]!;
				IReadOnlyList<BusinessRuleMetadataDto> graftedRules =
					BusinessRuleIdentityGrafter.Graft(existingRule, item.GeneratedRules, item.Enabled);
				BusinessRuleMetadataDto parent = graftedRules[0];
				RemoveChildRules(rules, resources, parent.UId);
				ruleIndex = FindSingleRuleIndexByName(rules, item.Name);
				rules[ruleIndex] = SerializeCreatedRule(parent);
				foreach (BusinessRuleMetadataDto child in graftedRules.Skip(1)) {
					rules.Add(SerializeCreatedRule(child));
				}

				foreach (BusinessRuleMetadataDto graftedRule in graftedRules) {
					if (!string.IsNullOrWhiteSpace(graftedRule.Caption)) {
						UpsertCaptionResource(resources, graftedRule.UId, graftedRule.Caption.Trim());
					}
				}

				pending.Add((index, item.Name));
			} catch (Exception exception) {
				results[index] = new BusinessRuleBatchItemResult(item.Name, false, null, exception.Message);
			}
		}

		StampPersistedOutcome(schema, metadata, resources, results, pending);
		return results;
	}

	public IReadOnlyList<BusinessRuleBatchItemResult> DeleteRules(
		AddonGetRequestDto request,
		IReadOnlyList<string> ruleNames) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(ruleNames);
		if (ruleNames.Count == 0) {
			throw new ArgumentException("At least one business-rule name is required.", nameof(ruleNames));
		}

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(request);
		JsonObject metadata = ParseMetadata(schema.MetaData);
		JsonArray rules = GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = NormalizeResourceKeys(schema.Resources.ToList());

		var results = new BusinessRuleBatchItemResult[ruleNames.Count];
		var pending = new List<(int Index, string Name)>();
		for (int index = 0; index < ruleNames.Count; index++) {
			string ruleName = ruleNames[index] ?? string.Empty;
			if (string.IsNullOrWhiteSpace(ruleName)) {
				results[index] = new BusinessRuleBatchItemResult(
					ruleName, false, null, "Business rule name is required.");
				continue;
			}

			int ruleIndex;
			try {
				ruleIndex = FindSingleRuleIndexByName(rules, ruleName);
			} catch (InvalidOperationException exception) {
				results[index] = new BusinessRuleBatchItemResult(ruleName, false, null, exception.Message);
				continue;
			}

			if (ruleIndex < 0) {
				results[index] = new BusinessRuleBatchItemResult(
					ruleName, false, null, $"Business rule '{ruleName}' was not found.");
				continue;
			}

			string? ruleUId = GetRuleUId((JsonObject)rules[ruleIndex]!);
			rules.RemoveAt(ruleIndex);
			if (!string.IsNullOrWhiteSpace(ruleUId)) {
				RemoveCaptionResource(resources, ruleUId);
				RemoveChildRules(rules, resources, ruleUId);
			}

			pending.Add((index, ruleName));
		}

		StampPersistedOutcome(schema, metadata, resources, results, pending);
		return results;
	}

	private void StampPersistedOutcome(
		AddonSchemaDto schema,
		JsonObject metadata,
		List<AddonResourceDto> resources,
		BusinessRuleBatchItemResult[] results,
		IReadOnlyList<(int Index, string Name)> pending) {
		if (pending.Count == 0) {
			return;
		}

		try {
			SaveAndPublish(schema, metadata, resources);
			foreach ((int index, string name) in pending) {
				results[index] = new BusinessRuleBatchItemResult(name, true, name, null);
			}
		} catch (Exception exception) {
			foreach ((int index, string name) in pending) {
				results[index] = new BusinessRuleBatchItemResult(name, false, null, exception.Message);
			}
		}
	}

	private void SaveAndPublish(AddonSchemaDto schema, JsonObject metadata, List<AddonResourceDto> resources) {
		schema.MetaData = metadata.ToJsonString(JsonOptions);
		schema.Resources = resources;

		addonSchemaDesignerClient.SaveSchema(schema);
		// Clears the server-side RequireJS module cache so the saved addon schema
		// is immediately visible to the current user without a full page reload.
		addonSchemaDesignerClient.ResetClientScriptCache();
		// Rebuilds static client content for changed schemas, broadcasts a
		// ConfigurationStructureChanged event to online users so their
		// crt-business-rules-cache is cleared immediately, and writes a new
		// ConfigurationHash to disk so offline users get cache invalidation on
		// their next startup via the /api/ClientCache/Hashes hash comparison.
		addonSchemaDesignerClient.BuildConfiguration();
	}

	private static void EnsureUniqueRuleNames(JsonArray existingRules, IReadOnlyList<BusinessRuleMetadataDto> createdRules) {
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

	private static int FindSingleRuleIndexByName(JsonArray rules, string ruleName) {
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

	private static string? GetRuleUId(JsonObject ruleObject) =>
		ruleObject["uId"] is JsonValue uIdValue && uIdValue.TryGetValue(out string? uId) ? uId : null;

	private static void RemoveChildRules(JsonArray rules, List<AddonResourceDto> resources, string parentUId) {
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

	private static void RemoveCaptionResource(List<AddonResourceDto> resources, string ruleUId) {
		string key = $"{ruleUId}.Caption";
		resources.RemoveAll(resource => string.Equals(resource.Key, key, StringComparison.OrdinalIgnoreCase));
	}

	private static JsonObject ParseMetadata(string? metaData) {
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

	private static JsonObject CreateEmptyMetadata() =>
		new() {
			["typeName"] = BusinessRulesMetadataTypeName,
			["rules"] = new JsonArray()
		};

	private static JsonArray GetOrCreateRules(JsonObject metadata) {
		if (!metadata.TryGetPropertyValue("rules", out JsonNode? rulesNode) || rulesNode is null) {
			JsonArray createdRules = [];
			metadata["rules"] = createdRules;
			return createdRules;
		}

		return rulesNode as JsonArray
			?? throw new InvalidOperationException("Business-rule add-on metadata 'rules' property must be a JSON array.");
	}

	private static JsonNode SerializeCreatedRule(BusinessRuleMetadataDto createdRule) =>
		JsonSerializer.SerializeToNode(createdRule, JsonOptions)
		?? throw new InvalidOperationException("Generated business-rule metadata could not be serialized.");

	/// <summary>
	/// Normalizes resource keys received from GetSchema to the format expected by SaveSchema.
	/// The server returns 4-part keys like <c>AddonConfig.Rules.{guid}.Caption</c>;
	/// this method extracts parts [2] and [3] to produce <c>{guid}.Caption</c>.
	/// Mirrors the frontend logic in <c>AddonInfo._setAddonResources</c>
	/// (libs/studio-enterprise/util/schema-designer-utils/src/lib/models/addon-info.ts).
	/// </summary>
	private static List<AddonResourceDto> NormalizeResourceKeys(List<AddonResourceDto> resources) {
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

	private static void UpsertCaptionResource(List<AddonResourceDto> resources, string ruleUId, string caption) {
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
}
