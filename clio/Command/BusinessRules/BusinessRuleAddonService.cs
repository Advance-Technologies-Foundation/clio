using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;
using Clio.Command.BusinessRules.Converters;

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

	AddonSchemaDto GetSchema(AddonGetRequestDto request);

	void SaveSchema(AddonSchemaDto schema);

	IReadOnlyList<BusinessRuleBatchItemResult> DeleteRules(
		AddonGetRequestDto request,
		IReadOnlyList<string> ruleNames);
}

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
		JsonObject metadata = BusinessRuleAddonMetadata.ParseMetadata(schema.MetaData);
		JsonArray rules = BusinessRuleAddonMetadata.GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = BusinessRuleAddonMetadata.NormalizeResourceKeys(schema.Resources.ToList());

		BusinessRuleAddonMetadata.EnsureUniqueRuleNames(rules, createdRules);
		foreach (BusinessRuleMetadataDto createdRule in createdRules) {
			rules.Add(BusinessRuleAddonMetadata.SerializeCreatedRule(createdRule));
			if (!string.IsNullOrWhiteSpace(createdRule.Caption)) {
				BusinessRuleAddonMetadata.UpsertCaptionResource(resources, createdRule.UId, createdRule.Caption.Trim());
			}
		}

		SaveAndPublish(schema, metadata, resources);
		return new BusinessRuleCreateResult(createdRules[0].Name);
	}

	public IReadOnlyList<BusinessRule> ReadRules(AddonGetRequestDto request) {
		ArgumentNullException.ThrowIfNull(request);

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(request);
		JsonObject metadata = BusinessRuleAddonMetadata.ParseMetadata(schema.MetaData);
		JsonArray rules = BusinessRuleAddonMetadata.GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = BusinessRuleAddonMetadata.NormalizeResourceKeys(schema.Resources.ToList());
		return FullToSimpleBusinessRuleConverter.Convert(rules, resources);
	}

	public AddonSchemaDto GetSchema(AddonGetRequestDto request) {
		ArgumentNullException.ThrowIfNull(request);
		return addonSchemaDesignerClient.GetSchema(request);
	}

	public void SaveSchema(AddonSchemaDto schema) {
		ArgumentNullException.ThrowIfNull(schema);
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

	public IReadOnlyList<BusinessRuleBatchItemResult> DeleteRules(
		AddonGetRequestDto request,
		IReadOnlyList<string> ruleNames) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(ruleNames);
		if (ruleNames.Count == 0) {
			throw new ArgumentException("At least one business-rule name is required.", nameof(ruleNames));
		}

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(request);
		JsonObject metadata = BusinessRuleAddonMetadata.ParseMetadata(schema.MetaData);
		JsonArray rules = BusinessRuleAddonMetadata.GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = BusinessRuleAddonMetadata.NormalizeResourceKeys(schema.Resources.ToList());

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
				ruleIndex = BusinessRuleAddonMetadata.FindSingleRuleIndexByName(rules, ruleName);
			} catch (InvalidOperationException exception) {
				results[index] = new BusinessRuleBatchItemResult(ruleName, false, null, exception.Message);
				continue;
			}

			if (ruleIndex < 0) {
				results[index] = new BusinessRuleBatchItemResult(
					ruleName, false, null, $"Business rule '{ruleName}' was not found.");
				continue;
			}

			string? ruleUId = BusinessRuleAddonMetadata.GetRuleUId((JsonObject)rules[ruleIndex]!);
			rules.RemoveAt(ruleIndex);
			if (!string.IsNullOrWhiteSpace(ruleUId)) {
				BusinessRuleAddonMetadata.RemoveCaptionResource(resources, ruleUId);
				BusinessRuleAddonMetadata.RemoveChildRules(rules, resources, ruleUId);
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
		SaveSchema(schema);
	}
}
